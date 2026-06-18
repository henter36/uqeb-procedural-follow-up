# متطلبات تشغيل Uqeb في الإنتاج

هذا المستند يحدد المتطلبات لتشغيل **Uqeb** على Windows إنتاجي (معزول أو محدود الإنترنت) باستخدام **حزمة publish جاهزة**.

---

## 1. نظام التشغيل

| المتطلب | التفاصيل |
|---------|----------|
| **نظام التشغيل** | Windows 10 Pro، Windows 11 Pro، أو Windows Server 2019/2022 |
| **الصلاحيات** | حساب مسؤول لتثبيت IIS وSQL Server وتشغيل سكربتات النشر |

---

## 2. بنية التشغيل (مهم)

| الطبقة | المسار / المنفذ |
|--------|------------------|
| **Frontend (IIS)** | موقع ثابت من `C:\Uqeb\web` |
| **Backend (Kestrel)** | `http://localhost:5000` — مجلد `C:\Uqeb\api` |
| **المرفقات** | `C:\Uqeb\uploads` (أو المسار في `FileStorage:Path`) |
| **السجلات** | `C:\Uqeb\logs` |
| **النسخ الاحتياطية** | `C:\Uqeb\backup` |

> **لا يفترض PR أن IIS يمرّر `/api` إلى Kestrel** إلا إذا نُصّب واختُبر reverse proxy (مثل ARR) بشكل منفصل. الواجهة تتصل بـ `/api` نسبيًا؛ في الإنتاج يجب ضبط `AllowedOrigins` في `appsettings.Production.json` ليطابق عنوان موقع IIS.

> **`/api/health` و `/swagger` غير مضمونين في Production** ما لم تُفعَّلا صراحة في التطبيق.

---

## 3. IIS

| الميزة | الغرض |
|--------|--------|
| **Static Content** | تقديم `index.html` و`assets/` |
| **Default Document** | فتح `index.html` تلقائيًا |
| **HttpErrors** | SPA fallback بدون URL Rewrite Module |

**المجلد الفعلي للموقع:** `C:\Uqeb\web`

---

## 4. SQL Server

| السيرفر | متى |
|---------|-----|
| `localhost` | نسخة افتراضية (`MSSQLSERVER`) |
| `localhost\SQLEXPRESS` | Express (`MSSQL$SQLEXPRESS`) |

**الإعداد:** `C:\Uqeb\api\appsettings.Production.json`

**sqlcmd:** عند استخدام شهادة SQL محلية غير موثوقة، استخدم `-C` (مثال: `sqlcmd -C -S localhost\SQLEXPRESS ...`).

---

## 5. ASP.NET Core Runtime 10.x

```powershell
dotnet --list-runtimes
```

يجب ظهور `Microsoft.AspNetCore.App 10.0.x`.

---

## 6. ما لا تحتاجه على الإنتاج (حزمة جاهزة)

| غير مطلوب | ملاحظة |
|-----------|--------|
| Git | النشر عبر `deploy-production.ps1` |
| Node.js / npm | الواجهة مبنية مسبقًا في الحزمة |
| .NET SDK | يكفي Runtime |
| dotnet ef | migrations عند الإعداد الأولي فقط |

---

## 7. هيكل المجلدات بعد النشر

```text
C:\Uqeb\
  api\           Backend (Uqeb.Api.dll)
  web\           Frontend (IIS root)
  logs\
  uploads\
  backup\        api-YYYYMMDD-HHmmss / web-YYYYMMDD-HHmmss
  BUILD_INFO.txt
  start-api.ps1
```

---

## 8. فحص سريع

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\check-prerequisites.ps1
powershell -ExecutionPolicy Bypass -File .\scripts\check-prerequisites.ps1 -InstallRoot "C:\Uqeb"
```

السكربت **قراءة فقط** — لا يثبت مكونات ولا يعدّل IIS أو SQL.

---

## 9. مراجع

- [DEPLOYMENT_NOTES.md](DEPLOYMENT_NOTES.md)
- [README.md](README.md)
