using Uqeb.Api.Models.Enums;

namespace Uqeb.Api.Models.Entities;

public class Transaction
{
    public int Id { get; set; }
    public string InternalTrackingNumber { get; set; } = string.Empty;
    public string IncomingNumber { get; set; } = string.Empty;
    public DateTime IncomingDate { get; set; }
    public string Subject { get; set; } = string.Empty;
    public string? IncomingFrom { get; set; }
    public IncomingSourceType IncomingSourceType { get; set; } = IncomingSourceType.External;
    public int? IncomingFromPartyId { get; set; }
    public int? IncomingFromDepartmentId { get; set; }
    public string? OutgoingNumber { get; set; }
    public DateTime? OutgoingDate { get; set; }
    public string? OutgoingTo { get; set; }
    public int? OutgoingToPartyId { get; set; }
    public bool RequiresResponse { get; set; }
    public ResponseType ResponseType { get; set; } = ResponseType.None;
    public bool ResponseCompleted { get; set; }
    public DateTime? ResponseCompletedDate { get; set; }
    public string? ResponseSummary { get; set; }
    public TransactionStatus Status { get; set; } = TransactionStatus.New;
    public Priority Priority { get; set; } = Priority.Normal;
    public string? Category { get; set; }
    public int? CategoryId { get; set; }
    public int? ResponseDueDays { get; set; }
    public DateTime? ResponseDueDate { get; set; }
    public string? Notes { get; set; }
    public int CreatedById { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public int? UpdatedById { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public bool IsArchived { get; set; }
    public DateTime? ArchivedAt { get; set; }
    public DateTime? ClosedAt { get; set; }

    public User CreatedBy { get; set; } = null!;
    public User? UpdatedBy { get; set; }
    public ExternalParty? IncomingFromParty { get; set; }
    public Department? IncomingFromDepartment { get; set; }
    public ExternalParty? OutgoingToParty { get; set; }
    public Category? CategoryEntity { get; set; }
    public ICollection<TransactionOutgoingParty> OutgoingParties { get; set; } = new List<TransactionOutgoingParty>();
    public ICollection<TransactionOutgoingDepartment> OutgoingDepartments { get; set; } = new List<TransactionOutgoingDepartment>();
    public ICollection<FollowUp> FollowUps { get; set; } = new List<FollowUp>();
    public ICollection<Assignment> Assignments { get; set; } = new List<Assignment>();
    public ICollection<Attachment> Attachments { get; set; } = new List<Attachment>();
    public ICollection<AuditLog> AuditLogs { get; set; } = new List<AuditLog>();
}
