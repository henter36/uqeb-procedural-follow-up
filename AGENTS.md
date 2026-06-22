# تعليمات المشروع — المتابعة الإجرائية (Uqeb)

هذه التعليمات مرجعية ملزمة لأي Agent أو مطور يعمل على المشروع.

## بيئة المشروع المعتمدة

- مستودع التطوير: `henter36/uqeb-procedural-follow-up`
- مسار المشروع على جهاز التطوير: `C:\Users\alqud\uqeb`
- Backend: `C:\Users\alqud\uqeb\backend\Uqeb.Api`
- Frontend: `C:\Users\alqud\uqeb\frontend\uqeb-ui`
- جهاز الإنتاج لا يستخدم GitHub؛ النقل يتم بحزمة ZIP موثقة ببصمة SHA256.
- عنوان جهاز الإنتاج الحالي: `10.0.177.17`
- واجهة الإنتاج: `http://10.0.177.17:8080`
- API الإنتاج: `http://10.0.177.17:5000`
- مجلد نشر API: `C:\Uqeb\publish\api`
- مجلد نشر الواجهة: `C:\Uqeb\publish\web`
- اسم مهمة التشغيل المجدولة: `UqebApi`

## قواعد نشر إلزامية

1. لا تنشر ملفات المصدر أو `node_modules` إلى الإنتاج.
2. لا تعدّل قاعدة الإنتاج أو تشغّل migrations إلا عند وجود Migration معتمدة ومراجعة صريحة.
3. لا تستبدل `appsettings.Production.json` بملف من جهاز التطوير.
4. لا تحذف مجلد المرفقات أو السجلات أثناء النشر.
5. يجب أخذ نسخة احتياطية من API والواجهة وإعدادات الإنتاج قبل الاستبدال.
6. عند وجود Migration أو تعديل عالي المخاطر، يجب أخذ نسخة احتياطية كاملة من قاعدة البيانات قبل التنفيذ.
7. لا تستخدم `npm audit fix` أثناء تجهيز إصدار الإنتاج؛ مراجعة الاعتماديات تتم في تغيير مستقل.
8. لا تعتمد على رسالة واجهة تسجيل الدخول لتشخيص الشبكة؛ الصفحة الحالية تعرض «بيانات الدخول غير صحيحة» لأي خطأ اتصال أيضًا.
9. لا تستخدم `/health` للتحقق إلا بعد وجود endpoint فعلي في الكود. في الوضع الحالي استخدم فحص المنفذ وطلب login تجريبيًا.
10. لا يجوز أن يحتوي بناء Frontend للإنتاج على `localhost:5000` أو `127.0.0.1:5000`.
11. قيمة `VITE_API_BASE_URL` تُحقن وقت البناء، ولا يمكن تصحيحها بعد البناء إلا بإعادة بناء Frontend.
12. يجب أن يستمع API على `0.0.0.0:5000` حتى تتمكن أجهزة الشبكة من الوصول إليه.

---

# مسار النشر القياسي

## 1. تحديث المصدر على جهاز التطوير

```powershell
cd C:\Users\alqud\uqeb

git checkout main
git pull origin main
git status -sb
```

يجب أن تكون الشجرة نظيفة قبل بناء الإصدار.

## 2. ضبط عنوان API للواجهة قبل البناء

```powershell
cd C:\Users\alqud\uqeb\frontend\uqeb-ui

@'
VITE_API_BASE_URL=http://10.0.177.17:5000/api
'@ | Set-Content ".env.production.local" -Encoding ASCII
```

إذا تغيّر عنوان جهاز الإنتاج، يجب تحديث:

- `.env.production.local` على جهاز التطوير.
- `AllowedOrigins` في `appsettings.Production.json` على جهاز الإنتاج.
- قواعد الجدار الناري واختبارات الاتصال.

## 3. إنشاء مجلد إصدار جديد

```powershell
$ErrorActionPreference = "Stop"

$projectRoot = "C:\Users\alqud\uqeb"
$releaseBase = "C:\UqebRelease"
$stamp = Get-Date -Format "yyyyMMdd-HHmmss"
$releaseRoot = Join-Path $releaseBase "Uqeb-$stamp"
$releaseZip = "$($releaseRoot).zip"

New-Item $releaseBase -ItemType Directory -Force | Out-Null
New-Item (Join-Path $releaseRoot "api") -ItemType Directory -Force | Out-Null
New-Item (Join-Path $releaseRoot "web") -ItemType Directory -Force | Out-Null
```

## 4. بناء ونشر Backend إلى مجلد الإصدار

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

تحقق من عدم وجود localhost:

```powershell
Select-String `
  -Path ".\dist\assets\*.js" `
  -Pattern "localhost:5000|127\.0\.0\.1:5000" `
  -AllMatches
```

يجب ألا تظهر أي نتيجة.

تحقق من عنوان الإنتاج:

```powershell
Select-String `
  -Path ".\dist\assets\*.js" `
  -Pattern "10\.0\.177\.17:5000" `
  -AllMatches
```

