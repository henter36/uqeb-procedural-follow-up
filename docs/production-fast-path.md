# دليل مسار النشر السريع (Production Fast Path)

## المسارات الرسمية على جهاز الإنتاج

| الغرض | المسار |
|--------|--------|
| الحزمة الواردة | `C:\Uqeb\incoming\` |
| فك الحزمة (مؤقت) | `C:\Uqeb\staging\<stamp>\` |
| إصدار API النشط | `C:\Uqeb\publish\api\` ← junction إلى `C:\Uqeb\current\api\` |
| إصدار Web النشط | `C:\Uqeb\publish\web\` ← junction إلى `C:\Uqeb\current\web\` |
| تاريخ الإصدارات | `C:\Uqeb\releases\<version>\` |
| manifest النشر | `C:\Uqeb\publish\release-manifest.json` |
| أدوات النشر | `C:\UqebTools\` |
| Common.ps1 | `C:\UqebTools\deployment\Common.ps1` |
| إعدادات الإنتاج | `C:\Uqeb\config\appsettings.Production.json` |
| Chromium | `C:\Uqeb\tools\ms-playwright\` |
| نسخ احتياطية DB | `C:\Uqeb\backup\db\` |
| سجل API | `C:\Uqeb\logs\api-runtime.log` |

**لا يجب** أن يعتمد الإنتاج على:
- `C:\Users\alqud\uqeb` (مجلد المستودع على جهاز المطور)
- وجود git أو dotnet CLI على جهاز الإنتاج
- اتصال بالإنترنت أثناء النشر

---

## إعدادات جهاز البناء

```
- dotnet SDK 10.x
- Node.js 24.x
- git
- PowerShell 7+ (pwsh)
```

---

## إعدادات جهاز الإنتاج

```
- Windows Server + IIS
- SQL Server
- PowerShell 5.1+
- Scheduled Task: UqebApi
  - Execute : C:\Uqeb\publish\api\Uqeb.Api.exe
  - WorkingDirectory: C:\Uqeb\publish\api
- IIS Site physicalPath: C:\Uqeb\publish\web
```

---

## مسار البناء الكامل (جهاز البناء)

```powershell
# 1. مزامنة الكود
git fetch origin --prune
git switch main
git pull --ff-only origin main

# 2. بناء الحزمة
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\build-production-package.ps1

# 3. تجهيز مجلد النقل
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\prepare-production-transfer.ps1
```

المخرجات:
- `artifacts\production\Uqeb-<timestamp>.zip`
- `artifacts\production\Uqeb-<timestamp>.zip.sha256.txt`
- `artifacts\transfer\UqebDeploy-<timestamp>\` (مجلد النقل الجاهز)

---

## نقل الحزمة للإنتاج

انسخ المجلد كاملاً `artifacts\transfer\UqebDeploy-<timestamp>\` إلى جهاز الإنتاج
بأي وسيلة (USB، شبكة داخلية، RDP drag-and-drop).

---

## مسار النشر السريع (جهاز الإنتاج — Administrator)

```powershell
# من داخل مجلد النقل المنسوخ:
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\deploy.ps1
```

أو بالتحكم الكامل:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass `
  -File .\tools\deploy-production-fast.ps1 `
  -TransferDir .
```

يتولى السكربت تلقائياً:
1. نسخ الأدوات إلى `C:\UqebTools`
2. التحقق من SHA256 للحزمة
3. فحص حالة DB قبل التثبيت
4. استدعاء `install-production-package.ps1`
5. نسخ احتياطي إلزامي لقاعدة البيانات
6. تطبيق migration إذا لزم
7. ترقية API و Web بشكل آمن (swap atomic)
8. تشغيل UqebApi Scheduled Task
9. فحص logo الخطاب
10. فحص health endpoints
11. طباعة تقرير GO/NO-GO

---

## فحوصات يدوية بعد النشر

### IIS
```powershell
Import-Module WebAdministration
(Get-WebSite | Where-Object { $_.Name -like '*Uqeb*' }).PhysicalPath
# يجب أن يكون: C:\Uqeb\publish\web
```

