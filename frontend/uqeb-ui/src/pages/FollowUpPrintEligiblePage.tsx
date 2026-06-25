import { useCallback, useEffect, useState, type FormEvent } from 'react';
import { Link } from 'react-router-dom';
import { categoriesApi, departmentsApi, followUpPrintApi, letterTemplatesApi } from '../api/services';
import type {
  Category, Department, EligibleTransaction, FollowUpPrintEligibilityPreview, LetterTemplate,
} from '../api/types';
import { getApiErrorMessage } from '../utils/apiHelpers';
import DateDisplay from '../components/DateDisplay';
import {
  Alert, EmptyState, LoadingInline, PageHeader, Pagination,
} from '../components/ui';

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
  const [filters, setFilters] = useState(DEFAULT_FILTER);
  const [items, setItems] = useState<EligibleTransaction[]>([]);
  const [totalCount, setTotalCount] = useState(0);
  const [preview, setPreview] = useState<FollowUpPrintEligibilityPreview | null>(null);
  const [templates, setTemplates] = useState<LetterTemplate[]>([]);
  const [departments, setDepartments] = useState<Department[]>([]);
  const [categories, setCategories] = useState<Category[]>([]);
  const [templateId, setTemplateId] = useState('');
  const [responseDeadlineDays, setResponseDeadlineDays] = useState('7');
  const [loading, setLoading] = useState(true);
  const [creating, setCreating] = useState(false);
  const [error, setError] = useState('');
  const [message, setMessage] = useState('');

  const buildFilter = useCallback(() => ({
    daysSinceLastFollowUp: filters.daysSinceLastFollowUp,
    excludeRecentlyPrinted: filters.excludeRecentlyPrinted,
    printedLetterExclusionDays: filters.printedLetterExclusionDays,
    departmentId: filters.departmentId ? Number(filters.departmentId) : undefined,
    categoryId: filters.categoryId ? Number(filters.categoryId) : undefined,
    search: filters.search.trim() || undefined,
    page: filters.page,
    pageSize: filters.pageSize,
  }), [filters]);

  const loadData = useCallback(async () => {
    setLoading(true);
    setError('');
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
      setItems(eligibleRes.data.items);
      setTotalCount(eligibleRes.data.totalCount);
      setPreview(previewRes.data);
    } catch (err: unknown) {
      setError(getApiErrorMessage(err));
    } finally {
      setLoading(false);
    }
  }, [buildFilter, responseDeadlineDays, templateId]);

  useEffect(() => {
    Promise.all([
      letterTemplatesApi.list({ active: true }),
      departmentsApi.getAll(),
      categoriesApi.getAll(),
    ])
      .then(([templatesRes, departmentsRes, categoriesRes]) => {
        setTemplates(templatesRes.data);
        setDepartments(departmentsRes.data);
        setCategories(categoriesRes.data);
        const defaultTemplate = templatesRes.data.find((t) => t.isDefault) ?? templatesRes.data[0];
        if (defaultTemplate) setTemplateId(String(defaultTemplate.id));
      })
      .catch(() => setError('تعذر تحميل بيانات الصفحة'));
  }, []);

  useEffect(() => {
    void loadData();
  }, [loadData]);

  const handleSearch = (event: FormEvent) => {
    event.preventDefault();
    setFilters((prev) => ({ ...prev, page: 1 }));
  };

  const handleCreateJob = async () => {
    if (!preview || preview.eligibleCount === 0) {
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
        idempotencyKey: crypto.randomUUID(),
      });
      setMessage(`تم إنشاء مهمة الطباعة رقم ${res.data.id}.`);
    } catch (err: unknown) {
      setError(getApiErrorMessage(err));
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
              value={filters.daysSinceLastFollowUp}
              onChange={(e) => setFilters((prev) => ({ ...prev, daysSinceLastFollowUp: Number(e.target.value) || 10 }))}
            />
          </div>
          <div className="form-group">
            <label htmlFor="exclusion-days">استبعاد المطبوع خلال (أيام)</label>
            <input
              id="exclusion-days"
              type="number"
              min={0}
              value={filters.printedLetterExclusionDays}
              onChange={(e) => setFilters((prev) => ({ ...prev, printedLetterExclusionDays: Number(e.target.value) || 0 }))}
            />
          </div>
          <div className="form-group">
            <label htmlFor="department-filter">الإدارة</label>
            <select
              id="department-filter"
              value={filters.departmentId}
              onChange={(e) => setFilters((prev) => ({ ...prev, departmentId: e.target.value, page: 1 }))}
            >
              <option value="">الكل</option>
              {departments.map((d) => <option key={d.id} value={d.id}>{d.name}</option>)}
            </select>
          </div>
          <div className="form-group">
            <label htmlFor="category-filter">التصنيف</label>
            <select
              id="category-filter"
              value={filters.categoryId}
              onChange={(e) => setFilters((prev) => ({ ...prev, categoryId: e.target.value, page: 1 }))}
            >
              <option value="">الكل</option>
              {categories.map((c) => <option key={c.id} value={c.id}>{c.name}</option>)}
            </select>
          </div>
          <div className="form-group">
            <label htmlFor="search-filter">بحث</label>
            <input
              id="search-filter"
              value={filters.search}
              onChange={(e) => setFilters((prev) => ({ ...prev, search: e.target.value }))}
              placeholder="رقم الوارد أو الموضوع"
            />
          </div>
          <div className="form-group">
            <label htmlFor="exclude-recent">
              <input
                id="exclude-recent"
                type="checkbox"
                checked={filters.excludeRecentlyPrinted}
                onChange={(e) => setFilters((prev) => ({ ...prev, excludeRecentlyPrinted: e.target.checked, page: 1 }))}
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
            <div><strong>مستحقة:</strong> {preview.eligibleCount}</div>
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
          </div>
          <div className="form-actions">
            <button type="button" className="btn btn-primary" disabled={creating || preview.eligibleCount === 0} onClick={() => { void handleCreateJob(); }}>
              {creating ? 'جاري الإنشاء...' : 'إنشاء مهمة طباعة'}
            </button>
          </div>
        </div>
      )}

      <div className="card mt-4">
        {loading ? (
          <LoadingInline label="جاري تحميل المعاملات..." />
        ) : items.length === 0 ? (
          <EmptyState title="لا توجد معاملات مستحقة" description="جرّب تعديل معايير البحث." />
        ) : (
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
              page={filters.page}
              pageSize={filters.pageSize}
              total={totalCount}
              itemCount={items.length}
              onPageChange={(page) => setFilters((prev) => ({ ...prev, page }))}
              onPageSizeChange={(pageSize) => setFilters((prev) => ({ ...prev, pageSize, page: 1 }))}
            />
          </>
        )}
      </div>
    </div>
  );
}
