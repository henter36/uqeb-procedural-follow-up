using Uqeb.Api.Models.Entities;
using Uqeb.Api.Models.Enums;

namespace Uqeb.Api.Reporting.Services;

/// <summary>
/// SQL-translatable overdue filter aligned with <see cref="InstitutionalReportMetricsCalculator.IsOverdue"/>.
/// Overdue begins when due date is strictly before <paramref name="today"/> (not on the due date itself).
/// </summary>
internal static class InstitutionalReportOverdueQuery
{
    internal static IQueryable<Transaction> ApplyOverdueFilter(IQueryable<Transaction> query, DateTime today)
    {
        var todayDate = today.Date;

        return query.Where(t =>
            t.Status != TransactionStatus.Closed
            && t.Status != TransactionStatus.Cancelled
            && t.Status != TransactionStatus.Archived
            && (
                t.Status == TransactionStatus.Overdue
                || (t.ResponseDueDate.HasValue
                    && t.ResponseDueDate.Value.Date < todayDate
                    && !t.ResponseCompleted)
                || (
                    t.Assignments.Any(a =>
                        a.Status == AssignmentStatus.Active
                        && a.RequiresReply
                        && a.ReplyStatus != ReplyStatus.Replied)
                    && t.Assignments
                        .Where(a => a.Status == AssignmentStatus.Active && a.DueDate.HasValue)
                        .Min(a => a.DueDate)!.Value.Date < todayDate)
            ));
    }
}
