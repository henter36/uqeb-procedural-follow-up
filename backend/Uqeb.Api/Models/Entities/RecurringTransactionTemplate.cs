using Uqeb.Api.Models.Enums;

namespace Uqeb.Api.Models.Entities;

public class RecurringTransactionTemplate
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string SubjectTemplate { get; set; } = string.Empty;
    public RecurrenceType RecurrenceType { get; set; }
    public RecurringTemplateStatus Status { get; set; } = RecurringTemplateStatus.Active;
    public DateTime StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public IncomingSourceType IncomingSourceType { get; set; } = IncomingSourceType.Internal;
    public int? IncomingFromPartyId { get; set; }
    public int? IncomingFromDepartmentId { get; set; }
    public int CategoryId { get; set; }
    public Priority Priority { get; set; } = Priority.Normal;
    public ResponseType ResponseType { get; set; } = ResponseType.None;
    public bool RequiresResponse { get; set; }
    public string DefaultRequiredAction { get; set; } = string.Empty;
    public int DueDaysAfterPeriodEnd { get; set; }
    public int? DefaultReplyDueDays { get; set; }
    public string? Notes { get; set; }
    public string? LastGeneratedPeriodKey { get; set; }
    public RecurringNextTransactionCreationMethod NextTransactionCreationMethod { get; set; } = RecurringNextTransactionCreationMethod.Manual;
    public int CreatedById { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
    public DateTime? PausedAt { get; set; }
    public int? PausedById { get; set; }
    public DateTime? ResumedAt { get; set; }
    public int? ResumedById { get; set; }
    public DateTime? TerminatedAt { get; set; }
    public int? TerminatedById { get; set; }
    public string? TerminationReason { get; set; }

    public ExternalParty? IncomingFromParty { get; set; }
    public Department? IncomingFromDepartment { get; set; }
    public Category? CategoryEntity { get; set; }
    public User CreatedBy { get; set; } = null!;
    public User? PausedBy { get; set; }
    public User? ResumedBy { get; set; }
    public User? TerminatedBy { get; set; }
    public ICollection<RecurringTransactionTemplateDepartment> Departments { get; set; } = new List<RecurringTransactionTemplateDepartment>();
    public ICollection<Transaction> GeneratedTransactions { get; set; } = new List<Transaction>();
}
