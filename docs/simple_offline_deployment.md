# النشر المبسط دون GitHub (offline)

الطريقة المفضلة لنقل تحديث من جهاز التطوير إلى جهاز إنتاج Windows غير متصل بـGitHub.

## المتطلبات الأولية (مرة واحدة على الإنتاج)

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass `
  -File ".\scripts\setup-production-tools.ps1"
```

ينشئ المجلدات وينسخ الأدوات إلى `C:\UqebTools` دون تعديل أسرار الإنتاج.

> **لا يمكن تنفيذ نشر إنتاج أو migrations دون نسخة قاعدة بيانات مكتملة ومتحقق منها. لا يوجد خيار تجاوز لهذه الخطوة.**

---

### على جهاز التطوير

```powershell
cd C:\path\to\uqeb-procedural-follow-up

powershell.exe -NoProfile -ExecutionPolicy Bypass `
  -File ".\scripts\build-production-package.ps1"
```

الناتج:

```text
artifacts\production\
├── Uqeb-20260623-120000.zip
└── Uqeb-20260623-120000.sha256.txt
```

### انقل إلى الإنتاج

```text
Uqeb-<version>.zip
Uqeb-<version>.sha256.txt
```

إلى:

```text
C:\Uqeb\incoming
```

### على جهاز الإنتاج

```powershell
$package = Get-ChildItem "C:\Uqeb\incoming\Uqeb-*.zip" |
    Sort-Object LastWriteTime -Descending |
    Select-Object -First 1

powershell.exe -NoProfile -ExecutionPolicy Bypass `
  -File "C:\UqebTools\install-production-package.ps1" `
  -PackagePath $package.FullName
```

---

## ماذا تفعل الحزمة؟

1. التحقق من SHA256.
2. فك الحزمة إلى `C:\Uqeb\staging\<timestamp>`.
3. التحقق من `manifest.json` والملفات الأساسية.
4. **إنشاء نسخة احتياطية إلزامية لقاعدة البيانات** في `C:\Uqeb\backup\db\UqebDb-before-<timestamp>.bak` باستخدام `BACKUP DATABASE ... WITH CHECKSUM` ثم `RESTORE VERIFYONLY ... WITH CHECKSUM` والتحقق من الحجم والتجزئة واسم القاعدة.
5. إيقاف مهمة `UqebApi` والانتظار حتى يتحرر المنفذ 5000.
6. نسخة احتياطية اختيارية للملفات في `C:\Uqeb\backup\before-<timestamp>` (يمكن تخطيها بـ `-SkipFileBackup` فقط — **لا يغني عن نسخة قاعدة البيانات**).
7. تطبيق `database\migrations-idempotent.sql` عبر `apply-migrations.ps1` (بدون `sqlcmd`).
8. نسخ API وWeb دون `appsettings.Production.json` من الحزمة.
9. إعادة وضع الإعداد المعتمد من `C:\Uqeb\config\appsettings.Production.json`.
10. تشغيل API وفحص `/health/live` و`/health/ready` و`/health` مع `database=pass`.
11. فحص السجل بحثًا عن أخطاء SQL أو أعمدة غير صالحة.
12. تطبيق سياسة الاحتفاظ بآخر 10 نسخ قاعدة بيانات على الأقل.
13. نقل الحزمة الناجحة إلى `C:\Uqeb\incoming\deployed`.

## سياسة نسخ قاعدة البيانات

| القاعدة | التفاصيل |
|---------|----------|
| إلزامية | قبل إيقاف API أو تطبيق migrations |
| المسار | `C:\Uqeb\backup\db\<Database>-before-<timestamp>.bak` |
| التحقق | `WITH CHECKSUM` + `RESTORE VERIFYONLY` + SHA256 للملف |
| الاحتفاظ | آخر 10 نسخ ناجحة كحد أدنى؛ لا حذف النسخ المرتبطة بإصدار منشور |
| عند الفشل | لا migrations ولا استبدال ملفات؛ يُعرض أمر `RESTORE DATABASE` اليدوي |
| تجاوز | **غير متاح** — لا معامل `-SkipDatabaseBackup` ولا متغير بيئة |

## خيارات التثبيت

| المعامل | الافتراضي | الوصف |
|---------|-----------|--------|
| `-PackagePath` | (إلزامي) | مسار ملف ZIP |
| `-ApiPath` | `C:\Uqeb\publish\api` | مجلد API |
| `-WebPath` | `C:\Uqeb\publish\web` | مجلد الواجهة |
| `-ConfigPath` | `C:\Uqeb\config\appsettings.Production.json` | إعداد الإنتاج المعتمد |
| `-TaskName` | `UqebApi` | مهمة الجدولة |
| `-ApiPort` | `5000` | منفذ API |
| `-ApiBaseUrl` | `http://localhost:5000` | لفحص الصحة محليًا |
| `-SkipFileBackup` | — | تخطي النسخة الاحتياطية للملفات فقط (نسخة قاعدة البيانات تبقى إلزامية) |
| `-SkipDatabaseMigration` | — | تخطي migrations (نسخة قاعدة البيانات تُنفَّذ قبل ذلك) |

