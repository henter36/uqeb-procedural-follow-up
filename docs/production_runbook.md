# دليل إعداد ونشر وتشغيل الإنتاج — Uqeb

هذا الدليل هو المرجع التشغيلي المعتمد لنشر نظام **المتابعة الإجرائية (Uqeb)** على جهاز Windows داخل الشبكة المحلية، ويثبت الخطوات التي نجحت فعليًا على خادم الإنتاج.

> يجب تنفيذ أوامر PowerShell من نافذة **Run as Administrator**، ويجب لصق الكتل المركبة كاملةً؛ لا تُنفذ `else` أو `finally` منفصلة عن كتلة `if/try` التابعة لها.

---

## 1. البيئة المعتمدة

| المكوّن | القيمة |
|---|---|
| خادم الإنتاج | `10.0.177.17` |
| API | `http://10.0.177.17:5000` |
| UI | `http://10.0.177.17:8080` |
| SQL Server | `localhost\SQLEXPRESS` |
| قاعدة البيانات | `UqebDb` |
| نشر API | `C:\Uqeb\publish\api` |
| نشر Web | `C:\Uqeb\publish\web` |
| إعدادات الإنتاج | `C:\Uqeb\config\appsettings.Production.json` |
| السجل | `C:\Uqeb\logs\api-runtime.log` |
| الحزم الواردة | `C:\Uqeb\incoming` |
| النسخ الاحتياطية للملفات | `C:\Uqeb\backup` |
| Scheduled Task | `UqebApi` |

Connection string المعتمد:

```text
Server=localhost\SQLEXPRESS;Database=UqebDb;Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=true
```

---

## 2. المتطلبات على جهاز الإنتاج

- Windows 10/11 أو Windows Server.
- SQL Server Express وخدمة `SQLEXPRESS` تعمل.
- .NET Runtime المطابق لإصدار التطبيق.
- Scheduled Task باسم `UqebApi`.
- خدمة ويب ثابتة للواجهة على المنفذ `8080`.
- السماح بالمنفذين `5000` و`8080` داخل الشبكة المحلية فقط.
- صلاحيات قراءة وكتابة للمجلدات التشغيلية اللازمة.

فحص SQL Server:

```powershell
Get-Service | Where-Object Name -Match "MSSQL" |
    Select-Object Name, Status, StartType
```

فحص المنافذ:

```powershell
Get-NetTCPConnection -LocalPort 5000,8080 -ErrorAction SilentlyContinue |
    Select-Object LocalAddress, LocalPort, State, OwningProcess
```

---

## 3. بنية الحزمة المنقولة

بعد فك الحزمة يجب أن تحتوي على:

```text
api\
web\
```

ولا يُمرر ملف ZIP إلى سكربت يتوقع مجلدًا مفكوكًا.

مثال مجلد staging:

```text
C:\Uqeb\staging\20260623-092128\api
C:\Uqeb\staging\20260623-092128\web
```

قبل النشر تأكد من الملفات الأساسية:

```powershell
$staging = "C:\Uqeb\staging\<TIMESTAMP>"

@(
    "$staging\api\Uqeb.Api.dll",
    "$staging\api\Uqeb.Api.exe",
    "$staging\web\index.html"
) | ForEach-Object {
    [pscustomobject]@{
        Path   = $_
        Exists = Test-Path $_
    }
}
```

---

## 4. نشر إصدار جديد

المدخل الرسمي:

```text
scripts/deploy-production.ps1
    -> scripts/deploy-production-v2.ps1
```

الترتيب:

1. بناء API والواجهة على جهاز التطوير.
2. إنشاء حزمة الإصدار والتحقق من SHA256.
3. نقل الحزمة إلى `C:\Uqeb\incoming`.
4. فكها إلى مجلد staging.
5. تشغيل سكربت النشر كمسؤول مع **مسار مجلد staging المفكوك**.
6. السكربت يوقف المهمة، ينسخ API وWeb، يحافظ على إعدادات الإنتاج، ثم يعيد تشغيل API.
7. تنفيذ فحوص الصحة وتسجيل الدخول يدويًا.

لا تستخدم `robocopy /MIR` على مجلد API؛ قد يحذف إعدادات أو ملفات تشغيلية محفوظة على الخادم.

---

## 5. إعدادات الإنتاج

الإعداد المرجعي:

```text
C:\Uqeb\config\appsettings.Production.json
```

يجب أن يحتوي على:

- Connection string الصحيح لـ `UqebDb`.
- JWT key قوي وغير موجود في Git.
- `AllowedOrigins` ويشمل:

