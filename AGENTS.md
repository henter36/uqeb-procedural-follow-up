# تعليمات المشروع — المتابعة الإجرائية (Uqeb)

هذه التعليمات مرجع ملزم لأي Agent أو مطور يعمل على المشروع. سجل المشكلات الفعلية ومعالجاتها موجود في:

```text
docs/PRODUCTION_DEPLOYMENT_TROUBLESHOOTING.md
```

> نقطة الدخول المتوافقة للنشر هي `scripts/deploy-production.ps1`، والتنفيذ الآلي المعتمد هو `scripts/deploy-production-v2.ps1`.

## البيئة المعتمدة

- المستودع: `henter36/uqeb-procedural-follow-up`
- جذر المشروع على جهاز التطوير: `C:\Users\alqud\uqeb`
- Backend: `C:\Users\alqud\uqeb\backend\Uqeb.Api`
- Frontend: `C:\Users\alqud\uqeb\frontend\uqeb-ui`
- جهاز الإنتاج لا يستخدم GitHub؛ النقل يتم بحزمة ZIP وبصمة SHA256.
- عنوان جهاز الإنتاج: `10.0.177.17`
- الواجهة: `http://10.0.177.17:8080`
- API: `http://10.0.177.17:5000`
- جذر الإنتاج: `C:\Uqeb`
- API المنشور: `C:\Uqeb\publish\api`
- الواجهة المنشورة: `C:\Uqeb\publish\web`
- السجلات: `C:\Uqeb\logs`
- النسخ الاحتياطية: `C:\Uqeb\backup`
- الحزم الواردة: `C:\Uqeb\incoming`
- مهمة التشغيل: `UqebApi`

## مصادر السلطة

1. المسارات الواردة أعلاه هي المسارات المعتمدة.
2. سكربت النشر الآلي يجب أن يبقى متوافقًا معها، ويستهدف `C:\Uqeb\publish\api` و`C:\Uqeb\publish\web`.
3. يجب أن يستمع Kestrel على `0.0.0.0:5000` أو عنوان قابل للوصول من الشبكة، وليس `localhost:5000` فقط.
4. عند اختلاف أي وثيقة أو سكربت قديم مع هذه التعليمات، توقف وعدّل التعارض قبل النشر.

## قواعد إلزامية

1. لا تنشر ملفات المصدر أو `node_modules` إلى الإنتاج.
2. لا تشغّل migrations على الإنتاج خارج `install-production-package.ps1`. المثبت يقرر تلقائيًا من `manifest.minimumDatabaseMigration` بعد نسخة احتياطية كاملة ومتحقق منها.
3. لا تستبدل إعدادات الإنتاج بملفات إعدادات جهاز التطوير.
4. لا تحذف المرفقات أو السجلات أو البيانات التشغيلية أثناء النشر.
5. خذ نسخة احتياطية من API والواجهة وإعدادات الإنتاج قبل أي استبدال.
6. لا تستخدم `npm audit fix` ضمن مسار تجهيز الإصدار؛ تعالج الاعتماديات في تغيير مستقل.
7. لا تعتمد على رسالة «بيانات الدخول غير صحيحة» لتشخيص الشبكة؛ افحص Request URL وStatus Code وConsole.
8. بوابة القبول الرسمية هي `/health/live` ثم `/health/ready` ثم `/health` مع نجاح فحوص قاعدة البيانات وPlaywright والتقارير.
9. لا يجوز أن يحتوي بناء Frontend للإنتاج على `localhost:5000` أو `127.0.0.1:5000`.
10. تُحقن `VITE_API_BASE_URL` وقت البناء؛ تعديلها بعد البناء لا يغير الملفات المنشورة.
11. قيم Robocopy من 0 إلى 7 نجاح أو اختلافات غير قاتلة؛ 8 فأعلى فشل.
12. لا تستخدم `/MIR` على مجلد API. يمكن استخدامه على مجلد الواجهة الثابت فقط.
13. لا تنفذ أي حذف إنتاجي أو تصفير معاملات دون نسخة قاعدة بيانات قابلة للتحقق وتأكيد صريح.

