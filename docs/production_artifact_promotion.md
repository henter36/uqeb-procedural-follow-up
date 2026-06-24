# إصدار الحزمة وترقيتها إلى الإنتاج

يوثق هذا الدليل المسار المعتمد لإصدار حزمة UQEB واختبارها ثم ترقيتها إلى الإنتاج وفق مبدأ:

> **Build once → test the same artifact → promote the same ZIP/SHA256**

لا تُعَد الحزمة بعد نجاح الاختبار على Windows VM. يجب نشر **نفس ملف ZIP ونفس ملف SHA256** اللذين اجتازا بوابة القبول.

## 1. إصدار الحزمة الرسمية على جهاز البناء Windows

افتح Windows PowerShell من جذر المستودع:

```powershell
$ErrorActionPreference = "Stop"

Set-Location "C:\Users\<USER>\uqeb"

git switch main
git pull --ff-only origin main

if (git status --porcelain) {
    throw "المستودع يحتوي تغييرات محلية؛ يمنع إصدار الحزمة."
}

git log -1 --oneline

.\scripts\build-production-package.ps1 `
    -ProductionApiBaseUrl "http://10.0.177.17:5000/api"
```

يجب أن ينتج:

```text
artifacts\production\Uqeb-*.zip
artifacts\production\Uqeb-*.zip.sha256
```

### التحقق من SHA256

```powershell
$Package = Get-ChildItem ".\artifacts\production\Uqeb-*.zip" |
    Sort-Object LastWriteTimeUtc -Descending |
    Select-Object -First 1

if (-not $Package) {
    throw "لم يتم إنشاء حزمة الإنتاج."
}

$ShaFile = "$($Package.FullName).sha256"

if (-not (Test-Path -LiteralPath $ShaFile)) {
    throw "ملف SHA256 غير موجود."
}

$ActualHash = (
    Get-FileHash -LiteralPath $Package.FullName -Algorithm SHA256
).Hash.ToUpperInvariant()

$ExpectedHash = (
    (Get-Content -LiteralPath $ShaFile -Raw).Trim() -split '\s+'
)[0].ToUpperInvariant()

if ($ActualHash -ne $ExpectedHash) {
    throw "فشل التحقق من SHA256."
}

Write-Host "PACKAGE: $($Package.FullName)"
Write-Host "SHA256: $ActualHash"
```

سجّل مع الإصدار:

```text
Git commit
اسم الحزمة
حجم الحزمة
SHA256
تاريخ الإصدار
```

## 2. اختبار نفس الحزمة على Windows VM أو خادم تجريبي

انقل ملفي ZIP وSHA256 نفسيهما إلى:

```text
C:\Uqeb\incoming\
```

لا تفك ضغط الحزمة يدويًا ولا تعدّل محتواها.

شغّل Windows PowerShell 5.1 كمسؤول:

```powershell
$ErrorActionPreference = "Stop"

$Package = Get-ChildItem "C:\Uqeb\incoming\Uqeb-*.zip" |
    Sort-Object LastWriteTimeUtc -Descending |
    Select-Object -First 1

if (-not $Package) {
    throw "لم يتم العثور على الحزمة."
}

powershell.exe `
    -NoProfile `
    -ExecutionPolicy Bypass `
    -File "C:\UqebTools\install-production-package.ps1" `
    -PackagePath $Package.FullName
```

## 3. بوابة القبول التشغيلية على جهاز الاختبار

```powershell
curl.exe -i http://127.0.0.1:5000/health/live
curl.exe -i http://127.0.0.1:5000/health/ready
curl.exe -i http://127.0.0.1:5000/health
```

النتائج المطلوبة:

```text
live                       PASS
ready                      PASS
database                   PASS
playwrightChromium         PASS أو not_applicable
reportNumberSequence       PASS أو not_applicable
institutionalReporting     PASS أو not_applicable
```

ثم شغّل:

```powershell
powershell.exe `
    -NoProfile `
    -ExecutionPolicy Bypass `
    -File "C:\UqebTools\verify-playwright-readiness.ps1"

powershell.exe `
    -NoProfile `
    -ExecutionPolicy Bypass `
    -File "C:\UqebTools\verify-deployment-health.ps1"
