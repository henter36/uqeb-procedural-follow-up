export const InstitutionalReportType = {
  ExecutiveComprehensive: 1,
  OverdueTransactions: 2,
  JointDepartmentTransactions: 3,
  PartialResponses: 4,
  SingleTransaction: 5,
} as const;

export const ReportSectionId = {
  Cover: 1,
  ExecutiveSummary: 2,
  IndicatorsDashboard: 3,
  DepartmentPerformance: 4,
  RisksAndAlerts: 5,
  ExecutiveRecommendations: 6,
  TransactionDetails: 7,
  Appendix: 8,
  PartialCover: 9,
  PartialManifest: 10,
  ReportMetadata: 11,
} as const;

export const ExportFormat = {
  Pdf: 1,
  Docx: 2,
  Xlsx: 3,
  Html: 4,
} as const;

export const ExportMode = {
  FullReport: 1,
  SelectedSections: 2,
  SelectedPages: 3,
  CurrentPage: 4,
} as const;

export const PageNumberingMode = {
  PreserveOriginal: 1,
  Restart: 2,
} as const;
