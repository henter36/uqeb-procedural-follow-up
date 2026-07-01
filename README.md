# المتابعة الإجرائية (Uqeb)

نظام ويب داخلي لإدارة الوارد والصادر والتعقيبات والاحالات والإفادات داخل بيئة عمل مغلقة أو شبكة محلية. الاسم الظاهر للمستخدم هو **المتابعة الإجرائية**، بينما يبقى الاسم التقني **Uqeb** مستخدمًا في الكود، namespaces، الحزم، وقاعدة البيانات.

هذا README مرجع عملي للمطور والمراجع ومسؤول النشر. التفاصيل التشغيلية الدقيقة، خصوصًا الإنتاج، تبقى في ملفات `docs/` المتخصصة.

## نظرة عامة

يدعم النظام:

- إدارة المعاملات الواردة والصادرة، الاحالةات، التعقيبات، المرفقات، وسجل التدقيق.
- أدوار وصلاحيات مثل `Admin` و`Supervisor` و`DataEntry` و`DepartmentUser` و`Reader`.
- إفادات الإدارات مع مسار تقديم ومراجعة ومرفقات.
- لوحات متابعة وتقارير تشغيلية أساسية.
- منشئ تقارير مؤسسية خلف feature flags وrollout settings.
- طباعة/معاينة خطابات التعقيب، قوالب الخطابات، ومهام الطباعة.
- تصدير تقارير مؤسسية بصيغ HTML وPDF وDOCX وXLSX حسب مسار التقرير.
- Scanner Bridge محلي اختياري على Windows لمسح المرفقات عبر WIA.

## التقنيات

| الطبقة | الواقع الحالي في المستودع |
|---|---|
| Backend | ASP.NET Core Web API |
| .NET SDK | `10.0.301` من `global.json` مع `rollForward: latestPatch` |
| Target Framework | `net10.0` في `backend/Uqeb.Api/Uqeb.Api.csproj` |
| Database | SQL Server عبر EF Core SQL Server |
| ORM | Entity Framework Core 10 |
| Auth | JWT Bearer + سياسات Authorization داخل `backend/Uqeb.Api/Authorization` |
| Frontend | React 19 + Vite 8 + TypeScript 6 |
| Node | `24.16.0` من `.nvmrc` |
| npm | `>=11 <12` من `frontend/uqeb-ui/package.json` |
| XLSX | ClosedXML |
| DOCX | DocumentFormat.OpenXml + System.IO.Packaging |
| PDF | Microsoft.Playwright/Chromium لمسار التقارير المؤسسية؛ QuestPDF موجود ضمن dependencies ويستخدم في مسارات PDF أخرى |
| HTML | rendering داخلي للتقارير وخطابات التعقيب |
| اختبارات الواجهة | Vitest + Testing Library + jsdom |
| CI | GitHub Actions في `.github/workflows` |

## بنية المشروع

| المسار | الغرض |
|---|---|
| `.github/workflows/` | CI، فحوص التقارير، وبوابة حزمة النشر |
| `backend/` | حل .NET الرئيسي `backend/Uqeb.sln` ومشاريع API والاختبارات وأدوات مساعدة |
| `backend/Uqeb.Api/` | Web API، Controllers، EF Core، Services، Reporting، Migrations |
| `backend/Uqeb.Api.Tests/` | اختبارات backend والتقارير وPlaywright/SQL Server عند تفعيلها |
| `frontend/uqeb-ui/` | تطبيق React/Vite RTL |
| `docs/` | وثائق النشر، التقارير المؤسسية، التشغيل، والجسور المحلية |
| `scripts/` | سكربتات البناء، النشر، التحقق، migrations، وPester tests |
| `performance-tests/` | اختبارات k6 لتدفق API والحمل |
| `tests/performance/` | اختبارات أداء/قراءة إضافية مبنية على Node |
| `scanner-bridge/` | خدمة محلية Windows-only للماسح الضوئي عبر WIA أو Mock |
| `artifacts/` | مخرجات بناء/اختبارات محلية أو عينات أداء؛ ليست مصدر حقيقة للكود |

## المتطلبات

