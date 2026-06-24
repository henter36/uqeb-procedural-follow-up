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

> **مرجع التشغيل المعتمد:** استخدم أوامر نوع الطرفية نفسها من قسم [دليل بيئة التطوير والتشغيل المعتمد](#13-دليل-بيئة-التطوير-والتشغيل-المعتمد). لا تستخدم أوامر CMD داخل PowerShell، ولا أوامر PowerShell داخل CMD.

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

للتطوير المحلي على Windows باستخدام SQL Server المثبت على `localhost`:

```json
"ConnectionStrings": {
  "DefaultConnection": "Server=localhost;Database=UqebDb;Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=true"
}
```

### تطبيق Migrations

نفّذ الأوامر من مجلد `backend/Uqeb.Api` باستخدام صيغة الطرفية المناسبة كما هو موضح في القسم 13:

```text
dotnet tool update --global dotnet-ef --version 10.0.9
dotnet ef migrations list
dotnet ef database update
```

> عند تشغيل التطبيق لأول مرة، يتم تنفيذ Seed تلقائياً (مستخدم admin + إدارات + معاملات تجريبية).

---

## 2. تشغيل Backend

استخدم قسم التشغيل المطابق للطرفية:

- Windows PowerShell: [تشغيل المشروع على Windows باستخدام PowerShell](#تشغيل-المشروع-على-windows-باستخدام-powershell)
- Windows CMD: [تشغيل المشروع على Windows باستخدام CMD](#تشغيل-المشروع-على-windows-باستخدام-cmd)
- macOS Terminal: [تشغيل المشروع على macOS](#تشغيل-المشروع-على-macos)

الـ API يعمل افتراضيًا على:

- Windows: `http://localhost:5000`
- macOS: `http://localhost:5080`

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

استخدم قسم التشغيل المطابق للطرفية في القسم 13. بعد تغيير الفرع أو تحديث `package-lock.json` استخدم دائمًا:

```text
npm ci
```

ثم شغّل الواجهة عبر الأمر المناسب للطرفية. الواجهة تعمل على:

```text
http://localhost:5173
```

### بناء الإنتاج

```text
npm run build
```

الملفات في `frontend/uqeb-ui/dist/`.

---

## 4. أوامر التحقق

نفّذ من جذر المشروع أو من المسارات الموضحة في القسم 13:

```text
Backend:
  dotnet build backend/Uqeb.Api/Uqeb.Api.csproj
  dotnet test backend/Uqeb.Api.Tests/Uqeb.Api.Tests.csproj --no-restore
  dotnet ef migrations list --project backend/Uqeb.Api/Uqeb.Api.csproj

Frontend:
  cd frontend/uqeb-ui
  npm ci
  npm run build
  npm test -- --run --maxWorkers=2
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
# macOS/Linux:
REQUIRE_PLAYWRIGHT_TESTS=1 dotnet test backend/Uqeb.Api.Tests/Uqeb.Api.Tests.csproj --filter "FullyQualifiedName~InstitutionalReportVisualRegressionTests|FullyQualifiedName~InstitutionalReportPlaywrightPdfExporterTests|FullyQualifiedName~InstitutionalReportPreviewPdfParityTests"

# PowerShell (Windows):
# $env:REQUIRE_PLAYWRIGHT_TESTS = "1"
# dotnet test backend/Uqeb.Api.Tests/Uqeb.Api.Tests.csproj --filter "FullyQualifiedName~InstitutionalReportVisualRegressionTests|FullyQualifiedName~InstitutionalReportPlaywrightPdfExporterTests|FullyQualifiedName~InstitutionalReportPreviewPdfParityTests"
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

---

## 13. دليل بيئة التطوير والتشغيل المعتمد

هذا القسم هو المرجع الأساسي لإعداد المشروع وتشغيله على Windows وmacOS. استخدم الأوامر الخاصة بنوع الطرفية التي تعمل عليها، ولا تخلط بين صيغة PowerShell وصيغة CMD.

### تمييز نوع الطرفية قبل التنفيذ

| الطرفية | شكل المؤشر المعتاد | تغيير المجلد | تعيين متغير البيئة |
|---|---|---|---|
| PowerShell | `PS C:\...>` | `Set-Location "C:\path"` | `$env:NAME = "value"` |
| CMD | `C:\...>` | `cd /d C:\path` | `set NAME=value` |
| macOS Terminal | `$` أو `%` | `cd /path` | `export NAME=value` |

> عندما يبدأ المؤشر بـ `PS` فأنت داخل PowerShell. لا تستخدم `cd /d` أو `set NAME=value` في هذه الحالة.

### القيم المعتمدة

| العنصر | القيمة |
|---|---|
| قاعدة التطوير | `UqebDb` |
| SQL Server على Windows | `localhost` |
| Backend على Windows | `http://localhost:5000` |
| Backend على macOS | `http://localhost:5080` |
| Frontend | `http://localhost:5173` |
| مسار مقترح على Windows | `C:\Users\<USER>\uqeb` |
| مسار مقترح على macOS | `~/workspace/uqeb` |

### التحقق من مسار المستودع

#### Windows PowerShell

```powershell
Set-Location "C:\Users\<USER>\uqeb"

if (-not (Test-Path ".git")) {
    throw "المسار الحالي ليس مستودع Git."
}

git status
git branch --show-current
git rev-parse --short HEAD
```

#### Windows CMD

```cmd
cd /d C:\Users\<USER>\uqeb
if not exist .git\NUL (
  echo المسار الحالي ليس مستودع Git.
  exit /b 1
)

git status
git branch --show-current
git rev-parse --short HEAD
```

#### macOS Terminal

```bash
cd ~/workspace/uqeb

test -d .git || {
  echo "المسار الحالي ليس مستودع Git."
  exit 1
}

git status
git branch --show-current
git rev-parse --short HEAD
```

### تحديث المشروع والانتقال إلى فرع العمل

انتقل إلى الفرع المطلوب أولًا، ثم نفّذ `git pull --ff-only`.

#### Windows PowerShell

```powershell
Set-Location "C:\Users\<USER>\uqeb"
$Branch = "اسم-الفرع"

if (git status --porcelain) {
    git stash push --include-untracked -m "before-switch-$(Get-Date -Format 'yyyyMMdd-HHmmss')"
}

git fetch origin --prune
git show-ref --verify --quiet "refs/heads/$Branch"
$BranchExists = $LASTEXITCODE

if ($BranchExists -eq 0) {
    git switch $Branch
}
else {
    git switch --track -c $Branch "origin/$Branch"
}

git pull --ff-only origin $Branch
```

#### Windows CMD

```cmd
cd /d C:\Users\<USER>\uqeb
set BRANCH=اسم-الفرع

git status --porcelain > "%TEMP%\uqeb-status.txt"
for %A in ("%TEMP%\uqeb-status.txt") do if %~zA GTR 0 git stash push --include-untracked -m "before-switch"
del "%TEMP%\uqeb-status.txt" 2>nul

git fetch origin --prune
git show-ref --verify --quiet refs/heads/%BRANCH%
if errorlevel 1 (
  git switch --track -c %BRANCH% origin/%BRANCH%
) else (
  git switch %BRANCH%
)

git pull --ff-only origin %BRANCH%
```

> الصيغة `%A` أعلاه مخصصة للنسخ المباشر داخل نافذة CMD. داخل ملف `.bat` استخدم `%%A`.

#### macOS Terminal

```bash
cd ~/workspace/uqeb
BRANCH="اسم-الفرع"

if [[ -n "$(git status --porcelain)" ]]; then
  git stash push --include-untracked -m "before-switch-$(date +%Y%m%d-%H%M%S)"
fi

git fetch origin --prune

if git show-ref --verify --quiet "refs/heads/$BRANCH"; then
  git switch "$BRANCH"
else
  git switch --track -c "$BRANCH" "origin/$BRANCH"
fi

git pull --ff-only origin "$BRANCH"
```

### إعداد قاعدة التطوير على Windows

الإعداد المحلي المعتمد:

```json
"ConnectionStrings": {
  "DefaultConnection": "Server=localhost;Database=UqebDb;Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=true"
}
```

التحقق:

```cmd
sqlcmd -S localhost -E -C -Q "SELECT name FROM sys.databases ORDER BY name;"
sqlcmd -S localhost -d UqebDb -E -C -Q "SELECT [MigrationId], [ProductVersion] FROM dbo.__EFMigrationsHistory ORDER BY [MigrationId];"
```

### تشغيل المشروع على Windows باستخدام CMD

استخدم هذا القسم فقط عندما يكون المؤشر مثل `C:\...>` وليس `PS C:\...>`، وافتح نافذتي CMD منفصلتين.

#### النافذة الأولى: Backend

```cmd
cd /d C:\Users\<USER>\uqeb\backend\Uqeb.Api

set ASPNETCORE_ENVIRONMENT=Development
set ASPNETCORE_URLS=http://localhost:5000
set FeatureFlags__InstitutionalReports=true
set ReportingRollout__EnforcementMode=ObserveOnly
set ReportingRollout__EmergencyDisable=false

dotnet restore
dotnet build
dotnet ef migrations list
dotnet ef database update
dotnet run
```

#### النافذة الثانية: Frontend

```cmd
cd /d C:\Users\<USER>\uqeb\frontend\uqeb-ui

set NODE_OPTIONS=--max-old-space-size=4096

npm ci
npm run build
npm test -- --run --maxWorkers=2
npm run dev
```

#### التشغيل السريع بعد اكتمال الإعداد

Backend:

```cmd
cd /d C:\Users\<USER>\uqeb\backend\Uqeb.Api
set ASPNETCORE_ENVIRONMENT=Development
set ASPNETCORE_URLS=http://localhost:5000
set FeatureFlags__InstitutionalReports=true
set ReportingRollout__EnforcementMode=ObserveOnly
set ReportingRollout__EmergencyDisable=false
dotnet run
```

Frontend:

```cmd
cd /d C:\Users\<USER>\uqeb\frontend\uqeb-ui
set NODE_OPTIONS=--max-old-space-size=4096
npm run dev
```

التحقق:

```cmd
curl -i http://localhost:5000/health/live
curl -i http://localhost:5000/health/ready
```

### تشغيل المشروع على Windows باستخدام PowerShell

استخدم هذا القسم عندما يبدأ المؤشر بـ `PS`، وافتح نافذتي PowerShell منفصلتين.

#### النافذة الأولى: Backend

```powershell
Set-Location "C:\Users\<USER>\uqeb\backend\Uqeb.Api"

$env:ASPNETCORE_ENVIRONMENT = "Development"
$env:ASPNETCORE_URLS = "http://localhost:5000"
$env:FeatureFlags__InstitutionalReports = "true"
$env:ReportingRollout__EnforcementMode = "ObserveOnly"
$env:ReportingRollout__EmergencyDisable = "false"

dotnet restore
dotnet build
dotnet ef migrations list
dotnet ef database update
dotnet run
```

#### النافذة الثانية: Frontend

```powershell
Set-Location "C:\Users\<USER>\uqeb\frontend\uqeb-ui"

$env:NODE_OPTIONS = "--max-old-space-size=4096"

npm ci
npm run build
npm test -- --run --maxWorkers=2
npm run dev
```

#### التشغيل السريع بعد اكتمال الإعداد

Backend:

```powershell
Set-Location "C:\Users\<USER>\uqeb\backend\Uqeb.Api"

$env:ASPNETCORE_ENVIRONMENT = "Development"
$env:ASPNETCORE_URLS = "http://localhost:5000"
$env:FeatureFlags__InstitutionalReports = "true"
$env:ReportingRollout__EnforcementMode = "ObserveOnly"
$env:ReportingRollout__EmergencyDisable = "false"

dotnet run
```

Frontend:

```powershell
Set-Location "C:\Users\<USER>\uqeb\frontend\uqeb-ui"
$env:NODE_OPTIONS = "--max-old-space-size=4096"
npm run dev
```

التحقق:

```powershell
curl.exe -i http://localhost:5000/health/live
curl.exe -i http://localhost:5000/health/ready
```

### تشغيل المشروع على macOS

نافذة Backend:

```bash
cd ~/workspace/uqeb/backend/Uqeb.Api

export ASPNETCORE_ENVIRONMENT=Development
export ASPNETCORE_URLS=http://localhost:5080
export FeatureFlags__InstitutionalReports=true
export ReportingRollout__EnforcementMode=ObserveOnly
export ReportingRollout__EmergencyDisable=false

dotnet restore
dotnet build
dotnet ef migrations list
dotnet ef database update
dotnet run
```

أنشئ `frontend/uqeb-ui/.env.local`:

```env
VITE_API_BASE_URL=http://localhost:5080/api
VITE_ENABLE_INSTITUTIONAL_REPORTS=true
```

نافذة Frontend:

```bash
cd ~/workspace/uqeb/frontend/uqeb-ui

rm -rf node_modules dist
export NODE_OPTIONS=--max-old-space-size=4096

npm ci
npm run build
npm test -- --run --maxWorkers=2
npm run dev
```

التحقق:

```bash
curl -i http://localhost:5080/health/live
curl -i http://localhost:5080/health/ready
```

### قواعد Entity Framework Core migrations

قبل إنشاء migration:

```bash
git status --short
dotnet build
dotnet ef migrations list
```

المفاتيح الرقمية ذات المعنى المنطقي، مثل السنة، يجب تعريفها صراحةً دون توليد تلقائي:

```csharp
modelBuilder.Entity<ReportNumberSequence>(entity =>
{
    entity.HasKey(x => x.Year);

    entity.Property(x => x.Year)
        .ValueGeneratedNever();

    entity.Property(x => x.LastNumber)
        .IsRequired();
});
```

بعد إنشاء migration، راجعها وولّد SQL قبل التطبيق:

```bash
dotnet ef migrations add MigrationName
dotnet ef migrations script LastAppliedMigration MigrationName --output migration-review.sql
dotnet ef migrations has-pending-model-changes
```

لا تطبق migration تحتوي حذفًا أو تغييرًا غير مقصود. لا يتم تعطيل `PendingModelChangesWarning`.

إذا كان schema الفعلي صحيحًا وكان المطلوب مزامنة metadata فقط، يمكن أن تكون migration بلا عمليات schema، ويجب أن ينتج عنها تسجيل فقط في `__EFMigrationsHistory`.

### التحقق من ReportNumberSequences على Windows

```cmd
sqlcmd -S localhost -d UqebDb -E -C -Q "SELECT c.name AS ColumnName, c.is_identity AS IsIdentity FROM sys.columns c JOIN sys.tables t ON c.object_id = t.object_id WHERE t.name = 'ReportNumberSequences' AND c.name = 'Year';"
```

النتيجة المطلوبة:

```text
Year    0
```

### تثبيت Chromium لتصدير PDF

Windows PowerShell أو CMD بعد بناء Backend:

```cmd
pwsh backend\Uqeb.Api\bin\Debug\net10.0\playwright.ps1 install chromium
```

macOS:

```bash
pwsh ./backend/Uqeb.Api/bin/Debug/net10.0/playwright.ps1 install chromium
```

### بوابة التحقق قبل commit

من جذر المشروع:

```bash
dotnet restore backend/Uqeb.Api/Uqeb.Api.csproj
dotnet build backend/Uqeb.Api/Uqeb.Api.csproj
dotnet test backend/Uqeb.Api.Tests/Uqeb.Api.Tests.csproj --no-restore

cd frontend/uqeb-ui
npm ci
npm run lint
npm run lint:css
npm run build
npm test -- --run --maxWorkers=2
cd ../..

git diff --check
git status --short
git diff --stat
```

### قواعد commit وpush

```bash
git add <files>
git diff --cached --check
git commit -m "type(scope): description"
git push origin HEAD:<remote-branch>
```

لا يتم رفع `.env.local` أو كلمات المرور أو JWT secrets الحقيقية أو connection strings إنتاجية أو مجلدات `bin`, `obj`, `dist`, `node_modules`.

### ترتيب العمل القياسي

1. التأكد من مسار المستودع.
2. حفظ التغييرات المحلية.
3. تنفيذ `git fetch`.
4. الانتقال إلى الفرع المطلوب.
5. تنفيذ `git pull --ff-only`.
6. تشغيل `dotnet restore` و`npm ci`.
7. مراجعة connection string وقاعدة `UqebDb`.
8. مراجعة migrations وتوليد SQL.
9. تطبيق migrations.
10. بناء واختبار Backend.
11. بناء واختبار Frontend.
12. اختبار التشغيل المحلي.
13. تنفيذ `git diff --check`.
14. إنشاء commit.
15. رفع التغييرات إلى فرع Pull Request.
16. مراجعة CI وSonarCloud قبل الدمج.
