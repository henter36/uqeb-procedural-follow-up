namespace Uqeb.Api.DTOs.Reports;

public class RecurringObligationsSummaryDto
{
    public int Total { get; set; }
    public int Active { get; set; }
    public int Upcoming { get; set; }
    public int DueSoon { get; set; }
    public int Overdue { get; set; }
    public int Suspended { get; set; }
    public int Terminated { get; set; }
    public List<RecurringObligationsGroupCountDto> Groups { get; set; } = new();
}

public class RecurringObligationsGroupCountDto
{
    public string GroupKey { get; set; } = string.Empty;
    public string GroupLabel { get; set; } = string.Empty;
    public int Count { get; set; }
}

public class RecurringObligationReportRowDto
{
    public int TemplateId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? OwningDepartmentName { get; set; }
    public List<string> ResponsibleDepartmentNames { get; set; } = new();
    public string RecurrenceType { get; set; } = string.Empty;
    public string RecurrenceTypeLabel { get; set; } = string.Empty;
    public DateTime StartDate { get; set; }
    public string? NextPeriodKey { get; set; }
    public string? NextPeriodLabel { get; set; }
    public DateTime? NextDueDate { get; set; }
    public string? NextDueDateHijri { get; set; }
    public DateTime? LastCompletionDate { get; set; }
    public string Status { get; set; } = string.Empty;
    public string ScheduleStatus { get; set; } = string.Empty;
    public int? DaysRemaining { get; set; }
    public string Priority { get; set; } = string.Empty;
    public int GeneratedTransactionsCount { get; set; }
}

public class RecurringObligationsReportFilterRequest
{
    public DateTime? DateFrom { get; set; }
    public DateTime? DateTo { get; set; }
    public int? DepartmentId { get; set; }
    public string? Status { get; set; }
    public string? RecurrenceType { get; set; }
    public string? Priority { get; set; }
    public string? ScheduleStatus { get; set; }
    public string? Search { get; set; }
    public string? GroupBy { get; set; }
    public int DueSoonWithinDays { get; set; } = 7;
}

public class RecurringObligationsReportPagedFilterRequest : RecurringObligationsReportFilterRequest
{
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 5;
}