أمر التثبيت الرسمي المبسط:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass `
  -File "C:\UqebTools\install-production-package.ps1" `
  -PackagePath $package.FullName
```

---

# تجهيز الإصدار على جهاز التطوير

## 1. تحديث المصدر

```powershell
$ErrorActionPreference = "Stop"

cd C:\Users\alqud\uqeb

git checkout main
git pull origin main
git status -sb
```

يجب أن تكون الشجرة نظيفة وأن يكون البناء من `main` المحدث.

## 2. تثبيت عنوان API الصحيح قبل بناء Frontend

```powershell
cd C:\Users\alqud\uqeb\frontend\uqeb-ui

@'
VITE_API_BASE_URL=http://10.0.177.17:5000/api
'@ | Set-Content ".env.production.local" -Encoding ASCII
```

عند تغير عنوان الإنتاج يجب تحديث:

- `.env.production.local` على جهاز التطوير.
- `AllowedOrigins` على جهاز الإنتاج.
- قواعد الجدار الناري واختبارات الاتصال.

## 3. إنشاء مجلد الإصدار

```powershell
$projectRoot = "C:\Users\alqud\uqeb"
$releaseBase = "C:\UqebRelease"
$stamp = Get-Date -Format "yyyyMMdd-HHmmss"
$releaseRoot = Join-Path $releaseBase "Uqeb-$stamp"
$releaseZip = "$releaseRoot.zip"
$hashPath = "$releaseRoot.sha256.txt"

New-Item "$releaseRoot\api" -ItemType Directory -Force | Out-Null
New-Item "$releaseRoot\web" -ItemType Directory -Force | Out-Null
```

لا تعتمد على متغيرات عُرّفت في جلسة PowerShell سابقة.

## 4. بناء Backend

```powershell
cd C:\Users\alqud\uqeb\backend\Uqeb.Api

dotnet restore
dotnet build -c Release
dotnet publish -c Release -o "$releaseRoot\api"
```

## 5. بناء Frontend

```powershell
cd C:\Users\alqud\uqeb\frontend\uqeb-ui

npm ci
npm run build

Copy-Item ".\dist\*" "$releaseRoot\web" -Recurse -Force
```

يجب أن يكون ترتيب التنفيذ: كتابة ملف البيئة، ثم البناء، ثم الفحص، ثم إنشاء ZIP. أي ZIP أنشئ قبل آخر بناء يُحذف ويعاد إنشاؤه.

## 6. فحص كامل مخرجات Frontend

افحص شجرة `dist` كاملة، وليس ملفات JavaScript في مستوى واحد فقط:

```powershell
$forbiddenFrontendRefs = Get-ChildItem ".\dist" -Recurse -File |
    Select-String `
        -Pattern "localhost:5000|127\.0\.0\.1:5000" `
        -AllMatches

if ($forbiddenFrontendRefs) {
    $forbiddenFrontendRefs | Format-Table Path, LineNumber, Line -AutoSize
    throw "يحتوي بناء Frontend على عنوان API محلي غير صالح للإنتاج."
}
```

وتحقق من وجود عنوان الإنتاج:

```powershell
$productionFrontendRefs = Get-ChildItem ".\dist" -Recurse -File |
    Select-String `
        -Pattern "10\.0\.177\.17:5000" `
        -AllMatches

if (!$productionFrontendRefs) {
    throw "عنوان API الإنتاج غير موجود في بناء Frontend."
}
```

## 7. إضافة `web.config`

```powershell
@'
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <system.webServer>
    <defaultDocument enabled="true">
      <files>
        <clear />
        <add value="index.html" />
      </files>
    </defaultDocument>
    <httpErrors errorMode="Custom" existingResponse="Replace">
      <remove statusCode="404" subStatusCode="-1" />
      <error statusCode="404" path="/index.html" responseMode="ExecuteURL" />
    </httpErrors>
  </system.webServer>
