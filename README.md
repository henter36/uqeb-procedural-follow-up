# المتابعة الإجرائية (Uqeb) — إدارة الوارد والصادر

نظام ويب داخلي (**المتابعة الإجرائية**) لإدارة المعاملات الواردة والصادرة والتعقيبات والتحويلات على الشبكة المحلية.

> الاسم الظاهر للمستخدم في الواجهة: **المتابعة الإجرائية**. أسماء المشروع التقنية (Uqeb، UqebDb، namespaces) لم تتغير.

## التقنيات

| الطبقة | التقنية |
|--------|---------|
| Backend | ASP.NET Core 10 Web API |
| Database | SQL Server |
| ORM | Entity Framework Core 10 |
| Frontend | React 19 + Vite + TypeScript |
| Auth | JWT Bearer |
| Reports Export | ClosedXML (Excel) |

## بنية المشروع

```
uqeb/
├── backend/
│   ├── Uqeb.sln
│   └── Uqeb.Api/
│       ├── Controllers/       # REST API
│       ├── Data/              # DbContext, Migrations, Seed
│       ├── DTOs/              # Data Transfer Objects
│       ├── Models/Entities/   # نماذج البيانات
│       ├── Services/          # منطق الأعمال
│       └── appsettings.example.json
├── frontend/
│   └── uqeb-ui/               # React RTL
└── README.md
```

## المتطلبات

- .NET 10 SDK
- SQL Server 2019+ أو SQL Server Express مع LocalDB
- Node.js 18+
- IIS (للنشر على الشبكة المحلية)

---

## 1. إعداد قاعدة البيانات

### إنشاء قاعدة البيانات

```sql
CREATE DATABASE UqebDb;
```

### تعديل Connection String

انسخ `appsettings.example.json` إلى `appsettings.json` وعدّل:

```json
"ConnectionStrings": {
  "DefaultConnection": "Server=YOUR_SERVER;Database=UqebDb;User Id=YOUR_USER;Password=YOUR_PASSWORD;TrustServerCertificate=True;MultipleActiveResultSets=true"
}
```

للتطوير المحلي مع LocalDB:

```json
"DefaultConnection": "Server=(localdb)\\mssqllocaldb;Database=UqebDb;Trusted_Connection=True;MultipleActiveResultSets=true"
```

### تطبيق Migrations

```bash
cd backend/Uqeb.Api
dotnet tool update --global dotnet-ef --version 10.0.9
dotnet ef database update
```

> عند تشغيل التطبيق لأول مرة، يتم تنفيذ Seed تلقائياً (مستخدم admin + إدارات + معاملات تجريبية).

---

## 2. تشغيل Backend

```bash
cd backend/Uqeb.Api
dotnet restore
dotnet build
dotnet run
```

الـ API يعمل على: `http://localhost:5000`

### مستخدمون افتراضيون (Seed)

| المستخدم | كلمة المرور | الدور |
|----------|-------------|-------|
| admin | Admin@123 | Admin |
| supervisor | Super@123 | Supervisor |
| dataentry | Data@123 | DataEntry |
| deptuser | Dept@123 | DepartmentUser |
| reader | Read@123 | Reader |

---

## 3. تشغيل Frontend

```bash
cd frontend/uqeb-ui
npm install
npm run dev
```

الواجهة تعمل على: `http://localhost:5173` (مع proxy للـ API)

### بناء الإنتاج

```bash
npm run build
```

الملفات في `frontend/uqeb-ui/dist/`

---

## 4. أوامر التحقق

```bash
# Backend
cd backend/Uqeb.Api
dotnet build          # ✅ نجح
dotnet ef migrations list

# Frontend
cd frontend/uqeb-ui
npm install
npm run build         # ✅ نجح
```

---

## 5. API Endpoints

| Method | Endpoint | الوصف |
|--------|----------|-------|
| POST | `/api/auth/login` | تسجيل الدخول |
| GET | `/api/transactions` | بحث المعاملات |
| GET/POST/PUT | `/api/transactions/{id}` | CRUD |
| POST | `/api/transactions/{id}/followups` | إضافة تعقيب |
| POST | `/api/transactions/{id}/assignments` | إضافة تحويل |
| POST | `/api/transactions/{id}/assignments/{aid}/reply` | تسجيل رد |
| POST | `/api/transactions/{id}/attachments` | رفع مرفق |
| GET | `/api/transactions/{id}/audit-log` | سجل التدقيق |
| POST | `/api/transactions/{id}/close` | إغلاق (Admin/Supervisor) |
| GET | `/api/reports/dashboard` | لوحة المتابعة |
| GET | `/api/reports/overdue` | المتأخرة |
| GET | `/api/reports/export/{type}` | تصدير Excel |
| GET/POST/PUT | `/api/departments` | الإدارات |
| GET/POST/PUT | `/api/external-parties` | الجهات الخارجية |
| GET/POST/PUT | `/api/users` | المستخدمون (Admin) |

