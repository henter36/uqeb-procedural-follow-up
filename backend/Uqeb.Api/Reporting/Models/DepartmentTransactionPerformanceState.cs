namespace Uqeb.Api.Reporting.Models;

/// <summary>
/// Reporting-only state for one transaction/department relationship. This is derived from the
/// department's non-cancelled assignments and never changes the transaction workflow state.
/// </summary>
internal sealed class DepartmentTransactionPerformanceState
{
    public int DepartmentId { get; init; }
    public string DepartmentName { get; init; } = string.Empty;
    public int TransactionId { get; init; }
    public bool IsOpenForDepartment { get; init; }
    public bool IsCompletedForDepartment { get; init; }
    public DateTime? DepartmentCompletionDate { get; init; }
    public DateTime? DepartmentDueDate { get; init; }
    public bool IsOpenOverdueForDepartment { get; init; }
    public bool IsOverdueForDepartment { get; init; }
    public bool IsCompletedLateForDepartment { get; init; }
    public bool IsOnTimeForDepartment { get; init; }
    public bool IsTimelinessEligible { get; init; }
    public int RepliedAssignmentCount { get; init; }
    public int PendingReplyAssignmentCount { get; init; }
    public bool IsPartialReplyForDepartment { get; init; }
}

internal sealed record DepartmentTransactionPerformanceObservation(
    TransactionReportSnapshot Snapshot,
    DepartmentTransactionPerformanceState State);