</configuration>
'@ | Set-Content "$releaseRoot\web\web.config" -Encoding UTF8
```

## 8. إضافة معلومات الإصدار

```powershell
cd $projectRoot
$commitSha = git rev-parse HEAD

@"
Uqeb Production Release
Created: $(Get-Date -Format "yyyy-MM-dd HH:mm:ss")
Commit: $commitSha
Branch: main
Production API: http://10.0.177.17:5000/api
Production UI: http://10.0.177.17:8080
"@ | Set-Content "$releaseRoot\RELEASE.txt" -Encoding UTF8

if (!(Test-Path "$releaseRoot\api\Uqeb.Api.dll")) {
    throw "ملفات Backend غير مكتملة."
}

if (!(Test-Path "$releaseRoot\web\index.html")) {
    throw "ملفات Frontend غير مكتملة."
}
```

## 9. إنشاء ZIP وبصمة SHA256

```powershell
Remove-Item $releaseZip, $hashPath -Force -ErrorAction SilentlyContinue

Compress-Archive `
    -Path "$releaseRoot\*" `
    -DestinationPath $releaseZip `
    -CompressionLevel Optimal `
    -Force

$releaseHash = Get-FileHash $releaseZip -Algorithm SHA256
$releaseHash.Hash | Set-Content $hashPath -Encoding ASCII

if ((Get-Content $hashPath -Raw).Trim().Length -ne 64) {
    throw "ملف SHA256 غير صالح."
}

Get-Item $releaseZip, $hashPath |
    Select-Object FullName, Length, LastWriteTime
```

انقل الملفين المتطابقين إلى `C:\Uqeb\incoming` على جهاز الإنتاج.

---

# النشر على جهاز الإنتاج

## 10. اختيار أحدث حزمة مع ملف بصمتها المطابق

لا تحدد `<timestamp>` حرفيًا، ولا تختَر أحدث ZIP وأحدث hash بصورة مستقلة؛ يجب اشتقاق اسم البصمة من اسم ZIP نفسه:

```powershell
$ErrorActionPreference = "Stop"
$incoming = "C:\Uqeb\incoming"

$zipFile = Get-ChildItem $incoming -Filter "Uqeb-*.zip" -File |
    Sort-Object LastWriteTime -Descending |
    Select-Object -First 1

if (!$zipFile) {
    throw "لا توجد حزمة إصدار في $incoming"
}

$zip = $zipFile.FullName
$hashFile = [System.IO.Path]::ChangeExtension($zip, $null) + ".sha256.txt"

if (!(Test-Path $hashFile)) {
    throw "ملف SHA256 المطابق للحزمة غير موجود: $hashFile"
}

$expectedHash = (Get-Content $hashFile -Raw).Trim()
$actualHash = (Get-FileHash $zip -Algorithm SHA256).Hash

if ($expectedHash -ne $actualHash) {
    throw "فشل التحقق من الحزمة. لا تتابع النشر."
}
```

## 11. التحقق من Runtime

```powershell
dotnet --list-runtimes
```

يجب توفر:

```text
Microsoft.AspNetCore.App 10.0.x
Microsoft.NETCore.App 10.0.x
```

## 12. فك الحزمة في Staging

```powershell
$stamp = Get-Date -Format "yyyyMMdd-HHmmss"
$stage = "C:\Uqeb\staging\release-$stamp"
$backup = "C:\Uqeb\backup\before-$stamp"
$apiTarget = "C:\Uqeb\publish\api"
$webTarget = "C:\Uqeb\publish\web"
$existingSettings = "$apiTarget\appsettings.Production.json"
$provisionedSettings = "C:\Uqeb\config\appsettings.Production.json"
$backupSettings = "$backup\appsettings.Production.json"

