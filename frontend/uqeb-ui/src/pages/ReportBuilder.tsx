import { type ChangeEvent, useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { Navigate } from 'react-router-dom';
import {
  institutionalReportsApi,
  departmentsApi,
  categoriesApi,
  externalPartiesApi,
  type InstitutionalReportManifest,
  type ReportBuildRequest,
  type ReportTemplate,
} from '../api/services';
import type { LookupItem } from '../api/types';
import {
  DetailOverflowAction,
  ExportFormat,
  ExportMode,
  InstitutionalReportType,
  PageNumberingMode,
  ReportComparisonMode,
  ReportContentLevel,
  ReportSectionId,
  ReportTimeGrouping,
} from '../api/institutionalReports.constants';
import { getApiErrorDetails } from '../utils/apiHelpers';
import { statusLabels, priorityLabels } from '../utils/labels';
import {
  defaultDate,
  exportFormatLabels,
  getPageSelectionSummary,
  getPreviewStatusMessage,
} from './reportBuilderHelpers';
import { useReportBuilderExport } from './useReportBuilderExport';
import { ReportPreviewDocument } from './ReportPreviewDocument';
import { useAuth } from '../context/useAuth';
import '../styles/institutional-report.css';

const REPORT_TYPES = [
  { value: InstitutionalReportType.ExecutiveComprehensive, label: 'التقرير التنفيذي الشامل' },
  { value: InstitutionalReportType.OverdueTransactions, label: 'تقرير المعاملات المتأخرة' },
  { value: InstitutionalReportType.JointDepartmentTransactions, label: 'تقرير معاملات الإدارات المشتركة' },
  { value: InstitutionalReportType.PartialResponses, label: 'تقرير الإفادات والردود الجزئية' },
] as const;

const SECTIONS = [
  { id: ReportSectionId.Cover, label: 'الغلاف' },
  { id: ReportSectionId.ExecutiveSummary, label: 'الملخص التنفيذي' },
  { id: ReportSectionId.KeyPerformanceIndicators, label: 'مؤشرات الأداء الرئيسية' },
  { id: ReportSectionId.SignificantFindings, label: 'النتائج المهمة' },
  { id: ReportSectionId.CriticalCases, label: 'الحالات الحرجة' },
  { id: ReportSectionId.TimeTrends, label: 'التحليل الزمني' },
  { id: ReportSectionId.IndicatorsDashboard, label: 'لوحة المؤشرات والاتجاهات' },
  { id: ReportSectionId.DepartmentPerformance, label: 'أداء الإدارات' },
  { id: ReportSectionId.ExternalPartyAnalysis, label: 'تحليل الجهات الخارجية' },
  { id: ReportSectionId.ClassificationAndPriorityAnalysis, label: 'التصنيفات والأولويات' },
  { id: ReportSectionId.DelayAndBottleneckAnalysis, label: 'الاختناقات والتأخر' },
  { id: ReportSectionId.DataQuality, label: 'جودة البيانات' },
  { id: ReportSectionId.RisksAndAlerts, label: 'المخاطر والتنبيهات' },
  { id: ReportSectionId.ExecutiveRecommendations, label: 'التوصيات التنفيذية' },
  { id: ReportSectionId.RecommendationsAndActionPlan, label: 'خطة الإجراءات' },
  { id: ReportSectionId.TransactionDetails, label: 'المعاملات التفصيلية' },
  { id: ReportSectionId.Appendices, label: 'الملاحق' },
  { id: ReportSectionId.MethodologyAndDefinitions, label: 'المنهجية والتعريفات' },
  { id: ReportSectionId.ReportMetadata, label: 'بيانات التقرير والفلاتر' },
] as const;

const SECTION_LABEL_MAP = new Map(SECTIONS.map((s) => [s.id as number, s.label]));

const SECTION_GROUPS = [
  {
    label: 'الأساسيات والملخص',
    ids: [
      ReportSectionId.Cover,
      ReportSectionId.ExecutiveSummary,
      ReportSectionId.KeyPerformanceIndicators,
      ReportSectionId.SignificantFindings,
    ] as number[],
  },
  {
    label: 'التحليل والأداء',
    ids: [
      ReportSectionId.TimeTrends,
      ReportSectionId.IndicatorsDashboard,
      ReportSectionId.DepartmentPerformance,
      ReportSectionId.ExternalPartyAnalysis,
      ReportSectionId.ClassificationAndPriorityAnalysis,
      ReportSectionId.DelayAndBottleneckAnalysis,
    ] as number[],
  },
  {
    label: 'المخاطر والجودة',
    ids: [
      ReportSectionId.CriticalCases,
      ReportSectionId.DataQuality,
      ReportSectionId.RisksAndAlerts,
      ReportSectionId.ExecutiveRecommendations,
      ReportSectionId.RecommendationsAndActionPlan,
    ] as number[],
  },
  {
    label: 'التفاصيل والملاحق',
    ids: [
      ReportSectionId.TransactionDetails,
      ReportSectionId.Appendices,
      ReportSectionId.MethodologyAndDefinitions,
      ReportSectionId.ReportMetadata,
    ] as number[],
  },
];

const EXECUTIVE_PRESET: number[] = [
  ReportSectionId.Cover,
  ReportSectionId.ExecutiveSummary,
  ReportSectionId.KeyPerformanceIndicators,
  ReportSectionId.ExecutiveRecommendations,
];

const ANALYTICAL_PRESET: number[] = [
  ReportSectionId.Cover,
  ReportSectionId.ExecutiveSummary,
  ReportSectionId.KeyPerformanceIndicators,
  ReportSectionId.SignificantFindings,
  ReportSectionId.TimeTrends,
  ReportSectionId.IndicatorsDashboard,
  ReportSectionId.DepartmentPerformance,
  ReportSectionId.ExecutiveRecommendations,
  ReportSectionId.RecommendationsAndActionPlan,
];

function parseBoundedPositiveInteger(value: string, min: number, max: number): number {
  const parsed = Number.parseInt(value, 10);

  if (Number.isNaN(parsed)) {
    return min;
  }

  return Math.min(Math.max(parsed, min), max);
}

function formatDateRangeSummary(dateFrom?: string, dateTo?: string): string {
  if (dateFrom && dateTo) {
    return `${dateFrom} – ${dateTo}`;
  }

  if (dateFrom) {
    return `من ${dateFrom}`;
  }

  if (dateTo) {
    return `إلى ${dateTo}`;
  }

  return 'بدون تقييد تاريخ';
}

function getActiveFilterCount(filters: {
  search: string;
  departmentIds: number[];
  categoryIds: number[];
  partyIds: number[];
  priorities: string[];
  statuses: string[];
  onlyOverdue: boolean;
}): number {
  return (
    (filters.search.trim() ? 1 : 0) +
    filters.departmentIds.length +
    filters.categoryIds.length +
    filters.partyIds.length +
    filters.priorities.length +
    filters.statuses.length +
    (filters.onlyOverdue ? 1 : 0)
  );
}

function getMultiSelectSize(itemCount: number): number {
  return Math.max(Math.min(itemCount, 5), 3);
}

function getSummaryItemClassName(className?: string): string {
  return ['rb-summary-item', className].filter(Boolean).join(' ');
}

function getContentLevelLabel(contentLevel: typeof ReportContentLevel[keyof typeof ReportContentLevel]): string {
  if (contentLevel === ReportContentLevel.Executive) {
    return 'تنفيذي';
  }

  if (contentLevel === ReportContentLevel.Analytical) {
    return 'تحليلي';
  }

  return 'تفصيلي';
}

function getReportTypeLabel(reportType: typeof InstitutionalReportType[keyof typeof InstitutionalReportType]): string {
  return REPORT_TYPES.find((t) => t.value === reportType)?.label ?? '';
}

function buildReportSummaryItems(options: {
  reportTypeLabel: string;
  contentLevelLabel: string;
  dateRangeLabel: string;
  includeComparison: boolean;
  selectedSectionCount: number;
  totalSectionCount: number;
  activeFilterCount: number;
}) {
  const comparisonStatus = options.includeComparison ? 'مفعّلة' : 'معطّلة';
  const items = [
    { key: 'type', label: options.reportTypeLabel, className: 'rb-summary-type' },
    { key: 'level', label: options.contentLevelLabel },
    { key: 'date', label: options.dateRangeLabel },
    { key: 'comparison', label: `المقارنة: ${comparisonStatus}` },
    { key: 'sections', label: `${options.selectedSectionCount} / ${options.totalSectionCount} قسم` },
  ];

  if (options.activeFilterCount > 0) {
    items.push({
      key: 'filters',
      label: `فلاتر نشطة: ${options.activeFilterCount}`,
      className: 'rb-summary-filter-active',
    });
  }

  return items;
}

function getPreviewContentState(options: {
  hasActivePage: boolean;
  loading: boolean;
  error: string;
}): 'document' | 'loading' | 'error' | 'empty' {
  if (options.hasActivePage) {
    return 'document';
  }

  if (options.loading) {
    return 'loading';
  }

  if (options.error) {
    return 'error';
  }

  return 'empty';
}

function getReportThumbClass(options: { isActive: boolean; isSelected: boolean }): string {
  const classes = ['report-thumb'];

  if (options.isActive) {
    classes.push('is-active');
  }

  if (options.isSelected) {
    classes.push('is-selected');
  }

  return classes.join(' ');
}

export default function ReportBuilderPage() {
  const { isAdmin } = useAuth();
  const [reportType, setReportType] = useState<typeof InstitutionalReportType[keyof typeof InstitutionalReportType]>(
    InstitutionalReportType.ExecutiveComprehensive,
  );
  const [dateFrom, setDateFrom] = useState(defaultDate(-30));
  const [dateTo, setDateTo] = useState(defaultDate(0));
  const [title, setTitle] = useState('تقرير المتابعة الإجرائية للمعاملات');
  const [sectionIds, setSectionIds] = useState<number[]>(SECTIONS.map((s) => s.id));
  const [contentLevel, setContentLevel] = useState<typeof ReportContentLevel[keyof typeof ReportContentLevel]>(ReportContentLevel.Analytical);
  const [comparisonMode, setComparisonMode] = useState<typeof ReportComparisonMode[keyof typeof ReportComparisonMode]>(ReportComparisonMode.PreviousEquivalentPeriod);
  const [timeGrouping, setTimeGrouping] = useState<typeof ReportTimeGrouping[keyof typeof ReportTimeGrouping]>(ReportTimeGrouping.Monthly);
  const [includeComparison, setIncludeComparison] = useState(true);
  const [maxCriticalCases, setMaxCriticalCases] = useState(10);
  const [maxFindings, setMaxFindings] = useState(5);
  const [maxRecommendations, setMaxRecommendations] = useState(10);
  const [manifest, setManifest] = useState<InstitutionalReportManifest | null>(null);
  const [currentPage, setCurrentPage] = useState(1);
  const [selectedPages, setSelectedPages] = useState<number[]>([]);
  const [pageSelectionMode, setPageSelectionMode] = useState<'thumbnails' | 'range'>('thumbnails');
  const [pageRange, setPageRange] = useState('');
  const [zoom, setZoom] = useState(0.75);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState('');
  const [errorCorrelationId, setErrorCorrelationId] = useState('');
  const [correlationCopied, setCorrelationCopied] = useState(false);
  const [templates, setTemplates] = useState<ReportTemplate[]>([]);
  const [selectedTemplateId, setSelectedTemplateId] = useState('');
  const [templateName, setTemplateName] = useState('');
  const [templateError, setTemplateError] = useState('');
  const [isSavingTemplate, setIsSavingTemplate] = useState(false);
  const previewRequestIdRef = useRef(0);
  const previewAbortRef = useRef<AbortController | null>(null);

  // Filters
  const [filterDepartmentIds, setFilterDepartmentIds] = useState<number[]>([]);
  const [filterCategoryIds, setFilterCategoryIds] = useState<number[]>([]);
  const [filterPartyIds, setFilterPartyIds] = useState<number[]>([]);
  const [filterPriorities, setFilterPriorities] = useState<string[]>([]);
  const [filterStatuses, setFilterStatuses] = useState<string[]>([]);
  const [filterOnlyOverdue, setFilterOnlyOverdue] = useState(false);
  const [filterSearch, setFilterSearch] = useState('');
  const [departments, setDepartments] = useState<LookupItem[]>([]);
  const [categories, setCategories] = useState<LookupItem[]>([]);
  const [parties, setParties] = useState<LookupItem[]>([]);

  const includesTransactionDetails = sectionIds.includes(ReportSectionId.TransactionDetails);

  const activeFilterCount = getActiveFilterCount({
    search: filterSearch,
    departmentIds: filterDepartmentIds,
    categoryIds: filterCategoryIds,
    partyIds: filterPartyIds,
    priorities: filterPriorities,
    statuses: filterStatuses,
    onlyOverdue: filterOnlyOverdue,
  });

  useEffect(() => {
    if (!isAdmin) return;
    let isMounted = true;
    institutionalReportsApi.getTemplates()
      .then(({ data }) => { if (isMounted) setTemplates(data); })
      .catch(() => { if (isMounted) setTemplates([]); });
    return () => { isMounted = false; };
  }, [isAdmin]);

  useEffect(() => {
    if (!isAdmin) return;
    let isMounted = true;
    departmentsApi.lookup('', true, 200)
      .then(({ data }) => { if (isMounted) setDepartments(data); })
      .catch(() => { if (isMounted) setDepartments([]); });
    return () => { isMounted = false; };
  }, [isAdmin]);

  useEffect(() => {
    if (!isAdmin) return;
    let isMounted = true;
    categoriesApi.lookup('', true, 200)
      .then(({ data }) => { if (isMounted) setCategories(data); })
      .catch(() => { if (isMounted) setCategories([]); });
    return () => { isMounted = false; };
  }, [isAdmin]);

  useEffect(() => {
    if (!isAdmin) return;
    let isMounted = true;
    externalPartiesApi.lookup('', true, 200)
      .then(({ data }) => { if (isMounted) setParties(data); })
      .catch(() => { if (isMounted) setParties([]); });
    return () => { isMounted = false; };
  }, [isAdmin]);

  const invalidatePreview = useCallback(() => {
    previewAbortRef.current?.abort();
    previewAbortRef.current = null;
    previewRequestIdRef.current += 1;
    setManifest(null);
    setLoading(false);
    setError('');
    setErrorCorrelationId('');
    setCorrelationCopied(false);
    setCurrentPage(1);
    setSelectedPages([]);
    setPageRange('');
  }, []);

  const clearFilters = () => {
    invalidatePreview();
    setFilterSearch('');
    setFilterDepartmentIds([]);
    setFilterCategoryIds([]);
    setFilterPartyIds([]);
    setFilterPriorities([]);
    setFilterStatuses([]);
    setFilterOnlyOverdue(false);
  };

  const applyPreset = (ids: number[]) => {
    invalidatePreview();
    setSectionIds([...ids]);
  };

  const updateBoundedMaxInput = useCallback((
    value: string,
    setValue: (nextValue: number) => void,
    max: number,
  ) => {
    invalidatePreview();
    setValue(parseBoundedPositiveInteger(value, 1, max));
  }, [invalidatePreview]);

  const handleMaxFindingsChange = useCallback((event: ChangeEvent<HTMLInputElement>) => {
    updateBoundedMaxInput(event.target.value, setMaxFindings, 5);
  }, [updateBoundedMaxInput]);

  const handleMaxCriticalCasesChange = useCallback((event: ChangeEvent<HTMLInputElement>) => {
    updateBoundedMaxInput(event.target.value, setMaxCriticalCases, 10);
  }, [updateBoundedMaxInput]);

  const handleMaxRecommendationsChange = useCallback((event: ChangeEvent<HTMLInputElement>) => {
    updateBoundedMaxInput(event.target.value, setMaxRecommendations, 10);
  }, [updateBoundedMaxInput]);

  const buildRequest = useCallback((): ReportBuildRequest => ({
    reportType,
    title,
    sectionIds: sectionIds as ReportBuildRequest['sectionIds'],
    contentLevel,
    comparisonMode: includeComparison ? comparisonMode : ReportComparisonMode.None,
    timeGrouping,
    includeExecutiveSummary: sectionIds.includes(ReportSectionId.ExecutiveSummary),
    includeComparison,
    includeCriticalCases: sectionIds.includes(ReportSectionId.CriticalCases),
    includeTimeTrends: sectionIds.includes(ReportSectionId.TimeTrends),
    includeDepartmentPerformance: sectionIds.includes(ReportSectionId.DepartmentPerformance),
    includeExternalPartyAnalysis: sectionIds.includes(ReportSectionId.ExternalPartyAnalysis),
    includeCategoryAnalysis: sectionIds.includes(ReportSectionId.ClassificationAndPriorityAnalysis),
    includeBottleneckAnalysis: sectionIds.includes(ReportSectionId.DelayAndBottleneckAnalysis),
    includeDataQuality: sectionIds.includes(ReportSectionId.DataQuality),
    includeRecommendations: sectionIds.includes(ReportSectionId.ExecutiveRecommendations) || sectionIds.includes(ReportSectionId.RecommendationsAndActionPlan),
    includeMethodology: sectionIds.includes(ReportSectionId.MethodologyAndDefinitions),
    maxCriticalCases,
    maxFindings,
    maxRecommendations,
    filters: {
      dateFrom: dateFrom || null,
      dateTo: dateTo || null,
      includeJointDepartmentTransactions: true,
      includeOverdue: filterOnlyOverdue,
      includeDetails: true,
      includeRisks: true,
      includeRecommendations: true,
      departmentIds: filterDepartmentIds,
      partyIds: filterPartyIds,
      categoryIds: filterCategoryIds,
      priorities: filterPriorities,
      statuses: filterStatuses,
      search: filterSearch || null,
    },
  }), [
    reportType, title, sectionIds, contentLevel, comparisonMode, timeGrouping,
    includeComparison, maxCriticalCases, maxFindings, maxRecommendations,
    dateFrom, dateTo, filterDepartmentIds, filterCategoryIds, filterPartyIds,
    filterPriorities, filterStatuses, filterOnlyOverdue, filterSearch,
  ]);

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
    setErrorCorrelationId,
  });

  const applyTemplate = useCallback((template: ReportTemplate) => {
    const filters: Partial<ReportTemplate['defaultFilters']> = template.defaultFilters ?? {};
    const templateSectionIds = Array.isArray(template.sectionIds) ? template.sectionIds : [];

    invalidatePreview();
    setSelectedTemplateId(String(template.id));
    setTemplateError('');
    setReportType(template.reportType);
    setSectionIds([...templateSectionIds]);
    setFilterDepartmentIds([...(filters.departmentIds ?? [])]);
    setFilterCategoryIds([...(filters.categoryIds ?? [])]);
    setFilterPartyIds([...(filters.partyIds ?? [])]);
    setFilterPriorities([...(filters.priorities ?? [])]);
    setFilterStatuses([...(filters.statuses ?? [])]);
    setFilterOnlyOverdue(Boolean(filters.includeOverdue));
    setFilterSearch(filters.search ?? '');
    setDateFrom(filters.dateFrom ?? '');
    setDateTo(filters.dateTo ?? '');
    setExportFormat(template.defaultFormat);
    setPageNumberingMode(template.pageNumberingMode);
    setIncludePartialCover(template.includePartialCover);
    setIncludePartialManifest(template.includePartialManifest);
  }, [
    invalidatePreview,
    setExportFormat,
    setIncludePartialCover,
    setIncludePartialManifest,
    setPageNumberingMode,
  ]);

  const handleTemplateChange = useCallback((templateId: string) => {
    if (!templateId) {
      invalidatePreview();
      setSelectedTemplateId('');
      return;
    }

    const template = templates.find((item) => String(item.id) === templateId);
    if (template) {
      applyTemplate(template);
    }
  }, [applyTemplate, invalidatePreview, templates]);

  const saveCurrentTemplate = useCallback(async () => {
    if (isSavingTemplate) {
      return;
    }

    const name = templateName.trim();
    if (!name) {
      setTemplateError('اسم القالب مطلوب.');
      return;
    }

    setTemplateError('');
    setIsSavingTemplate(true);
    try {
      const payload = buildRequest();
      const { data } = await institutionalReportsApi.saveTemplate({
        name,
        reportType: payload.reportType,
        sectionIds: payload.sectionIds,
        defaultFilters: payload.filters,
        defaultFormat: exportFormat,
        pageNumberingMode,
        includePartialCover,
        includePartialManifest,
      });
      setTemplates((prev) => [...prev.filter((item) => item.id !== data.id), data].sort((a, b) => a.name.localeCompare(b.name)));
      setSelectedTemplateId(String(data.id));
      setTemplateName('');
    } catch (error) {
      const apiError = getApiErrorDetails(error);
      setTemplateError(apiError.message?.trim() || 'تعذر حفظ القالب.');
    } finally {
      setIsSavingTemplate(false);
    }
  }, [
    buildRequest,
    exportFormat,
    includePartialCover,
    includePartialManifest,
    isSavingTemplate,
    pageNumberingMode,
    templateName,
  ]);

  const loadPreview = async () => {
    if (!isAdmin)
      return;

    previewAbortRef.current?.abort();
    const controller = new AbortController();
    previewAbortRef.current = controller;

    const requestId = previewRequestIdRef.current + 1;
    previewRequestIdRef.current = requestId;
    setManifest(null);
    setLoading(true);
    setError('');
    setErrorCorrelationId('');
    setCorrelationCopied(false);
    try {
      const { data } = await institutionalReportsApi.preview(buildRequest(), controller.signal);
      if (requestId !== previewRequestIdRef.current) return;
      setManifest(data);
      setCurrentPage(1);
      setSelectedPages([]);
    } catch (error) {
      if ((error as { name?: string })?.name === 'CanceledError' || (error as { name?: string })?.name === 'AbortError') return;
      if (requestId !== previewRequestIdRef.current) return;
      const apiError = getApiErrorDetails(error);

      const defaultMessage = 'تعذر إنشاء معاينة التقرير.';
      const backendMessage = apiError.message?.trim();

      setError(
        backendMessage && backendMessage !== defaultMessage
          ? `${defaultMessage} ${backendMessage}`
          : defaultMessage,
      );
      setErrorCorrelationId(apiError.correlationId);
      setManifest(null);
    } finally {
      if (requestId === previewRequestIdRef.current) {
        setLoading(false);
      }
    }
  };

  const copyCorrelationId = async () => {
    if (!errorCorrelationId)
      return;

    try {
      await navigator.clipboard.writeText(errorCorrelationId);
      setCorrelationCopied(true);
      globalThis.setTimeout(() => setCorrelationCopied(false), 2000);
    } catch {
      setCorrelationCopied(false);
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
    invalidatePreview();
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

  const reportTypeLabel = getReportTypeLabel(reportType);
  const contentLevelLabel = getContentLevelLabel(contentLevel);
  const summaryItems = buildReportSummaryItems({
    reportTypeLabel,
    contentLevelLabel,
    dateRangeLabel: formatDateRangeSummary(dateFrom, dateTo),
    includeComparison,
    selectedSectionCount: sectionIds.length,
    totalSectionCount: SECTIONS.length,
    activeFilterCount,
  });
  const previewContentState = getPreviewContentState({
    hasActivePage: Boolean(activePage),
    loading,
    error,
  });
  const overflowTotal = manifest?.totalMatchedRows ?? manifest?.totalMatchingTransactions;
  const overflowDisplayedRows = manifest?.exportedDetailRows ?? manifest?.includedTransactionCount;
  const overflowTotalLabel = overflowTotal?.toLocaleString('ar-SA') ?? '—';
  const overflowLimitLabel = manifest?.detailRowLimit?.toLocaleString('ar-SA') ?? '—';
  const overflowDisplayedRowsLabel = overflowDisplayedRows?.toLocaleString('ar-SA') ?? '—';
  const overflowPartsNote = manifest?.detailPartsCount && manifest.detailPartsCount > 1
    ? ` (التصدير الكامل يتطلب ${manifest.detailPartsCount} ملفات PDF)`
    : '';
  const selectedPagesModeLabel =
    pageSelectionMode === 'range' && pageRange.trim()
      ? 'نطاق الصفحات'
      : 'تحديد الصور المصغرة';

  if (!isAdmin)
    return <Navigate to="/" replace />;

  return (
    <div className="report-builder">

      {/* ── Header ── */}
      <div className="page-header">
        <div>
          <h2 className="page-title">منشئ التقارير</h2>
        </div>
        <div className="page-header-actions">
          <button type="button" className="btn btn-secondary" onClick={loadPreview} disabled={loading}>
            {loading ? 'جاري التوليد...' : 'معاينة التقرير'}
          </button>
          <button type="button" className="btn btn-primary" onClick={openExportDialog} disabled={!manifest}>
            تصدير
          </button>
        </div>
      </div>

      {/* ── Settings Summary Bar ── */}
      <div className="rb-summary-bar" aria-label="ملخص الإعدادات الحالية">
        {summaryItems.map((item, index) => (
          <span key={item.key} className="rb-summary-entry">
            {index > 0 && <span className="rb-summary-sep" aria-hidden="true">—</span>}
            <span className={getSummaryItemClassName(item.className)}>
              {item.label}
            </span>
          </span>
        ))}
      </div>

      {/* ── Alerts ── */}
      {error && (
        <div className="alert alert-error" role="alert">
          <div>{error}</div>
          {errorCorrelationId ? (
            <div className="report-error-meta">
              <span>رقم التتبع: {errorCorrelationId}</span>
              <button type="button" className="btn btn-sm btn-outline" onClick={copyCorrelationId}>
                {correlationCopied ? 'تم النسخ' : 'نسخ رقم التتبع'}
              </button>
            </div>
          ) : null}
        </div>
      )}

      {manifest?.requiresDetailOverflowAction && includesTransactionDetails && (
        <div className="alert alert-warning">
          يتجاوز عدد المعاملات المطابقة ({overflowTotalLabel})
          {' '}حد صفوف التفاصيل التشغيلي ({overflowLimitLabel}).
          {' '}بطاقات KPI والرسوم محسوبة من كامل النتائج؛ جدول التفاصيل يعرض {overflowDisplayedRowsLabel} صفًا
          {overflowPartsNote}.
        </div>
      )}

      {/* ── Main Grid ── */}
      <div className="report-builder-grid">

        {/* ═══ Sidebar: Settings ═══ */}
        <aside className="report-builder-panel">

          {/* 1 ── إعدادات التقرير الأساسية */}
          <h3>إعدادات التقرير</h3>

          <label htmlFor="report-type">نوع التقرير</label>
          <select
            id="report-type"
            value={reportType}
            onChange={(e) => { invalidatePreview(); setReportType(Number(e.target.value) as typeof reportType); }}
          >
            {REPORT_TYPES.map((t) => <option key={t.value} value={t.value}>{t.label}</option>)}
          </select>

          <label htmlFor="report-title">عنوان التقرير</label>
          <input id="report-title" value={title} onChange={(e) => { invalidatePreview(); setTitle(e.target.value); }} />

          <label htmlFor="content-level">مستوى المحتوى</label>
          <select
            id="content-level"
            value={contentLevel}
            onChange={(e) => { invalidatePreview(); setContentLevel(Number(e.target.value) as typeof contentLevel); }}
          >
            <option value={ReportContentLevel.Executive}>تنفيذي</option>
            <option value={ReportContentLevel.Analytical}>تحليلي</option>
            <option value={ReportContentLevel.Detailed}>تفصيلي</option>
          </select>

          <label htmlFor="saved-template">القالب المحفوظ</label>
          <select
            id="saved-template"
            value={selectedTemplateId}
            onChange={(e) => handleTemplateChange(e.target.value)}
          >
            <option value="">بدون قالب</option>
            {templates.map((template) => (
              <option key={template.id} value={template.id}>{template.name}</option>
            ))}
          </select>

          <div className="rb-template-save">
            <label htmlFor="template-name">حفظ الإعدادات كقالب</label>
            <div className="rb-template-save-row">
              <input
                id="template-name"
                value={templateName}
                onChange={(e) => {
                  setTemplateName(e.target.value);
                  setTemplateError('');
                }}
                placeholder="اسم القالب"
              />
              <button
                type="button"
                className="btn btn-sm btn-outline"
                onClick={saveCurrentTemplate}
                disabled={isSavingTemplate}
              >
                {isSavingTemplate ? 'جاري الحفظ...' : 'حفظ'}
              </button>
            </div>
            {templateError && <p className="text-danger">{templateError}</p>}
          </div>

          {/* 2 ── الفترة الزمنية */}
          <h3 className="report-builder-section-title">الفترة الزمنية</h3>

          <div className="rb-date-range">
            <div className="rb-date-field">
              <label htmlFor="date-from">من تاريخ</label>
              <input id="date-from" type="date" value={dateFrom} onChange={(e) => { invalidatePreview(); setDateFrom(e.target.value); }} />
            </div>
            <div className="rb-date-field">
              <label htmlFor="date-to">إلى تاريخ</label>
              <input id="date-to" type="date" value={dateTo} onChange={(e) => { invalidatePreview(); setDateTo(e.target.value); }} />
            </div>
          </div>

          <label htmlFor="time-grouping">تجميع الاتجاه الزمني</label>
          <select
            id="time-grouping"
            value={timeGrouping}
            onChange={(e) => { invalidatePreview(); setTimeGrouping(Number(e.target.value) as typeof timeGrouping); }}
          >
            <option value={ReportTimeGrouping.Daily}>يومي</option>
            <option value={ReportTimeGrouping.Weekly}>أسبوعي</option>
            <option value={ReportTimeGrouping.Monthly}>شهري</option>
            <option value={ReportTimeGrouping.Quarterly}>ربع سنوي</option>
          </select>

          <label htmlFor="include-comparison" className="rb-checkbox-label">
            <input
              id="include-comparison"
              type="checkbox"
              checked={includeComparison}
              onChange={(e) => { invalidatePreview(); setIncludeComparison(e.target.checked); }}
            />
            <span>تضمين المقارنة بالفترة السابقة</span>
          </label>

          <label htmlFor="comparison-mode">نمط المقارنة</label>
          <select
            id="comparison-mode"
            value={comparisonMode}
            disabled={!includeComparison}
            onChange={(e) => { invalidatePreview(); setComparisonMode(Number(e.target.value) as typeof comparisonMode); }}
          >
            <option value={ReportComparisonMode.PreviousEquivalentPeriod}>الفترة السابقة المكافئة</option>
            <option value={ReportComparisonMode.YearOverYear}>نفس الفترة من العام السابق</option>
            <option value={ReportComparisonMode.None}>بدون مقارنة</option>
          </select>

          {/* 3 ── الفلاتر */}
          <div className="rb-filter-header">
            <h3 className="report-builder-section-title rb-filter-title">
              الفلاتر
              {activeFilterCount > 0 && (
                <span className="rb-filter-badge" aria-label={`${activeFilterCount} فلتر نشط`}>
                  {activeFilterCount}
                </span>
              )}
            </h3>
            {activeFilterCount > 0 && (
              <button type="button" className="btn btn-sm btn-ghost rb-clear-btn" onClick={clearFilters}>
                مسح الفلاتر
              </button>
            )}
          </div>

          <label htmlFor="filter-search">بحث</label>
          <input
            id="filter-search"
            type="text"
            placeholder="رقم الوارد / رقم التتبع / الموضوع"
            value={filterSearch}
            onChange={(e) => { invalidatePreview(); setFilterSearch(e.target.value); }}
          />

          {departments.length > 0 && (
            <>
              <label htmlFor="filter-departments">
                الإدارات
                {filterDepartmentIds.length > 0 && (
                  <span className="rb-filter-count">{filterDepartmentIds.length} محدد</span>
                )}
              </label>
              <select
                id="filter-departments"
                multiple
                size={getMultiSelectSize(departments.length)}
                value={filterDepartmentIds.map(String)}
                onChange={(e) => {
                  invalidatePreview();
                  setFilterDepartmentIds(Array.from(e.target.selectedOptions).map((o) => Number(o.value)));
                }}
              >
                {departments.map((d) => <option key={d.id} value={d.id}>{d.name}</option>)}
              </select>
            </>
          )}

          {categories.length > 0 && (
            <>
              <label htmlFor="filter-categories">
                التصنيفات
                {filterCategoryIds.length > 0 && (
                  <span className="rb-filter-count">{filterCategoryIds.length} محدد</span>
                )}
              </label>
              <select
                id="filter-categories"
                multiple
                size={getMultiSelectSize(categories.length)}
                value={filterCategoryIds.map(String)}
                onChange={(e) => {
                  invalidatePreview();
                  setFilterCategoryIds(Array.from(e.target.selectedOptions).map((o) => Number(o.value)));
                }}
              >
                {categories.map((c) => <option key={c.id} value={c.id}>{c.name}</option>)}
              </select>
            </>
          )}

          {parties.length > 0 && (
            <>
              <label htmlFor="filter-parties">
                الجهات الخارجية
                {filterPartyIds.length > 0 && (
                  <span className="rb-filter-count">{filterPartyIds.length} محدد</span>
                )}
              </label>
              <select
                id="filter-parties"
                multiple
                size={getMultiSelectSize(parties.length)}
                value={filterPartyIds.map(String)}
                onChange={(e) => {
                  invalidatePreview();
                  setFilterPartyIds(Array.from(e.target.selectedOptions).map((o) => Number(o.value)));
                }}
              >
                {parties.map((p) => <option key={p.id} value={p.id}>{p.name}</option>)}
              </select>
            </>
          )}

          <label htmlFor="filter-priorities">
            الأولويات
            {filterPriorities.length > 0 && (
              <span className="rb-filter-count">{filterPriorities.length} محدد</span>
            )}
          </label>
          <select
            id="filter-priorities"
            multiple
            size={getMultiSelectSize(Object.keys(priorityLabels).length)}
            value={filterPriorities}
            onChange={(e) => {
              invalidatePreview();
              setFilterPriorities(Array.from(e.target.selectedOptions).map((o) => o.value));
            }}
          >
            {Object.entries(priorityLabels).map(([k, v]) => <option key={k} value={k}>{v}</option>)}
          </select>

          <label htmlFor="filter-statuses">
            الحالات
            {filterStatuses.length > 0 && (
              <span className="rb-filter-count">{filterStatuses.length} محدد</span>
            )}
          </label>
          <select
            id="filter-statuses"
            multiple
            size={getMultiSelectSize(Object.keys(statusLabels).length)}
            value={filterStatuses}
            onChange={(e) => {
              invalidatePreview();
              setFilterStatuses(Array.from(e.target.selectedOptions).map((o) => o.value));
            }}
          >
            {Object.entries(statusLabels).map(([k, v]) => <option key={k} value={k}>{v}</option>)}
          </select>

          <label htmlFor="filter-only-overdue" className="rb-checkbox-label">
            <input
              id="filter-only-overdue"
              type="checkbox"
              checked={filterOnlyOverdue}
              onChange={(e) => { invalidatePreview(); setFilterOnlyOverdue(e.target.checked); }}
            />
            <span>المتأخرة فقط</span>
          </label>

          {activeFilterCount === 0 && (
            <p className="rb-no-filters">لا توجد فلاتر نشطة — التقرير يشمل كل السجلات.</p>
          )}

          {/* 4 ── حدود التفاصيل */}
          <h3 className="report-builder-section-title">حدود التفاصيل</h3>

          <label htmlFor="max-findings">الحد الأقصى للنتائج</label>
          <input
            id="max-findings"
            type="number"
            min="1"
            max="5"
            value={maxFindings}
            onChange={handleMaxFindingsChange}
          />

          <label htmlFor="max-critical-cases">الحد الأقصى للحالات الحرجة</label>
          <input
            id="max-critical-cases"
            type="number"
            min="1"
            max="10"
            value={maxCriticalCases}
            onChange={handleMaxCriticalCasesChange}
          />

          <label htmlFor="max-recommendations">الحد الأقصى للتوصيات</label>
          <input
            id="max-recommendations"
            type="number"
            min="1"
            max="10"
            value={maxRecommendations}
            onChange={handleMaxRecommendationsChange}
          />

          {/* 5 ── الأقسام */}
          <h3 className="report-builder-section-title">
            <span>الأقسام</span>
            <span className="rb-filter-badge rb-sections-badge">
              {sectionIds.length} / {SECTIONS.length}
            </span>
          </h3>

          <div className="rb-preset-buttons">
            <button type="button" className="btn btn-sm btn-outline" onClick={() => applyPreset(EXECUTIVE_PRESET)}>
              تنفيذي
            </button>
            <button type="button" className="btn btn-sm btn-outline" onClick={() => applyPreset(ANALYTICAL_PRESET)}>
              تحليلي
            </button>
            <button type="button" className="btn btn-sm btn-outline" onClick={() => applyPreset(SECTIONS.map((s) => s.id as number))}>
              شامل
            </button>
            <button type="button" className="btn btn-sm btn-ghost" onClick={() => { invalidatePreview(); setSectionIds(SECTIONS.map((s) => s.id)); }}>
              تحديد الكل
            </button>
            <button type="button" className="btn btn-sm btn-ghost" onClick={() => { invalidatePreview(); setSectionIds([]); }}>
              إلغاء الكل
            </button>
          </div>

          {SECTION_GROUPS.map((group) => (
            <div key={group.label} className="rb-section-group">
              <h4 className="rb-section-group-title">{group.label}</h4>
              <div className="report-section-list">
                {group.ids.map((id) => {
                  const label = SECTION_LABEL_MAP.get(id);
                  if (!label) return null;
                  const inputId = `section-${id}`;
                  return (
                    <label key={id} htmlFor={inputId}>
                      <input
                        id={inputId}
                        type="checkbox"
                        checked={sectionIds.includes(id)}
                        onChange={() => toggleSection(id)}
                      />
                      <span>{label}</span>
                    </label>
                  );
                })}
              </div>
            </div>
          ))}
        </aside>

        {/* ═══ Preview Panel ═══ */}
        <section className="report-builder-panel">
          <div className="report-preview-toolbar">
            <button
              type="button"
              className="btn btn-sm btn-outline"
              disabled={!manifest || currentPage <= 1}
              onClick={() => setCurrentPage((p) => p - 1)}
            >
              السابق
            </button>
            <button
              type="button"
              className="btn btn-sm btn-outline"
              disabled={!manifest || currentPage >= (manifest?.totalPages ?? 1)}
              onClick={() => setCurrentPage((p) => p + 1)}
            >
              التالي
            </button>
            <span className="rb-page-indicator">
              {manifest ? `الصفحة ${currentPage} من ${manifest.totalPages}` : 'لا توجد معاينة بعد'}
            </span>
            {pageSelectionSummary && <span className="rb-selection-summary">{pageSelectionSummary}</span>}
            <div className="rb-zoom-control">
              <label htmlFor="preview-zoom" className="rb-zoom-label">تكبير</label>
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
          </div>

          <div className="report-preview-shell">
            <div className="report-thumbs">
              {manifest?.pages.map((page) => {
                const checkboxId = `thumb-select-${page.originalPageNumber}`;
                return (
                  <div
                    key={page.originalPageNumber}
                    className={getReportThumbClass({
                      isActive: page.originalPageNumber === currentPage,
                      isSelected: selectedPages.includes(page.originalPageNumber),
                    })}
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

              {previewContentState === 'document' && activePage && (
                <ReportPreviewDocument
                  htmlContent={activePage.htmlContent}
                  stylesheet={manifest?.stylesheet}
                  zoom={zoom}
                  title={`${activePage.sectionName} — صفحة ${activePage.renderedPageNumber}`}
                />
              )}

              {previewContentState === 'loading' && (
                <div className="rb-preview-empty">
                  <div className="rb-preview-spinner" aria-hidden="true" />
                  <p className="rb-preview-loading-text">جاري إنشاء التقرير، الرجاء الانتظار...</p>
                </div>
              )}

              {previewContentState === 'error' && (
                <div className="rb-preview-empty" aria-live="polite">
                  <h3 className="rb-preview-empty-title">فشل تحميل المعاينة</h3>
                  <p className="rb-preview-empty-desc">{error}</p>
                  <button type="button" className="btn btn-secondary" onClick={loadPreview}>
                    إعادة المحاولة
                  </button>
                </div>
              )}

              {previewContentState === 'empty' && (
                <div className="rb-preview-empty">
                  <div className="rb-preview-empty-icon" aria-hidden="true">
                    <svg width="52" height="52" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.2" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
                      <path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z" />
                      <polyline points="14 2 14 8 20 8" />
                      <line x1="16" y1="13" x2="8" y2="13" />
                      <line x1="16" y1="17" x2="8" y2="17" />
                      <polyline points="10 9 9 9 8 9" />
                    </svg>
                  </div>
                  <h3 className="rb-preview-empty-title">لا توجد معاينة بعد</h3>
                  <p className="rb-preview-empty-desc">{previewStatusMessage}</p>
                  <button type="button" className="btn btn-secondary" onClick={loadPreview}>
                    إنشاء المعاينة
                  </button>
                </div>
              )}
            </div>
          </div>
        </section>
      </div>

      {/* ═══ Export Dialog ═══ */}
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
                النمط النشط: {selectedPagesModeLabel}
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
          <label htmlFor="include-partial-cover" className="rb-checkbox-label">
            <input
              id="include-partial-cover"
              type="checkbox"
              checked={includePartialCover}
              onChange={(e) => setIncludePartialCover(e.target.checked)}
            />
            <span>إضافة غلاف للنسخة الجزئية</span>
          </label>
          <label htmlFor="include-partial-manifest" className="rb-checkbox-label">
            <input
              id="include-partial-manifest"
              type="checkbox"
              checked={includePartialManifest}
              onChange={(e) => setIncludePartialManifest(e.target.checked)}
            />
            <span>إدراج صفحة تعريف النسخة الجزئية</span>
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
              <label htmlFor="overflow-summary-only" className="rb-checkbox-label">
                <input
                  id="overflow-summary-only"
                  type="radio"
                  name="detailOverflowAction"
                  checked={detailOverflowAction === DetailOverflowAction.SummaryOnly}
                  onChange={() => setDetailOverflowAction(DetailOverflowAction.SummaryOnly)}
                />
                <span>ملخص كامل بدون تفاصيل مضمّنة (PDF/DOCX/HTML)</span>
              </label>
              <label htmlFor="overflow-split-pdf" className="rb-checkbox-label">
                <input
                  id="overflow-split-pdf"
                  type="radio"
                  name="detailOverflowAction"
                  checked={detailOverflowAction === DetailOverflowAction.SplitPdf}
                  onChange={() => setDetailOverflowAction(DetailOverflowAction.SplitPdf)}
                  disabled={exportFormat !== ExportFormat.Pdf}
                />
                <span>تقسيم التفاصيل إلى عدة ملفات PDF (ملف ZIP)</span>
              </label>
              <label htmlFor="overflow-xlsx" className="rb-checkbox-label">
                <input
                  id="overflow-xlsx"
                  type="radio"
                  name="detailOverflowAction"
                  checked={detailOverflowAction === DetailOverflowAction.FullDetailsXlsx}
                  onChange={() => setDetailOverflowAction(DetailOverflowAction.FullDetailsXlsx)}
                />
                <span>تصدير التفاصيل كاملة إلى Excel (XLSX)</span>
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
