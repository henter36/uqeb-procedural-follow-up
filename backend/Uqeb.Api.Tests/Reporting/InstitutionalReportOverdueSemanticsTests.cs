using Uqeb.Api.Models.Enums;
using Uqeb.Api.Reporting.Models;
using Uqeb.Api.Reporting.Services;
using Xunit;

namespace Uqeb.Api.Tests.Reporting;

/// <summary>
/// Assignment overdue semantics: due date must belong to the same pending-reply assignment.
/// Overdue begins strictly before today (due date equals today is not overdue).
/// </summary>
public class InstitutionalReportOverdueSemanticsTests
{
    private static readonly DateTime Today = new(2026, 6, 15);

    [Fact]
    public void IsOverdue_MixedAssignments_OldNonReplyDueDateAndFuturePendingReply_IsNotOverdue()
    {
        var snapshot = BuildSnapshot(
            TransactionStatus.New,
            assignments:
            [
                Assignment(AssignmentStatus.Active, requiresReply: false, ReplyStatus.Pending, Today.AddDays(-10)),
                Assignment(AssignmentStatus.Active, requiresReply: true, ReplyStatus.Pending, Today.AddDays(5)),
            ]);

        Assert.False(snapshot.IsOverdue);
        Assert.False(InstitutionalReportMetricsCalculator.IsOverdue(snapshot, Today));
    }

    [Fact]
    public void IsOverdue_RepliedOldAssignmentAndFuturePendingReply_IsNotOverdue()
    {
        var snapshot = BuildSnapshot(
            TransactionStatus.New,
            assignments:
            [
                Assignment(AssignmentStatus.Active, requiresReply: true, ReplyStatus.Replied, Today.AddDays(-10)),
                Assignment(AssignmentStatus.Active, requiresReply: true, ReplyStatus.Pending, Today.AddDays(3)),
            ]);

        Assert.False(snapshot.IsOverdue);
    }

    [Fact]
    public void IsOverdue_PendingReplyWithPastDueDate_IsOverdue()
    {
        var snapshot = BuildSnapshot(
            TransactionStatus.New,
            assignments:
            [
                Assignment(AssignmentStatus.Active, requiresReply: true, ReplyStatus.Pending, Today.AddDays(-2)),
            ]);

        Assert.True(snapshot.IsOverdue);
        Assert.Equal(Today.AddDays(-2), snapshot.EarliestPendingReplyDueDate);
    }

    [Fact]
    public void IsOverdue_InactiveAssignmentWithOldDueDate_DoesNotAffectOverdue()
    {
        var snapshot = BuildSnapshot(
            TransactionStatus.New,
            assignments:
            [
                Assignment(AssignmentStatus.Completed, requiresReply: true, ReplyStatus.Pending, Today.AddDays(-30)),
                Assignment(AssignmentStatus.Active, requiresReply: true, ReplyStatus.Pending, Today.AddDays(2)),
            ]);

        Assert.False(snapshot.IsOverdue);
    }

    [Fact]
    public void IsOverdue_DueDateEqualsToday_IsNotOverdue()
    {
        var snapshot = BuildSnapshot(
            TransactionStatus.New,
            assignments:
            [
                Assignment(AssignmentStatus.Active, requiresReply: true, ReplyStatus.Pending, Today),
            ]);

        Assert.False(snapshot.IsOverdue);
    }

    [Fact]
    public void Metrics_OverdueCount_MatchesSnapshotIsOverdueFlags()
    {
        var snapshots = new[]
        {
            BuildSnapshot(TransactionStatus.New, [Assignment(AssignmentStatus.Active, true, ReplyStatus.Pending, Today.AddDays(-1))]),
            BuildSnapshot(TransactionStatus.New, [Assignment(AssignmentStatus.Active, false, ReplyStatus.Pending, Today.AddDays(-5)), Assignment(AssignmentStatus.Active, true, ReplyStatus.Pending, Today.AddDays(2))]),
        };

        var metrics = InstitutionalReportMetricsCalculator.Calculate(snapshots, Today);

        Assert.Equal(1, metrics.OverdueCount);
        Assert.Equal(1, snapshots.Count(s => s.IsOverdue));
    }

    private static TransactionReportSnapshot BuildSnapshot(
        TransactionStatus status,
        IReadOnlyList<InstitutionalReportSnapshotQuery.AssignmentRow> assignments) =>
        InstitutionalReportSnapshotQuery.MapRowToSnapshot(
            new InstitutionalReportSnapshotQuery.SnapshotRow
            {
                Id = 1,
                InternalTrackingNumber = "INT-1",
                IncomingNumber = "IN-1",
                IncomingDate = Today.AddDays(-7),
                Subject = "test",
                Priority = Priority.Normal,
                Status = status,
                RequiresResponse = false,
                ResponseCompleted = false,
                CreatedAt = Today.AddDays(-7),
                Assignments = assignments.ToList(),
                OutgoingDepartments = [],
            },
            Today);

    private static InstitutionalReportSnapshotQuery.AssignmentRow Assignment(
        AssignmentStatus status,
        bool requiresReply,
        ReplyStatus replyStatus,
        DateTime? dueDate) => new()
    {
        DepartmentId = 1,
        DepartmentName = "إدارة",
        Status = status,
        RequiresReply = requiresReply,
        ReplyStatus = replyStatus,
        DueDate = dueDate,
    };
}
