# قرار وتصميم: Local Scanner Bridge لنظام Uqeb

**الحالة:** تخطيط وتصميم فقط (PR 1 — design gate)  
**التاريخ:** 2026-06-18  
**النطاق:** لا تنفيذ runtime، لا تغيير قاعدة البيانات، لا تغيير auth، لا تغيير deployment

---

## 0. بوابة القرار — قرارات ثابتة (Design Gate)

| # | القرار |
|---|--------|
| 1 | الواجهة **حاليًا** تستخدم `transactionsApi.uploadAttachment` من تبويب **المرفقات** في `TransactionDetail.tsx` لرفع الملفات يدويًا. |
| 2 | بعد المسح، يُرفع الملف عبر **`POST /api/transactions/{id}/attachments`** — نفس endpoint الموجود. |
| 3 | `attachmentType` للمسح الضوئي: **`"Scan"`** (قيمة ثابتة مقترحة). |
| 4 | **لا حاجة لـ backend PR الآن** — نموذج المرفقات و endpoints الحالية كافية. |
| 5 | **PR 2 (backend readiness)** اختياري وليس **blocker** — فقط إذا رُغب لاحقًا بتحقق إضافي من نوع الملف. |
| 6 | **Bridge endpoints:** `GET /status` · `GET /scanners` · `POST /scan` · `GET /scan/{scanId}/file` · `DELETE /scan/{scanId}` (اختياري لاحقًا). |
| 7 | **خطة PRs:** PR 1 design gate فقط → PR 2 backend اختياري → PR 3 UI + mock bridge → PR 4 .NET Scanner Bridge MVP → PR 5 integration test على جهاز بماسح فعلي. |

---

## 1. الهدف

تمكين المستخدم من داخل نافذة معاملة في Uqeb أن:

1. يفتح تبويب **المرفقات**.
2. يضغط **مسح ضوئي**.
3. يختار الماسح (إن وُجد أكثر من جهاز).
4. يعاين الوثيقة الممسوحة.
5. يحفظها كـ **مرفق** مرتبط بنفس المعاملة عبر API الحالي.

**مبدأ عدم الكسر:** إذا لم تكن خدمة الماسح المحلية (Bridge) متوفرة، يستمر النظام بالعمل كما هو اليوم (رفع ملف يدوي، معاملات، تقارير، تسجيل دخول) مع رسالة توضيحية فقط.

---

## 2. السياق التشغيلي الحالي

| المكوّن | الوضع الحالي |
|---------|--------------|
| Frontend | React/Vite عبر IIS (`C:\Uqeb\web`) |
| Backend | ASP.NET Core API على Kestrel `localhost:5000` |
| المرفقات | جدول `Attachments` + تخزين ملفات في `FileStorage:Path` |
| رفع المرفق | `POST /api/transactions/{id}/attachments` (multipart) |
| الواجهة | `TransactionDetail` — تبويب المرفقات + `uploadAttachment` |

**قيود المتصفح:** لا يمكن الوصول الموثوق لماسح ضوئي من JavaScript داخل المتصفح على Windows. الحل المعتمد صناعيًا هو **وسيط محلي** (Local Bridge) يعمل على نفس الجهاز ويتحدث مع الواجهة عبر `localhost`.

---

## 3. التصميم المعماري

```text
┌─────────────────────────────────────────────────────────────────┐
│  المتصفح (React — Uqeb UI عبر IIS)                              │
│  TransactionDetail → ScannerPanel / ScanAttachmentButton        │
└────────────┬───────────────────────────────┬────────────────────┘
             │ JWT (Bearer)                  │ HTTP (no auth / local token)
             │ multipart upload              │ JSON + binary preview
             ▼                               ▼
┌────────────────────────────┐   ┌────────────────────────────────┐
│  Uqeb API (Kestrel :5000)  │   │  Local Scanner Bridge (:5055)  │
│  Attachments endpoints     │   │  Windows only — WIA primary    │
│  FileStorage on disk       │   │  Temp scan folder              │
└────────────────────────────┘   └────────────────────────────────┘
                                              │
                                              ▼
                                    ┌──────────────────┐
                                    │  Scanner hardware │
                                    │  (WIA / TWAIN)    │
                                    └──────────────────┘
```