### API
```powershell
Invoke-RestMethod http://10.0.177.17:5000/health/live
Invoke-RestMethod http://10.0.177.17:5000/health/ready
Invoke-RestMethod http://10.0.177.17:5000/health
```

### DB migrations
```sql
SELECT TOP 1 [MigrationId] FROM [__EFMigrationsHistory] ORDER BY [MigrationId] DESC
-- يجب أن يكون: 20260627081504_AddSignatoryDefaultsToLetterTemplates
```

### release-manifest
```powershell
Test-Path "C:\Uqeb\publish\release-manifest.json"
Get-Content "C:\Uqeb\publish\release-manifest.json" | ConvertFrom-Json | Select-Object version, commitSha
```

### شعار خطاب التعقيب
```powershell
Test-Path "C:\Uqeb\publish\api\Assets\Brand\organization-logo.png"
(Get-Item "C:\Uqeb\publish\api\Assets\Brand\organization-logo.png").Length
# يجب أن يكون أكبر من 0
Invoke-RestMethod http://10.0.177.17:5000/api/branding/organization-logo -OutFile org-logo.png
# يجب أن يفتح كصورة PNG
```

### Scheduled Task
```powershell
$t = Get-ScheduledTask -TaskName UqebApi
$t.Actions[0].Execute         # يجب: C:\Uqeb\publish\api\Uqeb.Api.exe
$t.Actions[0].WorkingDirectory # يجب: C:\Uqeb\publish\api
```

---

## Rollback

### Rollback تلقائي (يحدث أثناء install-production-package.ps1 عند الفشل)
السكربت يعيد الإصدار السابق من `C:\Uqeb\releases\<previous-version>\` إذا فشل النشر.

### Rollback يدوي للملفات
```powershell
# قراءة الإصدار السابق من rollback-state.json
$state = Get-Content "C:\Uqeb\rollback-state.json" | ConvertFrom-Json
$prevVersion = $state.previousRelease
$prevApi = "C:\Uqeb\releases\$prevVersion\api"
$prevWeb = "C:\Uqeb\releases\$prevVersion\web"

# إيقاف API
Stop-ScheduledTask -TaskName UqebApi

# استعادة الملفات باستخدام robocopy
robocopy "$prevApi" "C:\Uqeb\current\api" /MIR /NFL /NDL
robocopy "$prevWeb" "C:\Uqeb\current\web" /MIR /NFL /NDL

