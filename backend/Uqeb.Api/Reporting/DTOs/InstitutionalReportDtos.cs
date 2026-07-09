using Uqeb.Api.Reporting.Enums;

namespace Uqeb.Api.Reporting.DTOs;

public sealed class ReportMetadataDto
{
    public string ReportNumber { get; set; } = string.Empty;
    public string ReportTypeName { get; set; } = string.Empty;
    public InstitutionalReportType ReportType { get; set; }
    public DateTime IssueDate { get; set; }
    public DateTime PeriodFrom { get; set; }
    public DateTime PeriodTo { get; set; }
    public string Title { get; set; } = "تقرير المتابعة الإجرائية للمعاملات";
    public string OrganizationName { get; set; } = "المتابعة الإجرائية";
    public string DepartmentName { get; set; } = "إدارة المتابعة والتقارير";
    public string? ConfidentialityLabel { get; set; }
    public string? Introduction { get; set; }
    public string VerificationId { get; set; } = string.Empty;
    public string? FileFingerprint { get; set; }
    public int TotalPages { get; set; }
    public DateTime GeneratedAt { get; set; }
    public int TotalMatchingTransactions { get; set; }
    public int IncludedTransactionCount { get; set; }
    public int DetailRowLimit { get; set; }
}

public sealed class ReportFiltersDto
{
    public DateTime? DateFrom { get; set; }
    public DateTime? DateTo { get; set; }
    public List<int> DepartmentIds { get; set; } = [];
    public List<int> PartyIds { get; set; } = [];
    public List<int> CategoryIds { get; set; } = [];
    public List<string> Priorities { get; set; } = [];
    public List<string> Statuses { get; set; } = [];
    public bool IncludeOverdue { get; set; } = false;
    public string? Search { get; set; }
}