---

## 6. النشر على الإنتاج (Windows — الطريقة المفضلة)

للنشر **دون GitHub** من جهاز التطوير إلى إنتاج offline:

📄 **[docs/simple_offline_deployment.md](docs/simple_offline_deployment.md)**

```powershell
# جهاز التطوير
.\scripts\build-production-package.ps1

# جهاز الإنتاج (بعد نقل ZIP + SHA256 إلى C:\Uqeb\incoming)
C:\UqebTools\install-production-package.ps1 -PackagePath C:\Uqeb\incoming\Uqeb-<version>.zip
```

إعداد أولي: `.\scripts\setup-production-tools.ps1` على جهاز الإنتاج.

> **لا يمكن تنفيذ نشر إنتاج أو migrations دون نسخة قاعدة بيانات مكتملة ومتحقق منها. لا يوجد خيار تجاوز لهذه الخطوة.**

قبل أي migrations، يُنشأ تلقائيًا نسخ احتياطي كامل في `C:\Uqeb\backup\db\` مع `WITH CHECKSUM` و`RESTORE VERIFYONLY`.

---

## 7. النشر على IIS (بديل)

### Backend (API)

1. انشر المشروع:
   ```bash
   dotnet publish -c Release -o ./publish
   ```

2. في IIS أنشئ **Application Pool** (.NET CLR: No Managed Code)

3. أنشئ **Website** أو **Application** يشير إلى مجلد `publish`

4. ثبّت [ASP.NET Core Hosting Bundle 10.0](https://dotnet.microsoft.com/download/dotnet/10.0)

5. عدّل `appsettings.json` بـ connection string الإنتاج و JWT Key قوي

6. أنشئ مجلد `Attachments` بصلاحيات كتابة لـ IIS App Pool

### Frontend

1. انسخ محتويات `frontend/uqeb-ui/dist/` إلى مجلد IIS (مثلاً `C:\inetpub\uqeb`)

2. أضف `web.config` لـ SPA routing:

```xml
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <system.webServer>
    <rewrite>
      <rules>
        <rule name="SPA" stopProcessing="true">
          <match url=".*" />
          <conditions logicalGrouping="MatchAll">
            <add input="{REQUEST_FILENAME}" matchType="IsFile" negate="true" />
          </conditions>
          <action type="Rewrite" url="/index.html" />
        </rule>
      </rules>
    </rewrite>
  </system.webServer>
