import { useCallback, useMemo, useState } from 'react';
import { institutionalReportsApi, type InstitutionalReportManifest, type ReportBuildRequest } from '../api/services';
import {
  DetailOverflowAction,
  ExportFormat,
  ExportMode,
  InstitutionalReportType,
  PageNumberingMode,
  ReportSectionId,
} from '../api/institutionalReports.constants';
import {
  defaultDate,
  exportFormatLabels,
  getPageSelectionSummary,
  getPreviewStatusMessage,
} from './reportBuilderHelpers';
import { useReportBuilderExport } from './useReportBuilderExport';
import { ReportPreviewDocument } from './ReportPreviewDocument';
import '../styles/institutional-report.css';

const REPORT_TYPES = [
  { value: InstitutionalReportType.ExecutiveComprehensive, label: 'التقرير التنفيذي الشامل' },
  { value: InstitutionalReportType.OverdueTransactions, label: 'تقرير المعاملات المتأخرة' },
  { value: InstitutionalReportType.JointDepartmentTransactions, label: 'تقرير معاملات الإدارات المشتركة' },
  { value: InstitutionalReportType.PartialResponses, label: 'تقرير الإفادات والردود الجزئية' },
  { value: InstitutionalReportType.SingleTransaction, label: 'تقرير معاملة واحدة' },
] as const;

const SECTIONS = [
  { id: ReportSectionId.Cover, label: 'الغلاف' },
  { id: ReportSectionId.ExecutiveSummary, label: 'الملخص التنفيذي' },
  { id: ReportSectionId.IndicatorsDashboard, label: 'لوحة المؤشرات والاتجاهات' },
  { id: ReportSectionId.DepartmentPerformance, label: 'أداء الإدارات' },
  { id: ReportSectionId.RisksAndAlerts, label: 'المخاطر والتنبيهات' },
  { id: ReportSectionId.ExecutiveRecommendations, label: 'التوصيات التنفيذية' },
  { id: ReportSectionId.TransactionDetails, label: 'المعاملات التفصيلية' },
  { id: ReportSectionId.ReportMetadata, label: 'بيانات التقرير والفلاتر' },
] as const;