### 3.1 تدفق المستخدم (سعيد المسار)

```text
1. المستخدم يفتح معاملة → تبويب المرفقات
2. Frontend يتحقق من Bridge: GET http://127.0.0.1:5055/status
3. إن كان متاحًا: GET /scanners → قائمة الأجهزة
4. المستخدم يختار ماسحًا ويضغط "مسح"
5. Frontend: POST /scan { scannerId, format: "image/jpeg", dpi: 300 }
6. Bridge يمسح → يرجع scanId + previewBase64 (معاينة منخفضة الدقة فقط)
7. المستخدم يعاين previewBase64 → تدوير / إعادة مسح / حفظ / إلغاء
8. عند "حفظ كمرفق":
   - Frontend: GET /scan/{scanId}/file → Blob بالدقة الكاملة (binary stream)
   - لا يُستخدم previewBase64 للرفع — لتجنب مشاكل الذاكرة على الملفات الكبيرة
   - يرفع Blob عبر transactionsApi.uploadAttachment → POST /api/transactions/{id}/attachments
   - Bridge يُبلَغ بحذف الملف المؤقت (أو يحذف تلقائيًا بعد TTL)
9. Frontend يحدّث قائمة المرفقات
```

### 3.2 Local Scanner Bridge — واجهة HTTP المقترحة

**Base URL:** `http://127.0.0.1:5055` أو `http://[::1]:5055` (ثابت في الإعدادات، قابل للتجاوز عبر `VITE_SCANNER_BRIDGE_URL` لاحقًا)

| Method | Path | الغرض |
|--------|------|--------|
| `GET` | `/status` | صحة الخدمة، إصدار Bridge، عدد الماسحات |
| `GET` | `/scanners` | قائمة الماسحات المتاحة |
| `POST` | `/scan` | تنفيذ مسح؛ إرجاع `scanId` + `previewBase64` للمعاينة |
| `GET` | `/scan/{scanId}/file` | استرجاع الملف بكامل الدقة كـ **binary stream** (للرفع) |
| `DELETE` | `/scan/{scanId}` | حذف نتيجة مؤقتة (اختياري — PR 4) |

#### `GET /status` — مثال استجابة

```json
{
  "ok": true,
  "version": "0.1.0",
  "scannerApi": "WIA",
  "scannerCount": 1,
  "tempFolder": "C:\\Users\\...\\AppData\\Local\\Uqeb\\ScannerBridge\\temp"
}
```

#### `GET /scanners` — مثال استجابة

```json
{
  "scanners": [
    {
      "id": "wia:device-id-guid",
      "name": "HP LaserJet Pro MFP M428fdw",
      "default": true
    }
  ]
}
```

#### `POST /scan` — طلب ومثال استجابة

**طلب:**

```json
{
  "scannerId": "wia:device-id-guid",
  "format": "image/jpeg",
  "dpi": 300,
  "colorMode": "color"
}
```

**استجابة:**

```json
{
  "scanId": "a1b2c3d4-e5f6-7890-abcd-ef1234567890",
  "contentType": "image/jpeg",
  "fileName": "scan-20260618-120000.jpg",
  "width": 2480,
  "height": 3508,
  "previewBase64": "<base64-thumbnail-only>",
  "expiresAtUtc": "2026-06-18T12:05:00Z"
}
```

> **`previewBase64` للمعاينة فقط** — صورة مصغّرة أو منخفضة الدقة لعرضها في `ScannerPanel`. **ليس** الملف الكامل ولا يُستخدم كمسار الرفع النهائي.

#### `GET /scan/{scanId}/file` — استرجاع الملف الكامل

