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
        && a.DueDate.HasValue && a.DueDate.Value < now;

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
        if (a.RequiresReply && a.ReplyStatus == ReplyStatus.Pending && a.DueDate < now)
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
}
