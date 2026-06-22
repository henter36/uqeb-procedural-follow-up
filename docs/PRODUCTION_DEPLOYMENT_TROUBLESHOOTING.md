# سجل أخطاء النشر ومعالجتها — Uqeb

هذا السجل مكمل لملف `AGENTS.md` ويحتوي على المشاكل الفعلية التي ظهرت أثناء تجهيز ونشر النظام على جهاز الإنتاج، مع السبب والإجراء الوقائي للنشر القادم.

## 1. متغيرات PowerShell غير معرّفة

### العرض
- إنشاء ZIP في مسار غير مقصود أو ظهور مسار مثل `.zip`.
- فشل أوامر تستخدم `$releaseRoot` أو `$releaseZip` بعد فتح جلسة PowerShell جديدة.

### السبب
متغيرات PowerShell لا تنتقل تلقائيًا إلى جلسة جديدة.

### المعالجة
- تعريف جميع متغيرات الإصدار في بداية المقطع نفسه.
- تنفيذ مسار إنشاء الإصدار كاملًا في جلسة واحدة.
- التحقق من القيم قبل الضغط:

```powershell
$releaseRoot
$releaseZip
```

## 2. التصاق أمرين في سطر واحد

### العرض
أخطاء مثل:

```text
Select-Object : A positional parameter cannot be found...
```

أو التصاق `cd` بنهاية `LastWriteTime`، أو التصاق `Get-FileHash` بأمر سابق.

### السبب
لصق عدة أوامر دون سطر جديد صحيح.

### المعالجة
- تنفيذ كل أمر مستقلًا.
- عدم لصق أمر جديد بعد نهاية pipeline.
- بعد أي خطأ، إعادة تنفيذ الأمر الذي لم يعمل بدل متابعة السلسلة بافتراض نجاحه.

## 3. لصق `else` منفصلًا

### العرض
خطأ parser عند لصق `else` وحده بعد أمر سابق.

### السبب
`else` يجب أن يكون جزءًا من كتلة `if` نفسها.

### المعالجة
- عدم لصق أجزاء كتلة `if/else` منفصلة.
- في حالة نجاح `npm ci` سابقًا، لا يعاد بناء الاستنتاج على خطأ `else` وحده.

## 4. إنشاء ZIP قبل إعادة بناء Frontend

### العرض
الحزمة تحتوي نسخة قديمة رغم تعديل `.env.production.local` لاحقًا.

### السبب
تم ضغط `dist` قبل تنفيذ `npm run build` بالقيمة الجديدة.

### المعالجة
الترتيب الإلزامي:

1. كتابة `.env.production.local`.
2. تشغيل `npm ci`.
3. تشغيل `npm run build`.
4. التحقق من محتوى ملفات JavaScript.
5. إنشاء ZIP بعد ذلك فقط.

أي ZIP أُنشئ قبل الخطوة 3 يُحذف ويعاد إنشاؤه.

## 5. ملف SHA256 فارغ أو قديم

### العرض
- `$frontendHash` غير معرّف.
- ملف `.sha256.txt` فارغ.
- تطابق غير صحيح بسبب استخدام بصمة لحزمة سابقة.

### السبب
لم يُنفذ `Get-FileHash` فعليًا أو تم إنشاء الحزمة بعد إنشاء البصمة.

### المعالجة

```powershell
$hash = Get-FileHash $zip -Algorithm SHA256
$hash.Hash | Set-Content $hashFile -Encoding ASCII
Get-Content $hashFile
```

- يجب أن يحتوي الملف على 64 حرفًا سداسيًا.
- يجب إنشاء البصمة بعد آخر تعديل للحزمة.
- يجب مقارنة Expected وActual على جهاز الإنتاج قبل فك الحزمة.

## 6. تحذير `npm audit`

### العرض

```text
1 high severity vulnerability
```

### القرار
- لا يُنفذ `npm audit fix` أثناء تجهيز إصدار الإنتاج.
- يُراجع التحذير في تغيير مستقل لأن الإصلاح التلقائي قد يغيّر الإصدارات أو يكسر البناء.

## 7. Runtime غير متطابق

### الخطر
الإصدار Framework-dependent ويحتاج Runtime مطابقًا على جهاز الإنتاج.

### التحقق

```powershell
dotnet --list-runtimes
```