- **الاستجابة:** `Content-Type: image/jpeg` (أو PNG/PDF حسب المسح) — **binary stream** بالدقة الكاملة.
- **الاستخدام:** Frontend يستدعي هذا endpoint عند «حفظ كمرفق»، يحوّل الاستجابة إلى `Blob`/`File`، ثم يرفعها إلى:
  `POST /api/transactions/{id}/attachments` مع `attachmentType = "Scan"`.
- **السبب:** تجنّب تحميل الملف الكامل في JSON/base64 داخل `POST /scan` — يقلّل استهلاك الذاكرة في المتصفح وفي Bridge.

```http
GET http://127.0.0.1:5055/scan/a1b2c3d4-e5f6-7890-abcd-ef1234567890/file
→ 200 OK, body: binary image data
```

### 3.3 Bridge — تقنية المسح

| الأولوية | API | الملاحظات |
|----------|-----|-----------|
| **1 (MVP)** | **WIA** (Windows Image Acquisition) | مدمج في Windows، يعمل مع معظم ماسحات المكتب |
| 2 (لاحقًا) | TWAIN | عبر wrapper إذا فشل WIA على جهاز معيّن |
| مرفوض في PR 1 | OCR / تحليل محتوى | خارج النطاق |

---

## 4. حدود الأمان

| القاعدة | التطبيق |
|---------|---------|
| **loopback فقط (IPv4 + IPv6)** | Bridge يستمع على **`127.0.0.1:5055`** و**`[::1]:5055`** — عبر Kestrel loopback bindings لكل من IPv4 وIPv6. **لا** `0.0.0.0` ولا `::` (جميع الواجهات) |
| **لا فتح على الشبكة** | لا reverse proxy، لا firewall rule، لا تعريض للشبكة المحلية |
| **أصل موثوق** | Bridge يتحقق من `Origin` / `Referer` ضد قائمة مسموحة: **`http://127.0.0.1`**, **`http://localhost`**, **`http://[::1]`** (إن استُخدم IPv6)، وأصل IIS المحلي (مثل `http://server-name`) — قابلة للإعداد في `appsettings` للـ Bridge |
| **CORS محدود** | `Access-Control-Allow-Origin` لأصول Uqeb المعروفة فقط |
| **مجلد مؤقت محدد** | `%LOCALAPPDATA%\Uqeb\ScannerBridge\temp` |
| **تنظيف تلقائي** | حذف الملفات بعد الرفع الناجح + TTL (مثلاً 10 دقائق) + تنظيف عند إغلاق Bridge |
| **لا صلاحيات مرتفعة إلزامية** | Bridge يعمل بصلاحيات المستخدم الحالي؛ لا يتطلب Admin إلا إذا طلبه برنامج تشغيل الماسح |
| **لا أوامر خطرة** | لا shell، لا مسارات مخصصة من العميل، لا كتابة خارج temp |
| **لا OCR** | لا معالجة محتوى في هذه المرحلة |
| **فصل عن API الرئيسي** | Bridge عملية/خدمة منفصلة — لا تدمج في `Uqeb.Api` |

### 4.1 تهديدات مُخفَّفة ومتبقية

| التهديد | التخفيف |
|---------|---------|
| موقع خبيث على نفس الجهاز يستدعي Bridge | فحص Origin + (اختياري لاحقًا) token محلي قصير العمر يُصدر عند فتح ScannerPanel |
| تراكم ملفات مؤقتة | TTL + حذف عند الحفظ/الإلغاء |
| ملف مسح كبير | `previewBase64` مصغّر فقط؛ الملف الكامل عبر `GET /scan/{scanId}/file` (stream). حد حجم في Bridge (مثلاً 25 MB) متوافق مع `[RequestSizeLimit(50_000_000)]` في API |

---

## 5. تجربة المستخدم (UX)

### 5.1 نقطة الدخول

- زر **مسح ضوئي** بجانب **رفع ملف** في تبويب المرفقات (`TransactionDetail`).
- يظهر فقط لمن لديه `canEdit` (نفس شرط رفع الملف).
- مكوّن منفصل: `ScanAttachmentButton` → يفتح `ScannerPanel` (modal/drawer).

### 5.2 حالات الشاشة