public sealed class KpiCardDto
{
    public string Key { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public string? Delta { get; set; }
    public string? Footnote { get; set; }
}

public sealed class ExecutiveSummaryDto
{
    public List<KpiCardDto> KpiCards { get; set; } = [];
    public string ExecutiveNarrative { get; set; } = string.Empty;
}

public sealed class ChartSeriesPointDto
{
    public string Label { get; set; } = string.Empty;
    public decimal Value { get; set; }
}

public sealed class ChartDto
{
    public string Key { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string ChartType { get; set; } = "bar";
    public List<ChartSeriesPointDto> Series { get; set; } = [];
    public string? Footnote { get; set; }
}

public sealed class KpiComparisonDto
{
    public decimal? CurrentValue { get; set; }
    public decimal? PreviousValue { get; set; }
    public decimal? AbsoluteChange { get; set; }
    public decimal? PercentageChange { get; set; }
    public TrendDirection TrendDirection { get; set; } = TrendDirection.NotComparable;
    public string TrendClassification { get; set; } = "not_comparable";
    public string? ComparisonLabel { get; set; }
}

public sealed class AnalyticalKpiDto
{
    public string Key { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Definition { get; set; } = string.Empty;
    public string Formula { get; set; } = string.Empty;
    public string FieldsUsed { get; set; } = string.Empty;
    public string DisplayValue { get; set; } = string.Empty;
    public decimal? NumericValue { get; set; }
    public string Unit { get; set; } = string.Empty;
    public string Format { get; set; } = "number";
    public int SampleSize { get; set; }
    public int MinimumSampleSize { get; set; }
    public KpiDirection Direction { get; set; } = KpiDirection.Neutral;
    public bool IsAvailable { get; set; } = true;
    public string? UnavailableReason { get; set; }
    public KpiComparisonDto Comparison { get; set; } = new();
}

public sealed class ExecutiveInsightDto
{
    public string Code { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
    public AnalyticalSeverity Severity { get; set; } = AnalyticalSeverity.Medium;
    public string Evidence { get; set; } = string.Empty;
}

public sealed class SignificantFindingDto
{
    public string Code { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Evidence { get; set; } = string.Empty;
    public AnalyticalSeverity Severity { get; set; } = AnalyticalSeverity.Medium;
    public decimal? CurrentValue { get; set; }
    public decimal? PreviousValue { get; set; }
    public string AffectedScope { get; set; } = string.Empty;
}

public sealed class CriticalCaseDto
{
    public int TransactionId { get; set; }
    public string IncomingNumber { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public string Department { get; set; } = string.Empty;
    public string ExternalParty { get; set; } = string.Empty;
    public string Priority { get; set; } = string.Empty;
    public int AgeDays { get; set; }
    public int? DaysOverdue { get; set; }
    public string ReasonCode { get; set; } = string.Empty;
    public string ReasonLabel { get; set; } = string.Empty;
    public string RequiredAction { get; set; } = string.Empty;
    public string SuggestedOwner { get; set; } = string.Empty;
    public AnalyticalSeverity Severity { get; set; } = AnalyticalSeverity.High;
}

public sealed class DepartmentAnalysisRowDto
{
    public int? DepartmentId { get; set; }
    public string DepartmentName { get; set; } = string.Empty;
    public int IncomingCount { get; set; }
    public int ClosedCount { get; set; }
    public int OpenCount { get; set; }
    public int OverdueCount { get; set; }
    public double OnTimeCompletionRate { get; set; }
    public double AverageCompletionDays { get; set; }
    public double MedianCompletionDays { get; set; }
    public int PendingAssignments { get; set; }
    public int PartialReplies { get; set; }
    public int BacklogChange { get; set; }
    public int OldestOpenAgeDays { get; set; }
    public double DataCompletenessRate { get; set; }
    public int SampleSize { get; set; }
    public bool HasSmallSample { get; set; }
    public string SystemComparison { get; set; } = string.Empty;
}

public sealed class DepartmentRecognitionRowDto
{
    public int? DepartmentId { get; set; }
    public string DepartmentName { get; set; } = string.Empty;
    public string RecognitionType { get; set; } = string.Empty;
    public int TransactionCount { get; set; }
    public double OnTimeCompletionRate { get; set; }
    public int OverdueCount { get; set; }
    public double AverageCompletionDays { get; set; }
    public double DataCompletenessRate { get; set; }
    public double ImprovementValue { get; set; }
    public string Reason { get; set; } = string.Empty;
    public double Score { get; set; }
    public bool HasSufficientSample { get; set; }
    public bool IsExcludedByDataQuality { get; set; }
}

public sealed class ExternalPartyAnalysisRowDto
{
    public string ExternalPartyName { get; set; } = string.Empty;
    public int IncomingCount { get; set; }
    public int OutgoingCount { get; set; }
    public int PendingResponseCount { get; set; }
    public int OverdueResponseCount { get; set; }
    public double AverageResponseDays { get; set; }
    public double MedianResponseDays { get; set; }
    public int FollowUpCount { get; set; }
    public int OldestPendingResponseDays { get; set; }
    public string TopCategories { get; set; } = string.Empty;
}

public sealed class CategoryAnalysisRowDto
{
    public string CategoryName { get; set; } = string.Empty;
    public int TransactionCount { get; set; }
    public int OpenCount { get; set; }
    public int OverdueCount { get; set; }
    public double OnTimeCompletionRate { get; set; }
    public double AverageCompletionDays { get; set; }
    public int PendingAssignments { get; set; }
}

public sealed class PriorityAnalysisRowDto
{
    public string Priority { get; set; } = string.Empty;
    public int Count { get; set; }
    public int OpenCount { get; set; }
    public int OverdueCount { get; set; }
    public double AverageAgeDays { get; set; }
    public double OnTimeRate { get; set; }
}

public sealed class BottleneckRowDto
{
    public string ReasonCode { get; set; } = string.Empty;
    public string ReasonLabel { get; set; } = string.Empty;
    public int Count { get; set; }
    public double SharePercent { get; set; }
    public double AverageDelayDays { get; set; }
    public string TopDepartments { get; set; } = string.Empty;
    public string TopExternalParties { get; set; } = string.Empty;
    public List<int> ExampleTransactionIds { get; set; } = [];
}

public sealed class DataQualityIssueDto
{
    public string IssueCode { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public int Count { get; set; }
    public double SharePercent { get; set; }
    public AnalyticalSeverity Severity { get; set; } = AnalyticalSeverity.Medium;
    public string AffectedFields { get; set; } = string.Empty;
    public string SuggestedCorrection { get; set; } = string.Empty;
}

public sealed class AnalyticalRecommendationDto
{
    public string RecommendationId { get; set; } = string.Empty;
    public string SourceFindingCode { get; set; } = string.Empty;
    public string Priority { get; set; } = "medium";
    public string RecommendationText { get; set; } = string.Empty;
    public string ResponsibleScope { get; set; } = string.Empty;
    public int SuggestedDueDays { get; set; }
    public string EvidenceSummary { get; set; } = string.Empty;
    public string Status { get; set; } = "Proposed";
}

public sealed class TimeSeriesPointDto
{
    public string PeriodLabel { get; set; } = string.Empty;
    public DateTime PeriodStart { get; set; }
    public int Incoming { get; set; }
    public int Closed { get; set; }
    public int OpenBalance { get; set; }
    public int Overdue { get; set; }
    public double OnTimeRate { get; set; }
    public double AverageCompletionDays { get; set; }
    public int BacklogGrowth { get; set; }
}

/// <summary>
/// One department's metrics within a single time-grouped period. Grouped by IncomingDate
/// (same basis as TimeSeriesPointDto) and ResponsibleDepartment — a transaction is counted
/// under its single responsible department, never duplicated across joint departments.
/// </summary>
public sealed class DepartmentTimeSeriesPointDto
{
    public int? DepartmentId { get; set; }
    public string DepartmentName { get; set; } = string.Empty;
    public DateTime PeriodStart { get; set; }
    public string PeriodLabel { get; set; } = string.Empty;
    public int IncomingCount { get; set; }
    public int ClosedCount { get; set; }
    public int OpenCount { get; set; }
    public int OverdueCount { get; set; }
    public double OnTimeCompletionRate { get; set; }
    public double AverageCompletionDays { get; set; }
    public int PendingAssignments { get; set; }
    public int PartialReplies { get; set; }
    public int BacklogGrowth { get; set; }
}

public sealed class MethodologyDto
{
    public string ReportName { get; set; } = string.Empty;
    public string ReportVersion { get; set; } = string.Empty;
    public DateTime GeneratedAtUtc { get; set; }
    public string DataPeriod { get; set; } = string.Empty;
    public string PeriodBasis { get; set; } = string.Empty;
    public string ComparisonPeriod { get; set; } = "غير مطبقة";
    public string Filters { get; set; } = string.Empty;
    public string DataSource { get; set; } = "Live query";
    public string SnapshotMode { get; set; } = "Live Preview / Generated Report";
    public string RowLimits { get; set; } = string.Empty;
    public string CalculationVersion { get; set; } = "2026.06.analysis.1";
    public string ApprovalStatus { get; set; } = "Draft";
    public List<string> DeferredMetrics { get; set; } = [];
}

public sealed class InstitutionalReportAnalysisResult
{
    public ReportContentLevel ContentLevel { get; set; } = ReportContentLevel.Analytical;
    public ReportComparisonMode ComparisonMode { get; set; } = ReportComparisonMode.PreviousEquivalentPeriod;
    public DateTime? ComparisonFrom { get; set; }
    public DateTime? ComparisonTo { get; set; }
    public List<AnalyticalKpiDto> Kpis { get; set; } = [];
    public List<ExecutiveInsightDto> ExecutiveInsights { get; set; } = [];
    public List<SignificantFindingDto> Findings { get; set; } = [];
    public List<CriticalCaseDto> CriticalCases { get; set; } = [];
    public List<TimeSeriesPointDto> TimeSeries { get; set; } = [];
    public List<DepartmentTimeSeriesPointDto> DepartmentTimeSeries { get; set; } = [];
    public List<DepartmentAnalysisRowDto> DepartmentPerformance { get; set; } = [];
    public List<DepartmentRecognitionRowDto> DepartmentRecognitions { get; set; } = [];
    public List<ExternalPartyAnalysisRowDto> ExternalParties { get; set; } = [];
    public List<CategoryAnalysisRowDto> Categories { get; set; } = [];
    public List<PriorityAnalysisRowDto> Priorities { get; set; } = [];
    public List<BottleneckRowDto> Bottlenecks { get; set; } = [];
    public List<DataQualityIssueDto> DataQualityIssues { get; set; } = [];
    public double DataCompletenessRate { get; set; }
    public List<AnalyticalRecommendationDto> Recommendations { get; set; } = [];
    public MethodologyDto Methodology { get; set; } = new();
}

public sealed class DepartmentPerformanceRowDto
{
    public int DepartmentId { get; set; }
    public string DepartmentName { get; set; } = string.Empty;
    public int TotalTransactions { get; set; }
    public int ClosedCount { get; set; }
    public int OpenCount { get; set; }
    public int WaitingForStatementCount { get; set; }
    public int OverdueCount { get; set; }
    public int JointDepartmentCount { get; set; }
    public double AverageCompletionDays { get; set; }
    public double OnTimeCompletionRate { get; set; }
    public DepartmentRatingLevel Rating { get; set; }
    public string RatingLabel { get; set; } = string.Empty;
}

public sealed class RiskAlertRowDto
{
    public int Sequence { get; set; }
    public string Alert { get; set; } = string.Empty;
    public string DepartmentName { get; set; } = string.Empty;
    public RiskSeverity Severity { get; set; }
    public string SeverityLabel { get; set; } = string.Empty;
    public int ElapsedDays { get; set; }
    public string SuggestedAction { get; set; } = string.Empty;
}

public sealed class RecommendationRowDto
{
    public string Observation { get; set; } = string.Empty;
    public string RequiredAction { get; set; } = string.Empty;
    public string ResponsibleDepartment { get; set; } = string.Empty;
    public string Priority { get; set; } = string.Empty;
    public string? TargetDate { get; set; }
    public RecommendationSource Source { get; set; }
    public string SourceLabel { get; set; } = string.Empty;
}

public sealed class RiskSummaryCountersDto
{
    public int DepartmentsNeedingFollowUp { get; set; }
    public int OpenJointDepartmentTransactions { get; set; }
    public int PartialResponses { get; set; }
    public int TransactionsWithoutRecentUpdate { get; set; }
    public int DataIntegrityIssues { get; set; }
}

public sealed class TransactionDetailRowDto
{
    public int Sequence { get; set; }
    public int TransactionId { get; set; }
    public string TrackingNumber { get; set; } = string.Empty;
    public string IncomingNumber { get; set; } = string.Empty;
    public DateTime IncomingDate { get; set; }
    public string Subject { get; set; } = string.Empty;
    public string IncomingParty { get; set; } = string.Empty;
    public string ResponsibleDepartment { get; set; } = string.Empty;
    public string JointDepartments { get; set; } = string.Empty;
    public string Priority { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string FollowUpStage { get; set; } = string.Empty;
    public int ElapsedDays { get; set; }
    public string? DueDate { get; set; }
    public string? LastActionDate { get; set; }
    public string ResponseState { get; set; } = string.Empty;
    public string? OutgoingNumber { get; set; }
    public string? OutgoingDate { get; set; }

    /// <summary>
    /// Populated only for <see cref="InstitutionalReportType.DepartmentTransactions"/>: each selected
    /// department this transaction actually matches, with how it matches (referral/outgoing/both).
    /// Empty for every other report type.
    /// </summary>
    public List<TransactionDetailDepartmentRelationDto> MatchedDepartments { get; set; } = [];

    /// <summary>
    /// Set only when this row is a department-grouped duplicate (GroupDetailsByDepartment): identifies
    /// which selected department this particular duplicate was emitted for. Null otherwise.
    /// </summary>
    public int? DepartmentGroupDepartmentId { get; set; }
    public string? DepartmentGroupDepartmentName { get; set; }

    /// <summary>
    /// DepartmentTransactions XLSX export only: ALL assignment/outgoing department names for this
    /// transaction (not just the selected/matched ones) for full auditability. Empty for every other
    /// report type.
    /// </summary>
    public List<string> AllAssignmentDepartments { get; set; } = [];
    public List<string> AllOutgoingDepartments { get; set; } = [];
}

public sealed class TransactionDetailDepartmentRelationDto
{
    public int DepartmentId { get; set; }
    public string DepartmentName { get; set; } = string.Empty;

