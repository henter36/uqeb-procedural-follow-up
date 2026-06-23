import { useCallback, useMemo, useState } from 'react';
import { institutionalReportsApi, type InstitutionalReportManifest } from '../api/services';
import { downloadBlob } from '../utils/downloadBlob';
import '../styles/institutional-report.css';

const REPORT_TYPES = [
  { value: 1, label: 'التقرير التنفيذي الشامل' },
  { value: 2, label: 'تقرير المعاملات المتأخرة' },
  { value: 3, label: 'تقرير معاملات الإدارات المشتركة' },
  { value: 4, label: 'تقرير الإفادات والردود الجزئية' },
  { value: 5, label: 'تقرير معاملة واحدة' },
];

const SECTIONS = [
  { id: 1, label: 'الغلاف' },
  { id: 2, label: 'الملخص التنفيذي' },
  { id: 3, label: 'لوحة المؤشرات والاتجاهات' },
  { id: 4, label: 'أداء الإدارات' },
  { id: 5, label: 'المخاطر والتنبيهات' },
  { id: 6, label: 'التوصيات التنفيذية' },
  { id: 7, label: 'المعاملات التفصيلية' },
  { id: 9, label: 'بيانات التقرير والفلاتر' },
];

type ExportMode = 1 | 2 | 3 | 4;
type ExportFormat = 1 | 2 | 3 | 4;

function defaultDate(offsetDays: number) {
  const d = new Date();
  d.setDate(d.getDate() + offsetDays);
  return d.toISOString().slice(0, 10);
}

