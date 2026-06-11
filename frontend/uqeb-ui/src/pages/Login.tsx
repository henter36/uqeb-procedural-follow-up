import { useState } from 'react';
import type { FormEvent } from 'react';
import { useNavigate } from 'react-router-dom';
import { authApi } from '../api/services';
import { useAuth } from '../context/AuthContext';
import { APP_DISPLAY_NAME, APP_SUBTITLE } from '../constants/app';

export default function Login() {
  const [username, setUsername] = useState('');
  const [password, setPassword] = useState('');
  const [error, setError] = useState('');
  const [loading, setLoading] = useState(false);
  const { login } = useAuth();
  const navigate = useNavigate();

  const handleSubmit = async (e: FormEvent) => {
    e.preventDefault();
    setError('');
    setLoading(true);
    try {
      const { data } = await authApi.login(username, password);
      login(data);
      navigate('/');
    } catch {
      setError('اسم المستخدم أو كلمة المرور غير صحيحة');
    } finally {
      setLoading(false);
    }
  };

  return (
    <div className="login-page">
      <div className="login-card">
        <h1>{APP_DISPLAY_NAME}</h1>
        <p className="login-subtitle">{APP_SUBTITLE}</p>
        نريد تعديلين على نظام "المتابعة الإجرائية":

1. تعديل الجهة الوارد منها لتدعم داخلي/خارجي.
2. إضافة اختبارات أداء/تحمل لإنشاء 100 و1000 و10000 معاملة.

المطلوب تنفيذ هذه التعديلات فقط، بدون كسر Login أو JWT أو الصلاحيات أو AuditLog.

أولًا: الجهة الوارد منها داخلي/خارجي

1. في شاشة إضافة معاملة، أضف حقلًا قبل "الجهة الوارد منها" باسم:
   "نوع الجهة الوارد منها"

القيم:

* خارجية
* داخلية

2. عند اختيار "خارجية":

   * حقل "الجهة الوارد منها" يعرض الجهات الخارجية ExternalParties فقط.
   * لا يعرض الإدارات.

3. عند اختيار "داخلية":

   * حقل "الجهة الوارد منها" يعرض الإدارات Departments فقط.
   * لا يعرض الجهات الخارجية.

4. أزل أي حقل نص حر للجهة الوارد منها.
   لا نريد إدخال يدوي أو "أخرى".

5. لا تحفظ الجهة الوارد منها كنص حر في المعاملات الجديدة.

ثانيًا: تعديل نموذج البيانات

6. أضف enum أو حقل واضح في Transaction:
   IncomingSourceType:

   * External
   * Internal

7. أضف في Transaction:
   IncomingFromDepartmentId nullable int
   IncomingFromPartyId nullable int

القواعد:

* إذا IncomingSourceType = External:

  * IncomingFromPartyId مطلوب
  * IncomingFromDepartmentId يجب أن يكون null
* إذا IncomingSourceType = Internal:

  * IncomingFromDepartmentId مطلوب
  * IncomingFromPartyId يجب أن يكون null

8. العلاقة مع Department و ExternalParty تكون DeleteBehavior.NoAction أو Restrict لتجنب multiple cascade paths.

9. أبقِ حقل IncomingFrom النصي القديم للتوافق فقط إن كان موجودًا، لكن لا تستخدمه في الواجهة الجديدة.

ثالثًا: API والـ DTOs

10. عدّل CreateTransactionDto و UpdateTransactionDto ليقبلا:
    incomingSourceType
    incomingFromPartyId
    incomingFromDepartmentId

11. أضف validation في Backend:

* لا يقبل حفظ معاملة بدون IncomingSourceType.
* إذا النوع خارجي، يجب إرسال incomingFromPartyId كرقم.
* إذا النوع داخلي، يجب إرسال incomingFromDepartmentId كرقم.
* لا يسمح بإرسال الاثنين معًا.
* لا يسمح بإرسال قيمة لا تطابق النوع.

12. رسائل الخطأ تكون واضحة بالعربية قدر الإمكان:

* "يجب اختيار نوع الجهة الوارد منها."
* "يجب اختيار جهة خارجية عند اختيار النوع خارجي."
* "يجب اختيار إدارة عند اختيار النوع داخلي."
* "لا يمكن اختيار جهة خارجية وإدارة داخلية في نفس الوقت."

13. عدّل عرض الجهة الوارد منها في:

* جدول المعاملات
* تفاصيل المعاملة
* التقارير
* التصدير Excel/PDF إن وجد

ليعرض:

* اسم الجهة الخارجية إذا النوع خارجي
* اسم الإدارة إذا النوع داخلي

رابعًا: الواجهة

14. في شاشة إضافة معاملة:

* أضف radio/select لاختيار نوع الجهة الوارد منها.
* القيمة الافتراضية: خارجية.
* عند تغيير النوع، يتم تفريغ اختيار الجهة السابقة.
* إذا خارجية، اعرض قائمة ExternalParties.
* إذا داخلية، اعرض قائمة Departments.
* لا ترسل "" لأي int?.
* أرسل null للقيمة غير المستخدمة.

15. في شاشة تعديل معاملة، طبّق نفس المنطق.

16. في الفلاتر:

* أضف فلتر نوع الجهة الوارد منها إن أمكن.
* أضف فلتر الجهة الوارد منها حسب النوع.

خامسًا: Migration

17. أنشئ Migration جديدة باسم:
    AddIncomingSourceTypeAndPerformanceTests

18. لا تحذف قاعدة البيانات.
    استخدم Migration محافظة.

سادسًا: اختبارات الأداء والتحمل

19. أضف مجلد:
    performance-tests

20. أضف ملف:
    performance-tests/uqeb-bulk-create-transactions.js

21. وظيفة هذا السكربت:

* تسجيل الدخول
* إنشاء عدد معاملات حسب متغير TEST_COUNT
* دعم 100 و1000 و10000 معاملة
* كل معاملة تبدأ رقم وارد فريد بالبادئة:
  LOAD-TEST-
* لا يستخدم بيانات حقيقية
* يستخدم جهات وإدارات موجودة من API أو قيم seed إن كانت معروفة
* لا يكرر IncomingNumber
* يقيس:

  * عدد المعاملات الناجحة
  * عدد الفاشلة
  * متوسط زمن الاستجابة
  * p95
  * عدد أخطاء 400
  * عدد أخطاء 500

22. السكربت يجب أن يقبل متغيرات:
    BASE_URL=http://localhost:5000
    USERNAME=admin
    PASSWORD=Admin@123
    TEST_COUNT=100
    BATCH_SIZE=10

23. أضف مراحل تشغيل:

* 100 معاملة: اختبار خفيف
* 1000 معاملة: اختبار متوسط
* 10000 معاملة: اختبار ثقيل

24. لا تجعل اختبار 10000 يرسل كل الطلبات دفعة واحدة.
    يجب استخدام batching أو pacing حتى لا يخنق SQL أو API.
    يفضل:

* BATCH_SIZE قابل للتعديل
* sleep بسيط بين الدفعات
* أو constant-arrival-rate مضبوط

25. أضف thresholds:

* http_req_failed أقل من 1% في اختبار 100 و1000
* لا توجد أخطاء 500
* p95 أقل من 2000ms لإنشاء المعاملة في 100 و1000
* في 10000 يتم تسجيل الأداء ولا يعتبر الفشل كارثة إذا ظهر bottleneck، لكن لا تقبل أخطاء 500 كنجاح

26. أضف سكربت آخر اختياري:
    performance-tests/uqeb-read-reports-load-test.js

لاختبار:

* Dashboard
* Transactions list
* Reports
  بعد وجود بيانات كثيرة.

27. أضف README داخل performance-tests يشرح:

* تثبيت k6
* تشغيل API
* تشغيل SQL
* أوامر تشغيل 100 و1000 و10000
* كيف قراءة النتائج
* متى نوقف الاختبار

سابعًا: أوامر التحقق

بعد التنفيذ شغّل:

* dotnet build
* dotnet ef migrations add AddIncomingSourceTypeAndPerformanceTests
* dotnet ef database update
* npm run build

ثامنًا: اختبارات يدوية

اختبر:

1. إضافة معاملة بنوع جهة وارد خارجية.
2. التأكد أن القائمة تعرض الجهات الخارجية فقط.
3. إضافة معاملة بنوع جهة وارد داخلية.
4. التأكد أن القائمة تعرض الإدارات فقط.
5. التأكد من عدم وجود نص حر.
6. محاولة إرسال النوع خارجي بدون جهة خارجية، يجب أن يفشل.
7. محاولة إرسال النوع داخلي بدون إدارة، يجب أن يفشل.
8. عرض المعاملة في الجدول والتفاصيل والتقارير.
9. تشغيل اختبار إنشاء 100 معاملة.
10. تشغيل اختبار إنشاء 1000 معاملة.
11. لا تشغل 10000 إلا بعد نجاح 100 و1000.

لا تفعل الآتي:

* لا تستخدم نصًا حرًا للجهة الوارد منها.
* لا تحفظ الجهة الداخلية في ExternalParties.
* لا تحفظ الجهة الخارجية في Departments.
* لا تسمح بإرسال incomingFromPartyId و incomingFromDepartmentId معًا.
* لا تكسر التقارير القديمة.
* لا تكسر Login أو JWT.
* لا تزيل AuditLog.
* لا تشغل اختبار 10000 تلقائيًا.
        <form onSubmit={handleSubmit}>
          {error && <div className="alert alert-error">{error}</div>}
          <div className="form-group">
            <label>اسم المستخدم</label>
            <input type="text" value={username} onChange={(e) => setUsername(e.target.value)} required autoFocus />
          </div>
          <div className="form-group">
            <label>كلمة المرور</label>
            <input type="password" value={password} onChange={(e) => setPassword(e.target.value)} required />
          </div>
          <button type="submit" className="btn btn-primary btn-block" disabled={loading}>
            {loading ? 'جاري الدخول...' : 'تسجيل الدخول'}
          </button>
        </form>
        <p className="login-hint">المستخدم الافتراضي: admin / Admin@123</p>
      </div>
    </div>
  );
}
