import { useCallback, useEffect, useState, type FormEvent } from 'react';
import { Link, useNavigate } from 'react-router-dom';
import { categoriesApi, departmentsApi, followUpPrintApi, letterTemplatesApi } from '../api/services';
import type {
  Category, Department, EligibleTransaction, FollowUpPrintEligibilityPreview, LetterTemplate,
} from '../api/types';
import { getApiErrorMessage } from '../utils/apiHelpers';
import { createIdempotencyKey } from '../utils/createIdempotencyKey';
import DateDisplay from '../components/DateDisplay';
import {
  Alert, EmptyState, LoadingInline, PageHeader, Pagination,
} from '../components/ui';
import { useDeferredEffect } from '../hooks/useDeferredEffect';

const DEFAULT_FILTER = {
  daysSinceLastFollowUp: 10,
  excludeRecentlyPrinted: true,
  printedLetterExclusionDays: 7,
  departmentId: '',
  categoryId: '',
  search: '',
  page: 1,
  pageSize: 25,
};

export default function FollowUpPrintEligiblePage() {
  const navigate = useNavigate();
  const [draftFilters, setDraftFilters] = useState(DEFAULT_FILTER);
  const [appliedFilters, setAppliedFilters] = useState(DEFAULT_FILTER);
  const [items, setItems] = useState<EligibleTransaction[]>([]);
  const [totalCount, setTotalCount] = useState(0);
  const [preview, setPreview] = useState<FollowUpPrintEligibilityPreview | null>(null);
  const [templates, setTemplates] = useState<LetterTemplate[]>([]);
  const [departments, setDepartments] = useState<Department[]>([]);
  const [categories, setCategories] = useState<Category[]>([]);
  const [templateId, setTemplateId] = useState('');
  const [responseDeadlineDays, setResponseDeadlineDays] = useState('7');
  const [signatoryPosition, setSignatoryPosition] = useState('');
  const [signatoryRank, setSignatoryRank] = useState('');
  const [signatoryNameOverride, setSignatoryNameOverride] = useState('');
  const [loading, setLoading] = useState(true);
  const [creating, setCreating] = useState(false);
  const [error, setError] = useState('');
  const [message, setMessage] = useState('');

  const buildFilter = useCallback(() => ({
    daysSinceLastFollowUp: appliedFilters.daysSinceLastFollowUp,
    excludeRecentlyPrinted: appliedFilters.excludeRecentlyPrinted,
    printedLetterExclusionDays: appliedFilters.printedLetterExclusionDays,
    departmentId: appliedFilters.departmentId ? Number(appliedFilters.departmentId) : undefined,
    categoryId: appliedFilters.categoryId ? Number(appliedFilters.categoryId) : undefined,
    search: appliedFilters.search.trim() || undefined,
    page: appliedFilters.page,
    pageSize: appliedFilters.pageSize,
  }), [appliedFilters]);

  const loadData = useCallback(async (active: () => boolean) => {
    await Promise.resolve();
    if (active()) {
      setLoading(true);
      setError('');
    }
    try {
      const filter = buildFilter();
      const [eligibleRes, previewRes] = await Promise.all([
        followUpPrintApi.getEligible(filter),
        followUpPrintApi.previewJob({
          filter,
          templateId: templateId ? Number(templateId) : undefined,
          responseDeadlineDays: responseDeadlineDays ? Number(responseDeadlineDays) : undefined,
        }),
      ]);
      if (!active()) return;
      setItems(eligibleRes.data.items);
      setTotalCount(eligibleRes.data.totalCount);
      setPreview(previewRes.data);
    } catch (err: unknown) {
      if (!active()) return;
      setError(getApiErrorMessage(err));
    } finally {
      if (active()) setLoading(false);
    }
  }, [buildFilter, responseDeadlineDays, templateId]);

  useEffect(() => {
    let cancelled = false;
    (async () => {
      try {
        const [templatesRes, departmentsRes, categoriesRes] = await Promise.all([
          letterTemplatesApi.list({ type: 'FollowUp', active: true }),
          departmentsApi.getAll(),
          categoriesApi.getAll(),
        ]);
        if (cancelled) return;
        setTemplates(templatesRes.data);
        setDepartments(departmentsRes.data);
        setCategories(categoriesRes.data);
        const defaultTemplate = templatesRes.data.find((t) => t.isDefault) ?? templatesRes.data[0];
        if (defaultTemplate) setTemplateId(String(defaultTemplate.id));
      } catch {
        if (!cancelled) setError('تعذر تحميل بيانات الصفحة');
      }
    })();
    return () => {
      cancelled = true;
    };
  }, []);

  useDeferredEffect(loadData, [loadData]);

  const handleSearch = (event: FormEvent) => {
    event.preventDefault();
    setAppliedFilters({ ...draftFilters, page: 1 });
  };

  const renderTransactionsContent = () => {
    if (loading) {
      return <LoadingInline label="جاري تحميل المعاملات..." />;
    }
    if (items.length === 0) {
      return <EmptyState title="لا توجد معاملات مستحقة" description="جرّب تعديل معايير البحث." />;
    }
    return (
      <>
        <div className="table-wrapper table-wrapper-spaced">
          <table className="data-table">
            <thead>
              <tr>
                <th>رقم الوارد</th>
                <th>الموضوع</th>
                <th>تاريخ الوارد</th>
                <th>أيام منذ المرجع</th>
                <th>ترتيب التعقيب</th>
                <th>الجهة</th>
                <th>عرض</th>
              </tr>
            </thead>
            <tbody>
              {items.map((item) => (
                <tr key={item.transactionId}>
                  <td>{item.incomingNumber}</td>
                  <td>{item.subject}</td>
                  <td><DateDisplay date={item.incomingDate} /></td>
                  <td>{item.daysSinceReference}</td>
                  <td>{item.expectedFollowUpSequence}</td>
                  <td>{item.primaryTargetEntity ?? '—'}</td>
                  <td><Link to={`/transactions/${item.transactionId}`} className="btn btn-sm btn-outline">عرض</Link></td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
        <Pagination
          page={appliedFilters.page}
          pageSize={appliedFilters.pageSize}
          total={totalCount}
          itemCount={items.length}
          onPageChange={(page) => setAppliedFilters((prev) => ({ ...prev, page }))}
          onPageSizeChange={(pageSize) => setAppliedFilters((prev) => ({ ...prev, pageSize, page: 1 }))}
        />
      </>
    );
  };

  const handleCreateJob = async () => {
    if (!preview || preview.eligibleTransactionCount === 0) {
      setError('لا توجد معاملات مستحقة للطباعة ضمن الفلاتر الحالية.');
      return;
    }
    setCreating(true);
    setError('');
    setMessage('');
    try {
      const res = await followUpPrintApi.createJob({
        filter: buildFilter(),
        templateId: templateId ? Number(templateId) : undefined,
        responseDeadlineDays: responseDeadlineDays ? Number(responseDeadlineDays) : undefined,
        idempotencyKey: createIdempotencyKey(),
        signatoryPosition: signatoryPosition.trim() || undefined,
        signatoryRank: signatoryRank.trim() || undefined,
        signatoryNameOverride: signatoryNameOverride.trim() || undefined,
      });
      // Navigate to the job detail page so the user can track progress and print
      navigate(`/follow-up-print/jobs/${res.data.id}`);
    } catch (err: unknown) {
      setError(getApiErrorMessage(err) || 'تعذر إنشاء مهمة الطباعة.');
    } finally {
      setCreating(false);
    }
  };

  return (
    <div dir="rtl">
      <PageHeader
        title="المعاملات المستحقة للتعقيب"
        subtitle="استعراض المعاملات المؤهلة وإنشاء مهام طباعة جماعية"
        actions={<Link to="/follow-up-print/jobs" className="btn btn-outline">مهام الطباعة</Link>}
      />

      {message && <Alert variant="success">{message}</Alert>}
      {error && <Alert variant="error">{error}</Alert>}

      <div className="card filter-card">
        <form className="form-grid" onSubmit={handleSearch}>
          <div className="form-group">
            <label htmlFor="days-since">أيام منذ آخر تعقيب</label>
            <input
              id="days-since"
              type="number"
              min={1}
              value={draftFilters.daysSinceLastFollowUp}
              onChange={(e) => setDraftFilters((prev) => ({ ...prev, daysSinceLastFollowUp: Number(e.target.value) || 10 }))}
            />
          </div>
          <div className="form-group">
            <label htmlFor="exclusion-days">استبعاد المطبوع خلال (أيام)</label>
            <input
              id="exclusion-days"
              type="number"
              min={0}
              value={draftFilters.printedLetterExclusionDays}
              onChange={(e) => setDraftFilters((prev) => ({ ...prev, printedLetterExclusionDays: Number(e.target.value) || 0 }))}
            />
          </div>
          <div className="form-group">
            <label htmlFor="department-filter">الإدارة</label>
            <select
              id="department-filter"
              value={draftFilters.departmentId}
              onChange={(e) => setDraftFilters((prev) => ({ ...prev, departmentId: e.target.value }))}
            >
              <option value="">الكل</option>
              {departments.map((d) => <option key={d.id} value={d.id}>{d.name}</option>)}
            </select>
          </div>
          <div className="form-group">
            <label htmlFor="category-filter">التصنيف</label>
            <select
              id="category-filter"
              value={draftFilters.categoryId}
              onChange={(e) => setDraftFilters((prev) => ({ ...prev, categoryId: e.target.value }))}
            >
              <option value="">الكل</option>
              {categories.map((c) => <option key={c.id} value={c.id}>{c.name}</option>)}
            </select>
          </div>
          <div className="form-group">
            <label htmlFor="search-filter">بحث</label>
            <input
              id="search-filter"
              value={draftFilters.search}
              onChange={(e) => setDraftFilters((prev) => ({ ...prev, search: e.target.value }))}
              placeholder="رقم الوارد أو الموضوع"
            />
          </div>
          <div className="form-group">
            <label htmlFor="exclude-recent">
              <input
                id="exclude-recent"
                type="checkbox"
                checked={draftFilters.excludeRecentlyPrinted}
                onChange={(e) => setDraftFilters((prev) => ({ ...prev, excludeRecentlyPrinted: e.target.checked }))}
              />
              {' '}استبعاد المطبوع مؤخراً
            </label>
          </div>
          <div className="form-actions full-width">
            <button type="submit" className="btn btn-secondary">تطبيق الفلاتر</button>
          </div>
        </form>
      </div>

      {preview && (
        <div className="card mt-4 follow-up-print-summary">
          <div className="stats-grid">
            <div><strong>مطابقة:</strong> {preview.matchedCount}</div>
            <div><strong>مستحقة:</strong> {preview.eligibleTransactionCount}</div>
            <div><strong>مستبعدة (مطبوعة):</strong> {preview.recentlyPrintedExcludedCount}</div>
            <div><strong>خطابات متوقعة:</strong> {preview.estimatedLetterCount}</div>
            <div><strong>أجزاء متوقعة:</strong> {preview.estimatedPartCount}</div>
          </div>
          <div className="form-grid mt-4">
            <div className="form-group">
              <label htmlFor="job-template">قالب الخطاب</label>
              <select id="job-template" value={templateId} onChange={(e) => setTemplateId(e.target.value)}>
                {templates.map((t) => <option key={t.id} value={t.id}>{t.name}</option>)}
              </select>
            </div>
            <div className="form-group">
              <label htmlFor="response-days">مهلة الرد (أيام)</label>
              <input
                id="response-days"
                type="number"
                min={1}
                value={responseDeadlineDays}
                onChange={(e) => setResponseDeadlineDays(e.target.value)}
              />
            </div>
            <div className="form-group">
              <label htmlFor="signatory-position">المنصب الوظيفي (اختياري)</label>
              <input
                id="signatory-position"
                value={signatoryPosition}
                onChange={(e) => setSignatoryPosition(e.target.value)}
                placeholder="مدير الإدارة"
              />
            </div>
            <div className="form-group">
              <label htmlFor="signatory-rank">الرتبة (اختياري)</label>
              <input
                id="signatory-rank"
                value={signatoryRank}
                onChange={(e) => setSignatoryRank(e.target.value)}
                placeholder="عميد"
              />
            </div>
            <div className="form-group">
              <label htmlFor="signatory-name-override">اسم الموقّع (اختياري)</label>
              <input
                id="signatory-name-override"
                value={signatoryNameOverride}
                onChange={(e) => setSignatoryNameOverride(e.target.value)}
                placeholder="اترك فارغاً لاستخدام اسم المستخدم الحالي"
              />
            </div>
          </div>
          <div className="form-actions">
            <p className="text-muted text-sm mt-0 mb-2">
              سيشمل إنشاء المهمة جميع المعاملات المستحقة ({preview.eligibleTransactionCount}) المطابقة للفلاتر الحالية، وليس الصفحة المعروضة فقط.
            </p>
            <button
              type="button"
              className="btn btn-primary"
              disabled={creating || preview.eligibleTransactionCount === 0}
              onClick={() => { handleCreateJob().catch(() => undefined); }}
            >
              {creating ? 'جاري الإنشاء...' : 'إنشاء مهمة طباعة'}
            </button>
          </div>
        </div>
      )}

      <div className="card mt-4">
        {renderTransactionsContent()}
      </div>
    </div>
  );
}
