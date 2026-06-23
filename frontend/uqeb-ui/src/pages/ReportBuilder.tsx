import { useCallback, useMemo, useState } from 'react';
import { institutionalReportsApi, type InstitutionalReportManifest, type ReportBuildRequest, type ReportExportRequest } from '../api/services';
import {
  ExportFormat,
  ExportMode,
  InstitutionalReportType,
  PageNumberingMode,
  ReportSectionId,
} from '../api/institutionalReports.constants';
import { downloadBlob } from '../utils/downloadBlob';
import { addDaysIso, todayLocalIso } from '../utils/localDate';
import { sanitizeReportHtml } from '../utils/sanitizeReportHtml';
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

type PageSelectionMode = 'thumbnails' | 'range';

/** Default YYYY-MM-DD for date inputs using local calendar parts (not UTC). */
export function defaultDate(offsetDays: number): string {
  return addDaysIso(todayLocalIso(), offsetDays);
}

export function buildReportExportPageSelection(
  exportMode: typeof ExportMode[keyof typeof ExportMode],
  pageSelectionMode: PageSelectionMode,
  selectedPages: number[],
  pageRange: string,
  currentPage: number,
): Pick<ReportExportRequest, 'selectedPageNumbers' | 'pageRangeExpression' | 'currentPageNumber'> {
  if (exportMode !== ExportMode.SelectedPages) {
    return {
      selectedPageNumbers: [],
      pageRangeExpression: null,
      currentPageNumber: exportMode === ExportMode.CurrentPage ? currentPage : null,
    };
  }

  if (pageSelectionMode === 'range' && pageRange.trim()) {
    return {
      selectedPageNumbers: [],
      pageRangeExpression: pageRange.trim(),
      currentPageNumber: null,
    };
  }

  return {
    selectedPageNumbers: selectedPages,
    pageRangeExpression: null,
    currentPageNumber: null,
  };
}

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
  const [pageSelectionMode, setPageSelectionMode] = useState<PageSelectionMode>('thumbnails');
  const [zoom, setZoom] = useState(0.75);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState('');
  const [exportOpen, setExportOpen] = useState(false);
  const [exportMode, setExportMode] = useState<typeof ExportMode[keyof typeof ExportMode]>(ExportMode.FullReport);
  const [exportFormat, setExportFormat] = useState<typeof ExportFormat[keyof typeof ExportFormat]>(ExportFormat.Pdf);
  const [pageRange, setPageRange] = useState('');
  const [pageNumberingMode, setPageNumberingMode] = useState<typeof PageNumberingMode[keyof typeof PageNumberingMode]>(
    PageNumberingMode.Restart,
  );
  const [includePartialCover, setIncludePartialCover] = useState(true);
  const [includePartialManifest, setIncludePartialManifest] = useState(true);

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

  const sanitizedActivePageHtml = useMemo(
    () => (activePage ? sanitizeReportHtml(activePage.htmlContent) : ''),
    [activePage],
  );

  const toggleSection = (id: number) => {
    setSectionIds((prev) => prev.includes(id) ? prev.filter((x) => x !== id) : [...prev, id]);
  };

  const togglePage = (pageNo: number) => {
    setPageSelectionMode('thumbnails');
    setPageRange('');
    setSelectedPages((prev) => prev.includes(pageNo) ? prev.filter((p) => p !== pageNo) : [...prev, pageNo].sort((a, b) => a - b));
  };

  const runExport = async () => {
    setLoading(true);
    setError('');
    try {
      const pageSelection = buildReportExportPageSelection(
        exportMode,
        pageSelectionMode,
        selectedPages,
        pageRange,
        currentPage,
      );

      const response = await institutionalReportsApi.export({
        buildRequest: buildRequest(),
        exportFormat,
        exportMode,
        selectedSectionIds: sectionIds as ReportExportRequest['selectedSectionIds'],
        includePartialCover,
        includePartialManifest,
        pageNumberingMode,
        ...pageSelection,
      });
      const ext = exportFormat === ExportFormat.Docx ? 'docx'
        : exportFormat === ExportFormat.Xlsx ? 'xlsx'
          : exportFormat === ExportFormat.Html ? 'html'
            : 'pdf';
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
            <span>
              {pageSelectionMode === 'thumbnails' && selectedPages.length > 0
                ? `تم تحديد ${selectedPages.length} من ${manifest?.totalPages ?? 0} صفحات (تحديد مصغرات)`
                : pageSelectionMode === 'range' && pageRange.trim()
                  ? `نطاق الصفحات النشط: ${pageRange.trim()}`
                  : ''}
            </span>
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
              {activePage ? (
                <div
                  className="report-preview-frame"
                  style={{ transform: `scale(${zoom})` }}
                  dangerouslySetInnerHTML={{ __html: sanitizedActivePageHtml }}
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
              <option value={ExportFormat.Pdf}>PDF</option>
              <option value={ExportFormat.Docx}>Word (DOCX)</option>
              <option value={ExportFormat.Xlsx}>Excel (XLSX)</option>
              <option value={ExportFormat.Html}>HTML</option>
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
                  onChange={(e) => {
                    setPageSelectionMode('range');
                    setSelectedPages([]);
                    setPageRange(e.target.value);
                  }}
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
