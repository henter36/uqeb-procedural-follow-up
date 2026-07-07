using Uqeb.Api.Models.Entities;
using Uqeb.Api.Models.Enums;

namespace Uqeb.Api.Helpers;

public static class WorkflowHelper
{
    public static DateTime? CalculateResponseDueDate(DateTime incomingDate, int? responseDueDays) =>
        responseDueDays.HasValue && responseDueDays > 0
            ? incomingDate.Date.AddDays(responseDueDays.Value)
            : null;

    public static int? CalculateResponseDueDays(DateTime incomingDate, DateTime? responseDueDate) =>
        responseDueDate.HasValue
            ? (responseDueDate.Value.Date - incomingDate.Date).Days
            : null;

    public static DateTime? CalculateAssignmentDueDate(DateTime assignedDate, int? replyDueDays, DateTime? explicitDueDate)
    {
        if (replyDueDays.HasValue && replyDueDays > 0)
            return assignedDate.Date.AddDays(replyDueDays.Value);
        return explicitDueDate;
    }

    public static int? CalculateAssignmentDueDays(DateTime assignedDate, DateTime? dueDate) =>
        dueDate.HasValue
            ? (dueDate.Value.Date - assignedDate.Date).Days
            : null;

    public static bool IsAssignmentOverdue(Assignment a, DateTime now) =>
        a.RequiresReply && a.ReplyStatus != ReplyStatus.Replied && a.Status == AssignmentStatus.Active
        && a.DueDate.HasValue && a.DueDate.Value.Date < now.Date;

    public static DateTime? ResolveResponseCompletionDate(Transaction t)
    {
        if (t.ClosedAt.HasValue)
            return t.ClosedAt.Value.Date;
        if ((t.ResponseCompleted || t.Status is TransactionStatus.ResponseCompleted or TransactionStatus.Closed)
            && t.ResponseCompletedDate.HasValue)
            return t.ResponseCompletedDate.Value.Date;
        return null;
    }

    public static bool IsResponseOverdue(Transaction t, DateTime now)
    {
        if (!t.RequiresResponse || !t.ResponseDueDate.HasValue)
            return false;

        var comparisonDate = ResolveResponseCompletionDate(t) ?? now.Date;
        return comparisonDate.Date > t.ResponseDueDate.Value.Date;
    }

    public static bool IsTransactionOverdue(Transaction t, DateTime now) =>
        IsResponseOverdue(t, now) || t.Assignments.Any(a => IsAssignmentOverdue(a, now));

    public static void UpdateAssignmentOverdueStatus(Assignment a, DateTime now)
    {
        if (a.RequiresReply && a.ReplyStatus == ReplyStatus.Pending && a.DueDate.HasValue && a.DueDate.Value.Date < now.Date)
            a.ReplyStatus = ReplyStatus.Overdue;
    }

    public static void UpdateTransactionStatusFromAssignments(Transaction t)
    {
        if (t.ResponseCompleted
            || t.Status is TransactionStatus.Closed or TransactionStatus.Cancelled or TransactionStatus.Archived)
            return;

        var requiringReply = t.Assignments.Where(a => a.RequiresReply).ToList();
        if (requiringReply.Count == 0) return;

        var pending = requiringReply.Where(a => a.ReplyStatus != ReplyStatus.Replied).ToList();
        var replied = requiringReply.Where(a => a.ReplyStatus == ReplyStatus.Replied).ToList();

        if (pending.Count == 0)
        {
            t.Status = t.RequiresResponse ? TransactionStatus.ReadyForResponse : TransactionStatus.InProgress;
        }
        else if (replied.Count > 0)
        {
            t.Status = TransactionStatus.PartiallyReplied;
        }
        else if (pending.Any(a => a.ReplyStatus == ReplyStatus.Overdue))
        {
            t.Status = TransactionStatus.Overdue;
        }
        else
        {
            t.Status = TransactionStatus.WaitingForReply;
        }
    }