New-Item $stage -ItemType Directory -Force | Out-Null
New-Item "$backup\api" -ItemType Directory -Force | Out-Null
New-Item "$backup\web" -ItemType Directory -Force | Out-Null
New-Item $apiTarget -ItemType Directory -Force | Out-Null
New-Item $webTarget -ItemType Directory -Force | Out-Null

Expand-Archive -Path $zip -DestinationPath $stage -Force

if (!(Test-Path "$stage\api\Uqeb.Api.dll")) {
    throw "ملفات Backend غير مكتملة داخل الحزمة."
}

if (!(Test-Path "$stage\web\index.html")) {
    throw "ملفات Frontend غير مكتملة داخل الحزمة."
}

Get-Content "$stage\RELEASE.txt"
```

## 13. تثبيت مصدر آمن لإعدادات الإنتاج قبل أي حذف أو إيقاف

في التحديثات استخدم الإعداد المنشور الحالي. في أول نشر يجب توفير ملف آمن مسبقًا في `C:\Uqeb\config\appsettings.Production.json`. لا تستخدم إعدادات قادمة من حزمة الإصدار كبديل تلقائي لأنها قد تكون إعدادات تطوير أو تحتوي قيمًا غير معتمدة.

```powershell
if (Test-Path $existingSettings) {
    Copy-Item $existingSettings $backupSettings -Force
}
elseif (Test-Path $provisionedSettings) {
    Copy-Item $provisionedSettings $backupSettings -Force
    Write-Host "First deployment: using provisioned production settings."
}
else {
    throw "لا توجد إعدادات إنتاج معتمدة. أنشئ $provisionedSettings قبل أول نشر."
}

$config = Get-Content $backupSettings -Raw | ConvertFrom-Json

if ([string]::IsNullOrWhiteSpace($config.ConnectionStrings.DefaultConnection)) {
    throw "DefaultConnection غير موجود في إعدادات الإنتاج."
}

if ($config.AllowedOrigins -notcontains "http://10.0.177.17:8080") {
    throw "AllowedOrigins لا يتضمن عنوان واجهة الإنتاج."
}
```

## 14. النسخ الاحتياطي مع دعم أول نشر

```powershell
if ((Test-Path $apiTarget) -and (Get-ChildItem $apiTarget -Force -ErrorAction SilentlyContinue)) {
    robocopy $apiTarget "$backup\api" /E /R:2 /W:2
    if ($LASTEXITCODE -ge 8) {
        throw "فشل نسخ Backend احتياطيًا."
    }
}
else {
    Write-Host "First deployment: no existing API files to back up."
}