يجب توفر:

```text
Microsoft.AspNetCore.App 10.0.x
Microsoft.NETCore.App 10.0.x
```

عند عدم توفره، يتوقف النشر أو يعاد النشر كـ self-contained بعد تحديد معمارية جهاز الإنتاج.

## 8. نجاح `schtasks /Run` لا يعني نجاح API

### العرض

```text
SUCCESS: Attempted to run the scheduled task "UqebApi".
```

لكن المنفذ 5000 لا يستمع.

### السبب
الرسالة تثبت محاولة تشغيل المهمة فقط، ولا تثبت بقاء العملية أو نجاح الإقلاع.

### المعالجة
بعد التشغيل يجب فحص:

```powershell
Get-NetTCPConnection -LocalPort 5000 -State Listen
Get-ScheduledTaskInfo -TaskName "UqebApi"
schtasks /Query /TN "UqebApi" /V /FO LIST
```

وعند الفشل مراجعة:

```text
C:\Uqeb\logs\api-runtime.log
```

## 9. Working Directory للمهمة المجدولة

### العرض
API يعمل عند تشغيله يدويًا لكنه يفشل من Task Scheduler.

### السبب المرجح
المهمة تبدأ من `C:\Windows\System32` بدل مجلد API، فلا تجد الإعدادات أو الموارد ذات المسارات النسبية.

### المعالجة
استخدام `C:\Uqeb\run-api.cmd`:

```bat
@echo off
cd /d C:\Uqeb\publish\api
set ASPNETCORE_ENVIRONMENT=Production
set DOTNET_ENVIRONMENT=Production
set ASPNETCORE_URLS=http://0.0.0.0:5000
C:\Uqeb\publish\api\Uqeb.Api.exe >> C:\Uqeb\logs\api-runtime.log 2>&1
```

## 10. API يستمع محليًا فقط

### العرض
النظام يعمل على جهاز الإنتاج ولا يمكن الوصول إلى API من أجهزة الشبكة.

### السبب
الاستماع على `127.0.0.1:5000` فقط.

### المعالجة

```text
ASPNETCORE_URLS=http://0.0.0.0:5000
```

ويجب أن يظهر `LocalAddress` كأحد القيم:

```text
0.0.0.0
::
```

## 11. المنفذ 5000 محجوب بالجدار الناري

### المعالجة
فتح المنفذ للشبكة المحلية فقط:

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

واختباره من جهاز عميل:

```powershell
Test-NetConnection 10.0.177.17 -Port 5000
```

## 12. `/health` يعيد 404

### العرض

```text
404 Not Found
```

### السبب
المسار `/health` غير مسجل في الكود الحالي.

### القرار
- لا يُعتبر 404 دليلًا على تعطل API.
- لا تعتمد إجراءات النشر على `/health` قبل إضافة:

```csharp
builder.Services.AddHealthChecks();
app.MapHealthChecks("/health");
```

- عند إضافته، الاستجابة الافتراضية غالبًا `Healthy` كنص وليست JSON مخصصًا.

## 13. رسالة «بيانات الدخول غير صحيحة» كانت مضللة

### العرض
الواجهة تعرض خطأ كلمة مرور بينما بيانات الدخول صحيحة وتعمل على جهاز الإنتاج.

### السبب
صفحة الدخول الحالية تعرض الرسالة نفسها لأي exception، بما في ذلك فشل الشبكة وCORS و404 و429 و502.

### المعالجة
- لا تستنتج أن كلمة المرور خاطئة من رسالة الواجهة وحدها.
- افتح DevTools > Network وافحص:
  - Request URL
  - Status Code
  - Response
  - Console

## 14. Frontend بُني باستخدام `localhost:5000`

### العرض
من جهاز عميل:

```text
POST http://localhost:5000/api/auth/login
net::ERR_CONNECTION_REFUSED
```

### السبب
`localhost` في المتصفح يعني جهاز العميل نفسه، وليس جهاز الإنتاج.

### المعالجة
قبل البناء:

```text
VITE_API_BASE_URL=http://10.0.177.17:5000/api
```

ثم إعادة بناء Frontend ونشر `dist` الجديد.

### تحقق إلزامي قبل إنشاء ZIP

```powershell
Select-String -Path ".\dist\assets\*.js" -Pattern "localhost:5000|127\.0\.0\.1:5000" -AllMatches
```

