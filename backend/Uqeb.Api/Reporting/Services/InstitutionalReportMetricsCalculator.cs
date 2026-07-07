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

        var overdueOpen = open.Count(s => IsOpenOverdue(s, today));
        var completedLate = unique.Count(s =>
            !TransactionStatusSemantics.IsCancelled(s.Status)
            && !TransactionStatusSemantics.IsArchived(s.Status)
            && !IsOpenOverdue(s, today)
            && IsCompletedLate(s));
        var overdueTotal = unique.Count(s => IsOverdue(s, today));
        var joint = unique.Count(s => s.IsJointDepartment);
        var partial = open.Count(s => s.IsPartialReply);
        // Open, not yet ResponseCompleted, but every required department referral has replied —
        // procedurally done, sitting only on the final response approval/registration.
        var pendingAdminReview = open.Count(s => !s.ResponseCompleted && s.IsProcedurallyCompleteForReporting);

        // Department performance (completion days / on-time rate) is measured against when the
        // departments actually finished (ProceduralCompletionDateForReporting), not when a
        // supervisor eventually got around to closing the transaction — otherwise a slow final
        // approval would unfairly drag down department metrics for work that was done on time.
        var completionDays = closed
            .Select(s => (s.IncomingDate, CompletionDate: s.ProceduralCompletionDateForReporting ?? s.ClosedAt))
            .Where(x => x.CompletionDate.HasValue)
            .Select(x => Math.Max(0, (x.CompletionDate!.Value.Date - x.IncomingDate.Date).Days))
            .ToList();

        var averageCompletion = completionDays.Count == 0
            ? 0
            : Math.Round(completionDays.Average(), 1);

        var measurableClosed = closed
            .Where(s => s.ResponseDueDate.HasValue && (s.ProceduralCompletionDateForReporting ?? s.ClosedAt).HasValue)
            .ToList();

        var onTimeClosed = measurableClosed.Count(s =>
            (s.ProceduralCompletionDateForReporting ?? s.ClosedAt)!.Value.Date <= s.ResponseDueDate!.Value.Date);
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
            OverdueCount = overdueTotal,
            OpenOverdueCount = overdueOpen,
            CompletedLateCount = completedLate,
            JointDepartmentCount = joint,
            PartialResponseCount = partial,
            PendingAdministrativeReviewCount = pendingAdminReview,
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
            || metrics.StaleUpdates >= criteria.CriticalStaleUpdateCountThreshold)
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
        if (TransactionStatusSemantics.IsCancelled(snapshot.Status) ||
            TransactionStatusSemantics.IsArchived(snapshot.Status))
            return false;

        return IsOpenOverdue(snapshot, today) || IsCompletedLate(snapshot);
    }

    public static bool IsOpenOverdue(TransactionReportSnapshot snapshot, DateTime today)
    {
        if (!snapshot.IsOpen)
            return false;

        // Departments finished their part (or the transaction never had referrals and the
        // response was registered) — a supervisor's still-pending final review must not keep
        // this counted as an open-and-overdue item forever. IsCompletedLate below decides
        // whether it should instead be flagged as completed-late.
        if (snapshot.IsProcedurallyCompleteForReporting)
            return false;

        if (snapshot.ResponseDueDate.HasValue && snapshot.ResponseDueDate.Value.Date < today.Date && !snapshot.ResponseCompleted)
            return true;

        if (snapshot.EarliestPendingReplyDueDate.HasValue
            && snapshot.EarliestPendingReplyDueDate.Value.Date < today.Date)
            return true;

        return snapshot.Status == TransactionStatus.Overdue;
    }

    public static bool IsCompletedLate(TransactionReportSnapshot snapshot)
    {
        if (!snapshot.ResponseDueDate.HasValue)
            return false;

        // Must match Calculate's AverageCompletionDays/OnTimeCompletionRate precedence
        // (procedural date first) — otherwise the same transaction could be counted as
        // "on time" in the rate but "completed late" here whenever a late administrative
        // ClosedAt would otherwise take priority over an on-time procedural completion.
        var completionDate = snapshot.ProceduralCompletionDateForReporting?.Date
            ?? (snapshot.ResponseCompleted ? snapshot.ResponseCompletedDate?.Date : null)
            ?? snapshot.ClosedAt?.Date;

        return completionDate.HasValue && completionDate.Value.Date > snapshot.ResponseDueDate.Value.Date;
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
        if (snapshot.IsProcedurallyCompleteForReporting && !snapshot.ResponseCompleted)
            stages.Add(FollowUpStage.PendingAdministrativeReview);
        if (IsWaitingForStatement(snapshot))
            stages.Add(FollowUpStage.WaitingForStatement);
        if (snapshot.Status == TransactionStatus.WaitingForReply || snapshot.PendingReplyAssignmentCount > 0)
            stages.Add(FollowUpStage.WaitingForDepartmentReply);
        if (IsPartialReply(snapshot))
            stages.Add(FollowUpStage.PartialReply);
        if (snapshot.Status is TransactionStatus.New or TransactionStatus.InProgress or TransactionStatus.Assigned)
            stages.Add(FollowUpStage.UnderProcessing);
        if (IsOverdue(snapshot, today))
            stages.Add(snapshot.IsCompletedLate ? FollowUpStage.CompletedLate : FollowUpStage.Overdue);
        return stages.Distinct().ToList();
    }

    public static string FollowUpStageLabel(FollowUpStage stage) => stage switch
    {
        FollowUpStage.WaitingForStatement => "بانتظار إفادة",
        FollowUpStage.WaitingForDepartmentReply => "بانتظار رد إدارة",
        FollowUpStage.PartialReply => "رد جزئي",
        FollowUpStage.UnderProcessing => "تحت الإجراء",
        FollowUpStage.Overdue => "متأخرة",
        FollowUpStage.CompletedLate => "منجزة متأخرة",
        FollowUpStage.PendingAdministrativeReview => "بانتظار مراجعة إدارية",
        _ => "—"
    };
}