    /// <summary>"إحالة" | "صادر لها" | "إحالة وصادر لها"</summary>
    public string Relation { get; set; } = string.Empty;
}

public sealed class IntegrityWarningDto
{
    public string Code { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string Severity { get; set; } = "warning";
    public int? TransactionId { get; set; }
}

public sealed class InstitutionalReportModel
{
    public ReportMetadataDto Metadata { get; set; } = new();
    public ReportFiltersDto Filters { get; set; } = new();
    public ExecutiveSummaryDto Summary { get; set; } = new();
    public List<ChartDto> Charts { get; set; } = [];
    public List<DepartmentPerformanceRowDto> DepartmentPerformance { get; set; } = [];

    /// <summary>Each transaction is counted exactly once under its ResponsibleDepartment — sums are additive.</summary>
    public string DepartmentAggregationMode { get; set; } = "ResponsibleDepartment";
    public bool DepartmentTotalsAreAdditive { get; set; } = true;
    public string DepartmentAggregationDescription { get; set; } =
        "مجمَّع حسب الإدارة المسؤولة — كل معاملة تُحتسب مرة واحدة — المجاميع قابلة للجمع.";

    public List<RiskAlertRowDto> Risks { get; set; } = [];
    public List<RecommendationRowDto> Recommendations { get; set; } = [];
    public RiskSummaryCountersDto RiskCounters { get; set; } = new();
    public List<TransactionDetailRowDto> Transactions { get; set; } = [];
    public List<IntegrityWarningDto> IntegrityWarnings { get; set; } = [];
    public InstitutionalReportAnalysisResult Analysis { get; set; } = new();
    public int TotalMatchedRows { get; set; }
    public int ExportedDetailRows { get; set; }
    public bool DetailRowsTruncated { get; set; }
    public int DetailPartsCount { get; set; }

