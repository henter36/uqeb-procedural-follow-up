namespace Uqeb.Api.DTOs.RecurringTemplates;

public class RecurringTemplateDepartmentDto
{
    public int DepartmentId { get; set; }
    public string DepartmentName { get; set; } = string.Empty;
    public int? SortOrder { get; set; }
}

public class RecurringTemplateListItemDto
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string RecurrenceType { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTime StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public string NextPeriodKey { get; set; } = string.Empty;
    public string NextPeriodLabel { get; set; } = string.Empty;
    public string? LastGeneratedPeriodKey { get; set; }
    public string? LastGeneratedPeriodLabel { get; set; }
    public int GeneratedTransactionsCount { get; set; }
}

public class RecurringTemplateDetailDto : RecurringTemplateListItemDto
{
    public string SubjectTemplate { get; set; } = string.Empty;
    public string IncomingSourceType { get; set; } = string.Empty;
    public int? IncomingFromPartyId { get; set; }
    public string? IncomingFromPartyName { get; set; }
    public int? IncomingFromDepartmentId { get; set; }
    public string? IncomingFromDepartmentName { get; set; }
    public int CategoryId { get; set; }
    public string? CategoryName { get; set; }
    public string Priority { get; set; } = string.Empty;
    public string ResponseType { get; set; } = string.Empty;
    public bool RequiresResponse { get; set; }
    public string DefaultRequiredAction { get; set; } = string.Empty;
    public int DueDaysAfterPeriodEnd { get; set; }
    public int? DefaultReplyDueDays { get; set; }
    public string? Notes { get; set; }
    public List<RecurringTemplateDepartmentDto> Departments { get; set; } = new();
    public string CreatedByName { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public DateTime? PausedAt { get; set; }
    public string? PausedByName { get; set; }
    public DateTime? ResumedAt { get; set; }
    public string? ResumedByName { get; set; }
    public DateTime? TerminatedAt { get; set; }
    public string? TerminatedByName { get; set; }
    public string? TerminationReason { get; set; }
}

public class CreateRecurringTemplateRequest
{
    public string Title { get; set; } = string.Empty;
    public string SubjectTemplate { get; set; } = string.Empty;
    public string? RecurrenceType { get; set; }
    public DateTime? StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public string? IncomingSourceType { get; set; }
    public int? IncomingFromPartyId { get; set; }
    public int? IncomingFromDepartmentId { get; set; }
    public int? CategoryId { get; set; }
    public string? Priority { get; set; }
    public string? ResponseType { get; set; }
    public bool? RequiresResponse { get; set; }
    public string? DefaultRequiredAction { get; set; }
    public int? DueDaysAfterPeriodEnd { get; set; }
    public int? DefaultReplyDueDays { get; set; }
    public string? Notes { get; set; }
    public List<int>? DepartmentIds { get; set; }
}

public class TerminateRecurringTemplateRequest
{
    public string Reason { get; set; } = string.Empty;
}

public class GenerateRecurringTransactionRequest
{
    public string PeriodKey { get; set; } = string.Empty;
    public DateTime? IncomingDate { get; set; }
    public DateTime? ReferralDate { get; set; }
    public string? ReferralLetterNumber { get; set; }
}

public class GenerateRecurringTransactionResponse
{
    public int TransactionId { get; set; }
    public string InternalTrackingNumber { get; set; } = string.Empty;
    public string PeriodKey { get; set; } = string.Empty;
    public string PeriodLabel { get; set; } = string.Empty;
    public DateTime DueDate { get; set; }
}

public class RecurringTemplateTransactionItemDto
{
    public int TransactionId { get; set; }
    public string InternalTrackingNumber { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public string PeriodKey { get; set; } = string.Empty;
    public string PeriodLabel { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTime IncomingDate { get; set; }
    public DateTime? DueDate { get; set; }
    public DateTime? ClosedAt { get; set; }
}