> **لا يمكن تنفيذ نشر إنتاج أو migrations دون نسخة قاعدة بيانات مكتملة ومتحقق منها. لا يوجد خيار تجاوز لهذه الخطوة.**

## أخطاء يجب تجنبها

| خطأ | السبب | البديل الصحيح |
|-----|--------|----------------|
| تمرير مجلد بدل ZIP | `install-production-package.ps1` يتوقع ملفًا | استخدم `-PackagePath` لملف `.zip` |
| كتابة مسار SQL فقط | لا يُنفّذ الملف تلقائيًا | شغّل `apply-migrations.ps1` مع `-MigrationFile` |
| الاعتماد على `sqlcmd` | غير مطلوب وقد لا يكون مثبتًا | `apply-migrations.ps1` يستخدم `System.Data.SqlClient` |
| نسخ `appsettings` من التطوير | تسريب أسرار أو إعدادات خاطئة | الإعداد يبقى في `C:\Uqeb\config` فقط |
| `localhost` في بناء الواجهة للإنتاج | الواجهة لن تتصل بالـAPI على الشبكة | البناء يستخدم `http://10.0.177.17:5000/api` افتراضيًا |
| إعلان النجاح لأن المهمة بدأت | المهمة قد تفشل بعد البدء | انتظر المنفذ 5000 + health checks |
| الاعتماد على health فقط | قد يمرّ دون قاعدة بيانات سليمة | تحقق من migrations + `database=pass` + السجل |
| `else`/`finally` على سطر جديد في PowerShell | خطأ parsing | ضع `} else {` و`} finally {` على نفس السطر |
| طباعة كلمات المرور أو JWT | تسريب أمني | السكربتات تعرض Server/Database فقط |

## Rollback

| المكوّن | Rollback تلقائي؟ | الإجراء اليدوي |
|---------|------------------|----------------|
| ملفات API/Web | نعم عند فشل النشر (إن وُجدت نسخة احتياطية) | استرجع من `C:\Uqeb\backup\before-<timestamp>` |
| قاعدة البيانات | **لا** | استخدم النسخة في `C:\Uqeb\backup\db\` مع أمر `RESTORE DATABASE` المعروض في تقرير الفشل |
| الحزمة المنقولة | تُنقل إلى `deployed` عند النجاح فقط | أعد نسخ ZIP السابق من `deployed` إن لزم |

## فحص الصحة اليدوي

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass `
  -File "C:\UqebTools\verify-deployment-health.ps1" `
  -ApiBaseUrl "http://localhost:5000"
```

## اختبارات Pester

```powershell
Install-Module Pester -MinimumVersion 5.0 -Force -Scope CurrentUser
Invoke-Pester -Path .\scripts\verify-deployment-health.Tests.ps1, .\scripts\deployment-package.Tests.ps1 -Output Detailed
```

## مراجع

- [production_runbook.md](production_runbook.md) — التشغيل والاستكشاف
- [README.md](../README.md) — نظرة عامة على المشروع
