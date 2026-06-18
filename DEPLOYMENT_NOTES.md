# ملاحظات نشر Uqeb في الإنتاج

دليل عملي لنشر **Uqeb** على Windows إنتاجي باستخدام حزمة جاهزة (`publish/api` + `publish/web`). لا يفترض وجود Git أو Node أو .NET SDK على جهاز الإنتاج.

راجع [PREREQUISITES.md](PREREQUISITES.md) قبل البدء.

---

## 1. هيكل حزمة النشر

يُفترض أن الحزمة القادمة من جهاز البناء بهذا الشكل:

```text
Uqeb-release-YYYYMMDD\
  publish\
    api\     ← ناتج dotnet publish
    web\     ← ناتج npm run build (محتويات dist/)
```

بعد النشر على الجهاز:

```text
C:\Uqeb\
  publish\
    api\
    web\
```

---

## 2. نشر Backend (`publish/api`)

### الطريقة الموصى بها: سكربت آمن

```powershell
# كمسؤول
.\scripts\deploy-production.ps1 -SourcePackagePath "D:\packages\Uqeb-release-20260617"
```

### يدويًا (إن لزم)

1. أوقف API الحالي (Scheduled Task أو خدمة NSSM) — انظر القسم 6.
2. انسخ محتويات `publish/api` إلى `C:\Uqeb\publish\api`.
3. **لا تستبدل** `appsettings.Production.json` إذا كان مضبوطًا مسبقًا.
4. تأكد من وجود:
   - `Uqeb.Api.dll`
   - `Uqeb.Api.runtimeconfig.json`
5. أنشئ مجلد المرفقات (مثلاً `C:\Uqeb\Attachments`) بصلاحيات كتابة لحساب تشغيل API.
6. أعد تشغيل Scheduled Task أو الخدمة.

### تشغيل API مباشرة (اختبار)

```powershell
cd C:\Uqeb\publish\api
$env:ASPNETCORE_ENVIRONMENT = "Production"
dotnet Uqeb.Api.dll --urls "http://0.0.0.0:5000"
```

---

## 3. نشر Frontend (`publish/web`)

1. انسخ محتويات `publish/web` إلى `C:\Uqeb\publish\web`.
2. تأكد من وجود `index.html` ومجلد `assets/`.
3. تأكد من وجود `web.config` (يُنشئه `deploy-production.ps1` أو يأتي من بناء Vite في `public/web.config`).
4. في IIS:
   - أنشئ **Application Pool** (**.NET CLR version: No Managed Code**).
   - أنشئ **Website** يشير إلى `C:\Uqeb\publish\web`.
   - فعّل Static Content و Default Document و HttpErrors.

### توجيه `/api` إلى Backend

الواجهة تستدعي `/api` نسبيًا. يجب أن يصل الطلب إلى API على المنفذ 5000 (أو عبر reverse proxy في IIS). خيارات شائعة:

- **Application Proxy في IIS** (يتطلب ARR — اختياري).
- **تشغيل API على `http://localhost:5000`** والوصول من نفس الجهاز عبر proxy في `vite` غير متاح في الإنتاج — استخدم IIS reverse proxy أو اجعل المستخدمين يصلون عبر نفس المضيف مع قاعدة توجيه `/api`.

> في بيئات معزولة بسيطة: غالبًا يُضبط موقع IIS للواجهة على المنفذ 80 وAPI على 5000 داخليًا، مع قاعدة توجيه `/api` → `http://localhost:5000` (إن وُجد ARR) أو وصول المستخدمين عبر `http://server/api` إذا وُجّه DNS/الجدار الناري accordingly.

---

## 4. `appsettings.Production.json`

| القاعدة | السبب |
|---------|--------|
| **لا يُستبدل تلقائيًا** عند النشر اللاحق | يحتوي connection string وJWT ومسارات حساسة مضبوطة محليًا |
| **يُنسخ من المثال مرة واحدة** عند الإعداد الأول فقط | من `appsettings.example.json` أو من الحزمة إن لم يوجد محليًا |
| **يُحفظ نسخة احتياطية** قبل أي تعديل يدوي | `appsettings.Production.json.bak` |

### ضبط `ConnectionStrings:DefaultConnection`

