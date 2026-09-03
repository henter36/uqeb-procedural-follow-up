using Uqeb.Api.Reporting.Models;

namespace Uqeb.Api.Reporting.Services;

internal static class DepartmentTransactionPerformanceObservationResolver
{
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
