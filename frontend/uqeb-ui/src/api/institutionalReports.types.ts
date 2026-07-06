export type InstitutionalReportPage = {
  renderedPageNumber: number;
  originalPageNumber: number;
  sectionId: number;
  sectionName: string;
  pageTitle: string;
  htmlContent: string;
  isSelectable: boolean;
};

export type InstitutionalReportManifest = {
  reportTitle?: string;
  reportId: string;
  totalPages: number;
  pages: InstitutionalReportPage[];
  isPartialExport?: boolean;
  partialExportNote?: string;
  totalMatchingTransactions?: number;
  includedTransactionCount?: number;
  detailRowLimit?: number;
  requiresDetailOverflowAction?: boolean;
  totalMatchedRows?: number;
  exportedDetailRows?: number;
  detailRowsTruncated?: boolean;
  detailPartsCount?: number;
  loadedDetailRows?: number;
  currentPartNumber?: number | null;
  rowsFrom?: number | null;
  rowsTo?: number | null;
  isSummaryOnly?: boolean;
  overflowAction?: DetailOverflowAction | null;
  stylesheet?: string | null;
  templateVersion?: string | null;
  fileFingerprint?: string | null;
  analysis?: unknown;
};

export type InstitutionalReportType = 1 | 2 | 3 | 4 | 5 | 6;

export type ReportDetailSortBy = 0 | 1 | 2 | 3 | 4 | 5;

export type ReportSectionId = 1 | 2 | 3 | 4 | 5 | 6 | 7 | 8 | 9 | 10 | 11 | 12 | 13 | 14 | 15 | 16 | 17 | 18 | 19 | 20 | 21;

export type ExportFormat = 1 | 2 | 3 | 4;

export type ExportMode = 1 | 2 | 3 | 4;

export type PageNumberingMode = 1 | 2;

export type DetailOverflowAction = 0 | 1 | 2 | 3;

export type ReportContentLevel = 1 | 2 | 3;

export type ReportComparisonMode = 0 | 1 | 2 | 3;

export type ReportTimeGrouping = 1 | 2 | 3 | 4;

export type ReportFilters = {
  dateFrom?: string | null;
  dateTo?: string | null;
  departmentIds: number[];
  partyIds: number[];
  categoryIds: number[];
  priorities: string[];
  statuses: string[];
  includeOverdue: boolean;
  search?: string | null;
};

export type ReportBuildRequest = {
  reportType: InstitutionalReportType;
  title?: string;
  introduction?: string;
  sectionIds: ReportSectionId[];
  singleTransactionId?: number | null;
  contentLevel?: ReportContentLevel;
  comparisonMode?: ReportComparisonMode;
  comparisonDateFrom?: string | null;
  comparisonDateTo?: string | null;
  timeGrouping?: ReportTimeGrouping;
  includeExecutiveSummary?: boolean;
  includeComparison?: boolean;
  includeCriticalCases?: boolean;
  includeTimeTrends?: boolean;
  includeDepartmentPerformance?: boolean;
  includeExternalPartyAnalysis?: boolean;
  includeCategoryAnalysis?: boolean;
  includeBottleneckAnalysis?: boolean;
  includeDataQuality?: boolean;
  includeRecommendations?: boolean;
  includeMethodology?: boolean;
  maxCriticalCases?: number;
  maxFindings?: number;
  maxRecommendations?: number;
  detailSortBy?: ReportDetailSortBy;
  groupDetailsByDepartment?: boolean;
  filters: ReportFilters;
};

export type ReportExportRequest = {
  reportId?: string | null;
  buildRequest: ReportBuildRequest;
  exportFormat: ExportFormat;
  exportMode: ExportMode;
  selectedSectionIds: ReportSectionId[];
  selectedPageNumbers?: number[];
  pageRangeExpression?: string | null;
  currentPageNumber?: number | null;
  includePartialCover: boolean;
  includePartialManifest: boolean;
  pageNumberingMode: PageNumberingMode;
  templateId?: number | null;
  reason?: string | null;
  detailOverflowAction?: DetailOverflowAction;
};

export type ReportTemplate = {
  id: number;
  name: string;
  reportType: InstitutionalReportType;
  sectionIds: ReportSectionId[];
  defaultFilters: ReportFilters;
  defaultFormat: ExportFormat;
  pageNumberingMode: PageNumberingMode;
  includePartialCover: boolean;
  includePartialManifest: boolean;
  detailSortBy?: ReportDetailSortBy;
  groupDetailsByDepartment?: boolean;
};

export type SaveReportTemplateRequest = {
  name: string;
  reportType: InstitutionalReportType;
  sectionIds: ReportSectionId[];
  defaultFilters: ReportFilters;
  defaultFormat: ExportFormat;
  pageNumberingMode: PageNumberingMode;
  includePartialCover: boolean;
  includePartialManifest: boolean;
  detailSortBy?: ReportDetailSortBy;
  groupDetailsByDepartment?: boolean;
};