    /// <summary>The sort actually applied to the TransactionDetails rows (after resolving Default per report type).</summary>
    public ReportDetailSortBy DetailSortByEffective { get; set; } = ReportDetailSortBy.Default;

    /// <summary>True only when GroupDetailsByDepartment duplicated rows per matched department (DepartmentTransactions with 2+ selected departments).</summary>
    public bool GroupDetailsByDepartmentEffective { get; set; }

    /// <summary>False only when GroupDetailsByDepartmentEffective is true — a shared transaction can appear under more than one department, so counts must not be summed across department groups.</summary>
    public bool DetailRowsAreAdditive { get; set; } = true;

    /// <summary>
    /// Non-null only when the caller requested comparison (IncludeComparison=true, mode != None) but the
    /// current period was incomplete (missing DateFrom/DateTo for a relative mode, or an invalid custom
    /// range) — null both when comparison wasn't requested and when it was successfully built.
    /// </summary>
    public string? ComparisonUnavailableReason { get; set; }
}

public sealed record RenderedReportPageDto
{
    public int RenderedPageNumber { get; init; }
    public int OriginalPageNumber { get; init; }
    public ReportSectionId SectionId { get; init; }
    public string SectionName { get; init; } = string.Empty;
    public string PageTitle { get; init; } = string.Empty;
    public string PdfProfileName { get; init; } = "StandardPortrait";
    public string HtmlContent { get; init; } = string.Empty;
    public bool IsSelectable { get; init; } = true;
}

public sealed class RenderedReportManifestDto
{
    public string ReportTitle { get; set; } = string.Empty;
    public string ReportId { get; set; } = string.Empty;
    public int TotalPages { get; set; }
    public List<RenderedReportPageDto> Pages { get; set; } = [];
    public bool IsPartialExport { get; set; }
    public string? PartialExportNote { get; set; }
    public int TotalMatchingTransactions { get; set; }
    public int IncludedTransactionCount { get; set; }
    public int DetailRowLimit { get; set; }
    public bool RequiresDetailOverflowAction { get; set; }
    public int TotalMatchedRows { get; set; }
    public int ExportedDetailRows { get; set; }
    public bool DetailRowsTruncated { get; set; }
    public int DetailPartsCount { get; set; }
    public int LoadedDetailRows { get; set; }
    public int? CurrentPartNumber { get; set; }
    public int? RowsFrom { get; set; }
    public int? RowsTo { get; set; }
    public bool IsSummaryOnly { get; set; }
    public DetailOverflowAction? OverflowAction { get; set; }
    public string? Stylesheet { get; set; }
    public string? TemplateVersion { get; set; }
    public string? FileFingerprint { get; set; }
    public InstitutionalReportAnalysisResult? Analysis { get; set; }