export default function ReportBuilderPage() {
  const [reportType, setReportType] = useState<typeof InstitutionalReportType[keyof typeof InstitutionalReportType]>(
    InstitutionalReportType.ExecutiveComprehensive,
  );
  const [dateFrom, setDateFrom] = useState(defaultDate(-30));
  const [dateTo, setDateTo] = useState(defaultDate(0));
  const [title, setTitle] = useState('تقرير المتابعة الإجرائية للمعاملات');
  const [sectionIds, setSectionIds] = useState<number[]>(SECTIONS.map((s) => s.id));
  const [manifest, setManifest] = useState<InstitutionalReportManifest | null>(null);
  const [currentPage, setCurrentPage] = useState(1);
  const [selectedPages, setSelectedPages] = useState<number[]>([]);
  const [pageSelectionMode, setPageSelectionMode] = useState<'thumbnails' | 'range'>('thumbnails');
  const [pageRange, setPageRange] = useState('');
  const [zoom, setZoom] = useState(0.75);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState('');

  const includesTransactionDetails = sectionIds.includes(ReportSectionId.TransactionDetails);

  const buildRequest = useCallback((): ReportBuildRequest => ({
    reportType,
    title,
    sectionIds: sectionIds as ReportBuildRequest['sectionIds'],
    filters: {
      dateFrom: dateFrom || null,
      dateTo: dateTo || null,
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

  const {
    openExportDialog,
    closeExportDialog,
    exportDialogRef,
    handleExportDialogCancel,
    exportMode,
    setExportMode,
    exportFormat,
    setExportFormat,
    pageNumberingMode,
    setPageNumberingMode,
    includePartialCover,
    setIncludePartialCover,
    includePartialManifest,
    setIncludePartialManifest,
    detailOverflowAction,
    setDetailOverflowAction,
    requiresOverflowChoice,
    runExport,
  } = useReportBuilderExport({
    buildRequest,
    manifest,
    sectionIds,
    selectedPages,
    pageSelectionMode,
    pageRange,
    currentPage,
    setLoading,
    setError,
  });

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

  const previewStatusMessage = getPreviewStatusMessage(loading, error, Boolean(manifest));
  const pageSelectionSummary = getPageSelectionSummary(
    pageSelectionMode,
    selectedPages,
    pageRange,
    manifest?.totalPages ?? 0,
  );

  const toggleSection = (id: number) => {
    setSectionIds((prev) => prev.includes(id) ? prev.filter((x) => x !== id) : [...prev, id]);
  };

  const togglePage = (pageNo: number) => {
    setPageSelectionMode('thumbnails');
    setPageRange('');
    setSelectedPages((prev) => prev.includes(pageNo) ? prev.filter((p) => p !== pageNo) : [...prev, pageNo].sort((a, b) => a - b));
  };

  const handlePageRangeChange = (value: string) => {
    setPageSelectionMode('range');
    setSelectedPages([]);
    setPageRange(value);
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
          <button type="button" className="btn btn-primary" onClick={openExportDialog} disabled={!manifest}>
            تصدير
          </button>
        </div>
      </div>

      {error && <div className="alert alert-error">{error}</div>}

      {manifest?.requiresDetailOverflowAction && includesTransactionDetails && (
        <div className="alert alert-warning">
          يتجاوز عدد المعاملات المطابقة ({manifest.totalMatchedRows?.toLocaleString('ar-SA') ?? manifest.totalMatchingTransactions?.toLocaleString('ar-SA') ?? '—'})
          {' '}حد صفوف التفاصيل التشغيلي ({manifest.detailRowLimit?.toLocaleString('ar-SA') ?? '—'}).
          {' '}بطاقات KPI والرسوم محسوبة من كامل النتائج؛ جدول التفاصيل يعرض {manifest.exportedDetailRows?.toLocaleString('ar-SA') ?? manifest.includedTransactionCount?.toLocaleString('ar-SA') ?? '—'} صفًا
          {manifest.detailPartsCount && manifest.detailPartsCount > 1
            ? ` (التصدير الكامل يتطلب ${manifest.detailPartsCount} ملفات PDF)`
            : ''}.
        </div>
      )}

      <div className="report-builder-grid">
        <aside className="report-builder-panel">
          <h3>إعدادات التقرير</h3>
          <label htmlFor="report-type">نوع التقرير</label>
          <select
            id="report-type"
            value={reportType}
            onChange={(e) => setReportType(Number(e.target.value) as typeof reportType)}
          >
            {REPORT_TYPES.map((t) => <option key={t.value} value={t.value}>{t.label}</option>)}
          </select>
          <label htmlFor="date-from">من تاريخ</label>
          <input id="date-from" type="date" value={dateFrom} onChange={(e) => setDateFrom(e.target.value)} />
          <label htmlFor="date-to">إلى تاريخ</label>
          <input id="date-to" type="date" value={dateTo} onChange={(e) => setDateTo(e.target.value)} />
          <label htmlFor="report-title">عنوان التقرير</label>
          <input id="report-title" value={title} onChange={(e) => setTitle(e.target.value)} />

          <h3 style={{ marginTop: '1rem' }}>الأقسام المراد تضمينها</h3>
          <div className="report-section-list">
            <button type="button" className="btn btn-sm btn-outline" onClick={() => setSectionIds(SECTIONS.map((s) => s.id))}>تحديد الكل</button>
            <button type="button" className="btn btn-sm btn-outline" onClick={() => setSectionIds([])}>إلغاء تحديد الكل</button>
            {SECTIONS.map((section) => (
              <label key={section.id} htmlFor={`section-${section.id}`}>
                <input
                  id={`section-${section.id}`}
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
            {pageSelectionSummary && <span>{pageSelectionSummary}</span>}
            <label htmlFor="preview-zoom">تكبير</label>
            <input
              id="preview-zoom"
              type="range"
              min="0.5"
              max="1"
              step="0.05"
              value={zoom}
              onChange={(e) => setZoom(Number(e.target.value))}
            />
          </div>

          <div className="report-preview-shell">
            <div className="report-thumbs">
              {manifest?.pages.map((page) => {
                const checkboxId = `thumb-select-${page.originalPageNumber}`;
                return (
                  <div
                    key={page.originalPageNumber}
                    className={`report-thumb${page.originalPageNumber === currentPage ? ' is-active' : ''}${selectedPages.includes(page.originalPageNumber) ? ' is-selected' : ''}`}
                  >
                    <input
                      id={checkboxId}
                      type="checkbox"
                      checked={selectedPages.includes(page.originalPageNumber)}
                      onChange={() => togglePage(page.originalPageNumber)}
                      aria-label={`تحديد الصفحة ${page.originalPageNumber}`}
                    />
                    <button
                      type="button"
                      className="report-thumb-open"
                      onClick={() => setCurrentPage(page.originalPageNumber)}
                    >
                      {page.originalPageNumber}. {page.sectionName}
                    </button>
                  </div>
                );
              })}
            </div>
            <div className="report-preview-stage">
              {manifest && (
                <div className="report-preview-stats" aria-live="polite">
                  <span>النتائج المطابقة: {(manifest.totalMatchedRows ?? manifest.totalMatchingTransactions ?? 0).toLocaleString('ar-SA')}</span>
                  <span>صفوف معروضة: {(manifest.loadedDetailRows ?? manifest.exportedDetailRows ?? manifest.includedTransactionCount ?? 0).toLocaleString('ar-SA')}</span>
                  <span>الصفحات: {manifest.totalPages.toLocaleString('ar-SA')}</span>
                  {manifest.detailRowsTruncated ? <span className="text-warning">التفاصيل مقتطعة — KPI من كامل النتائج</span> : null}
                </div>
              )}
              {activePage ? (
                <ReportPreviewDocument
                  htmlContent={activePage.htmlContent}
                  stylesheet={manifest?.stylesheet}
                  zoom={zoom}
                  title={`${activePage.sectionName} — صفحة ${activePage.renderedPageNumber}`}
                />
              ) : (
                <p className="text-muted">{previewStatusMessage}</p>
              )}
            </div>
          </div>
        </section>
      </div>

      <dialog
        ref={exportDialogRef}
        className="report-export-modal"
        aria-labelledby="report-export-dialog-title"
        onCancel={handleExportDialogCancel}
      >
        <div className="report-export-card">
          <h3 id="report-export-dialog-title">خيارات تصدير التقرير</h3>
          <label htmlFor="export-mode">وضع التصدير</label>
          <select
            id="export-mode"
            value={exportMode}
            onChange={(e) => setExportMode(Number(e.target.value) as typeof exportMode)}
          >
            <option value={ExportMode.FullReport}>تصدير كامل التقرير</option>
            <option value={ExportMode.SelectedSections}>تصدير حسب الأقسام</option>
            <option value={ExportMode.SelectedPages}>تصدير صفحات محددة</option>
            <option value={ExportMode.CurrentPage}>تصدير الصفحة الحالية</option>
          </select>
          <label htmlFor="export-format">صيغة الملف</label>
          <select
            id="export-format"
            value={exportFormat}
            onChange={(e) => setExportFormat(Number(e.target.value) as typeof exportFormat)}
          >
            {Object.entries(exportFormatLabels).map(([value, label]) => (
              <option key={value} value={value}>{label}</option>
            ))}
          </select>
          {exportMode === ExportMode.SelectedPages && (
            <>
              <p className="text-muted">
                النمط النشط: {pageSelectionMode === 'range' && pageRange.trim() ? 'نطاق الصفحات' : 'تحديد الصور المصغرة'}
              </p>
              <label htmlFor="page-range">نطاق الصفحات (مثال: 1,3-5,8)</label>
              <input
                id="page-range"
                value={pageRange}
                onChange={(e) => handlePageRangeChange(e.target.value)}
                placeholder="1,3-5,8"
              />
            </>
          )}
          <label htmlFor="page-numbering-mode">ترقيم الصفحات</label>
          <select
            id="page-numbering-mode"
            value={pageNumberingMode}
            onChange={(e) => setPageNumberingMode(Number(e.target.value) as typeof pageNumberingMode)}
          >
            <option value={PageNumberingMode.Restart}>إعادة الترقيم من 1</option>
            <option value={PageNumberingMode.PreserveOriginal}>الاحتفاظ بالترقيم الأصلي</option>
          </select>
          <label htmlFor="include-partial-cover">
            <input
              id="include-partial-cover"
              type="checkbox"
              checked={includePartialCover}
              onChange={(e) => setIncludePartialCover(e.target.checked)}
            />
            {' '}إضافة غلاف للنسخة الجزئية
          </label>
          <label htmlFor="include-partial-manifest">
            <input
              id="include-partial-manifest"
              type="checkbox"
              checked={includePartialManifest}
              onChange={(e) => setIncludePartialManifest(e.target.checked)}
            />
            {' '}إدراج صفحة تعريف النسخة الجزئية
          </label>
          {(exportFormat === ExportFormat.Docx || exportFormat === ExportFormat.Xlsx) && exportMode === ExportMode.SelectedPages && (
            <p className="text-muted">اختيار الصفحات الفعلية يطبق بدقة على PDF. في Word/Excel سيتم تصدير الأقسام المقابلة لأن توزيع الصفحات قد يختلف.</p>
          )}
          {requiresOverflowChoice && (
            <fieldset className="report-overflow-options">
              <legend>تجاوز حد صفوف التفاصيل</legend>
              <p className="text-muted">
                المطلوب: {manifest?.totalMatchingTransactions?.toLocaleString('ar-SA')} معاملة —
                الحد: {manifest?.detailRowLimit?.toLocaleString('ar-SA')} صف لكل ملف PDF/DOCX.
              </p>
              <label htmlFor="overflow-summary-only">
                <input
                  id="overflow-summary-only"
                  type="radio"
                  name="detailOverflowAction"
                  checked={detailOverflowAction === DetailOverflowAction.SummaryOnly}
                  onChange={() => setDetailOverflowAction(DetailOverflowAction.SummaryOnly)}
                />
                {' '}ملخص كامل بدون تفاصيل مضمّنة (PDF/DOCX/HTML)
              </label>
              <label htmlFor="overflow-split-pdf">
                <input
                  id="overflow-split-pdf"
                  type="radio"
                  name="detailOverflowAction"
                  checked={detailOverflowAction === DetailOverflowAction.SplitPdf}
                  onChange={() => setDetailOverflowAction(DetailOverflowAction.SplitPdf)}
                  disabled={exportFormat !== ExportFormat.Pdf}
                />
                {' '}تقسيم التفاصيل إلى عدة ملفات PDF (ملف ZIP)
              </label>
              <label htmlFor="overflow-xlsx">
                <input
                  id="overflow-xlsx"
                  type="radio"
                  name="detailOverflowAction"
                  checked={detailOverflowAction === DetailOverflowAction.FullDetailsXlsx}
                  onChange={() => setDetailOverflowAction(DetailOverflowAction.FullDetailsXlsx)}
                />
                {' '}تصدير التفاصيل كاملة إلى Excel (XLSX)
              </label>
            </fieldset>
          )}
          <div className="report-export-actions">
            <button type="button" className="btn btn-primary" onClick={runExport} disabled={loading}>تنفيذ التصدير</button>
            <button type="button" className="btn btn-outline" onClick={closeExportDialog}>إلغاء</button>
          </div>
        </div>
      </dialog>
    </div>
  );
}