```text
http://10.0.177.17:8080
```

فحص الإعداد دون إظهار كلمة مرور SQL:

```powershell
$config = Get-Content "C:\Uqeb\publish\api\appsettings.Production.json" -Raw |
    ConvertFrom-Json

$config.ConnectionStrings.DefaultConnection -replace `
    '(?i)(Password|Pwd)\s*=\s*[^;]+', '$1=***'

$config.AllowedOrigins
```

---

## 6. إدارة migrations

### 6.1 القاعدة العامة

- كتابة مسار ملف `.sql` في PowerShell **لا تنفذ الملف**.
- أوامر SQL مثل `SELECT` لا تُكتب مباشرة في PowerShell؛ تُنفذ عبر SSMS أو اتصال SQL.
- عند استخدام سكربت idempotent يجب أن يعكس جدول `__EFMigrationsHistory` المخطط الفعلي.
- لا تطبق أجزاء منتقاة من migration إلا كإصلاح موثق ومراجع.

### 6.2 مشكلة `NameNormalized` التي وقعت

ظهر الخطأ:

```text
Invalid column name 'NameNormalized'
```

السبب كان أن SQL Server جمع دفعة تحتوي على إضافة العمود ثم استخدامه قبل وجود فاصل batch. الإصلاح هو وجود `GO` بعد كتلة `ALTER TABLE` وقبل أول `UPDATE` يستخدم `NameNormalized`.

يجب أن يظهر في السكربت المصحح ترتيب مشابه:

```sql
ALTER TABLE [Categories]
ADD [NameNormalized] nvarchar(450) NOT NULL DEFAULT N'';
GO

UPDATE Departments
SET NameNormalized = ...;
```

### 6.3 تنفيذ ملف SQL دون `sqlcmd`

هذه الطريقة تعمل عبر `System.Data.SqlClient` وتقسم الملف عند `GO`:

```powershell
$ErrorActionPreference = "Stop"

$sqlFile = "C:\Uqeb\incoming\Uqeb-migrations-idempotent-fixed.sql"
$server = "localhost\SQLEXPRESS"
$database = "UqebDb"

if (-not (Test-Path -LiteralPath $sqlFile)) {
    throw "ملف migrations غير موجود: $sqlFile"
}

$sqlText = Get-Content -LiteralPath $sqlFile -Raw
$batches = [regex]::Split($sqlText, '(?im)^\s*GO\s*(?:--.*)?$') |
    Where-Object { -not [string]::IsNullOrWhiteSpace($_) }

$connectionString = "Server=$server;Database=$database;Integrated Security=True;TrustServerCertificate=True;MultipleActiveResultSets=True;"
$connection = New-Object System.Data.SqlClient.SqlConnection $connectionString
$connection.Open()

try {
    $batchNumber = 0

    foreach ($batch in $batches) {
        $batchNumber++
        $command = $connection.CreateCommand()
        $command.CommandTimeout = 300
        $command.CommandText = $batch

        try {
            [void]$command.ExecuteNonQuery()
        }
        catch {
            Write-Host "فشلت الدفعة رقم $batchNumber" -ForegroundColor Red
            Write-Host ($batch.Substring(0, [Math]::Min(1200, $batch.Length)))
            throw
        }
        finally {
            $command.Dispose()
        }
    }
}
finally {
    if ($connection.State -eq "Open") { $connection.Close() }
    $connection.Dispose()
}
```

### 6.4 التحقق بعد migrations

نفّذ في SSMS على `UqebDb`:

```sql
SELECT DB_NAME() AS CurrentDatabase;

SELECT MigrationId
FROM dbo.__EFMigrationsHistory
ORDER BY MigrationId;

SELECT
    COL_LENGTH('dbo.Departments', 'NameNormalized') AS DepartmentsNameNormalized,
    COL_LENGTH('dbo.ExternalParties', 'NameNormalized') AS ExternalPartiesNameNormalized,
    COL_LENGTH('dbo.Categories', 'NameNormalized') AS CategoriesNameNormalized;

SELECT
    OBJECT_ID(N'dbo.LoginAttemptLogs') AS LoginAttemptLogsTable,
    OBJECT_ID(N'dbo.SecurityAlerts') AS SecurityAlertsTable;
