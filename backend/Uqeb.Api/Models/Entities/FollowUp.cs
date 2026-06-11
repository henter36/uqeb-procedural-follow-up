using Uqeb.Api.Models.Enums;

namespace Uqeb.Api.Models.Entities;

public class FollowUp
{
    public int Id { get; set; }
    public int TransactionId { get; set; }
    public string? FollowUpNumber { get; set; }
    public DateTime FollowUpDate { get; set; }
    public string? SentTo { get; set; }
    public string? Notes { get; set; }
    public bool RequiresReply { get; set; }
    public ReplyStatus ReplyStatus { get; set; } = ReplyStatus.Pending;
    public DateTime? ReplyDate { get; set; }
    public string? ReplySummary { get; set; }
    public int CreatedById { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public Transaction Transaction { get; set; } = null!;
    public User CreatedBy { get; set; } = null!;
    public ICollection<FollowUpRecipient> Recipients { get; set; } = new List<FollowUpRecipient>();
    public ICollection<FollowUpDepartment> Departments { get; set; } = new List<FollowUpDepartment>();
}