    public static void RecalculateResponseDueDate(Transaction t)
    {
        t.ResponseDueDate = t.RequiresResponse
            ? CalculateResponseDueDate(t.IncomingDate, t.ResponseDueDays)
            : null;
    }

    /// <summary>
    /// A single required assignment's reply state, reduced to just the two facts needed to
    /// resolve procedural completion. Exists so the same resolution rule can be shared between
    /// the full <see cref="Transaction"/> entity (workspace/mutation layer) and the flattened
    /// snapshot rows used by the institutional report query (which never load full entities).
    /// </summary>
    public readonly record struct RequiredReplySignal(bool IsReplied, DateTime? ReplyDate);

    /// <summary>
    /// Resolves the date operational reporting should treat as "when this transaction's
    /// response was effectively completed", independent of whether it has been formally closed
    /// or had its final response registered yet.
    /// Rules: no required-reply items (department referrals) → fall back to the manual
    /// ResponseCompletedDate; some required items still unreplied → null (not complete); all
    /// required items replied → the latest of their reply dates.
    /// </summary>
    public static DateTime? ResolveProceduralCompletionDateFromRequiredReplies(
        IReadOnlyList<RequiredReplySignal> requiredReplySignals,
        DateTime? manualResponseCompletedDate)
    {
        if (requiredReplySignals.Count == 0)
            return manualResponseCompletedDate;
        if (requiredReplySignals.Any(s => !s.IsReplied || !s.ReplyDate.HasValue))
            return null;
        return requiredReplySignals.Max(s => s.ReplyDate!.Value);
    }

    /// <summary>
    /// Shape-agnostic extraction of the "required, non-cancelled referral" rule shared by the
    /// Transaction entity (workspace/mutation layer), TransactionService's flattened
    /// AssignmentSummaryRow projection, and the institutional report's AssignmentRow snapshot
    /// row — three different row shapes that all carry the same four facts.
    /// </summary>
    public static IReadOnlyList<RequiredReplySignal> BuildRequiredReplySignals<T>(
        IEnumerable<T> rows,
        Func<T, bool> requiresReply,
        Func<T, AssignmentStatus> status,
        Func<T, ReplyStatus> replyStatus,
        Func<T, DateTime?> replyDate) =>
        rows
            .Where(row => requiresReply(row) && status(row) != AssignmentStatus.Cancelled)
            .Select(row => new RequiredReplySignal(replyStatus(row) == ReplyStatus.Replied, replyDate(row)))
            .ToList();

    private static IReadOnlyList<RequiredReplySignal> RequiredReplySignalsFor(Transaction t) =>
        BuildRequiredReplySignals(t.Assignments, a => a.RequiresReply, a => a.Status, a => a.ReplyStatus, a => a.ReplyDate);

    /// <summary>
    /// Entity-typed convenience wrapper over <see cref="ResolveProceduralCompletionDateFromRequiredReplies"/>
    /// for callers that already have a Transaction with its Assignments loaded (e.g. the
    /// workspace/mutation layer). Never sets ClosedAt/Status/ResponseCompleted — this is purely a
    /// reporting-facing read, the transaction stays open and editable regardless of the result.
    /// </summary>
    public static DateTime? ResolveProceduralCompletionDateForReporting(Transaction t) =>
        ResolveProceduralCompletionDateFromRequiredReplies(RequiredReplySignalsFor(t), t.ResponseCompletedDate);

    /// <summary>
    /// True only when the transaction actually has department referrals requiring a reply and
    /// every one of them has been replied to — i.e. procedural completion was driven by
    /// department assignments, not just a manually-entered ResponseCompletedDate with no
    /// referrals at all.
    /// </summary>
    public static bool IsProcedurallyCompleteForReporting(Transaction t)
    {
        var signals = RequiredReplySignalsFor(t);
        return signals.Count > 0 && ResolveProceduralCompletionDateFromRequiredReplies(signals, t.ResponseCompletedDate).HasValue;
    }
}