# تشغيل API
Start-ScheduledTask -TaskName UqebApi
```

### Rollback قاعدة البيانات (يدوي فقط)
لا يوجد rollback تلقائي لقاعدة البيانات. أمر الاستعادة يُطبع في تقرير النشر عند الفشل:
```sql
RESTORE DATABASE [UqebDb]
FROM DISK = N'C:\Uqeb\backup\db\UqebDb-before-20260628-132645.bak'
WITH REPLACE, RECOVERY;
```

---

## ماذا يعني GO

يُعتبر النشر **GO** إذا تحقق كل التالي:
- `C:\Uqeb\publish\web\index.html` موجود
- `C:\Uqeb\publish\api\Uqeb.Api.dll` موجود
- `C:\Uqeb\publish\release-manifest.json` موجود ويطابق إصدار الحزمة
- آخر migration مطبّق = `minimumDatabaseMigration` في manifest
- IIS physicalPath = `C:\Uqeb\publish\web` (أو junction target `current\web`)
- Scheduled Task `UqebApi` — WorkingDirectory = `C:\Uqeb\publish\api` و Execute = `run-api.cmd` أو `Uqeb.Api.exe`
- `/health/live` = 200
- `/health/ready` = 200
- `/health` = 200 مع جميع الفحوصات الأساسية pass أو not_applicable
- شعار الخطاب موجود في `Assets\Brand\organization-logo.png` وحجمه > 0
- `/api/branding/organization-logo` = 200
- `POST /api/auth/login` ببيانات خاطئة → 401

**ملاحظة**: `FollowUpLetterPreviewLogo` و`FollowUpLetterPdfLogo` تُعاد كـ `not_applicable` لأن التحقق منهما يتطلب جلسة متصفح أو rendering headless. يكفي التحقق من وجود الملف وصحة الـ API endpoint.

## ماذا يعني NO-GO

يُعتبر النشر **NO-GO** إذا فشل أي من المعايير أعلاه. يُطبع السبب بوضوح في نهاية التقرير.

---

## الأخطاء الشائعة

### `QUOTED_IDENTIFIER` error أثناء migrations
```
UPDATE failed because the following SET options have incorrect settings: 'QUOTED_IDENTIFIER'
```
**السبب**: الاتصال بـ SQL Server بدون `SET QUOTED_IDENTIFIER ON`.  
**الإصلاح**: `apply-migrations.ps1` يُضيف الإعدادات المطلوبة تلقائياً الآن.

### `Invalid column name 'IsDefault'` في migration V2
**السبب**: SQL Server يُجمّع الـ batch كاملاً قبل تنفيذ `ALTER TABLE ADD`، فيرفض UPDATE على أعمدة لم تُضَف بعد.  
**الإصلاح**: Migration C# يستخدم `EXEC(N'UPDATE...')` لتأجيل التحقق من الأعمدة إلى وقت التشغيل. و`Repair-IdempotentMigrationScript` يُصلح الـ SQL الموجود.

### `MARS batch transaction still active`
**السبب**: `MultipleActiveResultSets=True` في connection string مع transaction تمتد عبر batches.  
**الإصلاح**: `apply-migrations.ps1` يُعطّل MARS تلقائياً.

### Nested path `C:\Uqeb\publish\api\api`
**السبب**: استخدام `Copy-Item -Recurse "staging\api" "C:\Uqeb\publish\api"` عندما الوجهة موجودة مسبقاً.  
**الإصلاح**: استخدم `robocopy` أو `Copy-Item "staging\api\*" "..."`. المسارات الصحيحة في المشروع تستخدم `Invoke-RobocopySafe`.  
**علاج**: إذا حدثت، احذف `C:\Uqeb\publish\api\api` وأعِد النشر.

### `release-manifest.json` غير موجود
**السبب**: النشر لم يكتمل (فشل قبل مرحلة الترقية).  
**الإصلاح**: أعِد تشغيل النشر من البداية. تحقق من سجل `C:\Uqeb\logs\api-runtime.log`.

### الواجهة القديمة تظهر بعد النشر
**السبب**: IIS يعرض مسار خاطئ، أو الـ junction لم يُحدَّث.  
**التشخيص**: `(Get-WebSite).PhysicalPath` — يجب أن يكون `C:\Uqeb\publish\web`.

### شعار خطاب التعقيب لا يظهر
**الأسباب المحتملة**:
1. الملف غير موجود: `C:\Uqeb\publish\api\Assets\Brand\organization-logo.png`
2. الملف فارغ (0 bytes)
3. الـ API يقرأ من ContentRoot خاطئ (تحقق من WorkingDirectory في Scheduled Task)
4. مشكلة CORS أو cache في المتصفح

**الإصلاح**: 
- `Test-Path "C:\Uqeb\publish\api\Assets\Brand\organization-logo.png"` → يجب True وحجم > 0
- تحقق أن `WorkingDirectory` للـ Scheduled Task = `C:\Uqeb\publish\api`
- أعِد تشغيل UqebApi بعد التأكد من وجود الملف

### Playwright / Chromium readiness fail
```
playwrightChromium=fail
```
**السبب**: Chromium غير مثبت في `C:\Uqeb\tools\ms-playwright\` أو مسار خاطئ.  
**التشخيص**: `Test-Path "C:\Uqeb\tools\ms-playwright\playwright-browser-manifest.json"`  
**الإصلاح**: تأكد من أن الحزمة تحتوي مجلد `browsers\` وأن `install-production-package.ps1` نسخه بنجاح.

### `reportNumberSequence=fail` أو `institutionalReporting=fail`
**السبب**: لا توجد بيانات ReportNumberSequences في قاعدة البيانات، أو لم يُفعَّل التقرير.  
**الإصلاح**: هذه الفحوصات تُعتبر `not_applicable` في بيئات لم تُفعَّل فيها خاصية التقارير. لا تمنع GO إذا أعادت `not_applicable`.
