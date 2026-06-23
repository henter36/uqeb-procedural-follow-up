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
};

export type InstitutionalReportType = 1 | 2 | 3 | 4 | 5;

export type ReportSectionId = 1 | 2 | 3 | 4 | 5 | 6 | 7 | 8 | 9 | 10 | 11;

export type ExportFormat = 1 | 2 | 3 | 4;

export type ExportMode = 1 | 2 | 3 | 4;

export type PageNumberingMode = 1 | 2;

export type DetailOverflowAction = 0 | 1 | 2 | 3;

export type ReportFilters = {
  dateFrom?: string | null;
  dateTo?: string | null;
  departmentIds: number[];
  partyIds: number[];
  categoryIds: number[];
  priorities: string[];
  statuses: string[];
  includeJointDepartmentTransactions: boolean;
  includeOverdue: boolean;
  includeDetails: boolean;
  includeRisks: boolean;
  includeRecommendations: boolean;
  search?: string | null;
};

export type ReportBuildRequest = {
  reportType: InstitutionalReportType;
  title?: string;
  introduction?: string;
  sectionIds: ReportSectionId[];
  singleTransactionId?: number | null;
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
};
