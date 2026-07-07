using Uqeb.Api.Helpers;
using Uqeb.Api.Models.Entities;
using Uqeb.Api.Models.Enums;
using Xunit;

namespace Uqeb.Api.Tests;

public class WorkflowHelperProceduralCompletionTests
{
    private static Transaction BuildTransaction(DateTime? manualResponseCompletedDate = null) => new()
    {
        Id = 1,
        InternalTrackingNumber = "UQEB-1",
        IncomingNumber = "IN-1",
        IncomingDate = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc),
        Subject = "معاملة",
        RequiresResponse = true,
        ResponseCompletedDate = manualResponseCompletedDate,
        CreatedById = 1,
    };

    private static Assignment RequiredAssignment(
        int id,
        ReplyStatus replyStatus,
        DateTime? replyDate,
        AssignmentStatus status = AssignmentStatus.Completed) => new()
    {
        Id = id,
        DepartmentId = id,
        AssignedDate = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc),
        RequiresReply = true,
        ReplyStatus = replyStatus,
        ReplyDate = replyDate,
        Status = status,
        CreatedById = 1,
    };

    [Fact]
    public void SingleRequiredReferral_UsesItsReplyDate()
    {
        var replyDate = new DateTime(2026, 7, 7, 0, 0, 0, DateTimeKind.Utc);
        var t = BuildTransaction();
        t.Assignments.Add(RequiredAssignment(1, ReplyStatus.Replied, replyDate));

        var result = WorkflowHelper.ResolveProceduralCompletionDateForReporting(t);

        Assert.Equal(replyDate, result);
        Assert.True(WorkflowHelper.IsProcedurallyCompleteForReporting(t));
    }

    [Fact]
    public void MultipleRequiredReferrals_UsesLatestReplyDate()
    {
        var t = BuildTransaction();
        t.Assignments.Add(RequiredAssignment(1, ReplyStatus.Replied, new DateTime(2026, 7, 5, 0, 0, 0, DateTimeKind.Utc)));
        t.Assignments.Add(RequiredAssignment(2, ReplyStatus.Replied, new DateTime(2026, 7, 9, 0, 0, 0, DateTimeKind.Utc)));

        var result = WorkflowHelper.ResolveProceduralCompletionDateForReporting(t);

        Assert.Equal(new DateTime(2026, 7, 9, 0, 0, 0, DateTimeKind.Utc), result);
        Assert.True(WorkflowHelper.IsProcedurallyCompleteForReporting(t));
    }

    [Fact]
    public void IncompleteReferral_ReturnsNullAndNotProcedurallyComplete()
    {
        var t = BuildTransaction();
        t.Assignments.Add(RequiredAssignment(1, ReplyStatus.Replied, new DateTime(2026, 7, 5, 0, 0, 0, DateTimeKind.Utc)));
        t.Assignments.Add(RequiredAssignment(2, ReplyStatus.Pending, null, AssignmentStatus.Active));

        var result = WorkflowHelper.ResolveProceduralCompletionDateForReporting(t);

        Assert.Null(result);
        Assert.False(WorkflowHelper.IsProcedurallyCompleteForReporting(t));
    }

    [Fact]
    public void NoReferrals_FallsBackToManualResponseCompletedDate()
    {
        var manualDate = new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc);
        var t = BuildTransaction(manualDate);

        var result = WorkflowHelper.ResolveProceduralCompletionDateForReporting(t);

        Assert.Equal(manualDate, result);
        // No department referrals required at all — this is a manual completion, not a
        // procedural one driven by department assignments.
        Assert.False(WorkflowHelper.IsProcedurallyCompleteForReporting(t));
    }

    [Fact]
    public void CancelledReferral_IsIgnored()
    {
        var replyDate = new DateTime(2026, 7, 7, 0, 0, 0, DateTimeKind.Utc);
        var t = BuildTransaction();
        t.Assignments.Add(RequiredAssignment(1, ReplyStatus.Replied, replyDate));
        t.Assignments.Add(RequiredAssignment(2, ReplyStatus.Pending, null, AssignmentStatus.Cancelled));

        var result = WorkflowHelper.ResolveProceduralCompletionDateForReporting(t);

        Assert.Equal(replyDate, result);
        Assert.True(WorkflowHelper.IsProcedurallyCompleteForReporting(t));
    }

    [Fact]
    public void ReplyStatusReplied_ButMissingReplyDate_IsTreatedAsIncomplete()
    {
        // Defensive: a Replied status with no ReplyDate (shouldn't happen via the normal
        // workflow, but reporting must not fabricate a date) keeps the transaction incomplete.
        var t = BuildTransaction();
        t.Assignments.Add(RequiredAssignment(1, ReplyStatus.Replied, null));

        var result = WorkflowHelper.ResolveProceduralCompletionDateForReporting(t);

        Assert.Null(result);
        Assert.False(WorkflowHelper.IsProcedurallyCompleteForReporting(t));
    }
}