```

المطلوب:

- ظهور migration:
  `20260622062754_AddReferenceDataNormalizedNames`
- كل قيم `COL_LENGTH` و`OBJECT_ID` غير `NULL`.

---

## 7. إعادة إنشاء قاعدة بيانات فارغة

هذه الخطوة مسموحة فقط عندما تكون قاعدة البيانات فارغة أو يوجد قرار صريح بحذفها.

> النسخة الاحتياطية ليست شرطًا مانعًا عندما تكون القاعدة فارغة ويكون قرار الحذف موثقًا. في أي قاعدة تحتوي بيانات تشغيلية، يجب تقييم النسخ الاحتياطي والاسترجاع قبل الحذف.

الترتيب المعتمد:

1. إيقاف وتعطيل `UqebApi` مؤقتًا.
2. التأكد من عدم وجود listener على المنفذ `5000`.
3. الاتصال بقاعدة `master`.
4. تحويل `UqebDb` إلى `SINGLE_USER WITH ROLLBACK IMMEDIATE`.
5. حذف القاعدة.
6. إنشاء `UqebDb` جديدة.
7. تنفيذ جميع migrations من البداية.
8. التحقق من `__EFMigrationsHistory` والأعمدة المطلوبة.
9. إعادة تمكين وتشغيل المهمة.
10. تنفيذ فحوص الصحة والدخول.

أمر الحذف والإنشاء داخل SQL:

```sql
USE master;
GO

IF DB_ID(N'UqebDb') IS NOT NULL
BEGIN
    ALTER DATABASE [UqebDb]
        SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
    DROP DATABASE [UqebDb];
END;
GO

CREATE DATABASE [UqebDb];
ALTER DATABASE [UqebDb] SET RECOVERY SIMPLE;
GO
```

بعد إنشاء قاعدة جديدة، تشغيل التطبيق لأول مرة ينفذ Seeder حسب إعدادات التطبيق الحالية. يجب تغيير كلمات المرور الافتراضية بعد أول دخول.

---

## 8. تشغيل وإيقاف API

إيقاف:

```powershell
schtasks /End /TN "UqebApi"
Start-Sleep -Seconds 3
```

تشغيل:

```powershell
schtasks /Run /TN "UqebApi"
Start-Sleep -Seconds 8
```

فحص المهمة والمنفذ:

```powershell
Get-ScheduledTask -TaskName "UqebApi" |
    Select-Object TaskName, State

Get-ScheduledTaskInfo -TaskName "UqebApi" |
    Select-Object LastRunTime, LastTaskResult

Get-NetTCPConnection -LocalPort 5000 -State Listen -ErrorAction SilentlyContinue |
    Select-Object LocalAddress, LocalPort, State, OwningProcess
```

إذا لم يظهر listener، افحص:

```powershell
Get-Content "C:\Uqeb\logs\api-runtime.log" -Tail 150
```

---

## 9. فحوص الصحة

على خادم API:

```powershell
Invoke-WebRequest -UseBasicParsing "http://localhost:5000/health/live"
Invoke-WebRequest -UseBasicParsing "http://localhost:5000/health/ready"
Invoke-WebRequest -UseBasicParsing "http://localhost:5000/health"
```

المطلوب:

```text
live   = 200
ready  = 200
health = 200، database = pass
```

> نجاح health/database لا يثبت أن جميع الأعمدة المطلوبة موجودة؛ يجب أيضًا تنفيذ smoke test لتسجيل الدخول وفتح شاشة تعتمد على العلاقات والبيانات المرجعية.

---

## 10. اختبار تسجيل الدخول المباشر

```powershell
$body = @{
    username = "admin"
    password = "<PASSWORD>"
} | ConvertTo-Json

try {
    $response = Invoke-WebRequest `
        -UseBasicParsing `
        -Method Post `
        -Uri "http://localhost:5000/api/auth/login" `
        -ContentType "application/json; charset=utf-8" `
        -Headers @{ Origin = "http://10.0.177.17:8080" } `
        -Body $body

    Write-Host "HTTP STATUS: $($response.StatusCode)" -ForegroundColor Green
}
catch {
    if ($_.Exception.Response) {
        Write-Host "HTTP STATUS:" ([int]$_.Exception.Response.StatusCode)
    }

    Write-Host $_.Exception.Message
}

$body = $null
```

التفسير:

| الرمز | المعنى |
|---|---|
| 200 | الدخول ناجح |
| 400 | حقول الطلب فارغة أو غير صحيحة بنيويًا |
| 401 | المستخدم/كلمة المرور/حالة الحساب غير صحيحة |
| 429 | تجاوز حد محاولات الدخول |
| 500 | خطأ تطبيق أو مخطط قاعدة بيانات |

لا تستخدم:

```powershell
$username = Read-Host "admin"
```

على أنه تعيين لقيمة `admin`؛ النص داخل `Read-Host` مجرد عنوان للسؤال، والقيمة لا تُسجل إلا بعد كتابتها عند ظهور المؤشر.

---

## 11. التحقق من Build الواجهة

في التطوير، الأفضل أن تستخدم الواجهة:

```text
VITE_API_BASE_URL=/api
```

ويقوم Vite proxy بتحويل `/api` إلى `http://localhost:5000`.