</configuration>
```

3. عدّل `vite.config.ts` proxy أو استخدم reverse proxy في IIS لتوجيه `/api` إلى Backend

---

## 8. قواعد العمل المطبّقة

- رقم الوارد فريد (لا يتكرر)
- لا حذف نهائي — الإلغاء/الأرشفة فقط
- لا إغلاق مع تحويلات بانتظار رد
- ResponseCompleted يتطلب ResponseCompletedDate
- DepartmentUser يرى معاملات إدارته فقط
- DataEntry لا يغلق المعاملات
- كل تعديل يُسجّل في AuditLog
- التأخير يُحسب تلقائياً عند تجاوز DueDate
- **بيانات الصادر** غير إلزامية عند الإنشاء، لكن عند البدء بتعبئتها يجب إكمال رقم الصادر وتاريخه والإدارة الصادر لها
- **نوع الإفادة** يحدد `RequiresResponse` تلقائياً في Backend (الافتراضي: إفادة للجهة)
- **التعقيبات للتوثيق والمتابعة فقط** — أما إثبات الرد الرسمي للإدارة فيتم من **التحويلات (Assignments)** فقط

---

## 9. المرحلة الثانية — سير العمل والتقارير

### Migration

```bash
cd backend/Uqeb.Api
dotnet ef migrations add AddWorkflowUiReportingEnhancements
dotnet ef database update
```

### ما أُضيف

- **جهات صادر لها متعددة** عبر جدول `TransactionOutgoingParties`
- **تصنيفات** عبر جدول `Categories` وربط `CategoryId` بالمعاملة
- **مستلمو التعقيب** عبر جدول `FollowUpRecipients`
- **مهلة الإفادة** `ResponseDueDays` / `ResponseDueDate` مع حساب تلقائي
- **مهلة رد التحويل** `ReplyDueDays` مع حساب `DueDate`
- **منع الإغلاق** في Backend عند وجود إدارات لم ترد أو إفادة غير مكتملة
- **فلاتر متقدمة** للمعاملات والتقارير
- **لوحة متابعة** و**تقارير موسّعة**

### التاريخ الهجري

- التخزين في قاعدة البيانات يبقى **ميلادياً** (DateTime) فقط.
- الواجهة تعرض التاريخ **ميلادي + هجري** (تقويم أم القرى) للعرض.
- **إدخال التاريخ الهجري** غير مفعّل حالياً — مرحلة لاحقة بعد اعتماد مكوّن إدخال موثوق (مثل `@hijri-date-picker` مع تحويل أم القرى قبل الإرسال للـ API).

### API Endpoints الجديدة

| Method | Endpoint |
|--------|----------|
| GET/POST/PUT | `/api/categories` |
| GET | `/api/dashboard/summary` |
| GET | `/api/reports/response-required` |
| GET | `/api/reports/overdue-responses` |
| GET | `/api/reports/pending-assignments` |
| GET | `/api/reports/partial-replies` |
| GET | `/api/reports/by-category` |
| GET | `/api/reports/by-outgoing-party` |

فلاتر `GET /api/transactions`: `requiresResponse`, `responseCompleted`, `responseOverdue`, `hasPendingAssignments`, `hasPartialReplies`, `categoryId`, `outgoingPartyId`, `departmentId`, `dateFrom`, `dateTo`, `responseDueDateFrom`, `responseDueDateTo`

---

## 10. حالة التنفيذ

- [x] المرحلة 1: بنية المشروع + نماذج + DbContext + Migrations
- [x] المرحلة 2: JWT + Roles + Seed
- [x] المرحلة 3: CRUD المعاملات + AuditLog
- [x] المرحلة 4: FollowUps + Assignments + منع الإغلاق
- [x] المرحلة 5: Attachments + Reports + Excel Export
- [x] المرحلة 6: واجهة React RTL
- [x] المرحلة 7: Build + README
- [x] المرحلة 8: سير العمل + فلاتر + Dashboard + Reports + Migration `AddWorkflowUiReportingEnhancements`

### ملاحظة بخصوص قاعدة البيانات

إذا لم يكن SQL Server/LocalDB مثبتاً على الجهاز، `dotnet ef database update` سيفشل. ثبّت SQL Server Express أو LocalDB ثم نفّذ الأمر أعلاه.

تصدير Excel و PDF متوفر لتقرير الوارد والمغلق لكل إدارة.

---

## 11. التقارير المؤسسية (معطّلة افتراضيًا)

ميزة **منشئ التقارير المؤسسية** موجودة خلف Feature Flag ولا تُفعَّل في الإنتاج بعد:

```json
"FeatureFlags": { "InstitutionalReports": false }
```

### التشغيل المحلي

Backend (`appsettings.Development.json`):

```json
"FeatureFlags": { "InstitutionalReports": true },
"Reporting": {
  "MaxPreviewDetailRows": 500,
  "MaxPdfDetailRows": 5000,
  "MaxPdfDetailRowsPerPart": 5000
}
```

Frontend (`.env.local`):

```bash
VITE_ENABLE_INSTITUTIONAL_REPORTS=true
```

### الاختبارات والمعاينة البصرية

```bash
dotnet test backend/Uqeb.Api.Tests/Uqeb.Api.Tests.csproj --filter "FullyQualifiedName!~InstitutionalReportVisualRegressionTests&FullyQualifiedName!~InstitutionalReportPlaywrightPdfExporterTests"

# Playwright (Ubuntu CI أو بعد تثبيت Chromium محليًا)
REQUIRE_PLAYWRIGHT_TESTS=1 dotnet test backend/Uqeb.Api.Tests/Uqeb.Api.Tests.csproj --filter "FullyQualifiedName~InstitutionalReportVisual"
```

تثبيت Chromium (أول مرة، Windows/Linux):

```powershell
pwsh backend/Uqeb.Api/bin/Debug/net10.0/playwright.ps1 install --with-deps chromium
```

### KPI مقابل التفاصيل

- **KPI والملخص** يُحسبان دائمًا من كامل النتائج المطابقة للفلاتر.
- **صفوف التفاصيل** في المعاينة/PDF/DOCX قد تُقتطع بحدود قابلة للإعداد مع إعلان صريح في الـ Manifest.
- **XLSX** هو الخيار الموصى به للبيانات التفصيلية الكبيرة؛ PDF يمكن تقسيمه إلى أجزاء.

بوابة القبول الكاملة: [`docs/institutional_reporting_visual_and_scale_acceptance_gate.md`](docs/institutional_reporting_visual_and_scale_acceptance_gate.md)

---

## 12. اختبارات التحمل (k6)

راجع `performance-tests/README.md` للتفاصيل.

```bash
# smoke test (ابدأ بهذا)
k6 run performance-tests/uqeb-load-test.js

# load test
K6_SCENARIO=load k6 run performance-tests/uqeb-load-test.js
```