| الحالة | العرض |
|--------|--------|
| Bridge غير متاح | تنبيه: «خدمة الماسح غير شغالة» + رابط تعليمات تثبيت (لاحقًا) — **لا يتعطل التبويب** |
| لا يوجد ماسح | «لا يوجد ماسح متصل» |
| جاري المسح | مؤشر تحميل |
| نجاح | معاينة الصورة + أزرار التحكم |
| فشل | رسالة محددة (انظر أدناه) |

### 5.3 أزرار المعاينة

| الزر | السلوك |
|------|--------|
| **تدوير** | تدوير 90° في الذاكرة (Canvas) قبل الحفظ |
| **إعادة المسح** | `POST /scan` مجددًا |
| **حفظ كمرفق** | `GET /scan/{scanId}/file` → Blob → `POST /api/transactions/{id}/attachments` → إغلاق اللوحة → تحديث القائمة |
| **إلغاء** | إغلاق + طلب حذف temp من Bridge إن وُجد `scanId` |

### 5.4 رسائل الخطأ (عربية — ثابتة في الواجهة)

| الرمز الداخلي | الرسالة للمستخدم |
|---------------|----------------|
| `BRIDGE_OFFLINE` | خدمة الماسح غير شغالة. يمكنك رفع ملف يدويًا. |
| `NO_SCANNER` | لا يوجد ماسح متصل بهذا الجهاز. |
| `SCAN_FAILED` | فشل الاتصال بالماسح. تحقق من تشغيل الجهاز وأعد المحاولة. |
| `UPLOAD_FAILED` | فشل حفظ المرفق. الملف لم يُحفظ في المعاملة. |
| `BRIDGE_TIMEOUT` | انتهت مهلة الاتصال بخدمة الماسح. |

---

## 6. Backend — تقييم الوضع الحالي

### 6.1 هل نموذج المرفقات يكفي؟

**نعم — لا حاجة لتغيير قاعدة البيانات في هذه المرحلة.**

الكيان `Attachment` يحتوي: `TransactionId`, `AttachmentType`, `OriginalFileName`, `StoredFileName`, `FilePath`, `ContentType`, `FileSize`, `UploadedById`, `UploadedAt`.

مسح ضوئي = ملف صورة (JPEG/PNG/PDF) يُرفع كأي مرفق آخر مع **`attachmentType = "Scan"`**.

### 6.2 Endpoints القائمة (كافية)

| Endpoint | الاستخدام |
|----------|-----------|
| `GET /api/transactions/{id}/attachments` | عرض المرفقات بعد الحفظ |
| `POST /api/transactions/{id}/attachments` | **رفع نتيجة المسح** — `IFormFile file`, `attachmentType` اختياري |
| `GET /api/transactions/{id}/attachments/{attachmentId}/download` | تحميل المرفق |

**الموقع في الكود:**

- `TransactionsController.UploadAttachment` — `[RequestSizeLimit(50_000_000)]`, `[Authorize(Policy = "CanEditTransactions")]`
- `AttachmentService.UploadAsync` — تخزين في `FileStorage:Path`
- Frontend: `frontend/uqeb-ui/src/pages/TransactionDetail.tsx` — تبويب المرفقات يستدعي `transactionsApi.uploadAttachment`
- API client: `frontend/uqeb-ui/src/api/services.ts` → `uploadAttachment`

### 6.3 تغييرات Backend المقترحة (PR 2 — اختياري، ليس blocker)

| التغيير | ضرورة | الملاحظة |
|---------|--------|----------|
| endpoint جديد | **لا** | الموجود يكفي |
| migration | **لا** | — |
| قبول `image/jpeg` صراحة | اختياري | التحقق من الامتداد/النوع إن رُغب — ليس blocker |
| `attachmentType` enum | اختياري لاحقًا | نص حر كافٍ الآن |

**لا تغيير في منطق المعاملات** — المسح يضيف مرفقًا فقط عبر المسار الحالي.

---

## 7. Frontend — تصميم المكوّنات

