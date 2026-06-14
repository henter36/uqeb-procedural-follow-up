using Uqeb.Api.DTOs.Transactions;

namespace Uqeb.Api.DTOs.Reports;

public class DashboardSummaryDto
{
    public int TotalOpen { get; set; }
    public int RequiresResponsePending { get; set; }
    public int ResponseOverdueCount { get; set; }
    public int WaitingForReply { get; set; }
    public int PartiallyReplied { get; set; }
    public int ReadyForResponse { get; set; }
    public int ClosedThisMonth { get; set; }
    public double AverageCompletionDays { get; set; }
}

public class DashboardDto : DashboardSummaryDto
{
    public int NewCount { get; set; }
    public int OverdueCount { get; set; }
    public int RequiresResponse { get; set; }
    public int ResponseCompleted { get; set; }
    public int ClosedCount { get; set; }
    public List<DepartmentOverdueDto> TopOverdueDepartments { get; set; } = new();
    public List<ExternalPartyReportDto> TopIncomingParties { get; set; } = new();
    public List<CategoryDistributionDto> ByCategory { get; set; } = new();
    public List<StatusDistributionDto> ByStatus { get; set; } = new();
    public List<TransactionListDto> ActionRequired { get; set; } = new();
}

public class DepartmentOverdueDto
{
    public int DepartmentId { get; set; }
    public string DepartmentName { get; set; } = string.Empty;
    public int OverdueCount { get; set; }
}

public class MonthlyReportDto
{
    public int Year { get; set; }
    public int Month { get; set; }
    public int IncomingCount { get; set; }
    public int OutgoingCount { get; set; }
}

public class DepartmentReportDto
{
    public int DepartmentId { get; set; }
    public string DepartmentName { get; set; } = string.Empty;
    public int TotalAssigned { get; set; }
    public int Pending { get; set; }
    public int Replied { get; set; }
    public int Overdue { get; set; }
}

public class ExternalPartyReportDto
{
    public string PartyName { get; set; } = string.Empty;
    public int TransactionCount { get; set; }
}

public class CategoryDistributionDto
{
    public int? CategoryId { get; set; }
    public string CategoryName { get; set; } = string.Empty;
    public int Count { get; set; }
}

public class StatusDistributionDto
{
    public string Status { get; set; } = string.Empty;
    public int Count { get; set; }
}

public class OutgoingPartyReportDto
{
    public int ExternalPartyId { get; set; }
    public string PartyName { get; set; } = string.Empty;
    public int TransactionCount { get; set; }
}

public class OutgoingDepartmentReportDto
{
    public int DepartmentId { get; set; }
    public string DepartmentName { get; set; } = string.Empty;
    public int TransactionCount { get; set; }
    public int OpenCount { get; set; }
    public int ClosedCount { get; set; }
    public int OverdueCount { get; set; }
}

public class DepartmentSummaryDto
{
    public int DepartmentId { get; set; }
    public string DepartmentName { get; set; } = string.Empty;
    public int TotalIncoming { get; set; }
    public int OpenCount { get; set; }
    public int WaitingForReplyCount { get; set; }
    public int OverdueCount { get; set; }
    public int ClosedCount { get; set; }
    public double CloseRate { get; set; }
}

public class ReportFilterRequest
{
    public DateTime? DateFrom { get; set; }
    public DateTime? DateTo { get; set; }
    public string? Status { get; set; }
    public int? CategoryId { get; set; }
    public int? DepartmentId { get; set; }
    public int? IncomingPartyId { get; set; }
    public int? OutgoingPartyId { get; set; }
    public int? OutgoingDepartmentId { get; set; }
    public string? IncomingSourceType { get; set; }
    public string? Search { get; set; }
}

public class ReportPagedFilterRequest : ReportFilterRequest
{
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 5;
}

public class ReportSectionCountsDto
{
    public int ResponseRequired { get; set; }
    public int OverdueResponses { get; set; }
    public int OpenAssignments { get; set; }
    public int PartialReplies { get; set; }
    public int Overdue { get; set; }
    public int WaitingReply { get; set; }
    public int Open { get; set; }
}

public class ReportTransactionRowDto
{
    public int Id { get; set; }
    public string InternalTrackingNumber { get; set; } = string.Empty;
    public string IncomingNumber { get; set; } = string.Empty;
    public DateTime IncomingDate { get; set; }
    public string? IncomingHijriDate { get; set; }
    public string Subject { get; set; } = string.Empty;
    public string? IncomingFromDisplayName { get; set; }
    public List<string> OutgoingDepartmentsDisplayNames { get; set; } = new();
    public string? CategoryName { get; set; }
    public string Priority { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string ResponseType { get; set; } = string.Empty;
    public DateTime? ResponseDueDate { get; set; }
    public DateTime? AssignmentDueDate { get; set; }
    public int? DaysOverdue { get; set; }
    public DateTime CreatedAt { get; set; }
    public bool IsOverdue { get; set; }
}
