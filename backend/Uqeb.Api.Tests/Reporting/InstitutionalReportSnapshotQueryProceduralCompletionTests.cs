using Uqeb.Api.Models.Enums;
using Uqeb.Api.Reporting.Services;
using Xunit;

namespace Uqeb.Api.Tests.Reporting;

/// <summary>
/// End-to-end coverage of InstitutionalReportSnapshotQuery.MapRowToSnapshot's procedural
/// completion wiring (row projection -> snapshot), as opposed to
/// InstitutionalReportMetricsCalculatorTests, which exercises the pure calculator against
/// hand-built snapshots.
/// </summary>
public class InstitutionalReportSnapshotQueryProceduralCompletionTests
{
    private static readonly DateTime Today = new(2026, 7, 20);

    private static InstitutionalReportSnapshotQuery.SnapshotRow BuildRow(
        IReadOnlyList<InstitutionalReportSnapshotQuery.AssignmentRow> assignments,
        DateTime? responseCompletedDate = null) => new()
    {
        Id = 1,
        InternalTrackingNumber = "INT-1",
        IncomingNumber = "IN-1",
        IncomingDate = new DateTime(2026, 6, 20),
        Subject = "test",
        Priority = Priority.Normal,
        Status = TransactionStatus.ReadyForResponse,
        RequiresResponse = true,
        ResponseCompleted = false,
        ResponseCompletedDate = responseCompletedDate,
        ResponseDueDate = new DateTime(2026, 7, 10),
        CreatedAt = new DateTime(2026, 6, 20),
        Assignments = assignments.ToList(),
        OutgoingDepartments = [],
    };

    private static InstitutionalReportSnapshotQuery.AssignmentRow ReplyAssignment(
        ReplyStatus replyStatus,
        DateTime? replyDate,
        AssignmentStatus status = AssignmentStatus.Completed) => new()
    {
        DepartmentId = 1,
        DepartmentName = "إدارة",
        RequiresReply = true,
        Status = status,
        ReplyStatus = replyStatus,
        ReplyDate = replyDate,
    };

    [Fact]
    public void SingleReferral_MapsProceduralCompletionDateFromItsReplyDate()
    {
        var replyDate = new DateTime(2026, 7, 7);
        var row = BuildRow([ReplyAssignment(ReplyStatus.Replied, replyDate)]);

        var snapshot = InstitutionalReportSnapshotQuery.MapRowToSnapshot(row, Today);

        Assert.True(snapshot.IsProcedurallyCompleteForReporting);
        Assert.Equal(replyDate, snapshot.ProceduralCompletionDateForReporting);
    }

    [Fact]
    public void MultipleReferrals_MapsProceduralCompletionDateToLatestReplyDate()
    {
        var row = BuildRow([
            ReplyAssignment(ReplyStatus.Replied, new DateTime(2026, 7, 5)),
            ReplyAssignment(ReplyStatus.Replied, new DateTime(2026, 7, 9)),
        ]);

        var snapshot = InstitutionalReportSnapshotQuery.MapRowToSnapshot(row, Today);

        Assert.True(snapshot.IsProcedurallyCompleteForReporting);
        Assert.Equal(new DateTime(2026, 7, 9), snapshot.ProceduralCompletionDateForReporting);
    }

    [Fact]
    public void IncompleteReferral_MapsToNullAndNotProcedurallyComplete()
    {
        var row = BuildRow([
            ReplyAssignment(ReplyStatus.Replied, new DateTime(2026, 7, 5)),
            ReplyAssignment(ReplyStatus.Pending, null, AssignmentStatus.Active),
        ]);

        var snapshot = InstitutionalReportSnapshotQuery.MapRowToSnapshot(row, Today);

        Assert.False(snapshot.IsProcedurallyCompleteForReporting);
        Assert.Null(snapshot.ProceduralCompletionDateForReporting);
        // Still due-date driven, still counted as genuinely open/overdue territory.
        Assert.True(InstitutionalReportMetricsCalculator.IsOpenOverdue(snapshot, Today));
    }

    [Fact]
    public void NoRequiredReferrals_FallsBackToManualResponseCompletedDate()
    {
        var manualDate = new DateTime(2026, 7, 1);
        var row = BuildRow([], manualDate);

        var snapshot = InstitutionalReportSnapshotQuery.MapRowToSnapshot(row, Today);

        Assert.False(snapshot.IsProcedurallyCompleteForReporting);
        Assert.Equal(manualDate, snapshot.ProceduralCompletionDateForReporting);
    }
}