عدّل في `C:\Uqeb\publish\api\appsettings.Production.json`:

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=localhost\\SQLEXPRESS;Database=UqebDb;Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=true"
  },
  "Jwt": {
    "Key": "ضع-مفتاحًا-عشوائيًا-طويلًا-32-حرفًا-على-الأقل",
    "Issuer": "UqebApi",
    "Audience": "UqebClient",
    "ExpireMinutes": 480
  },
  "AllowedOrigins": [
    "http://your-server-name"
  ],
  "FileStorage": {
    "Path": "C:\\Uqeb\\Attachments"
  }
}
```

| السيناريو | قيمة `Server` |
|-----------|---------------|
| SQL Server افتراضي | `localhost` |
| SQL Server Express | `localhost\SQLEXPRESS` |
| سيرفر بعيد | `SERVERNAME\INSTANCE` |

بعد التعديل أعد تشغيل API.

---

## 5. Scheduled Task مقابل NSSM / Windows Service

| الطريقة | المزايا | ملاحظات |
|---------|---------|---------|
| **Scheduled Task** | بسيطة، لا تحتاج أدوات إضافية | مهمة تعمل عند تسجيل الدخول أو At startup مع حساب خدمة |
| **NSSM / Windows Service** | إعادة تشغيل تلقائية، تشغيل قبل تسجيل الدخول | يتطلب تثبيت NSSM أو `sc create` مع wrapper |

### مثال Scheduled Task (اسم افتراضي: `UqebApi`)

- **البرنامج:** `C:\Program Files\dotnet\dotnet.exe`
- **الوسائط:** `C:\Uqeb\publish\api\Uqeb.Api.dll --urls http://0.0.0.0:5000`
- **المجلد:** `C:\Uqeb\publish\api`
- **متغير بيئة:** `ASPNETCORE_ENVIRONMENT=Production`
- **التشغيل:** عند بدء النظام (مع حسام له صلاحية SQL ومجلد المرفقات)
- **السجل:** وجّه stdout/stderr إلى `C:\Uqeb\logs\api-task.log`

سكربت `deploy-production.ps1` يوقف المهمة قبل النسخ ويعيد تشغيلها بعده إن وُجدت.

---

## 6. المنفذ 5000 — تجنب التعارض

قبل تشغيل Scheduled Task أو بعد النشر:

1. **أوقف** أي مهمة مجدولة أو خدمة قديمة لـ Uqeb.
2. **تحقق** من عدم احتلال المنفذ:

```powershell
netstat -ano | findstr :5000
```

3. إن وُجدت عملية قديمة:

```powershell
# استبدل PID بالرقم الظاهر
taskkill /F /PID <PID>
```

4. أعد تشغيل مهمة `UqebApi`.

> تشغيل نسختين من API (مهمة قديمة + مهمة جديدة، أو `dotnet run` يدوي + مهمة) يسبب فشل الربط على المنفذ 5000.

---

## 7. فحص الأخطاء بعد النشر

### سجل Scheduled Task

```powershell
Get-Content C:\Uqeb\logs\api-task.log -Tail 50
```

ابحث عن: أخطاء connection string، JWT Key، أو فشل migrations.

### المنفذ والعملية

```powershell
netstat -ano | findstr :5000
Get-Process -Id <PID>
```

### اختبار Login API

```powershell
$body = '{"username":"admin","password":"YOUR_PASSWORD"}'
Invoke-RestMethod -Uri "http://localhost:5000/api/auth/login" -Method POST -ContentType "application/json" -Body $body
```

النتيجة المتوقعة: `token` في الاستجابة (HTTP 200).

### صفحة الويب

افتح `http://<server>/` — يجب تحميل الواجهة دون الضغط على «تحديث» يدويًا للبيانات الداخلية (بعد تسجيل الدخول).

---

## 8. `BUILD_INFO.txt`

يُنشأ تلقائيًا في `C:\Uqeb\BUILD_INFO.txt` عند كل نشر عبر `deploy-production.ps1` ويحتوي:

- تاريخ ووقت النشر
- اسم/مسار الحزمة المصدر
- إصدار runtime المتوقع

---

## 9. إعادة تعيين كلمة مرور admin

راجع `scripts/sql/reset-admin-password.sql`. **لا يُنصح** بتحديث BCrypt hash يدويًا من SQL — استخدم واجهة إدارة المستخدمين (Admin) أو أداة توليد hash من التطبيق.

---

## 10. سكربتات مساعدة

| السكربت | الغرض |
|---------|--------|
| `scripts/check-prerequisites.ps1` | فحص IIS / SQL / dotnet / المجلدات |
| `scripts/deploy-production.ps1` | نشر آمن من حزمة إلى `C:\Uqeb` |

---

## 11. إعداد أولي لقاعدة البيانات (مرة واحدة)

من جهاز **بناء** (ليس بالضرورة الإنتاج):

```powershell
cd backend\Uqeb.Api
dotnet ef database update
```

أو طبّق scripts migrations الموجودة في الحزمة وفق سياسة مؤسستك.
