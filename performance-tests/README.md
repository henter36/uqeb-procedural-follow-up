# اختبارات تحمل API — المتابعة الإجرائية

اختبارات تحمل لـ API باستخدام [k6](https://k6.io/).

## التثبيت

### Windows (Chocolatey)
```bash
choco install k6
```

### macOS
```bash
brew install k6
```

## المتطلبات قبل التشغيل

1. تشغيل SQL Server وقاعدة البيانات محدثة
2. تشغيل Backend:
```bash
cd backend/Uqeb.Api
dotnet run
```

## المتغيرات المشتركة

| المتغير | الافتراضي | الوصف |
|---------|-----------|--------|
| `BASE_URL` | `http://localhost:5000` | عنوان API |
| `USERNAME` | `admin` | مستخدم الاختبار |
| `PASSWORD` | `Admin@123` | كلمة المرور |

---

## 1. اختبار سير العمل العام — `uqeb-load-test.js`

```bash
k6 run performance-tests/uqeb-load-test.js
K6_SCENARIO=load k6 run performance-tests/uqeb-load-test.js
```

---

## 2. إنشاء معاملات بالجملة — `uqeb-bulk-create-transactions.js`

| المتغير | الافتراضي | الوصف |
|---------|-----------|--------|
| `TEST_COUNT` | `100` | عدد المعاملات |
| `BATCH_SIZE` | `10` | حجم الدفعة (VUs) |
| `BATCH_SLEEP` | `0.2` | ثوانٍ بين الدفعات |

### 100 معاملة (اختبار خفيف)
```bash
k6 run -e TEST_COUNT=100 -e BATCH_SIZE=10 performance-tests/uqeb-bulk-create-transactions.js
```

### 1000 معاملة (اختبار متوسط)
```bash
k6 run -e TEST_COUNT=1000 -e BATCH_SIZE=20 -e BATCH_SLEEP=0.3 performance-tests/uqeb-bulk-create-transactions.js
```

### 10000 معاملة (اختبار ثقيل — شغّله فقط بعد نجاح 100 و1000)
```bash
k6 run -e TEST_COUNT=10000 -e BATCH_SIZE=25 -e BATCH_SLEEP=0.5 performance-tests/uqeb-bulk-create-transactions.js
```

**المقاييس المسجلة:**
- عدد المعاملات الناجحة / الفاشلة
- متوسط زمن الاستجابة و p95
- أخطاء 400 و 500

**العتبات:**
- 100 و 1000: `http_req_failed < 1%`، لا أخطاء 500، `p95 < 2000ms`
- 10000: يُسجّل الأداء فقط (لا عتبات صارمة)

كل معاملة تستخدم بادئة `LOAD-TEST-` ورقم وارد فريد.

---

## 3. قراءة التقارير بعد بيانات كثيرة — `uqeb-read-reports-load-test.js`

```bash
k6 run performance-tests/uqeb-read-reports-load-test.js
k6 run -e VUS=20 -e DURATION=5m performance-tests/uqeb-read-reports-load-test.js
```

يختبر: Dashboard، قائمة المعاملات، التقارير المفتوحة، تقرير الإدارات، مطلوب إفادة.

---

## قراءة النتائج

- **checks**: نسبة نجاح الفحوصات
- **http_req_duration**: زمن الاستجابة (avg, p95)
- **transactions_created / transactions_failed**: في اختبار الإنشاء الجماعي
- **errors_500**: يجب أن تكون 0 في الاختبارات الخفيفة والمتوسطة

## متى توقف الاختبار

- ظهور أخطاء 500 متكررة
- `http_req_failed` أعلى من 5%
- تدهور شديد في SQL (بطء غير طبيعي، timeouts)
- استخدام الجهاز أثناء العمل — لا تشغّل 10000 على جهاز الإنتاج

## تحذير

- ابدأ دائمًا بـ 100 ثم 1000 ثم 10000
- لا تشغّل الاختبار الثقيل أثناء استخدام النظام
- البيانات ببادئة `LOAD-TEST-` وليست بيانات إنتاج
