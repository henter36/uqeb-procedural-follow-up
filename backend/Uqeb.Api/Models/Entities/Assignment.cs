using Uqeb.Api.Models.Enums;

namespace Uqeb.Api.Models.Entities;

public class Assignment
{
    public int Id { get; set; }
    public int TransactionId { get; set; }
    public int DepartmentId { get; set; }
    public DateTime AssignedDate { get; set; }
    public string? RequiredAction { get; set; }
    public bool RequiresReply { get; set; }
    public int? ReplyDueDays { get; set; }
    public DateTime? DueDate { get; set; }
    public ReplyStatus ReplyStatus { get; set; } = ReplyStatus.Pending;
    public DateTime? ReplyDate { get; set; }
    public string? ReplySummary { get; set; }
    public AssignmentStatus Status { get; set; } = AssignmentStatus.Active;
    public string? LetterNumber { get; set; }
    public int CreatedById { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public Transaction Transaction { get; set; } = null!;
    public Department Department { get; set; } = null!;
    public User CreatedBy { get; set; } = null!;
}