if ((Test-Path $webTarget) -and (Get-ChildItem $webTarget -Force -ErrorAction SilentlyContinue)) {
    robocopy $webTarget "$backup\web" /E /R:2 /W:2
    if ($LASTEXITCODE -ge 8) {
        throw "فشل نسخ Frontend احتياطيًا."
    }
}
else {
    Write-Host "First deployment: no existing web files to back up."
}
```

## 15. إعداد التشغيل الشبكي

يجب أن يتضمن `C:\Uqeb\run-api.cmd`:

```bat
@echo off
cd /d C:\Uqeb\publish\api
set ASPNETCORE_ENVIRONMENT=Production
set DOTNET_ENVIRONMENT=Production
set ASPNETCORE_URLS=http://10.0.177.17:5000
C:\Uqeb\publish\api\Uqeb.Api.exe >> C:\Uqeb\logs\api-runtime.log 2>&1
```

ويجب أن تكون قاعدة الجدار الناري مقيدة بالشبكة المحلية:

```powershell
if (!(Get-NetFirewallRule -DisplayName "Uqeb API TCP 5000 LAN" -ErrorAction SilentlyContinue)) {
    New-NetFirewallRule `
        -DisplayName "Uqeb API TCP 5000 LAN" `
        -Direction Inbound `
        -Protocol TCP `
        -LocalPort 5000 `
        -Action Allow `
        -Profile Private `
        -RemoteAddress LocalSubnet
}
```

## 16. إيقاف API

```powershell
schtasks /End /TN "UqebApi" 2>$null

$apiProcess = Get-NetTCPConnection `
    -LocalPort 5000 `
    -State Listen `
    -ErrorAction SilentlyContinue |
    Select-Object -First 1 -ExpandProperty OwningProcess

if ($apiProcess) {
    Stop-Process -Id $apiProcess -Force
}

Start-Sleep -Seconds 3

if (Get-NetTCPConnection -LocalPort 5000 -State Listen -ErrorAction SilentlyContinue) {
    throw "تعذر إيقاف API على المنفذ 5000."
}
```

## 17. نشر Backend مع الحفاظ على الإعدادات

```powershell
robocopy `
    "$stage\api" `
    $apiTarget `
    /E `
    /R:2 `
    /W:2 `
    /XF appsettings.json appsettings.Development.json appsettings.Production.json

if ($LASTEXITCODE -ge 8) {
    throw "فشل نشر Backend. رمز Robocopy: $LASTEXITCODE"
}

Copy-Item $backupSettings $existingSettings -Force

if (!(Test-Path $existingSettings)) {
    throw "فشل تثبيت appsettings.Production.json."
}
```

لا يوجد fallback تلقائي من Staging لإعدادات الإنتاج؛ يجب أن يأتي الملف من الإعداد المنشور الحالي أو من مجلد provisioning الآمن.

## 18. نشر Frontend

```powershell
Remove-Item "$webTarget\*" -Recurse -Force -ErrorAction SilentlyContinue

robocopy "$stage\web" $webTarget /MIR /R:2 /W:2

if ($LASTEXITCODE -ge 8) {
    throw "فشل نشر Frontend. رمز Robocopy: $LASTEXITCODE"
}
```

## 19. تشغيل API والتحقق

```powershell
schtasks /Run /TN "UqebApi"
Start-Sleep -Seconds 8

$listener = Get-NetTCPConnection -LocalPort 5000 -State Listen -ErrorAction SilentlyContinue

if (!$listener) {
    throw "API لا يستمع على المنفذ 5000. راجع C:\Uqeb\logs\api-runtime.log"
}

if ($listener.LocalAddress -notcontains "0.0.0.0" -and $listener.LocalAddress -notcontains "::") {
    throw "API لا يستمع على عنوان شبكي صالح: $($listener.LocalAddress -join ', ')"
}
```

شغّل بوابة الصحة الرسمية:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass `
    -File "C:\UqebTools\verify-deployment-health.ps1" `
    -ApiBaseUrl "http://10.0.177.17:5000"
```

يجب أن تنجح `/health/live` و`/health/ready` و`/health`، وأن يعيد الملخص `database=pass` مع نجاح فحوص Playwright وتسلسل أرقام التقارير والتقارير المؤسسية. بعد ذلك يمكن استخدام طلب دخول وهمي كـsmoke test إضافي، والنتيجة الصحيحة هي `401`:

```powershell
$body = @{
    username = "__deployment_probe__"
    password = "__deployment_probe__"
} | ConvertTo-Json

try {
    Invoke-WebRequest `
        -UseBasicParsing `
        -Uri "http://localhost:5000/api/auth/login" `
        -Method Post `
        -ContentType "application/json" `
        -Body $body

    throw "طلب الدخول الوهمي أعاد نجاحًا غير متوقع."
}
catch {
    if (!$_.Exception.Response) {
        throw "تعذر الاتصال بـ API: $($_.Exception.Message)"
    }

    $statusCode = [int]$_.Exception.Response.StatusCode
    if ($statusCode -ne 401) {
        throw "API استجاب بحالة غير متوقعة: $statusCode"
    }
}
```

## 20. فحص كامل الواجهة المنشورة

```powershell
$forbiddenPublishedRefs = Get-ChildItem $webTarget -Recurse -File |
    Select-String `
        -Pattern "localhost:5000|127\.0\.0\.1:5000" `
        -AllMatches

