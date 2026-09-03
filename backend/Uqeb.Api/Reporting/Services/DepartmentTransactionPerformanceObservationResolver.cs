using Uqeb.Api.Reporting.Models;
using Uqeb.Api.Models.Enums;

namespace Uqeb.Api.Reporting.Services;

internal static class DepartmentTransactionPerformanceObservationResolver
{
    /// <summary>
    /// Resolves the single reporting state for one transaction/department assignment relationship.
    /// Both institutional reports and the department transaction workspace call this method so the
    /// assignment completion/timeliness rules have one implementation.
    /// </summary>
    internal static DepartmentTransactionPerformanceState ResolveState(
        int transactionId,
        IEnumerable<InstitutionalReportSnapshotQuery.AssignmentRow> departmentAssignments,
        DateTime evaluationDate)
    {
        var assignments = departmentAssignments
            .OrderByDescending(assignment => assignment.Status == AssignmentStatus.Active)
            .ThenByDescending(assignment => assignment.AssignedDate)
            .ThenByDescending(assignment => assignment.CreatedAt)
            .ThenByDescending(assignment => assignment.Id)
            .ToList();

        if (assignments.Count == 0)
            throw new ArgumentException("At least one assignment is required.", nameof(departmentAssignments));

        var requiredReplies = assignments.Where(assignment => assignment.RequiresReply).ToList();

        static bool IsCompletedRequiredReply(InstitutionalReportSnapshotQuery.AssignmentRow assignment) =>
            assignment.ReplyStatus == ReplyStatus.Replied && assignment.ReplyDate.HasValue;

        var completedRequiredReplies = requiredReplies.Where(IsCompletedRequiredReply).ToList();
        var pendingRequiredReplies = requiredReplies.Where(assignment => !IsCompletedRequiredReply(assignment)).ToList();
        // A referral that explicitly requires no reply carries no outstanding response obligation.
        // It is therefore complete for departmental response-performance counting, but has no
        // measurable completion date and is excluded from timeliness rates.
        var isCompleted = pendingRequiredReplies.Count == 0;
        var completionDate = isCompleted && completedRequiredReplies.Count > 0
            ? completedRequiredReplies.Max(assignment => assignment.ReplyDate!.Value.Date)
            : (DateTime?)null;
        var dueAssignments = requiredReplies.Where(assignment => assignment.DueDate.HasValue).ToList();
        var departmentDueDate = dueAssignments.Count > 0
            ? dueAssignments.Min(assignment => assignment.DueDate!.Value.Date)
            : (DateTime?)null;
        var isOpenOverdue = !isCompleted && pendingRequiredReplies.Any(assignment =>
            assignment.DueDate.HasValue && assignment.DueDate.Value.Date < evaluationDate.Date);
        var hasLateCompletedAssignment = completedRequiredReplies.Any(assignment =>
            assignment.DueDate.HasValue
            && assignment.ReplyDate!.Value.Date > assignment.DueDate.Value.Date);
        var isCompletedLate = isCompleted && hasLateCompletedAssignment;
        var isOnTime = isCompleted
            && dueAssignments.Count > 0
            && dueAssignments.All(assignment => assignment.ReplyDate!.Value.Date <= assignment.DueDate!.Value.Date);
        var isOverdue = isOpenOverdue || hasLateCompletedAssignment;

        return new DepartmentTransactionPerformanceState
        {
            DepartmentId = assignments[0].DepartmentId,
            DepartmentName = assignments[0].DepartmentName,
            TransactionId = transactionId,
            IsOpenForDepartment = !isCompleted,
            IsCompletedForDepartment = isCompleted,
            DepartmentCompletionDate = completionDate,
            DepartmentDueDate = departmentDueDate,
            IsOpenOverdueForDepartment = isOpenOverdue,
            IsOverdueForDepartment = isOverdue,
            IsCompletedLateForDepartment = isCompletedLate,
            IsOnTimeForDepartment = isOnTime,
            IsTimelinessEligible = isOverdue || (isCompleted && dueAssignments.Count > 0),
            RepliedAssignmentCount = completedRequiredReplies.Count,
            PendingReplyAssignmentCount = pendingRequiredReplies.Count,
            IsPartialReplyForDepartment = completedRequiredReplies.Count > 0 && pendingRequiredReplies.Count > 0,
        };
    }

