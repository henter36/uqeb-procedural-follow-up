using Uqeb.Api.Models.Enums;
using Uqeb.Api.Reporting.Enums;

namespace Uqeb.Api.Reporting.Models;

public sealed class TransactionReportSnapshot
{
    public int TransactionId { get; set; }
    public string TrackingNumber { get; set; } = string.Empty;
    public string IncomingNumber { get; set; } = string.Empty;
    public DateTime IncomingDate { get; set; }
    public string Subject { get; set; } = string.Empty;
    public string IncomingParty { get; set; } = string.Empty;
    public string? CategoryName { get; set; }
    public Priority Priority { get; set; }
    public TransactionStatus Status { get; set; }
    public bool RequiresResponse { get; set; }
    public bool ResponseCompleted { get; set; }
    public DateTime? ResponseCompletedDate { get; set; }
    public DateTime? ResponseDueDate { get; set; }
    public DateTime? ClosedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public string? OutgoingNumber { get; set; }
    public DateTime? OutgoingDate { get; set; }
    public string ResponsibleDepartment { get; set; } = string.Empty;
    public int? ResponsibleDepartmentId { get; set; }
    public List<int> AssignmentDepartmentIds { get; set; } = [];
    public List<string> AssignmentDepartmentNames { get; set; } = [];
    public List<int> OutgoingDepartmentIds { get; set; } = [];
    public List<string> OutgoingDepartmentNames { get; set; } = [];
    public int ActiveAssignmentCount { get; set; }
    public int RepliedAssignmentCount { get; set; }
    public int PendingReplyAssignmentCount { get; set; }
    public DateTime? LastFollowUpDate { get; set; }
    /// <summary>
    /// Earliest due date among active assignments that require a reply and have not been replied to.
    /// Used with strict &lt; today comparison for overdue assignment detection.
    /// </summary>
    public DateTime? EarliestPendingReplyDueDate { get; set; }
    public bool IsClosed { get; set; }
    public bool IsOpen { get; set; }
    public bool IsOverdue { get; set; }
    public bool IsOpenOverdue { get; set; }
    public bool IsCompletedLate { get; set; }
    public bool IsWaitingForStatement { get; set; }
    public bool IsPartialReply { get; set; }
    public bool IsJointDepartment { get; set; }
    public int ElapsedDays { get; set; }
    public List<FollowUpStage> FollowUpStages { get; set; } = [];
}

public sealed class InstitutionalMetricsResult
{
    public int TotalTransactions { get; init; }
    public int ClosedCount { get; init; }
    public int OpenCount { get; init; }
    public int CancelledCount { get; init; }
    public int ArchivedCount { get; init; }
    public int OverdueCount { get; init; }
    public int OpenOverdueCount { get; init; }
    public int CompletedLateCount { get; init; }
    public int JointDepartmentCount { get; init; }
    public int PartialResponseCount { get; init; }
    public double AverageCompletionDays { get; init; }
    public double OnTimeCompletionRate { get; init; }
    public List<TransactionReportSnapshot> Snapshots { get; init; } = [];
}