export default function ReportBuilderPage() {
  const [reportType, setReportType] = useState(1);
  const [dateFrom, setDateFrom] = useState(defaultDate(-30));
  const [dateTo, setDateTo] = useState(defaultDate(0));
  const [title, setTitle] = useState('تقرير المتابعة الإجرائية للمعاملات');
  const [sectionIds, setSectionIds] = useState<number[]>(SECTIONS.map((s) => s.id));
  const [manifest, setManifest] = useState<InstitutionalReportManifest | null>(null);
  const [currentPage, setCurrentPage] = useState(1);
  const [selectedPages, setSelectedPages] = useState<number[]>([]);
  const [zoom, setZoom] = useState(0.75);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState('');
  const [exportOpen, setExportOpen] = useState(false);
  const [exportMode, setExportMode] = useState<ExportMode>(1);
  const [exportFormat, setExportFormat] = useState<ExportFormat>(1);
  const [pageRange, setPageRange] = useState('');
  const [pageNumberingMode, setPageNumberingMode] = useState<1 | 2>(2);
  const [includePartialCover, setIncludePartialCover] = useState(true);
  const [includePartialManifest, setIncludePartialManifest] = useState(true);

  const buildRequest = useCallback(() => ({
    reportType,
    title,
    sectionIds,
    filters: {
      dateFrom,
      dateTo,
      includeJointDepartmentTransactions: true,
      includeOverdue: true,
      includeDetails: true,
      includeRisks: true,
      includeRecommendations: true,
      departmentIds: [],
      partyIds: [],
      categoryIds: [],
      priorities: [],
      statuses: [],
    },
  }), [reportType, title, sectionIds, dateFrom, dateTo]);

  const loadPreview = async () => {
    setLoading(true);
    setError('');
    try {
      const { data } = await institutionalReportsApi.preview(buildRequest());
      setManifest(data);
      setCurrentPage(1);
      setSelectedPages([]);
    } catch {
      setError('تعذر إنشاء معاينة التقرير.');
    } finally {
      setLoading(false);
    }
  };

  const activePage = useMemo(
    () => manifest?.pages.find((p) => p.originalPageNumber === currentPage) ?? null,
    [manifest, currentPage],
  );

  const toggleSection = (id: number) => {
    setSectionIds((prev) => prev.includes(id) ? prev.filter((x) => x !== id) : [...prev, id]);
  };

  const togglePage = (pageNo: number) => {
    setSelectedPages((prev) => prev.includes(pageNo) ? prev.filter((p) => p !== pageNo) : [...prev, pageNo].sort((a, b) => a - b));
  };

  const runExport = async () => {
    setLoading(true);
    setError('');
    try {
      const response = await institutionalReportsApi.export({
        buildRequest: buildRequest(),
        exportFormat,
        exportMode,
        selectedSectionIds: sectionIds,
        selectedPageNumbers: exportMode === 3 ? selectedPages : [],
        pageRangeExpression: exportMode === 3 ? pageRange : undefined,
        currentPageNumber: exportMode === 4 ? currentPage : undefined,
        includePartialCover,
        includePartialManifest,
        pageNumberingMode,
      });
      const ext = exportFormat === 2 ? 'docx' : exportFormat === 3 ? 'xlsx' : exportFormat === 4 ? 'html' : 'pdf';
      const contentType = typeof response.headers['content-type'] === 'string'
        ? response.headers['content-type']
        : 'application/octet-stream';
      const blob = new Blob([response.data], { type: contentType });
      downloadBlob(blob, `institutional-report.${ext}`);
      setExportOpen(false);
    } catch {
      setError('تعذر تصدير التقرير.');
    } finally {
      setLoading(false);
    }
  };

  return (
    <div className="report-builder">
      <div className="page-header">
        <div>
          <h2 className="page-title">منشئ التقارير</h2>
          <p className="page-subtitle">إنشاء وتصدير التقارير المؤسسية متعددة الصيغ</p>
        </div>
        <div className="form-actions">
          <button type="button" className="btn btn-secondary" onClick={loadPreview} disabled={loading}>
            {loading ? 'جاري التوليد...' : 'معاينة التقرير'}
          </button>
          <button type="button" className="btn btn-primary" onClick={() => setExportOpen(true)} disabled={!manifest}>
            تصدير
          </button>
        </div>
      </div>

      {error && <div className="alert alert-error">{error}</div>}

      <div className="report-builder-grid">
        <aside className="report-builder-panel">
          <h3>إعدادات التقرير</h3>
          <label>نوع التقرير</label>
          <select value={reportType} onChange={(e) => setReportType(Number(e.target.value))}>
            {REPORT_TYPES.map((t) => <option key={t.value} value={t.value}>{t.label}</option>)}
          </select>
          <label>من تاريخ</label>
          <input type="date" value={dateFrom} onChange={(e) => setDateFrom(e.target.value)} />
          <label>إلى تاريخ</label>
          <input type="date" value={dateTo} onChange={(e) => setDateTo(e.target.value)} />
          <label>عنوان التقرير</label>
          <input value={title} onChange={(e) => setTitle(e.target.value)} />

          <h3 style={{ marginTop: '1rem' }}>الأقسام المراد تضمينها</h3>
          <div className="report-section-list">
            <button type="button" className="btn btn-sm btn-outline" onClick={() => setSectionIds(SECTIONS.map((s) => s.id))}>تحديد الكل</button>
            <button type="button" className="btn btn-sm btn-outline" onClick={() => setSectionIds([])}>إلغاء تحديد الكل</button>
            {SECTIONS.map((section) => (
              <label key={section.id}>
                <input
                  type="checkbox"
                  checked={sectionIds.includes(section.id)}
                  onChange={() => toggleSection(section.id)}
                />
                {section.label}
              </label>
            ))}
          </div>
        </aside>

        <section className="report-builder-panel">
          <div className="report-preview-toolbar">
            <button type="button" className="btn btn-sm btn-outline" disabled={!manifest || currentPage <= 1} onClick={() => setCurrentPage((p) => p - 1)}>السابق</button>
            <button type="button" className="btn btn-sm btn-outline" disabled={!manifest || currentPage >= (manifest?.totalPages ?? 1)} onClick={() => setCurrentPage((p) => p + 1)}>التالي</button>
            <span>{manifest ? `الصفحة ${currentPage} من ${manifest.totalPages}` : 'لا توجد معاينة بعد'}</span>
            <span>{selectedPages.length > 0 ? `تم تحديد ${selectedPages.length} من ${manifest?.totalPages ?? 0} صفحات` : ''}</span>
            <label>تكبير</label>
            <input type="range" min="0.5" max="1" step="0.05" value={zoom} onChange={(e) => setZoom(Number(e.target.value))} />
          </div>

          <div className="report-preview-shell">
            <div className="report-thumbs">
              {manifest?.pages.map((page) => (
                <button
                  key={page.originalPageNumber}
                  type="button"
                  className={`report-thumb${page.originalPageNumber === currentPage ? ' is-active' : ''}${selectedPages.includes(page.originalPageNumber) ? ' is-selected' : ''}`}
                  onClick={() => setCurrentPage(page.originalPageNumber)}
                >
                  <label style={{ display: 'flex', gap: '0.35rem', alignItems: 'center' }}>
                    <input
                      type="checkbox"
                      checked={selectedPages.includes(page.originalPageNumber)}
                      onChange={(e) => { e.stopPropagation(); togglePage(page.originalPageNumber); }}
                    />
                    {page.originalPageNumber}. {page.sectionName}
                  </label>
                </button>
              ))}
            </div>
            <div className="report-preview-stage">
              {activePage ? (
                <div className="report-preview-frame" style={{ transform: `scale(${zoom})` }}
                  dangerouslySetInnerHTML={{ __html: activePage.htmlContent }}
                />
              ) : (
                <p className="text-muted">اضغط «معاينة التقرير» لعرض الصفحات.</p>
              )}
            </div>
          </div>
        </section>
      </div>

      {exportOpen && (
        <div className="report-export-modal" role="dialog" aria-label="خيارات تصدير التقرير">
          <div className="report-export-card">
            <h3>خيارات تصدير التقرير</h3>
            <label>وضع التصدير</label>
            <select value={exportMode} onChange={(e) => setExportMode(Number(e.target.value) as ExportMode)}>
              <option value={1}>تصدير كامل التقرير</option>
              <option value={2}>تصدير حسب الأقسام</option>
              <option value={3}>تصدير صفحات محددة</option>
              <option value={4}>تصدير الصفحة الحالية</option>
            </select>
            <label>صيغة الملف</label>
            <select value={exportFormat} onChange={(e) => setExportFormat(Number(e.target.value) as ExportFormat)}>
              <option value={1}>PDF</option>
              <option value={2}>Word (DOCX)</option>
              <option value={3}>Excel (XLSX)</option>
              <option value={4}>HTML</option>
            </select>
            {exportMode === 3 && (
              <>
                <label>نطاق الصفحات (مثال: 1,3-5,8)</label>
                <input value={pageRange} onChange={(e) => setPageRange(e.target.value)} placeholder="1,3-5,8" />
              </>
            )}
            <label>ترقيم الصفحات</label>
            <select value={pageNumberingMode} onChange={(e) => setPageNumberingMode(Number(e.target.value) as 1 | 2)}>
              <option value={2}>إعادة الترقيم من 1</option>
              <option value={1}>الاحتفاظ بالترقيم الأصلي</option>
            </select>
            <label><input type="checkbox" checked={includePartialCover} onChange={(e) => setIncludePartialCover(e.target.checked)} /> إضافة غلاف للنسخة الجزئية</label>
            <label><input type="checkbox" checked={includePartialManifest} onChange={(e) => setIncludePartialManifest(e.target.checked)} /> إدراج صفحة تعريف النسخة الجزئية</label>
            {(exportFormat === 2 || exportFormat === 3) && exportMode === 3 && (
              <p className="text-muted">اختيار الصفحات الفعلية يطبق بدقة على PDF. في Word/Excel سيتم تصدير الأقسام المقابلة لأن توزيع الصفحات قد يختلف.</p>
            )}
            <div className="report-export-actions">
              <button type="button" className="btn btn-primary" onClick={runExport} disabled={loading}>تنفيذ التصدير</button>
              <button type="button" className="btn btn-outline" onClick={() => setExportOpen(false)}>إلغاء</button>
            </div>
          </div>
        </div>
      )}
    </div>
  );
}
