export const InstitutionalReportType = {
  ExecutiveComprehensive: 1,
  OverdueTransactions: 2,
  JointDepartmentTransactions: 3,
  PartialResponses: 4,
  SingleTransaction: 5,
  DepartmentTransactions: 6,
} as const;

export const ReportDetailSortBy = {
  Default: 0,
  IncomingDateDesc: 1,
  Department: 2,
  Status: 3,
  Priority: 4,
  DueDate: 5,
} as const;

export const ReportSectionId = {
  Cover: 1,
  ExecutiveSummary: 2,
  IndicatorsDashboard: 3,
  DepartmentPerformance: 4,
  RisksAndAlerts: 5,
  ExecutiveRecommendations: 6,
  TransactionDetails: 7,
  Appendices: 8,
  ReportMetadata: 9,
  PartialCover: 10,
  PartialManifest: 11,
  KeyPerformanceIndicators: 12,
  SignificantFindings: 13,
  CriticalCases: 14,
  TimeTrends: 15,
  ExternalPartyAnalysis: 16,
  ClassificationAndPriorityAnalysis: 17,
  DelayAndBottleneckAnalysis: 18,
  DataQuality: 19,
  RecommendationsAndActionPlan: 20,
  MethodologyAndDefinitions: 21,
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

export const DetailOverflowAction = {
  None: 0,
  SummaryOnly: 1,
  SplitPdf: 2,
  FullDetailsXlsx: 3,
} as const;

export const ReportContentLevel = {
  Executive: 1,
  Analytical: 2,
  Detailed: 3,
} as const;

export const ReportComparisonMode = {
  None: 0,
  PreviousEquivalentPeriod: 1,
  YearOverYear: 2,
  Custom: 3,
} as const;

export const ReportTimeGrouping = {
  Daily: 1,
  Weekly: 2,
  Monthly: 3,
  Quarterly: 4,
} as const;