### التطوير المحلي

- Git.
- .NET SDK `10.0.301` أو patch أعلى ضمن .NET 10.
- Node.js `24.16.0` وnpm 11.
- SQL Server 2019+ أو SQL Server Developer/Express مناسب للتطوير.
- PowerShell عند تشغيل سكربتات النشر أو فحوص Windows.

### تشغيل Frontend

- Node 24 وnpm 11.
- `npm ci` من `frontend/uqeb-ui`.
- `VITE_API_BASE_URL` يحدد عنوان API. إن لم يحدد، يستخدم client القيمة الافتراضية `/api`.
- في التطوير، `vite.config.ts` يوجه `/api` إلى `http://localhost:5000`.

### SQL Server

- connection string يقرأ من `ConnectionStrings:DefaultConnection`.
- `backend/Uqeb.Api/appsettings.example.json` يحتوي placeholder فقط ولا يحتوي أسرار إنتاج.
- migrations موجودة في `backend/Uqeb.Api/Migrations`.

### تصدير PDF

- مسارات PDF التي تعتمد Playwright تحتاج Chromium متوافقًا.
- CI يثبت Chromium عبر `playwright.ps1 install --with-deps chromium` في jobs الخاصة بالتقارير.
- في الإنتاج، الحزمة الرسمية تتضمن `browsers/` و`api/playwright.ps1`. لا تشغل `playwright install` يدويًا على الإنتاج عند استخدام الحزمة الرسمية.

### الإنتاج Windows/offline

- الإنتاج لا يحتاج Git أو .NET SDK إذا استُخدمت الحزمة الرسمية.
- المسار المعتمد يبني ZIP وSHA256 على جهاز البناء ثم يرقّي نفس artifact إلى الإنتاج.
- PowerShell 5.1+ مطلوب على الإنتاج.
- SQL Server مطلوب.
- Scheduled Task باسم `UqebApi` موثق في أدلة النشر.

### IIS

IIS مستخدم لمسار الواجهة المنشورة في وثائق الإنتاج الحالية. تفاصيل IIS ليست بديلًا عن أدلة `docs/production*.md`.

## إعداد قاعدة البيانات محليًا

مثال قاعدة تطوير:

```sql
CREATE DATABASE UqebDb;
```