يجب ألا تظهر نتائج.

```powershell
Select-String -Path ".\dist\assets\*.js" -Pattern "10\.0\.177\.17:5000" -AllMatches
```

يجب أن تظهر نتيجة.

## 15. `0 B transferred` في DevTools

### الدلالة
غالبًا لم تصل استجابة HTTP أصلًا، مثل:

- Connection refused.
- CORS قبل إتاحة الاستجابة للواجهة.
- مسار غير متاح.

لا يُعامل هذا كـ 401 كلمة مرور قبل فحص Console وRequest URL.

## 16. CORS و`AllowedOrigins`

عند اتصال الواجهة مباشرة بالـ API على منفذ مختلف، يجب أن يحتوي إنتاج API على:

```json
"AllowedOrigins": [
  "http://localhost:8080",
  "http://10.0.177.17:8080"
]
```

بعد أي تعديل يجب إعادة تشغيل API.

## 17. Cache المتصفح بعد نشر Frontend

### العرض
المتصفح يستمر في استخدام ملف JavaScript القديم.

### المعالجة

```text
Ctrl + Shift + R
```

وعند الحاجة حذف Cache الموقع، ثم التأكد من اسم ملف asset الجديد ومن Request URL في Network.

## 18. رموز خروج Robocopy

- القيم من 0 إلى 7 ليست فشلًا قاتلًا.
- القيمة 8 أو أكثر تعني فشلًا يجب إيقاف النشر عنده.

النمط المعتمد:

```powershell
if ($LASTEXITCODE -ge 8) {
    throw "Robocopy failed: $LASTEXITCODE"
}
```

## 19. عدم استخدام `/MIR` على مجلد API

### الخطر
`/MIR` قد يحذف مجلدات أو ملفات تشغيلية غير موجودة في الحزمة، ومنها المرفقات أو بيانات محلية.

### المعالجة
- Backend: استخدام `/E` مع استثناء إعدادات الإنتاج.
- Frontend: يمكن استخدام `/MIR` لأن مجلده static build فقط.

## 20. الحفاظ على إعدادات الإنتاج

لا تنسخ هذه الملفات من التطوير فوق الإنتاج:

```text
appsettings.json
appsettings.Development.json
appsettings.Production.json
```

يجب الاحتفاظ بنسخة من `appsettings.Production.json` ثم إعادته بعد نسخ ملفات Backend.

## 21. تصفير معاملات الإنتاج

### الخطر
حذف `Transactions` مباشرة قد يفشل بسبب `AuditLogs` المرتبطة بعلاقة `NoAction`، كما أن سجلات المرفقات لا تحذف الملفات الفعلية من القرص تلقائيًا.

### القاعدة
- نسخة احتياطية كاملة لقاعدة البيانات مع `RESTORE VERIFYONLY` قبل الحذف.
- حذف `AuditLogs` المرتبطة بالمعاملات أولًا.
- حذف `Transactions` داخل transaction واحدة.
- علاقات Assignments وFollowUps وAttachments وجداول الجهات التابعة تُحذف بالـ Cascade.
- تنظيف ملفات المرفقات من المسار الفعلي بعد نجاح حذف قاعدة البيانات فقط.
- إعادة sequence الخاصة برقم التتبع عند اعتماد تصفير كامل.
- إبقاء Users وDepartments وCategories وExternalParties وLetterTemplates وإعدادات النظام.

## 22. قاعدة تحقق نهائية قبل إعلان نجاح النشر

لا يُعلن نجاح النشر إلا بعد تحقق جميع الآتي:

1. SHA256 متطابق.
2. Runtime المطلوب موجود.
3. النسخة الاحتياطية موجودة.
4. API يستمع على `0.0.0.0:5000` أو `::`.
5. طلب login تجريبي ببيانات وهمية يعيد 401، لا connection error.
6. بناء Frontend لا يحتوي `localhost:5000`.
7. بناء Frontend يحتوي `10.0.177.17:5000`.
8. جهاز داخل الشبكة يصل إلى المنفذ 5000.
9. تسجيل الدخول الفعلي يعمل من جهاز الإنتاج ومن جهاز عميل.
10. المرفقات والبيانات القائمة سليمة عند عدم وجود قرار تصفير.