يجب أن تظهر نتيجة تتضمن:

```text
http://10.0.177.17:5000/api
```

## 6. إضافة `web.config` للواجهة

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

## 7. إضافة معلومات الإصدار والتحقق من الملفات

```powershell
cd C:\Users\alqud\uqeb

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
    throw "ملفات Backend غير مكتملة"
}

if (!(Test-Path "$releaseRoot\web\index.html")) {
    throw "ملفات Frontend غير مكتملة"
}
```

## 8. إنشاء ZIP وبصمة SHA256

```powershell
Remove-Item $releaseZip -Force -ErrorAction SilentlyContinue

Compress-Archive `
  -Path "$releaseRoot\*" `
  -DestinationPath $releaseZip `
  -CompressionLevel Optimal `
  -Force

if (!(Test-Path $releaseZip)) {
    throw "فشل إنشاء ملف ZIP"
}

$releaseHash = Get-FileHash $releaseZip -Algorithm SHA256
$hashPath = "$releaseRoot.sha256.txt"

$releaseHash.Hash |
  Set-Content $hashPath -Encoding ASCII

Get-Item $releaseZip, $hashPath |
  Select-Object FullName, Length, LastWriteTime
```

يُنقل إلى جهاز الإنتاج ملفا:

- `Uqeb-<timestamp>.zip`
- `Uqeb-<timestamp>.sha256.txt`

ويتم وضعهما في:

```text
C:\Uqeb\incoming
```

---

# خطوات جهاز الإنتاج

## 9. التحقق من سلامة الحزمة

```powershell
$ErrorActionPreference = "Stop"

$zip = "C:\Uqeb\incoming\Uqeb-<timestamp>.zip"
$hashFile = "C:\Uqeb\incoming\Uqeb-<timestamp>.sha256.txt"

$expectedHash = (Get-Content $hashFile -Raw).Trim()
$actualHash = (Get-FileHash $zip -Algorithm SHA256).Hash

if ($expectedHash -ne $actualHash) {
    throw "فشل التحقق من الحزمة. لا تتابع النشر."
}
```

## 10. التأكد من Runtime

```powershell
dotnet --list-runtimes
```

يجب توفر:

```text
Microsoft.AspNetCore.App 10.0.x
Microsoft.NETCore.App 10.0.x
```

## 11. فك الحزمة في Staging

```powershell
$stamp = Get-Date -Format "yyyyMMdd-HHmmss"
$stage = "C:\Uqeb\staging\release-$stamp"
$backup = "C:\Uqeb\backup\before-$stamp"

New-Item "C:\Uqeb\staging" -ItemType Directory -Force | Out-Null
New-Item "C:\Uqeb\backup" -ItemType Directory -Force | Out-Null

Expand-Archive -Path $zip -DestinationPath $stage -Force

if (!(Test-Path "$stage\api\Uqeb.Api.dll")) {
    throw "ملفات Backend غير مكتملة داخل الحزمة"
}

if (!(Test-Path "$stage\web\index.html")) {
    throw "ملفات Frontend غير مكتملة داخل الحزمة"
}

Get-Content "$stage\RELEASE.txt"
```

## 12. أخذ نسخة احتياطية

```powershell
New-Item "$backup\api" -ItemType Directory -Force | Out-Null
New-Item "$backup\web" -ItemType Directory -Force | Out-Null

robocopy "C:\Uqeb\publish\api" "$backup\api" /E /R:2 /W:2
if ($LASTEXITCODE -ge 8) { throw "فشل نسخ Backend احتياطيًا" }

robocopy "C:\Uqeb\publish\web" "$backup\web" /E /R:2 /W:2
if ($LASTEXITCODE -ge 8) { throw "فشل نسخ Frontend احتياطيًا" }

if (!(Test-Path "C:\Uqeb\publish\api\appsettings.Production.json")) {
    throw "appsettings.Production.json غير موجود. لا تتابع."
}

Copy-Item `
  "C:\Uqeb\publish\api\appsettings.Production.json" `
  "$backup\appsettings.Production.json" `
  -Force
```

## 13. التحقق من إعدادات الشبكة في الإنتاج

يجب أن يتضمن `appsettings.Production.json`:

```json
"AllowedOrigins": [
  "http://localhost:8080",
  "http://10.0.177.17:8080"
]
```

ويجب أن يستخدم ملف التشغيل:

```text
C:\Uqeb\run-api.cmd
```

بالمحتوى:

```bat
@echo off
cd /d C:\Uqeb\publish\api

set ASPNETCORE_ENVIRONMENT=Production
set DOTNET_ENVIRONMENT=Production
set ASPNETCORE_URLS=http://0.0.0.0:5000