if ($forbiddenPublishedRefs) {
    throw "الواجهة المنشورة تحتوي عنوان API محليًا غير صالح."
}

$productionPublishedRefs = Get-ChildItem $webTarget -Recurse -File |
    Select-String `
        -Pattern "10\.0\.177\.17:5000" `
        -AllMatches

if (!$productionPublishedRefs) {
    throw "عنوان API الإنتاج غير موجود في الواجهة المنشورة."
}
```

من جهاز داخل الشبكة:

```powershell
Test-NetConnection 10.0.177.17 -Port 5000
```

ثم افتح `http://10.0.177.17:8080`، نفّذ `Ctrl + Shift + R`، وتأكد أن طلب الدخول يتجه إلى:

```text
http://10.0.177.17:5000/api/auth/login
```

---

# التراجع الآمن

يجب تحديد نسخة احتياطية صالحة قبل حذف أي ملف إنتاجي. لا تستخدم متغير `$backup` غير معرّف، ولا تختَر مجلدًا لا يحتوي `api` و`web`.

```powershell
$backupCandidate = Get-ChildItem "C:\Uqeb\backup" -Directory |
    Where-Object {
        (Test-Path (Join-Path $_.FullName "api")) -and
        (Test-Path (Join-Path $_.FullName "web")) -and
        (Test-Path (Join-Path $_.FullName "appsettings.Production.json"))
    } |
    Sort-Object LastWriteTime -Descending |
    Select-Object -First 1

if (!$backupCandidate) {
    throw "لا توجد نسخة احتياطية مكتملة صالحة للتراجع."
}

$backup = $backupCandidate.FullName
$rollbackApi = Join-Path $backup "api"
$rollbackWeb = Join-Path $backup "web"
$rollbackSettings = Join-Path $backup "appsettings.Production.json"

schtasks /End /TN "UqebApi" 2>$null

$apiProcess = Get-NetTCPConnection `
    -LocalPort 5000 `
    -State Listen `
    -ErrorAction SilentlyContinue |
    Select-Object -First 1 -ExpandProperty OwningProcess

if ($apiProcess) {
    Stop-Process -Id $apiProcess -Force
}

# لا تبدأ الحذف إلا بعد اكتمال جميع فحوص المصدر أعلاه.
Remove-Item "C:\Uqeb\publish\api\*" -Recurse -Force
Remove-Item "C:\Uqeb\publish\web\*" -Recurse -Force

Copy-Item "$rollbackApi\*" "C:\Uqeb\publish\api" -Recurse -Force
Copy-Item "$rollbackWeb\*" "C:\Uqeb\publish\web" -Recurse -Force
Copy-Item $rollbackSettings "C:\Uqeb\publish\api\appsettings.Production.json" -Force

schtasks /Run /TN "UqebApi"
Start-Sleep -Seconds 8

if (!(Get-NetTCPConnection -LocalPort 5000 -State Listen -ErrorAction SilentlyContinue)) {
    throw "فشل تشغيل API بعد التراجع."
}
```

## بوابة النجاح النهائية

لا يُعلن نجاح النشر إلا بعد تحقق جميع الآتي:

1. SHA256 متطابق للحزمة وملفها المطابق.
2. Runtime المطلوب موجود.
3. إعدادات إنتاج آمنة موجودة قبل أي استبدال.
4. النسخة الاحتياطية مكتملة أو تم إثبات أن العملية أول نشر.
5. API يستمع على `0.0.0.0:5000` أو `::`.
6. طلب login وهمي يعيد `401`، لا connection error.
7. شجرة Frontend كاملة خالية من `localhost:5000`.
8. شجرة Frontend تحتوي عنوان الإنتاج.
9. جهاز داخل الشبكة يصل إلى المنفذ 5000.
10. تسجيل الدخول الفعلي يعمل من جهاز الإنتاج ومن جهاز عميل.
11. المرفقات والبيانات القائمة سليمة ما لم يوجد قرار تصفير معتمد.
