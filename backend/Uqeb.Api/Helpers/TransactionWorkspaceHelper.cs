using Uqeb.Api.DTOs.Transactions;
using Uqeb.Api.Models.Entities;
using Uqeb.Api.Models.Enums;
using Uqeb.Api.Services;

namespace Uqeb.Api.Helpers;

public static class TransactionWorkspaceHelper
{
    public static TransactionTemporalFactsDto BuildTemporalFacts(
        Transaction transaction,
        IReadOnlyList<AssignmentDto> assignments,
        DateTime now)
    {
        var assignmentFacts = assignments
            .Select(a => new TransactionTemporalCalculator.AssignmentSummaryFacts(
                ParseReplyStatusOrDefault(a.ReplyStatus),
                a.RequiresReply,
                ParseAssignmentStatusOrDefault(a.Status),
                a.DueDate))
            .ToList();

        DateTime? earliestPendingDue = assignmentFacts
            .Where(a => a.RequiresReply
                && a.ReplyStatus != ReplyStatus.Replied
                && a.Status == AssignmentStatus.Active)
            .Select(a => a.DueDate)
            .Min();

        return new TransactionTemporalFactsDto
        {
            IsOpen = TransactionTemporalCalculator.IsOpen(transaction),
            IsResponseOverdue = TransactionTemporalCalculator.IsResponseOverdue(transaction, now),
            IsOverdue = TransactionTemporalCalculator.IsOverdue(transaction, assignmentFacts, now),
            AgeDays = TransactionTemporalCalculator.AgeDays(transaction, now),
            DaysOverdue = TransactionTemporalCalculator.DaysOverdue(transaction, now, earliestPendingDue),
            CompletionDays = TransactionTemporalCalculator.CompletionDays(transaction)
        };
    }

    public static TransactionWorkspaceAllowedActionsDto BuildAllowedActions(
        TransactionDetailDto transaction,
        ICurrentUserService user)
    {
        var canEdit = user.Role is UserRole.Admin or UserRole.Supervisor or UserRole.DataEntry;
        var canClose = user.Role is UserRole.Admin or UserRole.Supervisor;
        var isDepartmentUser = user.Role == UserRole.DepartmentUser;
        var needsResponse = transaction.RequiresResponse
            || transaction.ResponseType != ResponseType.None.ToString();
        var isTerminal = transaction.Status is nameof(TransactionStatus.Closed)
            or nameof(TransactionStatus.Cancelled)
            or nameof(TransactionStatus.Archived);

        return new TransactionWorkspaceAllowedActionsDto
        {
            CanEdit = canEdit,
            CanClose = canClose,
            IsDepartmentUser = isDepartmentUser,
            CanRegisterResponse = canClose && needsResponse && !transaction.ResponseCompleted && !isTerminal,
            CanShowClose = canClose && !isTerminal && (!needsResponse || transaction.ResponseCompleted),
            ShowMutationActions = canEdit && !isDepartmentUser,
            CanReply = canEdit && !isDepartmentUser,
            HasPendingDepartments = transaction.PendingDepartmentNames.Count > 0
        };
    }

    private static ReplyStatus ParseReplyStatusOrDefault(string? value) =>
        Enum.TryParse<ReplyStatus>(value, ignoreCase: true, out var status)
            ? status
            : ReplyStatus.Pending;

    private static AssignmentStatus ParseAssignmentStatusOrDefault(string? value) =>
        Enum.TryParse<AssignmentStatus>(value, ignoreCase: true, out var status)
            ? status
            : AssignmentStatus.Active;
}
