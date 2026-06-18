# متطلبات تشغيل Uqeb في الإنتاج

هذا المستند يحدد المتطلبات الدقيقة لتشغيل نظام **Uqeb** على جهاز Windows إنتاجي (معزول أو محدود الإنترنت). الهدف هو تجهيز الجهاز مرة واحدة ثم نشر **حزمة publish جاهزة** دون الحاجة إلى أدوات التطوير على الإنتاج.

---

## 1. نظام التشغيل

| المتطلب | التفاصيل |
|---------|----------|
| **نظام التشغيل** | Windows 10 Pro، Windows 11 Pro، أو Windows Server 2019/2022 |
| **الصلاحيات** | حساب مسؤول (Administrator) لتثبيت IIS وSQL Server وتشغيل سكربتات النشر |
| **الشبكة** | لا يلزم إنترنت مستمر بعد تثبيت المتطلبات؛ يمكن نقل الحزمة عبر USB أو شبكة داخلية |

---

## 2. IIS (واجهة الويب)

يجب تثبيت **Internet Information Services (IIS)** مع الميزات التالية على الأقل:

| الميزة | الغرض |
|--------|--------|
| **Static Content** | تقديم ملفات الواجهة (`index.html`, `assets/`) |
| **Default Document** | فتح `index.html` تلقائيًا عند زيارة الموقع |
| **HttpErrors** | إرجاع `index.html` لمسارات SPA (بدون URL Rewrite Module) |

### تفعيل سريع (PowerShell كمسؤول)

```powershell
# Windows 10/11
Enable-WindowsOptionalFeature -Online -FeatureName IIS-WebServerRole,IIS-StaticContent,IIS-DefaultDocument,IIS-HttpErrors -All

# Windows Server
Install-WindowsFeature Web-Server,Web-Static-Content,Web-Default-Document,Web-Http-Errors
```

### موقع IIS المقترح

- **المجلد الفعلي:** `C:\Uqeb\publish\web`
- **المنفذ:** 80 أو 443 حسب سياسة المؤسسة

> **ملاحظة:** واجهة Uqeb لا تعتمد على **IIS URL Rewrite Module**. التوجيه لـ SPA يتم عبر `web.config` المضمّن (HttpErrors + Default Document).

---

## 3. SQL Server

| المتطلب | التفاصيل |
|---------|----------|
| **الإصدار** | SQL Server 2019 أو أحدث (Express أو Standard أو Enterprise) |
| **قاعدة البيانات** | `UqebDb` (أو اسم تختاره في connection string) |
| **المصادقة** | Windows Authentication أو SQL Authentication حسب بيئتك |

### الفرق بين `localhost` و `localhost\SQLEXPRESS`

| السيرفر في connection string | متى تستخدمه |
|------------------------------|-------------|
| `localhost` أو `(local)` | عند تثبيت **SQL Server** كنسخة افتراضية (خدمة `MSSQLSERVER`) |
| `localhost\SQLEXPRESS` | عند تثبيت **SQL Server Express** كنسخة مسماة (خدمة `MSSQL$SQLEXPRESS`) |

**أمثلة connection string:**

```text
# نسخة افتراضية + Windows Auth
Server=localhost;Database=UqebDb;Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=true

# Express + Windows Auth
Server=localhost\SQLEXPRESS;Database=UqebDb;Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=true

# SQL Auth
Server=localhost\SQLEXPRESS;Database=UqebDb;User Id=uqeb_app;Password=***;TrustServerCertificate=True;MultipleActiveResultSets=true
```

يُضبط الاتصال في `C:\Uqeb\publish\api\appsettings.Production.json` — راجع [DEPLOYMENT_NOTES.md](DEPLOYMENT_NOTES.md).

### Migrations

تُطبَّق migrations **مرة واحدة** أثناء الإعداد الأولي (من جهاز بناء أو عبر أداة EF)، وليس بالضرورة على كل نشر لاحق. راجع `DEPLOYMENT_NOTES.md`.

---

## 4. ASP.NET Core Runtime (Backend API)

المشروع يستهدف **.NET 10** (`net10.0`).

| المكوّن | المطلوب على الإنتاج |
|---------|---------------------|
| **ASP.NET Core Runtime 10.x** | لتشغيل `Uqeb.Api.dll` |
| **Hosting Bundle 10.x** | إذا كان API خلف IIS In-Process (اختياري حسب طريقة التشغيل) |

### التحقق

```powershell
dotnet --list-runtimes
```

يجب أن يظهر سطر مثل:

```text
Microsoft.AspNetCore.App 10.0.x [...]
```

### التثبيت دون إنترنت

حمّل **ASP.NET Core Runtime 10.0** (وHosting Bundle إن لزم) من جهاز متصل بالإنترنت، ثم انقل المثبّت إلى الإنتاج:

- [https://dotnet.microsoft.com/download/dotnet/10.0](https://dotnet.microsoft.com/download/dotnet/10.0)

---

## 5. ما **لا** تحتاجه على جهاز الإنتاج (عند استخدام حزمة جاهزة)

إذا نُشر النظام من مجلد `publish/` المُعد مسبقًا:

| غير مطلوب على الإنتاج | ملاحظة |
|----------------------|--------|
| **Git** | النشر عبر نسخ الملفات أو `deploy-production.ps1` |
| **Node.js / npm** | الواجهة مُجمّعة مسبقًا في `publish/web` |
| **.NET SDK** | يكفي **Runtime** لتشغيل API |
| **dotnet ef** | Migrations تُطبَّق عند الإعداد الأولي فقط |

---

## 6. هيكل المجلدات المقترح على الإنتاج

```text
C:\Uqeb\
  publish\
    api\          ← Backend (Uqeb.Api.dll + appsettings.Production.json)
    web\          ← Frontend (index.html + assets/)
  Attachments\    ← مرفقات المعاملات (مسار قابل للتعديل في الإعدادات)
  logs\           ← سجلات اختيارية (مثل api-task.log)
  BUILD_INFO.txt  ← يُنشأ عند كل نشر
```

---

## 7. فحص سريع

شغّل من PowerShell (كمسؤول):

```powershell
.\scripts\check-prerequisites.ps1
```

---

## 8. مراجع

- [DEPLOYMENT_NOTES.md](DEPLOYMENT_NOTES.md) — خطوات النشر والتشغيل
- [README.md](README.md) — نظرة عامة على المشروع للمطورين