```

ومن الواجهة اختبر:

- تسجيل الدخول والعمليات الأساسية.
- تصدير HTML.
- تصدير XLSX.
- تصدير DOCX.
- تصدير PDF فعلي.
- سلامة العربية وRTL والجداول في PDF.
- عدم استهلاك رقم تقرير بواسطة استدعاءات health.
- عدم تراكم عمليات `chrome.exe`.

## 4. اختبار Rollback على جهاز الاختبار

اختبر الرجوع المتحكم به وتأكد من استعادة:

```text
API السابق
الواجهة السابقة
Chromium السابق
run-api.cmd
release-manifest
PLAYWRIGHT_BROWSERS_PATH
تشغيل Scheduled Task
health السابق
```

قاعدة البيانات لا تُسترجع تلقائيًا. يجب توثيق:

```text
مسار نسخة SQL الاحتياطية
SHA256 للنسخة
وقت إنشاء النسخة
إجراء الاستعادة اليدوي
```

## 5. ترقية نفس الحزمة إلى جهاز الإنتاج

بعد نجاح القبول والـrollback، انقل **نفس ZIP ونفس SHA256** إلى:

```text
C:\Uqeb\incoming\
```

على جهاز الإنتاج. لا تنشئ ZIP جديدًا ولا تعدّل الحزمة أو تعيد ضغطها.

### التحقق من مطابقة الحزمة المختبرة

```powershell
$Package = Get-ChildItem "C:\Uqeb\incoming\Uqeb-*.zip" |
    Sort-Object LastWriteTimeUtc -Descending |
    Select-Object -First 1

$ShaFile = "$($Package.FullName).sha256"

$ActualHash = (
    Get-FileHash -LiteralPath $Package.FullName -Algorithm SHA256
).Hash.ToUpperInvariant()

$ExpectedHash = (
    (Get-Content -LiteralPath $ShaFile -Raw).Trim() -split '\s+'
)[0].ToUpperInvariant()

if ($ActualHash -ne $ExpectedHash) {
    throw "حزمة الإنتاج لا تطابق الحزمة المختبرة."
}

Write-Host "PRODUCTION PACKAGE VERIFIED: $ActualHash"
```

### تنفيذ النشر

```powershell
powershell.exe `
    -NoProfile `
    -ExecutionPolicy Bypass `
    -File "C:\UqebTools\install-production-package.ps1" `
    -PackagePath $Package.FullName
```

## 6. اختبار ما بعد النشر الإنتاجي

```powershell
curl.exe -i http://127.0.0.1:5000/health/live
curl.exe -i http://127.0.0.1:5000/health/ready
curl.exe -i http://127.0.0.1:5000/health

powershell.exe `
    -NoProfile `
    -ExecutionPolicy Bypass `
    -File "C:\UqebTools\verify-playwright-readiness.ps1"

powershell.exe `
    -NoProfile `
    -ExecutionPolicy Bypass `
    -File "C:\UqebTools\verify-deployment-health.ps1"
```

نفّذ smoke test محدودًا:

```text
تسجيل الدخول
فتح لوحة المتابعة
فتح معاملة
تشغيل تقرير
تصدير PDF واحد
التحقق من العربية وRTL
فحص السجلات
```

## قرار الاعتماد

يصبح القرار **GO** فقط عند نجاح:

```text
Package SHA256
Windows VM acceptance
health/live
health/ready
health summary
Playwright readiness
PDF export
Arabic/RTL rendering
Report-number sequence safety
Rollback proof
Production smoke test
```

## التسلسل المعتمد

```text
Merge
→ Build once from main
→ Record Git SHA and Package SHA256
→ Test the same artifact on Windows VM
→ Prove health + PDF + sequence safety + rollback
→ Promote the same ZIP/SHA256 to production
→ Verify SHA256 again
→ Deploy
→ Post-deployment smoke test
→ Approve or rollback
```