### 7.1 هيكل الملفات المقترح (PR 3)

```text
frontend/uqeb-ui/src/
  features/scanner/
    ScanAttachmentButton.tsx    # زر في تبويب المرفقات
    ScannerPanel.tsx            # modal: اختيار ماسح + معاينة + أزرار
    scannerBridgeClient.ts      # استدعاءات localhost:5055
    scannerTypes.ts             # أنواع TypeScript
    scannerErrors.ts            # رموز ورسائل الخطأ
    useScannerBridge.ts         # hook: status, scanners, scan, rotate
```

### 7.2 مبادئ عدم الكسر

| المبدأ | التطبيق |
|--------|---------|
| لا اعتماد على Bridge أثناء build | `scannerBridgeClient` يستدعي `fetch` runtime فقط؛ لا import لحزم native |
| Bridge غير موجود | `catch` على `fetch` → `BRIDGE_OFFLINE` — بقية الصفحة تعمل |
| لا تغيير routing | المكوّن داخل `TransactionDetail` فقط |
| لا تغيير تبويبات أخرى | login, reports, transactions list دون مساس |
| feature flag اختياري | `VITE_SCANNER_ENABLED=true` (افتراضي `true` — الإخفاء عند `false` فقط) |

### 7.3 دمج في `TransactionDetail`

```tsx
// مقترح — PR 3
{canEdit && (
  <>
    <label className="btn btn-sm btn-primary">رفع ملف ...</label>
    <ScanAttachmentButton
      transactionId={+id!}
      onSaved={() => { /* refresh attachments tab */ }}
    />
  </>
)}
```

`ScanAttachmentButton` لا يُحمّل `ScannerPanel` إلا عند النقر (lazy) لتقليل تأثير على التحميل الأولي.

---

## 8. Bridge — خيارات التنفيذ والتوصية

| الخيار | المزايا | العيوب |
|--------|---------|--------|
| **A) .NET Worker/Console + WIA** | متسق مع stack المشروع، نشر Windows بسيط، Kestrel خفيف | WIA فقط في MVP؛ TWAIN يحتاج مكتبة إضافية موثّقة |
| B) Windows desktop helper (WinForms/WPF) | UI محلي للتشخيص | ازدواجية واجهة؛ أصعب في التكامل مع React |
| C) Electron (لاحقًا) | تطبيق سطح مكتب موحّد | حجم كبير؛ خارج نطاق الإنتاج الحالي (IIS + browser) |

### التوصية: **A — .NET Local Scanner Bridge منفصل**

```text
scanner-bridge/
  Uqeb.ScannerBridge/          # ASP.NET Core minimal API
  Uqeb.ScannerBridge.Wia/      # تكامل WIA (مكتبة/System.Drawing أو NAPS2.Sdk لاحقًا — يُوثَّق قبل الإضافة)
  README.md                    # تثبيت وتشغيل كخدمة Windows أو Scheduled Task
```

**تشغيل مقترح:**

- Development: `dotnet run` من مجلد Bridge
- Production: Windows Service أو Scheduled Task «At log on» للمستخدم
- المنفذ: `5055` على loopback فقط (`127.0.0.1` + `::1`) — لا يتعارض مع API `5000`

**لا يُضاف إلى `Uqeb.Api`** — عملية منفصلة، إصدار منفصل، نشر اختياري على أجهزة بها ماسح.

---

## 9. خطة التنفيذ على PRs

| PR | المحتوى | تغيير DB | تغيير runtime إنتاجي |
|----|---------|----------|----------------------|
| **PR 1** (هذا) | `docs/SCANNER_BRIDGE_DESIGN.md` — قرار وتصميم | لا | لا |
| **PR 2** | Backend readiness (اختياري — **ليس blocker**): تحقق اختياري من أنواع الملفات، توثيق `attachmentType=Scan` | لا | لا |
| **PR 3** | Frontend: `ScanAttachmentButton`, `ScannerPanel`, mock bridge للتطوير | لا | لا — mock فقط |
| **PR 4** | `Uqeb.ScannerBridge` MVP: WIA, `/status`, `/scanners`, `/scan`, `/scan/{scanId}/file`, temp cleanup | لا | Bridge جديد (منفصل) |
| **PR 5** | Integration: اختبار على جهاز بماسح فعلي، تعليمات تثبيت، تحسين رسائل الخطأ | لا | Bridge + ربط UI |

