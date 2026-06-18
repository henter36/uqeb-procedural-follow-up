# ملاحظات نشر Uqeb في الإنتاج

دليل نشر **Uqeb** على Windows باستخدام حزمة جاهزة. لا يفترض Git أو Node أو .NET SDK على جهاز الإنتاج.

راجع [PREREQUISITES.md](PREREQUISITES.md) أولًا.

---

## 1. بنية التشغيل

```text
Browser --> IIS (static) --> C:\Uqeb\web
Browser --> /api/*     --> Kestrel http://localhost:5000 --> C:\Uqeb\api
```

| المكوّن | التفاصيل |
|---------|----------|
| **Frontend** | IIS static site من `C:\Uqeb\web` |
| **Backend** | Kestrel على `http://localhost:5000` |
| **AllowedOrigins** | يجب أن يتضمن `appsettings.Production.json` عنوان الواجهة (مثل `http://server-name`) |
| **IIS /api proxy** | **غير مفترض** في هذا PR — لا تدّعِ تمرير `/api` عبر IIS إلا بعد إعداد واختبار reverse proxy (ARR) منفصل |
| **/api/health, /swagger** | غير مضمونين في Production ما لم تُضافا صراحة |

---

## 2. حزمة النشر (من جهاز البناء)

```text
release\
  publish\
    api\     dotnet publish -c Release
    web\     npm run build (dist contents)
```

```powershell
cd backend\Uqeb.Api
dotnet publish -c Release -o ..\..\release\publish\api

cd frontend\uqeb-ui
npm run build
xcopy /E /I dist ..\..\release\publish\web
```

---

## 3. النشر على الإنتاج

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\deploy-production.ps1 `
  -SourcePackagePath "C:\path\to\release" `
  -InstallRoot "C:\Uqeb"
```

### ما يفعله السكربت

1. يتحقق من `publish\api` و`publish\web` والملفات الأساسية.
2. يوقف **Uqeb.Api** أو **dotnet** المرتبط بـ `-InstallRoot\api` فقط — لا يقتل عمليات أخرى على المنفذ. إذا كان `-ApiPort` مستخدمًا من عملية غير معروفة، **يفشل النشر** برسالة واضحة.
3. **ينسخ احتياطيًا** إلى `C:\Uqeb\backup\api-YYYYMMDD-HHmmss` و`web-YYYYMMDD-HHmmss` — **يفشل النشر إذا فشل النسخ الاحتياطي**.
4. ينشر إلى `C:\Uqeb\api` و`C:\Uqeb\web`.
5. **لا يستبدل** `C:\Uqeb\api\appsettings.Production.json` إن وُجد.
6. يحافظ على `logs` و`uploads` و`backup` (خارج مجلدي api/web).
7. يكتب `web.config` للـ SPA و`BUILD_INFO.txt` و`start-api.ps1`.

**متطلبات التشغيل:** يجب تشغيل السكربت **كمسؤول** (Administrator) لأنه يوقف Scheduled Tasks وعمليات API ويكتب تحت `-InstallRoot`.

**معاملات اختيارية:** `-InstallRoot` (افتراضي `C:\Uqeb`)، `-ApiPort` (افتراضي `5000`)، `-ScheduledTaskName` (افتراضي `UqebApi`).

---

## 3.1 قيود web.config (SPA fallback)

السكربت يكتب `web.config` يعتمد على **defaultDocument** و**httpErrors** فقط — **بدون** IIS URL Rewrite Module.

| السلوك | التفاصيل |
|--------|----------|
| المسار `/` | يُقدّم `index.html` عبر Default Document |
| مسارات عميقة (مثل `/reports`) | عند عدم وجود ملف فعلي، يعيد IIS **HTTP 404** مع **محتوى** `index.html` (بفضل `httpErrors`) |
| التوجيه داخل React | يعمل بعد تحميل الصفحة |
| حالة HTTP | تبقى **404** وليست 200 — قد يؤثر على تحليلات أو SEO |

هذا **مقبول للتشغيل الداخلي الحالي**. لكن **المسارات العميقة قد ترجع HTTP 404** (مع محتوى `index.html`) وليس **200** — وهذا قد يؤثر على التحليلات أو SEO.

إذا كانت **رموز 200 نظيفة** مطلوبة لكل مسار SPA، فالخيار الأفضل هو **IIS URL Rewrite** (أو reverse proxy مع rewrite rule) بدل الاعتماد على `httpErrors` فقط — خارج نطاق هذا PR.

---

## 4. appsettings.Production.json

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=localhost\\SQLEXPRESS;Database=UqebDb;Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=true"
  },
  "Jwt": {
    "Key": "REPLACE_WITH_32_PLUS_CHAR_SECRET",
    "Issuer": "UqebApi",
    "Audience": "UqebClient",
    "ExpireMinutes": 480
  },
  "AllowedOrigins": [
    "http://your-iis-site-hostname"
  ],
  "FileStorage": {
    "Path": "C:\\Uqeb\\uploads"
  }
}
```

---

## 5. تشغيل API

```powershell
powershell -ExecutionPolicy Bypass -File C:\Uqeb\start-api.ps1
```

أو Scheduled Task:

- Program: `C:\Program Files\dotnet\dotnet.exe`
- Arguments: `C:\Uqeb\api\Uqeb.Api.dll --urls http://0.0.0.0:5000`
- Working directory: `C:\Uqeb\api`
- Environment: `ASPNETCORE_ENVIRONMENT=Production`

---

## 6. IIS

1. Application Pool: **No Managed Code**
2. Physical path: `C:\Uqeb\web`
3. Static Content + Default Document + HttpErrors مفعّلة

---

## 7. استكشاف الأخطاء

```powershell
Get-Content C:\Uqeb\logs\api.log -Tail 50
netstat -ano | findstr :5000
```

### اختبار API

```powershell
$body = '{"username":"admin","password":"YOUR_PASSWORD"}'
Invoke-RestMethod -Uri "http://localhost:5000/api/auth/login" -Method POST -ContentType "application/json" -Body $body
```

### sqlcmd

```powershell
sqlcmd -C -S localhost\SQLEXPRESS -Q "SELECT 1"
```

استخدم `-C` عندما تكون شهادة SQL Server المحلية غير موثوقة.

---

## 8. نتائج التحقق (بيئة المراجعة)

| الاختبار | النتيجة |
|----------|---------|
| `dotnet build` | ناجح |
| `npm run build` | ناجح |
| `dotnet publish` | ناجح |
| `check-prerequisites.ps1` | يعمل على PowerShell 5.1 (ASCII) |
| `deploy-production.ps1` | يعمل على PowerShell 5.1؛ ينشئ backup قبل النسخ |
| API على `localhost:5000` | يعمل بعد `start-api.ps1` |
| `POST /api/auth/login` | 200 مع بيانات صحيحة |
| `GET /api/reports/page-summary` | 200 مع Bearer token |
| `GET /api/transactions` | 200 مع Bearer token |
| `appsettings.Production.json` | لم يُستبدل عند إعادة النشر |
| IIS + refresh `/reports`, `/transactions` | يتطلب إعداد IIS يدوي على جهاز الإنتاج |

---

## 9. سكربتات

| السكربت | الغرض |
|---------|--------|
| `scripts/check-prerequisites.ps1` | فحص قراءة فقط |
| `scripts/deploy-production.ps1` | نشر آمن مع backup |
| `scripts/sql/reset-admin-password.sql` | قالب توثيقي فقط — لا يُشغَّل من سكربت النشر |

---

## 10. قاعدة البيانات (مرة واحدة)

```powershell
cd backend\Uqeb.Api
dotnet ef database update
```