    public RenderedReportManifestDto CloneWithoutHtml() => new()
    {
        ReportTitle = ReportTitle,
        ReportId = ReportId,
        TotalPages = TotalPages,
        IsPartialExport = IsPartialExport,
        PartialExportNote = PartialExportNote,
        TotalMatchingTransactions = TotalMatchingTransactions,
        IncludedTransactionCount = IncludedTransactionCount,
        DetailRowLimit = DetailRowLimit,
        RequiresDetailOverflowAction = RequiresDetailOverflowAction,
        TotalMatchedRows = TotalMatchedRows,
        ExportedDetailRows = ExportedDetailRows,
        DetailRowsTruncated = DetailRowsTruncated,
        DetailPartsCount = DetailPartsCount,
        LoadedDetailRows = LoadedDetailRows,
        CurrentPartNumber = CurrentPartNumber,
        RowsFrom = RowsFrom,
        RowsTo = RowsTo,
        IsSummaryOnly = IsSummaryOnly,
        OverflowAction = OverflowAction,
        Stylesheet = Stylesheet,
        TemplateVersion = TemplateVersion,
        FileFingerprint = FileFingerprint,
        Analysis = Analysis,
        Pages = Pages.Select(p => p with { HtmlContent = string.Empty }).ToList()
    };
}

public sealed class ReportBuildRequestDto
{
    public InstitutionalReportType ReportType { get; set; } = InstitutionalReportType.ExecutiveComprehensive;
    public ReportFiltersDto Filters { get; set; } = new();
    public string? Title { get; set; }
    public string? Introduction { get; set; }
    public List<ReportSectionId> SectionIds { get; set; } = [];
    public int? SingleTransactionId { get; set; }
    public ReportContentLevel? ContentLevel { get; set; }
    public ReportComparisonMode? ComparisonMode { get; set; }
    public DateTime? ComparisonDateFrom { get; set; }
    public DateTime? ComparisonDateTo { get; set; }
    public ReportTimeGrouping? TimeGrouping { get; set; }
    public bool? IncludeExecutiveSummary { get; set; }
    public bool? IncludeComparison { get; set; }
    public bool? IncludeCriticalCases { get; set; }
    public bool? IncludeTimeTrends { get; set; }
    public bool? IncludeDepartmentPerformance { get; set; }
    public bool? IncludeExternalPartyAnalysis { get; set; }
    public bool? IncludeCategoryAnalysis { get; set; }
    public bool? IncludeBottleneckAnalysis { get; set; }
    public bool? IncludeDataQuality { get; set; }
    public bool? IncludeRecommendations { get; set; }
    public bool? IncludeMethodology { get; set; }
    public int? MaxCriticalCases { get; set; }
    public int? MaxFindings { get; set; }
    public int? MaxRecommendations { get; set; }