في Build الإنتاج يجب ألا تشير ملفات JavaScript إلى `localhost:5000`؛ لأن `localhost` عند المستخدم يعني جهاز المستخدم نفسه.

فحص Build الإنتاج:

```powershell
Select-String `
    -Path "C:\Uqeb\publish\web\assets\*.js" `
    -Pattern "localhost:5000", "10.0.177.17:5000" |
    Select-Object Path, LineNumber, Line
```

بعد النشر نفّذ تحديثًا قسريًا في المتصفح:

```text
Ctrl + Shift + R
```

---

## 12. تشخيص الأخطاء

### صفحة الدخول تفتح لكن الدخول لا يعمل

1. اختبر API مباشرة.
2. حدّد رمز HTTP.
3. افحص آخر السجل:

```powershell
Get-Content "C:\Uqeb\logs\api-runtime.log" -Tail 300 |
    Select-String -Pattern `
        "Invalid column", `
        "Invalid object", `
        "SqlException", `
        "DbUpdateException", `
        "Unhandled", `
        "auth/login"
```

### `ERR_CONNECTION_REFUSED`

- API متوقف أو غير مستمع على `5000`.
- افحص المهمة والمنفذ والسجل.

### CORS ظاهر في المتصفح

لا تفترض أن CORS هو السبب إذا كان الطلب يرجع `500`. افحص:

- OPTIONS preflight.
- `AllowedOrigins`.
- رمز HTTP الحقيقي للطلب.
- سجل API.

### رسائل `contentscript.js` و`ObjectMultiplex`

غالبًا من إضافات المتصفح وليست من Uqeb. اختبر في نافذة خاصة بدون إضافات قبل تعديل التطبيق.

---

## 13. قائمة قبول ما بعد النشر

- [ ] `UqebApi` تعمل.
- [ ] المنفذ `5000` في حالة Listen.
- [ ] `/health/live` يعيد 200.
- [ ] `/health/ready` يعيد 200.
- [ ] `/health` يعيد database pass.
- [ ] تسجيل الدخول الصحيح يعيد 200.
- [ ] تسجيل الدخول الخاطئ يعيد 401، وليس 500.
- [ ] فتح الواجهة من جهاز على الشبكة.
- [ ] إنشاء معاملة وتعديلها.
- [ ] اختبار التحويل والتعقيب والإفادة والإغلاق.
- [ ] اختبار PDF وExcel.
- [ ] اختبار Scanner Bridge عند توفر الجهاز.
- [ ] عدم وجود `SqlException` أو `Invalid column` أو `Unhandled exception` جديدة.
- [ ] تغيير كلمة مرور admin الافتراضية.

---

## 14. ممنوعات تشغيلية

- لا تعتبر وجود ملف `.sql` أو كتابة مساره تنفيذًا له.
- لا تشغّل SQL مباشرة في PowerShell دون موصل SQL.
- لا تعتبر health 200 دليلًا كافيًا على سلامة كل مخطط قاعدة البيانات.
- لا تفصل `else` أو `finally` عن كتلتها في PowerShell.
- لا تعرض كلمات المرور أو connection strings الكاملة في السجلات أو Git.
- لا تستخدم `localhost` داخل Build واجهة موجّه لمستخدمين على أجهزة أخرى.
- لا تعلن الجاهزية النهائية قبل نجاح تسجيل الدخول واختبار سير العمل الأساسي.

---

## 15. السجل المرجعي للحادثة

المشكلة التي تم حلها في 23 يونيو 2026:

- الواجهة فتحت، لكن تسجيل الدخول أعاد 500.
- API كان يعمل وCORS صحيحًا.
- سجل EF Core كشف أن `Departments.NameNormalized` مفقود.
- ملف migration الأصلي احتاج فصل batch باستخدام `GO`.
- كتابة مسار ملف SQL في PowerShell لم تنفذه.
- `sqlcmd` لم يكن مثبتًا.
- الحل النهائي في قاعدة فارغة كان إعادة إنشاء `UqebDb` وتطبيق migrations كاملة ثم تشغيل Seeder.
- بعد ذلك اشتغل النظام وتسجيل الدخول بنجاح.
