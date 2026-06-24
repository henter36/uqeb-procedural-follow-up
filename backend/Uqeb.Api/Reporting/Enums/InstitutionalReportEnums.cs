namespace Uqeb.Api.Reporting.Enums;

public enum InstitutionalReportType
{
    ExecutiveComprehensive = 1,
    OverdueTransactions = 2,
    JointDepartmentTransactions = 3,
    PartialResponses = 4,
    SingleTransaction = 5
}

public enum ReportSectionId
{
    Cover = 1,
    ExecutiveSummary = 2,
    IndicatorsDashboard = 3,
    DepartmentPerformance = 4,
    RisksAndAlerts = 5,
    ExecutiveRecommendations = 6,
    TransactionDetails = 7,
    Appendices = 8,
    ReportMetadata = 9,
    PartialCover = 10,
    PartialManifest = 11,
    KeyPerformanceIndicators = 12,
    SignificantFindings = 13,
    CriticalCases = 14,
    TimeTrends = 15,
    ExternalPartyAnalysis = 16,
    ClassificationAndPriorityAnalysis = 17,
    DelayAndBottleneckAnalysis = 18,
    DataQuality = 19,
    RecommendationsAndActionPlan = 20,
    MethodologyAndDefinitions = 21
}

public enum ExportFormat
{
    Pdf = 1,
    Docx = 2,
    Xlsx = 3,
    Html = 4
}

public enum ExportMode
{
    FullReport = 1,
    SelectedSections = 2,
    SelectedPages = 3,
    CurrentPage = 4
}

public enum PageNumberingMode
{
    Original = 1,
    Restart = 2
}

public enum DepartmentRatingLevel
{
    Good = 1,
    NeedsFollowUp = 2,
    Critical = 3
}

public enum RiskSeverity
{
    Informational = 1,
    Elevated = 2,
    High = 3,
    Critical = 4
}

public enum FollowUpStage
{
    WaitingForStatement = 1,
    WaitingForDepartmentReply = 2,
    PartialReply = 3,
    UnderProcessing = 4,
    Overdue = 5
}

public enum RecommendationSource
{
    Automated = 1,
    Manual = 2
}

public enum ReportContentLevel
{
    Executive = 1,
    Analytical = 2,
    Detailed = 3
}

public enum ReportComparisonMode
{
    None = 0,
    PreviousEquivalentPeriod = 1,
    YearOverYear = 2,
    Custom = 3
}

public enum ReportTimeGrouping
{
    Daily = 1,
    Weekly = 2,
    Monthly = 3,
    Quarterly = 4
}

public enum KpiDirection
{
    HigherIsBetter = 1,
    LowerIsBetter = 2,
    Neutral = 3
}

public enum TrendDirection
{
    Improved = 1,
    Declined = 2,
    Stable = 3,
    NotComparable = 4
}

public enum AnalyticalSeverity
{
    Low = 1,
    Medium = 2,
    High = 3,
    Critical = 4
}

/// <summary>How to handle exports when matching transactions exceed <see cref="Configuration.ReportingOptions.MaxPdfDetailRows"/>.</summary>
public enum DetailOverflowAction
{
    None = 0,
    /// <summary>Summary PDF/DOCX without embedded detail rows; overflow documented in report body.</summary>
    SummaryOnly = 1,
    /// <summary>ZIP containing a full summary PDF plus multiple detail PDF parts.</summary>
    SplitPdf = 2,
    /// <summary>Export all transaction details to XLSX (full row set).</summary>
    FullDetailsXlsx = 3
}
