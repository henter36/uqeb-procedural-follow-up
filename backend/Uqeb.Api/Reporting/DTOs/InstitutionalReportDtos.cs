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
    public string OrganizationName { get; set; } = "الهيئة العامة للمتابعة الإجرائية";
    public string DepartmentName { get; set; } = "إدارة المتابعة والتقارير";
    public string? Introduction { get; set; }
    public string VerificationId { get; set; } = string.Empty;
    public string? FileFingerprint { get; set; }
    public int TotalPages { get; set; }
    public DateTime GeneratedAt { get; set; }
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
    public bool IncludeJointDepartmentTransactions { get; set; } = true;
    public bool IncludeOverdue { get; set; } = true;
    public bool IncludeDetails { get; set; } = true;
    public bool IncludeRisks { get; set; } = true;
    public bool IncludeRecommendations { get; set; } = true;
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
    public List<RiskAlertRowDto> Risks { get; set; } = [];
    public List<RecommendationRowDto> Recommendations { get; set; } = [];
    public RiskSummaryCountersDto RiskCounters { get; set; } = new();
    public List<TransactionDetailRowDto> Transactions { get; set; } = [];
    public List<IntegrityWarningDto> IntegrityWarnings { get; set; } = [];
}

public sealed record RenderedReportPageDto
{
    public int RenderedPageNumber { get; init; }
    public int OriginalPageNumber { get; init; }
    public ReportSectionId SectionId { get; init; }
    public string SectionName { get; init; } = string.Empty;
    public string PageTitle { get; init; } = string.Empty;
    public string HtmlContent { get; init; } = string.Empty;
    public bool IsSelectable { get; init; } = true;
}

public sealed class RenderedReportManifestDto
{
    public string ReportId { get; set; } = string.Empty;
    public int TotalPages { get; set; }
    public List<RenderedReportPageDto> Pages { get; set; } = [];
    public bool IsPartialExport { get; set; }
    public string? PartialExportNote { get; set; }

    public RenderedReportManifestDto CloneWithoutHtml() => new()
    {
        ReportId = ReportId,
        TotalPages = TotalPages,
        IsPartialExport = IsPartialExport,
        PartialExportNote = PartialExportNote,
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
}

public sealed class ReportExportRequestDto
{
    public string? ReportId { get; set; }
    public ReportBuildRequestDto BuildRequest { get; set; } = new();
    public ExportFormat ExportFormat { get; set; } = ExportFormat.Pdf;
    public ExportMode ExportMode { get; set; } = ExportMode.FullReport;
    public List<ReportSectionId> SelectedSectionIds { get; set; } = [];
    public List<int> SelectedPageNumbers { get; set; } = [];
    public string? PageRangeExpression { get; set; }
    public int? CurrentPageNumber { get; set; }
    public bool IncludePartialCover { get; set; }
    public bool IncludePartialManifest { get; set; }
    public PageNumberingMode PageNumberingMode { get; set; } = PageNumberingMode.Restart;
    public int? TemplateId { get; set; }
    public string? Reason { get; set; }
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
}

public sealed class SaveReportTemplateRequestDto
{
    public string Name { get; set; } = string.Empty;
    public InstitutionalReportType ReportType { get; set; }
    public List<ReportSectionId> SectionIds { get; set; } = [];
    public ReportFiltersDto DefaultFilters { get; set; } = new();
    public ExportFormat DefaultFormat { get; set; } = ExportFormat.Pdf;
    public PageNumberingMode PageNumberingMode { get; set; } = PageNumberingMode.Restart;
    public bool IncludePartialCover { get; set; }
    public bool IncludePartialManifest { get; set; }
}
