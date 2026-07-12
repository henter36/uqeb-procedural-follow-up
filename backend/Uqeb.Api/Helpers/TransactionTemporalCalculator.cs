using Uqeb.Api.Models.Entities;
using Uqeb.Api.Models.Enums;

namespace Uqeb.Api.Helpers;

public static class TransactionTemporalCalculator
{
    public static bool IsOpen(TransactionStatus status) =>
        status is not TransactionStatus.Closed
            and not TransactionStatus.Cancelled
            and not TransactionStatus.Archived;

    public static bool IsOpen(Transaction transaction) => IsOpen(transaction.Status);

    public static bool IsResponseOverdue(
        Transaction transaction,
        DateTime referenceDate,
        DateTime? proceduralCompletionDateForReporting = null)
    {
        if (!transaction.RequiresResponse || !transaction.ResponseDueDate.HasValue)
            return false;

        var comparisonDate = ResolveAuthoritativeCompletionDate(transaction, proceduralCompletionDateForReporting)
            ?? referenceDate.Date;
        return comparisonDate.Date > transaction.ResponseDueDate.Value.Date;
    }

    public static DateTime? ResolveAuthoritativeCompletionDate(
        Transaction transaction,
        DateTime? proceduralCompletionDateForReporting = null) =>
        proceduralCompletionDateForReporting?.Date
        ?? (transaction.ResponseCompleted ? transaction.ResponseCompletedDate?.Date : null)
        ?? transaction.ClosedAt?.Date;

    public static bool IsAssignmentOverdue(
        ReplyStatus replyStatus,
        bool requiresReply,
        AssignmentStatus status,
        DateTime? dueDate,
        DateTime referenceDate) =>
        requiresReply
        && replyStatus != ReplyStatus.Replied
        && status == AssignmentStatus.Active
        && dueDate.HasValue
        && dueDate.Value.Date < referenceDate.Date;

    public static bool IsAssignmentOverdue(Assignment assignment, DateTime referenceDate) =>
        IsAssignmentOverdue(
            assignment.ReplyStatus,
            assignment.RequiresReply,
            assignment.Status,
            assignment.DueDate,
            referenceDate);

    public static bool IsOverdue(Transaction transaction, DateTime referenceDate) =>
        IsResponseOverdue(transaction, referenceDate)
        || transaction.Assignments.Any(a => IsAssignmentOverdue(a, referenceDate));

    public static bool IsOverdue(
        Transaction transaction,
        IEnumerable<AssignmentSummaryFacts> assignmentRows,
        DateTime referenceDate,
        DateTime? proceduralCompletionDateForReporting = null) =>
        IsResponseOverdue(transaction, referenceDate, proceduralCompletionDateForReporting)
        || assignmentRows.Any(a => IsAssignmentOverdue(
            a.ReplyStatus,
            a.RequiresReply,
            a.Status,
            a.DueDate,
            referenceDate));

    public static int AgeDays(Transaction transaction, DateTime referenceDate) =>
        Math.Max(0, (referenceDate.Date - transaction.IncomingDate.Date).Days);

    public static int? DaysOverdue(
        Transaction transaction,
        DateTime referenceDate,
        DateTime? earliestPendingAssignmentDueDate = null,
        DateTime? proceduralCompletionDateForReporting = null)
    {
        var dueDates = new List<DateTime>();
        if (transaction.RequiresResponse && transaction.ResponseDueDate.HasValue)
            dueDates.Add(transaction.ResponseDueDate.Value.Date);
        if (earliestPendingAssignmentDueDate.HasValue)
            dueDates.Add(earliestPendingAssignmentDueDate.Value.Date);

        if (dueDates.Count == 0)
            return null;

        var earliestDue = dueDates.Min();
        var comparisonDate = ResolveAuthoritativeCompletionDate(transaction, proceduralCompletionDateForReporting)
            ?? referenceDate.Date;
        if (comparisonDate.Date <= earliestDue)
            return null;

        return Math.Max(0, (comparisonDate.Date - earliestDue).Days);
    }

    public static bool IsStale(
        Transaction transaction,
        DateTime referenceDate,
        int staleDays,
        DateTime? lastFollowUpDate = null)
    {
        var lastAction = new[] { transaction.UpdatedAt, lastFollowUpDate, transaction.ClosedAt }
            .Where(d => d.HasValue)
            .Select(d => d!.Value.Date)
            .DefaultIfEmpty(transaction.CreatedAt.Date)
            .Max();
        return Math.Max(0, (referenceDate.Date - lastAction).Days) >= staleDays;
    }

    public static int? CompletionDays(Transaction transaction) =>
        transaction.Status == TransactionStatus.Closed && transaction.ClosedAt.HasValue
            ? Math.Max(0, (transaction.ClosedAt.Value.Date - transaction.IncomingDate.Date).Days)
            : null;

    public readonly record struct AssignmentSummaryFacts(
        ReplyStatus ReplyStatus,
        bool RequiresReply,
        AssignmentStatus Status,
        DateTime? DueDate);
}
