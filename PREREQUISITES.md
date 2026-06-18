# متطلبات تشغيل Uqeb في الإنتاج

هذا المستند يحدد المتطلبات لتشغيل **Uqeb** على Windows إنتاجي (معزول أو محدود الإنترنت) باستخدام **حزمة publish جاهزة**.

---

## 1. نظام التشغيل

| المتطلب | التفاصيل |
|---------|----------|
| **نظام التشغيل** | Windows 10 Pro، Windows 11 Pro، أو Windows Server 2019/2022 |
| **الصلاحيات** | حساب مسؤول لتثبيت IIS وSQL Server؛ **مطلوب** لتشغيل `deploy-production.ps1` (ليس لـ `check-prerequisites.ps1` — قراءة فقط) |

---

## 2. بنية التشغيل (مهم)

| الطبقة | المسار / المنفذ |
|--------|------------------|
| **Frontend (IIS)** | موقع ثابت من `C:\Uqeb\web` |
| **Backend (Kestrel)** | `http://localhost:5000` — مجلد `C:\Uqeb\api` |
| **المرفقات** | `C:\Uqeb\uploads` (أو المسار في `FileStorage:Path`) |
| **السجلات** | `C:\Uqeb\logs` |
| **النسخ الاحتياطية** | `C:\Uqeb\backup` |

> **لا يفترض PR أن IIS يمرّر `/api` إلى Kestrel** إلا إذا نُصّب واختُبر reverse proxy (مثل ARR) بشكل منفصل. الواجهة تتصل بـ `/api` نسبيًا؛ في الإنتاج يجب ضبط `AllowedOrigins` في `appsettings.Production.json` ليطابق عنوان موقع IIS.

> **`/api/health` و `/swagger` غير مضمونين في Production** ما لم تُفعَّلا صراحة في التطبيق.

---

## 3. IIS

| الميزة | الغرض |
|--------|--------|
| **Static Content** | تقديم `index.html` و`assets/` |
| **Default Document** | فتح `index.html` تلقائيًا |
| **HttpErrors** | SPA fallback بدون URL Rewrite Module — انظر القسم 3.2 |

**المجلد الفعلي للموقع:** `C:\Uqeb\web` (أو `-InstallRoot\web`)

### 3.2 قيود SPA بدون URL Rewrite

`web.config` المُنشأ يستخدم `httpErrors` لإرجاع `index.html` عند 404. النتيجة:

- المسارات العميقة (مثل `/transactions`) تعمل في المتصفح بعد تحميل React.
- **رمز الاستجابة HTTP يبقى 404** وليس 200.
- مناسب للاستخدام الداخلي الحالي.
- للحصول على 200 لكل مسار SPA: ثبّت **URL Rewrite** أو **reverse proxy** مع rewrite rule — ليس جزءًا من هذا PR.

---

## 4. SQL Server

| السيرفر | متى |
|---------|-----|
| `localhost` | نسخة افتراضية (`MSSQLSERVER`) |
| `localhost\SQLEXPRESS` | Express (`MSSQL$SQLEXPRESS`) |

**الإعداد:** `C:\Uqeb\api\appsettings.Production.json`

**sqlcmd:** عند استخدام شهادة SQL محلية غير موثوقة، استخدم `-C` (مثال: `sqlcmd -C -S localhost\SQLEXPRESS ...`).

---

## 5. ASP.NET Core Runtime

```powershell
dotnet --list-runtimes
```

يجب توفر `Microsoft.AspNetCore.App` متوافق مع TFM الحزمة (`net10.0` في `Uqeb.Api.runtimeconfig.json`).

`check-prerequisites.ps1` يقرأ `InstallRoot\api\Uqeb.Api.runtimeconfig.json` إن وُجد لتحديد الإصدار المطلوب؛ وإلا يفترض `net10.0` / ASP.NET Core 10.x.

---

## 6. ما لا تحتاجه على الإنتاج (حزمة جاهزة)

| غير مطلوب | ملاحظة |
|-----------|--------|
| Git | النشر عبر `deploy-production.ps1` |
| Node.js / npm | الواجهة مبنية مسبقًا في الحزمة |
| .NET SDK | يكفي Runtime |
| dotnet ef | migrations عند الإعداد الأولي فقط |

---

## 7. هيكل المجلدات بعد النشر

```text
C:\Uqeb\
  api\           Backend (Uqeb.Api.dll)
  web\           Frontend (IIS root)
  logs\
  uploads\
  backup\        api-YYYYMMDD-HHmmss / web-YYYYMMDD-HHmmss
  BUILD_INFO.txt
  start-api.ps1
```

---

## 8. فحص سريع

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\check-prerequisites.ps1
powershell -ExecutionPolicy Bypass -File .\scripts\check-prerequisites.ps1 -InstallRoot "C:\Uqeb"
```

السكربت **قراءة فقط** — لا يثبت مكونات ولا يعدّل IIS أو SQL.

---

## 9. مراجع

- [DEPLOYMENT_NOTES.md](DEPLOYMENT_NOTES.md)
- [README.md](README.md)
