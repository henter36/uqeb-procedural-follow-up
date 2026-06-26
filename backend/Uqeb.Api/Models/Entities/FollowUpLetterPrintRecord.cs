namespace Uqeb.Api.Models.Entities;

public class FollowUpLetterPrintRecord
{
    public int Id { get; set; }
    public int TransactionId { get; set; }
    public int? TargetDepartmentId { get; set; }
    public int? TargetEntityId { get; set; }
    public string? TargetEntityNameSnapshot { get; set; }
    public int TemplateId { get; set; }
    public int FollowUpSequence { get; set; }
    public int? ResponseDeadlineDays { get; set; }
    public string? DocumentSnapshotJson { get; set; }
    public DateTime PrintRequestedAt { get; set; }
    public int PrintRequestedById { get; set; }
    public DateTime? PrintConfirmedAt { get; set; }
    public int? PrintConfirmedById { get; set; }
    public int? RegisteredFollowUpId { get; set; }
    public DateTime? RegisteredAt { get; set; }
    public bool IsCancelled { get; set; }
    public DateTime? CancelledAt { get; set; }
    public int? CancelledById { get; set; }
    public string? CancellationReason { get; set; }
    public int? BatchJobId { get; set; }
    public int? BatchJobPartId { get; set; }
    public int? ReprintOfId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public byte[] RowVersion { get; set; } = Array.Empty<byte>();

    public Transaction Transaction { get; set; } = null!;
    public Department? TargetDepartment { get; set; }
    public ExternalParty? TargetEntity { get; set; }
    public LetterTemplate Template { get; set; } = null!;
    public User PrintRequestedBy { get; set; } = null!;
    public User? PrintConfirmedBy { get; set; }
    public FollowUp? RegisteredFollowUp { get; set; }
    public User? CancelledBy { get; set; }
    public FollowUpPrintJob? BatchJob { get; set; }
    public FollowUpPrintJobPart? BatchJobPart { get; set; }
    public FollowUpLetterPrintRecord? ReprintOf { get; set; }
}