انسخ إعدادات المثال أو استخدم user-secrets/ملف تطوير محلي:

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=localhost;Database=UqebDb;Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=true"
  },
  "Jwt": {
    "Key": "ضع-قيمة-تطوير-بطول-32-حرفا-على-الأقل",
    "Issuer": "UqebApi",
    "Audience": "UqebClient",
    "ExpireMinutes": 480
  }
}
```

لا تضع أسرار إنتاج في README أو في ملفات committed.

## التشغيل المحلي

المسارات المقترحة:

| النظام | مسار المستودع |
|---|---|
| Windows | `C:\Users\<USER>\uqeb` |
| macOS | `~/workspace/uqeb` |

### Windows PowerShell

Backend:

```powershell
Set-Location "C:\Users\<USER>\uqeb"
$env:ASPNETCORE_ENVIRONMENT = "Development"
$env:ASPNETCORE_URLS = "http://localhost:5000"
dotnet run --project backend/Uqeb.Api/Uqeb.Api.csproj
```

Frontend:

```powershell
Set-Location "C:\Users\<USER>\uqeb\frontend\uqeb-ui"
npm ci
$env:VITE_API_BASE_URL = "/api"
npm run dev
```

Health check:

```powershell
Invoke-RestMethod http://localhost:5000/health/live
Invoke-RestMethod http://localhost:5000/health/ready
Invoke-RestMethod http://localhost:5000/health
```

### Windows CMD

Backend:

```cmd
cd /d C:\Users\<USER>\uqeb
set ASPNETCORE_ENVIRONMENT=Development
set ASPNETCORE_URLS=http://localhost:5000
dotnet run --project backend\Uqeb.Api\Uqeb.Api.csproj
```

Frontend:

```cmd
cd /d C:\Users\<USER>\uqeb\frontend\uqeb-ui
npm ci
set VITE_API_BASE_URL=/api
npm run dev
```

Health check:

```cmd
curl http://localhost:5000/health/live
curl http://localhost:5000/health/ready
curl http://localhost:5000/health
```

### macOS

Backend:

```bash
cd ~/workspace/uqeb
export ASPNETCORE_ENVIRONMENT=Development
export ASPNETCORE_URLS=http://localhost:5080
dotnet run --project backend/Uqeb.Api/Uqeb.Api.csproj
```

Frontend:

```bash
cd ~/workspace/uqeb/frontend/uqeb-ui
npm ci
export VITE_API_BASE_URL=http://localhost:5080/api
npm run dev
```

Health check:

```bash
curl http://localhost:5080/health/live
curl http://localhost:5080/health/ready
curl http://localhost:5080/health
```

## بوابة التحقق المحلية

### Backend

```bash
dotnet restore backend/Uqeb.sln
dotnet build backend/Uqeb.sln
dotnet test backend/Uqeb.Api.Tests/Uqeb.Api.Tests.csproj
dotnet ef migrations list --project backend/Uqeb.Api/Uqeb.Api.csproj
```

### Frontend

```bash
cd frontend/uqeb-ui
npm ci
npm run lint
npm run lint:css
npm run build
npm test -- --run --maxWorkers=2
```

### Git

```bash
git diff --check
git status --short
```

### اختبارات خاصة

- اختبارات Playwright/PDF تحتاج Chromium وبيئة مناسبة.
- `reporting-acceptance-large` في CI يعمل فقط عبر `workflow_dispatch`.
- اختبارات SQL Server ذات التصنيف `Category=SqlServer` تحتاج SQL Server متاحًا ومتغيرات البيئة المناسبة كما في `.github/workflows/ci.yml`.

## المستخدمون الافتراضيون

المستخدمون التاليون موجودون في `DefaultUsersProvisioner`، ولا تُنشأ إلا عند تفعيل `DatabaseStartup:RunDefaultUsersSeedOnStartup` أو استدعاء provisioning مكافئ:

| المستخدم | كلمة المرور | الدور | ملاحظة |
|---|---|---|---|
| `admin` | `Admin@123` | `Admin` | مدير النظام |
| `supervisor` | `Super@123` | `Supervisor` | مشرف المعاملات |
| `dataentry` | `Data@123` | `DataEntry` | مدخل بيانات |
| `deptuser` | `Dept@123` | `DepartmentUser` | يتطلب إدارة بالكود `ADM` |
| `reader` | `Read@123` | `Reader` | قارئ |

هذه بيانات تطوير/اختبار. لا تستخدم كلمات المرور الافتراضية في الإنتاج، ولا تضع أسرار إنتاج في README.

## واجهات المستخدم الفعلية

| المسار | الشاشة | الدور العام من الواجهة |
|---|---|---|
| `/login` | تسجيل الدخول | عام |
| `/` | لوحة المتابعة | مستخدم مسجل |
| `/transactions` | قائمة المعاملات | Admin/Supervisor/DataEntry |
| `/transactions/import` | استيراد Excel | Admin |
| `/transactions/new` | إنشاء معاملة | Admin/Supervisor/DataEntry |
| `/transactions/:id` | تفاصيل المعاملة | مستخدم مسجل مع ضوابط backend |
| `/transactions/:id/edit` | تعديل معاملة | Admin/Supervisor/DataEntry |
| `/reports` | التقارير التشغيلية | Admin/Supervisor/DataEntry |
| `/report-builder` | منشئ التقارير | Admin، ويظهر فقط عند تفعيل `VITE_ENABLE_INSTITUTIONAL_REPORTS` |
| `/letter-template` | قوالب خطاب التعقيب | Admin/Supervisor |
| `/follow-up-print/eligible` | معاملات مستحقة للتعقيب | Admin/Supervisor/DataEntry |
| `/follow-up-print/jobs` | مهام طباعة التعقيب | Admin/Supervisor/DataEntry |
| `/follow-up-print/jobs/:id` | تفاصيل مهمة طباعة | Admin/Supervisor/DataEntry |
| `/follow-up-print/pending` | بانتظار تسجيل التعقيب | Admin/Supervisor/DataEntry |
| `/follow-up-print/parts/:jobId/:partNumber/print` | صفحة طباعة جزء | Admin/Supervisor/DataEntry |
| `/department-responses` | معاملات إدارتي/إفادات الإدارة | Admin/Supervisor/DataEntry/DepartmentUser |
| `/department-responses/review` | مراجعة الإفادات | Admin/Supervisor/DataEntry |
| `/users` | المستخدمون | Admin |
| `/departments` | الإدارات | Admin |
| `/external-parties` | الجهات الخارجية | Admin |
| `/categories` | التصنيفات | Admin |
| `/security` | الأمن والتنبيهات | Admin |

## API Endpoints

هذه قائمة عملية عالية المستوى من Controllers الحالية، وليست توثيق DTO كاملًا.

### Authentication

| Method | Endpoint | الغرض |
|---|---|---|
| POST | `/api/auth/login` | تسجيل الدخول وإرجاع JWT |

### Health

| Method | Endpoint | الغرض |
|---|---|---|
| GET | `/health/live` | فحص حياة سريع |
| GET | `/health/ready` | فحص الجاهزية |
| GET | `/health` | فحص شامل للنشر والتبعيات |

### Transactions

| Method | Endpoint | الغرض |
|---|---|---|
| GET | `/api/transactions` | البحث والقائمة مع فلاتر |
| GET | `/api/transactions/{id}` | تفاصيل معاملة |
| GET | `/api/transactions/{id}/basic` | بيانات أساسية |
| GET | `/api/transactions/{id}/workspace` | بيانات مساحة العمل |
| POST | `/api/transactions` | إنشاء معاملة |
| PUT | `/api/transactions/{id}` | تعديل معاملة |
| POST | `/api/transactions/{id}/cancel` | إلغاء |
| POST | `/api/transactions/{id}/archive` | أرشفة |
| POST | `/api/transactions/{id}/complete-response` | إكمال الإفادة |
| POST | `/api/transactions/{id}/close` | إغلاق |
| GET | `/api/transactions/{id}/audit-log` | سجل التدقيق |
| POST | `/api/transactions/import/excel/preview` | معاينة استيراد Excel |
| POST | `/api/transactions/import/excel/commit` | تنفيذ استيراد Excel |

### Follow-ups

| Method | Endpoint | الغرض |
|---|---|---|
| GET | `/api/transactions/{id}/followups` | تعقيبات المعاملة |
| GET | `/api/transactions/{id}/followup-departments` | إدارات التعقيب |
| POST | `/api/transactions/{id}/followups` | إضافة تعقيب |
| POST | `/api/transactions/{id}/followups/{followUpId}/reply` | رد على تعقيب |

### Assignments

| Method | Endpoint | الغرض |
|---|---|---|
| GET | `/api/transactions/{id}/assignments` | احالةات المعاملة |
| POST | `/api/transactions/{id}/assignments` | إضافة احالة |
| POST | `/api/transactions/{id}/assignments/{assignmentId}/reply` | رد على احالة |

### Attachments

| Method | Endpoint | الغرض |
|---|---|---|
| GET | `/api/transactions/{id}/attachments` | مرفقات المعاملة |
| POST | `/api/transactions/{id}/attachments` | رفع مرفق |
| GET | `/api/transactions/{id}/attachments/{attachmentId}/download` | تنزيل مرفق |

### Department Responses / إفادات الإدارة

| Method | Endpoint | الغرض |
|---|---|---|
| GET | `/api/department-responses/department-transactions` | معاملات الإدارة |
| GET | `/api/department-responses/my` | إفادات المستخدم/الإدارة |
| GET | `/api/department-responses/my-stats` | إحصاءات الإفادات |
| GET | `/api/department-responses/pending-review` | إفادات بانتظار المراجعة |
| GET | `/api/department-responses/{id}` | تفاصيل إفادة |
| POST | `/api/department-responses` | إنشاء إفادة |
| PUT | `/api/department-responses/{id}` | تعديل إفادة |
| POST | `/api/department-responses/{id}/submit` | إرسال للمراجعة |
| POST | `/api/department-responses/{id}/approve` | اعتماد |
| POST | `/api/department-responses/{id}/return` | إرجاع للتعديل |
| POST | `/api/department-responses/{id}/reject` | رفض |
| POST | `/api/department-responses/{id}/attachments` | رفع مرفق إفادة |
| DELETE | `/api/department-responses/{id}/attachments/{attachmentId}` | حذف مرفق إفادة |
| GET | `/api/department-responses/{id}/attachments/{attachmentId}/download` | تنزيل مرفق إفادة |

### Reports

| Method | Endpoint | الغرض |
|---|---|---|
| GET | `/api/dashboard/summary` | ملخص لوحة المتابعة |
| GET | `/api/dashboard/action-required` | عناصر تحتاج إجراء |
| GET | `/api/dashboard/top-overdue-departments` | أعلى الإدارات تأخرًا |
| GET | `/api/dashboard/top-incoming-parties` | أعلى الجهات الواردة |
| GET | `/api/dashboard/category-distribution` | توزيع التصنيفات |
| GET | `/api/dashboard/status-distribution` | توزيع الحالات |
| GET | `/api/reports/dashboard` | بيانات لوحة التقارير |
| GET | `/api/reports/page-summary` | ملخص صفحة التقارير |
| GET | `/api/reports/overdue` | المعاملات المتأخرة |
| GET | `/api/reports/open` | المفتوحة |
| GET | `/api/reports/waiting-replies` | بانتظار الرد |
| GET | `/api/reports/pending-response` | إفادات معلقة |
| GET | `/api/reports/response-required` | مطلوب إفادة |
| GET | `/api/reports/overdue-responses` | إفادات متأخرة |
| GET | `/api/reports/pending-assignments` | احالةات معلقة |
| GET | `/api/reports/partial-replies` | ردود جزئية |
| GET | `/api/reports/by-department` | تجميع حسب الإدارة |
| GET | `/api/reports/by-external-party` | تجميع حسب الجهة الخارجية |
| GET | `/api/reports/by-category` | تجميع حسب التصنيف |
| GET | `/api/reports/by-incoming-party` | تجميع حسب جهة واردة |
| GET | `/api/reports/by-outgoing-party` | تجميع حسب جهة صادرة |
| GET | `/api/reports/by-outgoing-department` | تجميع حسب إدارة صادرة |
| GET | `/api/reports/department-summary` | ملخص الإدارات |
| GET | `/api/reports/department-incoming-closed` | وارد/مغلق حسب الإدارة |
| GET | `/api/reports/department-incoming-closed/export-excel` | تصدير Excel |
| GET | `/api/reports/department-incoming-closed/export-pdf` | تصدير PDF |
| GET | `/api/reports/monthly` | تقرير شهري |
| GET | `/api/reports/export/{reportType}` | تصدير تقرير |
| GET | `/api/reports/*/details` | تفاصيل لبعض التقارير التشغيلية |

### Institutional Reports

| Method | Endpoint | الغرض |
|---|---|---|
| POST | `/api/institutional-reports/preview` | بناء Preview |
| POST | `/api/institutional-reports/export` | تصدير HTML/PDF/DOCX/XLSX |
| GET | `/api/institutional-reports/templates` | قوالب محفوظة |
| POST | `/api/institutional-reports/templates` | حفظ قالب |
| DELETE | `/api/institutional-reports/templates/{id}` | حذف قالب |
| GET | `/api/institutional-reports/configuration` | حدود وإعدادات التقارير |
| GET | `/api/institutional-reports/readiness` | جاهزية التقارير/Chromium |
| GET | `/api/institutional-reports/rollout-status` | حالة rollout |

هذه endpoints مقيّدة بـ Admin في controllers الحالية، وتخضع أيضًا لـ feature flag/rollout middleware.

### Letter Templates / Print Jobs

| Method | Endpoint | الغرض |
|---|---|---|
| GET | `/api/letter-templates` | قائمة القوالب |
| GET | `/api/letter-templates/{id}` | قالب محدد |
| POST | `/api/letter-templates` | إنشاء قالب |
| PUT | `/api/letter-templates/{id}` | تعديل قالب |
| POST | `/api/letter-templates/{id}/copy` | نسخ قالب |
| POST | `/api/letter-templates/{id}/set-default` | تعيين افتراضي |
| PATCH | `/api/letter-templates/{id}/activate` | تفعيل |
| PATCH | `/api/letter-templates/{id}/deactivate` | تعطيل |
| PATCH | `/api/letter-templates/reorder` | ترتيب |
| DELETE | `/api/letter-templates/{id}` | حذف |
| GET | `/api/letter-templates/follow-up` | قالب التعقيب الحالي |
| PUT | `/api/letter-templates/follow-up` | تحديث قالب التعقيب |
| GET | `/api/letter-templates/variables` | متغيرات القوالب |
| POST | `/api/letter-templates/validate` | التحقق من قالب |
| POST | `/api/letter-templates/preview` | معاينة قالب |
| POST | `/api/transactions/{id}/follow-up-letter/preview` | معاينة خطاب معاملة |
| POST | `/api/transactions/{id}/follow-up-letter/pdf` | PDF خطاب معاملة |
| GET/POST | `/api/follow-up-print/*` | إدارة المستحقات، مهام الطباعة، أجزاء الطباعة، وسجلات التعقيب |

### Reference/Admin

| Method | Endpoint | الغرض |
|---|---|---|
| GET/POST/PUT | `/api/users` و`/api/users/{id}` | إدارة المستخدمين |
| POST | `/api/users/{id}/reset-password` | إعادة تعيين كلمة المرور |
| GET/POST/PUT | `/api/departments` و`/api/departments/{id}` | الإدارات |
| GET | `/api/departments/lookup` | قائمة lookup |
| GET/POST/PUT | `/api/external-parties` و`/api/external-parties/{id}` | الجهات الخارجية |
| GET | `/api/external-parties/lookup` | قائمة lookup |
| GET/POST/PUT | `/api/categories` و`/api/categories/{id}` | التصنيفات |
| GET | `/api/categories/lookup` | قائمة lookup |

### Security, Notifications, Branding

| Method | Endpoint | الغرض |
|---|---|---|
| GET | `/api/security/login-attempts` | محاولات الدخول |
| GET | `/api/security/alerts` | التنبيهات الأمنية |
| POST | `/api/security/alerts/{id}/read` | تعليم تنبيه كمقروء |
| POST | `/api/security/alerts/mark-all-read` | تعليم جميع التنبيهات |
| GET | `/api/security/audit-integrity-report` | تقرير سلامة التدقيق |
| GET | `/api/notifications` | إشعارات المستخدم |
| POST | `/api/notifications/{id}/read` | تعليم إشعار كمقروء |
| GET | `/api/branding/organization-logo` | شعار الجهة |

### Scanner Bridge

Scanner Bridge خدمة محلية منفصلة، وليست جزءًا من `Uqeb.Api`:

| Method | Endpoint | الغرض |
|---|---|---|
| GET | `http://127.0.0.1:5055/status` | حالة الجسر |
| GET | `http://127.0.0.1:5055/scanners` | الماسحات المتاحة |
| POST | `http://127.0.0.1:5055/scan` | تنفيذ المسح |
| GET | `http://127.0.0.1:5055/scan/{scanId}/file` | ملف المسح |
| DELETE | `http://127.0.0.1:5055/scan/{scanId}` | حذف ملف مؤقت |

راجع `scanner-bridge/Uqeb.ScannerBridge/README.md`.

## التقارير

### التقارير التشغيلية

صفحة `/reports` و`/api/reports/*` تغطي تقارير مثل المفتوحة، المتأخرة، مطلوب إفادة، الاحالات المعلقة، الردود الجزئية، والتجميعات حسب الإدارات والجهات والتصنيفات.

### منشئ التقارير المؤسسية

منشئ التقارير المؤسسية موجود في:

- Backend: `backend/Uqeb.Api/Reporting`
- Frontend: `/report-builder`
- Docs: `docs/institutional_reporting_visual_and_scale_acceptance_gate.md` و`docs/institutional_reporting_analytical_content.md`

التفعيل يتطلب توافق backend وfrontend:

```json
{
  "FeatureFlags": {
    "InstitutionalReports": true
  },
  "ReportingRollout": {
    "EnforcementMode": "ObserveOnly",
    "Percentage": 0
  }
}
```

```bash
VITE_ENABLE_INSTITUTIONAL_REPORTS=true
```

حدود التصدير الافتراضية من `ReportingOptions` و`appsettings.example.json`:

| الإعداد | الافتراضي |
|---|---:|
| `MaxPreviewDetailRows` | 500 |
| `MaxPdfDetailRows` | 5,000 |
| `MaxPdfDetailRowsPerPart` | 5,000 |
| `MaxDocxDetailRows` | 20,000 |
| `MaxXlsxDetailRows` | 100,000 |
| `MaxHtmlDetailRows` | 20,000 |
| `MaxPdfParts` | 20 |
| `MaxExportFileSizeMb` | 100 |
| `MaxExportDurationSeconds` | 120 |

Preview يبني manifest وصفحات HTML للعرض. Export يستخدم نفس نموذج التقرير لصيغ HTML/PDF/DOCX/XLSX. KPI والمؤشرات تُحسب من كامل النتائج المطابقة، بينما صفوف التفاصيل قد تخضع لحدود معلنة في manifest. XLSX هو الخيار الأنسب للتفاصيل الكبيرة وفق الحدود الحالية. PDF يتطلب Chromium.

## الطباعة والخطابات

النظام يدعم:

- إدارة قوالب خطاب التعقيب من `/letter-template`.
- معاينة قالب الخطاب والتحقق منه عبر `/api/letter-templates/preview` و`/validate`.
- معاينة/PDF خطاب معاملة عبر endpoints تحت `/api/transactions/{id}/follow-up-letter/*`.
- مهام طباعة التعقيب تحت `/follow-up-print/*` و`/api/follow-up-print/*`.
- إعدادات مثل المدة الافتراضية، حجم الدفعة، حدود المحتوى، ومدة صلاحية المهام تحت `FollowUpLetters` في `appsettings.example.json`.
- قيم رسمية مثل التوقيع/المنصب/الرتبة/الإدارة ضمن نموذج القوالب الحالي، وليست موثقة في README كسياسة إنتاج.

PDF يحتاج Chromium عند استخدام مسارات rendering المعتمدة عليه. HTML print views لا تحتاج تنزيل متصفح على جهاز العميل.

## النشر على الإنتاج

المسار المفضل هو **build once / promote same artifact**:

1. بناء ZIP وSHA256 على جهاز البناء من `main`.
2. اختبار نفس ZIP وSHA256 على بيئة اختبار Windows.
3. نقل نفس ZIP وSHA256 إلى الإنتاج.
4. تثبيت الحزمة دون إعادة البناء على الإنتاج.

الوثائق الأساسية:

- [docs/simple_offline_deployment.md](docs/simple_offline_deployment.md)
- [docs/production_artifact_promotion.md](docs/production_artifact_promotion.md)
- [docs/production-fast-path.md](docs/production-fast-path.md)
- [docs/PRODUCTION_DEPLOYMENT_TROUBLESHOOTING.md](docs/PRODUCTION_DEPLOYMENT_TROUBLESHOOTING.md)
- [docs/production_runbook.md](docs/production_runbook.md)

الأوامر الأساسية على جهاز البناء:

```powershell
git fetch origin --prune
git switch main
git pull --ff-only origin main

powershell.exe -NoProfile -ExecutionPolicy Bypass `
  -File ".\scripts\build-production-package.ps1"

powershell.exe -NoProfile -ExecutionPolicy Bypass `
  -File ".\scripts\prepare-production-transfer.ps1"
```

المسار السريع على الإنتاج من مجلد النقل:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\deploy.ps1
```

المسار اليدوي على الإنتاج:

```powershell
$package = Get-ChildItem "C:\Uqeb\incoming\Uqeb-*.zip" |
    Sort-Object LastWriteTime -Descending |
    Select-Object -First 1

if (-not $package) {
    throw "لم يتم العثور على حزمة Uqeb في C:\Uqeb\incoming."
}

powershell.exe -NoProfile -ExecutionPolicy Bypass `
  -File "C:\UqebTools\install-production-package.ps1" `
  -PackagePath $package.FullName
```

قواعد الإنتاج المختصرة:

- لا تُعد بناء الحزمة على الإنتاج.
- تحقق من SHA256 قبل التثبيت.
- نسخة قاعدة البيانات الاحتياطية إلزامية قبل migrations أو استبدال الملفات.
- يستخدم المثبت `BACKUP DATABASE ... WITH CHECKSUM` ثم `RESTORE VERIFYONLY`.
- فحوص الصحة الرسمية: `/health/live` ثم `/health/ready` ثم `/health`.
- يجب أن يخلو بناء الواجهة المنشور من `localhost:5000` و`127.0.0.1:5000`.

## CI

الملف الرئيسي: `.github/workflows/ci.yml`.

Jobs الأساسية الحالية:

- `backend`
- `backend-release`
- `frontend`
- `visual-regression`
- `pdf-linux`
- `reporting-acceptance-small`
- `reporting-acceptance-large` عبر `workflow_dispatch` فقط
- `transaction-sqlserver-integration`
- `build-integration`
- `zizmor`

بوابة حزمة النشر في `.github/workflows/deployment-package.yml` تعمل عند تغييرات scripts أو ملفات نشر/صحة مرتبطة، وتتحقق من Pester tests وبنية الحزمة على Windows.

## اختبارات الأداء

اختبارات k6 موجودة في `performance-tests/`:

```bash
k6 run performance-tests/uqeb-load-test.js
K6_SCENARIO=load k6 run performance-tests/uqeb-load-test.js
```

راجع [performance-tests/README.md](performance-tests/README.md) قبل تشغيل اختبارات 1000 أو 10000 معاملة. لا تشغل اختبارات الحمل على الإنتاج أثناء استخدام النظام.

## حالة معروفة / ملاحظات

- `NU1903` قد يظهر على `SQLitePCLRaw.lib.e_sqlite3 2.1.11` في مشروع الاختبارات. هذا تحذير vulnerability لحزمة ويحتاج PR dependency منفصل إذا تقرر رفعها.
- `npm run build` قد يعرض تحذير Vite عن chunk أكبر من 500 kB. هذا ليس فشل build، ومعالجته تعني code splitting/تغيير bundling.
- اختبارات Playwright/PDF تحتاج Chromium وبيئة مناسبة.
- `reporting-acceptance-large` skipped افتراضيًا في pull requests لأنه مشروط بـ `workflow_dispatch`.
- `artifacts/` يحتوي مخرجات محلية وقد لا يمثل حالة build الحالية.

## ما لا يغطيه README

- لا يشرح كل DTO أو payload.
- لا يقرر سياسة كلمات مرور الإنتاج أو أسراره.
- لا يستبدل أدلة النشر التفصيلية في `docs/production*.md`.
- لا يثبت جاهزية الإنتاج لميزة خلف feature flag؛ التفعيل الإنتاجي يحتاج قبولًا وتشغيلًا وفق أدلة النشر.
- لا يوثق كل migration تاريخية أو كل قاعدة عمل تفصيلية داخل services.

## مصادر الحقيقة المستخدمة لهذا README

- `global.json`
- `.nvmrc`
- `frontend/uqeb-ui/package.json`
- `backend/Uqeb.sln`
- `backend/Uqeb.Api/Uqeb.Api.csproj`
- `backend/Uqeb.Api/appsettings.example.json`
- `backend/Uqeb.Api/appsettings.Development.json`
- Controllers و`Program.cs` وملفات Authorization في backend
- routes وnavigation في `frontend/uqeb-ui/src`
- `.github/workflows/*.yml`
- وثائق `docs/` المذكورة أعلاه
- `performance-tests/README.md`
- `scanner-bridge/Uqeb.ScannerBridge/README.md`