### تبعيات

```text
PR 1 ──► PR 2 (اختياري) ──► PR 3 ──► PR 4 ──► PR 5
                └──────────────── PR 3 يمكن mock بدون PR 2
```

---

## 10. اختبارات القبول

| # | السيناريو | النتيجة المتوقعة |
|---|-----------|------------------|
| AC-1 | Bridge غير شغال | النظام يعمل؛ تبويب المرفقات يعمل؛ رسالة «خدمة الماسح غير شغالة» |
| AC-2 | Bridge شغال + ماسح متصل | تظهر قائمة الماسحات |
| AC-3 | تنفيذ مسح | تظهر معاينة الصورة |
| AC-4 | حفظ كمرفق | `POST /api/transactions/{id}/attachments` ناجح |
| AC-5 | بعد الحفظ | المرفق يظهر في جدول المرفقات |
| AC-6 | فشل المسح | رسالة خطأ؛ يمكن إعادة المحاولة أو الإلغاء؛ لا crash |
| AC-7 | إلغاء | لا مرفق جديد؛ temp يُنظَّف |
| AC-8 | Login | لا يتأثر |
| AC-9 | Reports | لا تتأثر |
| AC-10 | Transactions list/detail (بدون مسح) | لا تتأثر |
| AC-11 | رفع ملف يدوي | يستمر بالعمل |

---

## 11. ما هو خارج النطاق (صريح)

- OCR واستخراج نص
- مسح من الشبكة / RDP بعيد (يتطلب Bridge على جهاز المستخدم الفعلي)
- تغيير auth أو JWT
- تغيير deployment scripts الحالية
- دمج Bridge داخل `Uqeb.Api`
- مكتبات TWAIN/Electron كبيرة قبل توثيق السبب وموافقة PR منفصل

---

## 12. قرارات معتمدة (ADR مختصر)

| # | القرار | السبب |
|---|--------|--------|
| D1 | Local Bridge على loopback `127.0.0.1` + `::1` (منفذ `5055`) | المتصفح لا يصل للماسح مباشرة؛ دعم IPv4/IPv6 |
| D2 | إعادة استخدام `POST .../attachments` | API وDB جاهزان — أقل مخاطرة |
| D3 | WIA أولًا في MVP | تغطية Windows + ماسحات مكتب شائعة |
| D4 | مكوّن frontend منفصل + lazy load | عدم كسر `TransactionDetail` |
| D5 | Bridge عملية منفصلة عن API | أمان ونشر مستقل |
| D6 | لا DB changes في PR 1–4 | نموذج `Attachment` كافٍ |
| D7 | معاينة عبر `previewBase64`؛ رفع عبر `GET /scan/{scanId}/file` | تجنّب base64 للملف الكامل — مشاكل الذاكرة |

---

## 13. مراجع الكود الحالي

| المكوّن | المسار |
|---------|--------|
| Upload endpoint | `backend/Uqeb.Api/Controllers/TransactionsController.cs` |
| Attachment service | `backend/Uqeb.Api/Services/AttachmentService.cs` |
| Entity | `backend/Uqeb.Api/Models/Entities/Attachment.cs` |
| API client | `frontend/uqeb-ui/src/api/services.ts` → `uploadAttachment` |
| UI | `frontend/uqeb-ui/src/pages/TransactionDetail.tsx` — تبويب المرفقات |

---

## 14. الخطوة التالية

بعد دمج **PR 1**، يبدأ **PR 3** (واجهة + mock) بالتوازي مع تصميم **PR 4** (Bridge)، مع تأجيل أي مكتبة مسح خارج .NET BCL حتى توثيق الحجم والترخيص في PR 4.