    /// <summary>Detail-table row order. Report-run option, not a data-scope filter — lives here, not in Filters.</summary>
    public ReportDetailSortBy? DetailSortBy { get; set; }

    /// <summary>
    /// DepartmentTransactions only: when true and 2+ departments are selected, duplicates each matching
    /// transaction once per matched department (grouped, non-additive). Report-run option, not a filter.
    /// </summary>
    public bool? GroupDetailsByDepartment { get; set; }
}

/// <summary>Export request. Boolean and enum fields default intentionally when omitted (under-posting is valid).</summary>
public sealed class ReportExportRequestDto
{
    public string? ReportId { get; set; }
    public ReportBuildRequestDto BuildRequest { get; set; } = new();
    /// <summary>Defaults to PDF when omitted from JSON.</summary>
    public ExportFormat? ExportFormat { get; set; }
    /// <summary>Defaults to full report when omitted from JSON.</summary>
    public ExportMode? ExportMode { get; set; }
    public List<ReportSectionId> SelectedSectionIds { get; set; } = [];
    public List<int> SelectedPageNumbers { get; set; } = [];
    public string? PageRangeExpression { get; set; }
    public int? CurrentPageNumber { get; set; }
    /// <summary>Defaults to false when omitted from JSON.</summary>
    public bool? IncludePartialCover { get; set; }
    /// <summary>Defaults to false when omitted from JSON.</summary>
    public bool? IncludePartialManifest { get; set; }
    /// <summary>Defaults to restart numbering when omitted from JSON.</summary>
    public PageNumberingMode? PageNumberingMode { get; set; }
    public int? TemplateId { get; set; }
    public string? Reason { get; set; }
    /// <summary>Defaults to none when omitted from JSON.</summary>
    public DetailOverflowAction? DetailOverflowAction { get; set; }
}

public sealed class ReportExportResultDto
{
    public byte[] Content { get; set; } = [];
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public string FileFingerprint { get; set; } = string.Empty;
    public RenderedReportManifestDto Manifest { get; set; } = new();
}

public sealed class ReportTemplateDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public InstitutionalReportType ReportType { get; set; }
    public List<ReportSectionId> SectionIds { get; set; } = [];
    public ReportFiltersDto DefaultFilters { get; set; } = new();
    public ExportFormat DefaultFormat { get; set; } = ExportFormat.Pdf;
    public PageNumberingMode PageNumberingMode { get; set; } = PageNumberingMode.Restart;
    public bool IncludePartialCover { get; set; }
    public bool IncludePartialManifest { get; set; }
    public ReportDetailSortBy? DetailSortBy { get; set; }
    public bool? GroupDetailsByDepartment { get; set; }
}

public sealed class SaveReportTemplateRequestDto
{
    public string Name { get; set; } = string.Empty;
    public InstitutionalReportType? ReportType { get; set; }
    public List<ReportSectionId> SectionIds { get; set; } = [];
    public ReportFiltersDto DefaultFilters { get; set; } = new();
    public ExportFormat? DefaultFormat { get; set; }
    public PageNumberingMode? PageNumberingMode { get; set; }
    public bool? IncludePartialCover { get; set; }
    public bool? IncludePartialManifest { get; set; }
    public ReportDetailSortBy? DetailSortBy { get; set; }
    public bool? GroupDetailsByDepartment { get; set; }
}