C:\Uqeb\publish\api\Uqeb.Api.exe >> C:\Uqeb\logs\api-runtime.log 2>&1
```

قاعدة الجدار الناري المطلوبة:

```powershell
New-NetFirewallRule `
  -DisplayName "Uqeb API TCP 5000 LAN" `
  -Direction Inbound `
  -Protocol TCP `
  -LocalPort 5000 `
  -Action Allow `
  -Profile Private `
  -RemoteAddress LocalSubnet
```

لا تُنشأ القاعدة مجددًا إذا كانت موجودة وصحيحة.

## 14. إيقاف API قبل الاستبدال

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
```

## 15. نشر Backend مع الحفاظ على إعدادات الإنتاج

```powershell
robocopy `
  "$stage\api" `
  "C:\Uqeb\publish\api" `
  /E `
  /R:2 `
  /W:2 `
  /XF appsettings.json appsettings.Development.json appsettings.Production.json

if ($LASTEXITCODE -ge 8) {
    throw "فشل نشر Backend. رمز Robocopy: $LASTEXITCODE"
}

Copy-Item `
  "$backup\appsettings.Production.json" `
  "C:\Uqeb\publish\api\appsettings.Production.json" `
  -Force
```

لا تستخدم `/MIR` على مجلد API حتى لا تُحذف مجلدات تشغيلية أو مرفقات غير مقصودة.

## 16. نشر Frontend

```powershell
Remove-Item "C:\Uqeb\publish\web\*" `
  -Recurse `
  -Force `
  -ErrorAction SilentlyContinue

robocopy `
  "$stage\web" `
  "C:\Uqeb\publish\web" `
  /MIR `
  /R:2 `
  /W:2

if ($LASTEXITCODE -ge 8) {
    throw "فشل نشر Frontend. رمز Robocopy: $LASTEXITCODE"
}
```

## 17. تشغيل API والتحقق

```powershell
schtasks /Run /TN "UqebApi"
Start-Sleep -Seconds 8

Get-NetTCPConnection `
  -LocalPort 5000 `
  -State Listen
```

يجب أن يكون `LocalAddress` هو `0.0.0.0` أو `::`.

لا تعتمد على `/health` حاليًا. استخدم طلب دخول ببيانات وهمية، والنتيجة الصحيحة هي `401` من API:

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
}
catch {
    if (!$_.Exception.Response) {
        throw "تعذر الاتصال بـ API"
    }

    $statusCode = [int]$_.Exception.Response.StatusCode

    if ($statusCode -ne 401) {
        throw "API استجاب بحالة غير متوقعة: $statusCode"
    }

    Write-Host "API probe passed with expected 401."
}
```

## 18. التحقق من الواجهة المنشورة

```powershell
Select-String `
  -Path "C:\Uqeb\publish\web\assets\*.js" `
  -Pattern "localhost:5000|127\.0\.0\.1:5000" `
  -AllMatches
```

يجب ألا تظهر نتيجة.

```powershell
Select-String `
  -Path "C:\Uqeb\publish\web\assets\*.js" `
  -Pattern "10\.0\.177\.17:5000" `
  -AllMatches
```

يجب أن تظهر نتيجة.

من جهاز داخل الشبكة:

```powershell
Test-NetConnection 10.0.177.17 -Port 5000
```

المطلوب:

```text
TcpTestSucceeded : True
```

ثم افتح:

```text
http://10.0.177.17:8080
```

ونفّذ تحديثًا إجباريًا للمتصفح:

```text
Ctrl + Shift + R
```

في DevTools يجب أن يكون طلب تسجيل الدخول:

```text
http://10.0.177.17:5000/api/auth/login
```

ولا يجوز أن يكون:

```text
http://localhost:5000/api/auth/login
```

---

# التراجع السريع

استخدم آخر مجلد نسخة احتياطية صالح في `C:\Uqeb\backup`:

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

Remove-Item "C:\Uqeb\publish\api\*" -Recurse -Force
Remove-Item "C:\Uqeb\publish\web\*" -Recurse -Force

Copy-Item "$backup\api\*" "C:\Uqeb\publish\api" -Recurse -Force
Copy-Item "$backup\web\*" "C:\Uqeb\publish\web" -Recurse -Force

schtasks /Run /TN "UqebApi"
Start-Sleep -Seconds 8

Get-NetTCPConnection -LocalPort 5000 -State Listen
```

## ملاحظات تشغيلية ثابتة

- جهاز الإنتاج الحالي: `10.0.177.17`.
- API يجب أن يستمع على `0.0.0.0:5000`.
- الواجهة تُبنى بعنوان API الإنتاج قبل إنشاء ZIP.
- رسالة «بيانات الدخول غير صحيحة» لا تثبت خطأ كلمة المرور؛ افحص Network وRequest URL وStatus Code.
- `ERR_CONNECTION_REFUSED` إلى `localhost:5000` من جهاز عميل يعني أن Frontend بُني بعنوان خاطئ.
- لا تُنفذ تغييرات تنظيف بيانات الإنتاج أو تصفير المعاملات دون نسخة احتياطية كاملة وتأكيد صريح.
