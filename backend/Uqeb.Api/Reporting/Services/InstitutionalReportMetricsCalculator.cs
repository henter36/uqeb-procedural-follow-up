using Uqeb.Api.Models.Enums;
using Uqeb.Api.Reporting.Configuration;
using Uqeb.Api.Reporting.Enums;
using Uqeb.Api.Reporting.Models;

namespace Uqeb.Api.Reporting.Services;

public static class InstitutionalReportMetricsCalculator
{
    public static InstitutionalMetricsResult Calculate(
        IReadOnlyList<TransactionReportSnapshot> snapshots,
        DateTime today)
    {
        var unique = snapshots
            .GroupBy(s => s.TransactionId)
            .Select(g => g.First())
            .ToList();

        var closed = unique.Where(s => s.IsClosed).ToList();
        var open = unique.Where(s => s.IsOpen).ToList();
        var cancelled = unique.Count(s => TransactionStatusSemantics.IsCancelled(s.Status));
        var archived = unique.Count(s => TransactionStatusSemantics.IsArchived(s.Status));

        var overdueOpen = open.Count(s => s.IsOverdue);
        var joint = unique.Count(s => s.IsJointDepartment);
        var partial = open.Count(s => s.IsPartialReply);

        var completionDays = closed
            .Where(s => s.ClosedAt.HasValue)
            .Select(s => Math.Max(0, (s.ClosedAt!.Value.Date - s.IncomingDate.Date).Days))
            .ToList();

        var averageCompletion = completionDays.Count == 0
            ? 0
            : Math.Round(completionDays.Average(), 1);

        var measurableClosed = closed
            .Where(s => s.ResponseDueDate.HasValue && s.ClosedAt.HasValue)
            .ToList();

        var onTimeClosed = measurableClosed.Count(s => s.ClosedAt!.Value.Date <= s.ResponseDueDate!.Value.Date);
        var onTimeRate = measurableClosed.Count == 0
            ? 0
            : Math.Round(onTimeClosed * 100.0 / measurableClosed.Count, 1);

        return new InstitutionalMetricsResult
        {
            TotalTransactions = unique.Count,
            ClosedCount = closed.Count,
            OpenCount = open.Count,
            CancelledCount = cancelled,
            ArchivedCount = archived,
            OverdueCount = overdueOpen,
            JointDepartmentCount = joint,
            PartialResponseCount = partial,
            AverageCompletionDays = averageCompletion,
            OnTimeCompletionRate = onTimeRate,
            Snapshots = unique
        };
    }

    public static DepartmentRatingLevel RateDepartment(
        DepartmentPerformanceMetrics metrics,
        DepartmentRatingCriteria criteria)
    {
        if (metrics.OnTimeCompletionRate < criteria.NeedsFollowUpOnTimeRateThreshold
            || metrics.OverdueCount >= criteria.CriticalOverdueCountThreshold
            || metrics.OldestOpenDays >= criteria.CriticalOldestOpenDaysThreshold
            || metrics.PartialResponses >= criteria.CriticalPartialResponsesThreshold
            || metrics.StaleUpdates >= criteria.CriticalStaleUpdateDaysThreshold)
        {
            return DepartmentRatingLevel.Critical;
        }

        if (metrics.OnTimeCompletionRate < criteria.GoodOnTimeRateThreshold)
            return DepartmentRatingLevel.NeedsFollowUp;

        return DepartmentRatingLevel.Good;
    }

    public static string RatingLabel(DepartmentRatingLevel level) => level switch
    {
        DepartmentRatingLevel.Good => "جيد",
        DepartmentRatingLevel.NeedsFollowUp => "يحتاج متابعة",
        DepartmentRatingLevel.Critical => "حرج",
        _ => "—"
    };

    public sealed class DepartmentPerformanceMetrics
    {
        public double OnTimeCompletionRate { get; init; }
        public int OverdueCount { get; init; }
        public int OldestOpenDays { get; init; }
        public int PartialResponses { get; init; }
        public int StaleUpdates { get; init; }
    }

    public static bool IsOpenStatus(TransactionStatus status) =>
        TransactionStatusSemantics.IsOperationalOpen(status);

    public static bool IsOverdue(TransactionReportSnapshot snapshot, DateTime today)
    {
        if (!snapshot.IsOpen)
            return false;

        if (snapshot.ResponseDueDate.HasValue && snapshot.ResponseDueDate.Value.Date < today.Date && !snapshot.ResponseCompleted)
            return true;

        if (snapshot.LastAssignmentDueDate.HasValue && snapshot.LastAssignmentDueDate.Value.Date < today.Date
            && snapshot.PendingReplyAssignmentCount > 0)
            return true;

        return snapshot.Status == TransactionStatus.Overdue;
    }

    public static bool IsJointDepartment(TransactionReportSnapshot snapshot) =>
        snapshot.ActiveAssignmentCount > 1
        || snapshot.AssignmentDepartmentIds.Distinct().Count() > 1
        || snapshot.OutgoingDepartmentIds.Distinct().Count() > 1;

    public static bool IsWaitingForStatement(TransactionReportSnapshot snapshot) =>
        snapshot.RequiresResponse && !snapshot.ResponseCompleted
        && snapshot.Status is TransactionStatus.ReadyForResponse
            or TransactionStatus.WaitingForReply
            or TransactionStatus.PartiallyReplied;

    public static bool IsPartialReply(TransactionReportSnapshot snapshot) =>
        snapshot.Status == TransactionStatus.PartiallyReplied
        || (snapshot.ActiveAssignmentCount > 1
            && snapshot.RepliedAssignmentCount > 0
            && snapshot.PendingReplyAssignmentCount > 0);

    public static List<FollowUpStage> ResolveFollowUpStages(TransactionReportSnapshot snapshot, DateTime today)
    {
        var stages = new List<FollowUpStage>();
        if (IsWaitingForStatement(snapshot))
            stages.Add(FollowUpStage.WaitingForStatement);
        if (snapshot.Status == TransactionStatus.WaitingForReply || snapshot.PendingReplyAssignmentCount > 0)
            stages.Add(FollowUpStage.WaitingForDepartmentReply);
        if (IsPartialReply(snapshot))
            stages.Add(FollowUpStage.PartialReply);
        if (snapshot.Status is TransactionStatus.New or TransactionStatus.InProgress or TransactionStatus.Assigned)
            stages.Add(FollowUpStage.UnderProcessing);
        if (IsOverdue(snapshot, today))
            stages.Add(FollowUpStage.Overdue);
        return stages.Distinct().ToList();
    }

    public static string FollowUpStageLabel(FollowUpStage stage) => stage switch
    {
        FollowUpStage.WaitingForStatement => "بانتظار إفادة",
        FollowUpStage.WaitingForDepartmentReply => "بانتظار رد إدارة",
        FollowUpStage.PartialReply => "رد جزئي",
        FollowUpStage.UnderProcessing => "تحت الإجراء",
        FollowUpStage.Overdue => "متأخرة",
        _ => "—"
    };
}