    internal static string StatusLabel(DepartmentTransactionPerformanceState state)
    {
        if (state.IsOpenOverdueForDepartment)
            return "مفتوحة متأخرة";
        if (state.IsOpenForDepartment)
            return "مفتوحة";
        if (state.IsCompletedLateForDepartment)
            return "منجزة متأخرة";
        if (state.IsOnTimeForDepartment)
            return "منجزة ضمن المهلة";
        if (state.IsCompletedForDepartment)
            return "منجزة — دون مهلة قابلة للقياس";
        return "—";
    }

    internal static string ResponseStateLabel(DepartmentTransactionPerformanceState state) =>
        state.IsCompletedForDepartment ? "مكتمل" : "بانتظار";

    /// <summary>
    /// Expands each snapshot into its participating departments. The fallback only supports
    /// legacy/manually-built snapshots that predate assignment-level state; query-built snapshots
    /// always carry exact assignment states and never use transaction closure for a department.
    /// </summary>
    public static List<DepartmentTransactionPerformanceObservation> Expand(
        IEnumerable<TransactionReportSnapshot> snapshots)
    {
        var result = new List<DepartmentTransactionPerformanceObservation>();
        foreach (var snapshot in snapshots)
        {
            if (snapshot.DepartmentPerformanceStates.Count > 0)
            {
                result.AddRange(snapshot.DepartmentPerformanceStates.Select(state => new DepartmentTransactionPerformanceObservation(snapshot, state)));
                continue;
            }

            if (!snapshot.ResponsibleDepartmentId.HasValue
                || ReportDepartmentNameNormalizer.IsUndefined(snapshot.ResponsibleDepartment))
            {
                continue;
            }

            var completionDate = snapshot.ProceduralCompletionDateForReporting?.Date
                ?? (snapshot.ResponseCompleted ? snapshot.ResponseCompletedDate?.Date : null)
                ?? snapshot.ClosedAt?.Date;
            var isOnTime = snapshot.IsClosed
                && completionDate.HasValue
                && snapshot.ResponseDueDate.HasValue
                && completionDate.Value.Date <= snapshot.ResponseDueDate.Value.Date;
            result.Add(new DepartmentTransactionPerformanceObservation(
                snapshot,
                new DepartmentTransactionPerformanceState
                {
                    DepartmentId = snapshot.ResponsibleDepartmentId.Value,
                    DepartmentName = snapshot.ResponsibleDepartment,
                    TransactionId = snapshot.TransactionId,
                    IsOpenForDepartment = snapshot.IsOpen,
                    IsCompletedForDepartment = snapshot.IsClosed,
                    DepartmentCompletionDate = completionDate,
                    DepartmentDueDate = snapshot.ResponseDueDate,
                    IsOpenOverdueForDepartment = snapshot.IsOpenOverdue,
                    IsOverdueForDepartment = snapshot.IsOverdue,
                    IsCompletedLateForDepartment = snapshot.IsCompletedLate,
                    IsOnTimeForDepartment = isOnTime,
                    IsTimelinessEligible = snapshot.IsOpenOverdue
                        || (snapshot.IsClosed && completionDate.HasValue && snapshot.ResponseDueDate.HasValue),
                    RepliedAssignmentCount = snapshot.RepliedAssignmentCount,
                    PendingReplyAssignmentCount = snapshot.PendingReplyAssignmentCount,
                    IsPartialReplyForDepartment = snapshot.IsPartialReply,
                }));
        }

        return result;
    }

    public static List<int> CompletionDays(IEnumerable<DepartmentTransactionPerformanceObservation> observations) =>
        observations
            .Where(observation => observation.State.IsCompletedForDepartment
                && observation.State.DepartmentCompletionDate.HasValue)
            .Select(observation =>
                (observation.State.DepartmentCompletionDate!.Value.Date - observation.Snapshot.IncomingDate.Date).Days)
            .Where(days => days >= 0)
            .ToList();

    public static double OnTimeRate(IEnumerable<DepartmentTransactionPerformanceObservation> observations)
    {
        var eligible = observations.Where(observation => observation.State.IsTimelinessEligible).ToList();
        return eligible.Count == 0
            ? 0
            : Math.Round(eligible.Count(observation => observation.State.IsOnTimeForDepartment) * 100.0 / eligible.Count, 1);
    }

    public static double OverdueRate(IEnumerable<DepartmentTransactionPerformanceObservation> observations)
    {
        var eligible = observations.Where(observation => observation.State.IsTimelinessEligible).ToList();
        return eligible.Count == 0
            ? 0
            : Math.Round(eligible.Count(observation => observation.State.IsOverdueForDepartment) * 100.0 / eligible.Count, 1);
    }
}
