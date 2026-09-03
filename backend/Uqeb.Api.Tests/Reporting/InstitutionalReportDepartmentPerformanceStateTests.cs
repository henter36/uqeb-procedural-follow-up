using Uqeb.Api.Models.Enums;
using Uqeb.Api.Reporting.Models;
using Uqeb.Api.Reporting.Services;
using Xunit;

namespace Uqeb.Api.Tests.Reporting;

public class InstitutionalReportDepartmentPerformanceStateTests
{
    private static readonly DateTime EvaluationDate = new(2026, 8, 20);

    [Fact]
    public void SingleDepartment_OpenAssignment_IsOpenForDepartment()
    {
        var snapshot = Map([Assignment(1, 10, dueDate: new DateTime(2026, 8, 25))]);

        var state = Assert.Single(snapshot.DepartmentPerformanceStates);
        Assert.True(state.IsOpenForDepartment);
        Assert.False(state.IsCompletedForDepartment);
        Assert.False(state.IsOverdueForDepartment);
        Assert.False(state.IsTimelinessEligible);
    }

    [Fact]
    public void SingleDepartment_CompletedWithinDueDate_IsCompletedAndOnTime()
    {
        var snapshot = Map([
            Assignment(1, 10, dueDate: new DateTime(2026, 8, 10), replyDate: new DateTime(2026, 8, 5)),
        ]);

        var state = Assert.Single(snapshot.DepartmentPerformanceStates);
        Assert.True(state.IsCompletedForDepartment);
        Assert.False(state.IsOpenForDepartment);
        Assert.True(state.IsOnTimeForDepartment);
        Assert.False(state.IsCompletedLateForDepartment);
        Assert.Equal(new DateTime(2026, 8, 5), state.DepartmentCompletionDate);
    }

    [Fact]
    public void SingleDepartment_CompletedAfterDueDate_IsCompletedAndLate()
    {
        var snapshot = Map([
            Assignment(1, 10, dueDate: new DateTime(2026, 8, 4), replyDate: new DateTime(2026, 8, 5)),
        ]);

        var state = Assert.Single(snapshot.DepartmentPerformanceStates);
        Assert.True(state.IsCompletedForDepartment);
        Assert.True(state.IsCompletedLateForDepartment);
        Assert.True(state.IsOverdueForDepartment);
        Assert.False(state.IsOnTimeForDepartment);
    }

    [Fact]
    public void SharedTransaction_CompletedDepartmentAndOpenDepartment_HaveIndependentStates()
    {
        var snapshot = Map([
            Assignment(1, 10, dueDate: new DateTime(2026, 8, 6), replyDate: new DateTime(2026, 8, 5)),
            Assignment(2, 20, dueDate: new DateTime(2026, 8, 25)),
        ]);

        AssertDepartmentState(snapshot, 10, isCompleted: true, isOpen: false, isOverdue: false);
        AssertDepartmentState(snapshot, 20, isCompleted: false, isOpen: true, isOverdue: false);
    }

    [Fact]
    public void SharedTransaction_OnTimeDepartmentAndOpenOverdueDepartment_HaveIndependentTimeliness()
    {
        var snapshot = Map([
            Assignment(1, 10, dueDate: new DateTime(2026, 8, 6), replyDate: new DateTime(2026, 8, 5)),
            Assignment(2, 20, dueDate: new DateTime(2026, 8, 10)),
        ]);

        var completed = AssertDepartmentState(snapshot, 10, isCompleted: true, isOpen: false, isOverdue: false);
        Assert.True(completed.IsOnTimeForDepartment);
        var pending = AssertDepartmentState(snapshot, 20, isCompleted: false, isOpen: true, isOverdue: true);
        Assert.True(pending.IsOpenOverdueForDepartment);
    }

    [Fact]
    public void SharedTransaction_BothDepartmentsCompleted_KeepTheirOwnCompletionDates()
    {
        var snapshot = Map([
            Assignment(1, 10, dueDate: new DateTime(2026, 8, 6), replyDate: new DateTime(2026, 8, 5)),
            Assignment(2, 20, dueDate: new DateTime(2026, 8, 12), replyDate: new DateTime(2026, 8, 9)),
        ]);

        Assert.Equal(new DateTime(2026, 8, 5), StateFor(snapshot, 10).DepartmentCompletionDate);
        Assert.Equal(new DateTime(2026, 8, 9), StateFor(snapshot, 20).DepartmentCompletionDate);
        Assert.All(snapshot.DepartmentPerformanceStates, state => Assert.True(state.IsCompletedForDepartment));
    }

    [Fact]
    public void AssignmentNotRequiringReply_IsCompletedButNotTimelinessMeasurable()
    {
        var snapshot = Map([Assignment(1, 10, requiresReply: false)]);

        var state = Assert.Single(snapshot.DepartmentPerformanceStates);
        Assert.True(state.IsCompletedForDepartment);
        Assert.False(state.IsOpenForDepartment);
        Assert.Null(state.DepartmentCompletionDate);
        Assert.False(state.IsTimelinessEligible);
        Assert.False(state.IsOnTimeForDepartment);
    }

    [Fact]
    public void CancelledAssignment_DoesNotCreateDepartmentPerformanceState()
    {
        var snapshot = Map([Assignment(1, 10, status: AssignmentStatus.Cancelled)]);

        Assert.Empty(snapshot.DepartmentPerformanceStates);
    }

    [Fact]
    public void CompletedAssignmentWithReplyDate_UsesAssignmentReplyAsCompletionEvidence()
    {
        var replyDate = new DateTime(2026, 8, 5);
        var snapshot = Map([
            Assignment(1, 10, dueDate: new DateTime(2026, 8, 6), replyDate: replyDate, status: AssignmentStatus.Completed),
        ]);

        var state = Assert.Single(snapshot.DepartmentPerformanceStates);
        Assert.True(state.IsCompletedForDepartment);
        Assert.Equal(replyDate, state.DepartmentCompletionDate);
    }

    [Fact]
    public void LateCentralClosure_DoesNotMakeOnTimeDepartmentLate()
    {
        var snapshot = Map(
            [Assignment(1, 10, dueDate: new DateTime(2026, 8, 6), replyDate: new DateTime(2026, 8, 5))],
            closedAt: new DateTime(2026, 8, 15));

        var state = Assert.Single(snapshot.DepartmentPerformanceStates);
        Assert.True(state.IsOnTimeForDepartment);
        Assert.False(state.IsCompletedLateForDepartment);
        Assert.Equal(new DateTime(2026, 8, 5), state.DepartmentCompletionDate);
    }

    [Fact]
    public void GeneralTransaction_RemainsOpenAfterOnlyOneDepartmentCompletes()
    {
        var snapshot = Map([
            Assignment(1, 10, dueDate: new DateTime(2026, 8, 6), replyDate: new DateTime(2026, 8, 5)),
            Assignment(2, 20, dueDate: new DateTime(2026, 8, 25)),
        ]);

        Assert.True(snapshot.IsOpen);
        Assert.False(snapshot.IsClosed);
        Assert.True(StateFor(snapshot, 10).IsCompletedForDepartment);
    }

    [Fact]
    public void CompletedDepartment_DoesNotAppearOpenInItsDepartmentState()
    {
        var snapshot = Map([
            Assignment(1, 10, dueDate: new DateTime(2026, 8, 6), replyDate: new DateTime(2026, 8, 5)),
            Assignment(2, 20, dueDate: new DateTime(2026, 8, 25)),
        ]);

        Assert.False(StateFor(snapshot, 10).IsOpenForDepartment);
    }

    [Fact]
    public void GeneralMetrics_KeepSharedTransactionOpenWhileAnotherDepartmentIsPending()
    {
        var snapshot = Map([
            Assignment(1, 10, dueDate: new DateTime(2026, 8, 6), replyDate: new DateTime(2026, 8, 5)),
            Assignment(2, 20, dueDate: new DateTime(2026, 8, 25)),
        ]);

        var metrics = InstitutionalReportMetricsCalculator.Calculate([snapshot], EvaluationDate);

        Assert.Equal(1, metrics.TotalTransactions);
        Assert.Equal(1, metrics.OpenCount);
        Assert.Equal(0, metrics.ClosedCount);
    }

    [Fact]
    public void MultipleAssignmentsForSameDepartment_RequireAllRepliesAndPreserveIndividualDeadlines()
    {
        var snapshot = Map([
            Assignment(1, 10, dueDate: new DateTime(2026, 8, 4), replyDate: new DateTime(2026, 8, 5)),
            Assignment(2, 10, dueDate: new DateTime(2026, 8, 12), replyDate: new DateTime(2026, 8, 9)),
        ]);

        var state = Assert.Single(snapshot.DepartmentPerformanceStates);
        Assert.True(state.IsCompletedForDepartment);
        Assert.Equal(new DateTime(2026, 8, 9), state.DepartmentCompletionDate);
        Assert.Equal(new DateTime(2026, 8, 4), state.DepartmentDueDate);
        Assert.True(state.IsCompletedLateForDepartment);
        Assert.False(state.IsOnTimeForDepartment);
    }

    private static DepartmentTransactionPerformanceState AssertDepartmentState(
        TransactionReportSnapshot snapshot,
        int departmentId,
        bool isCompleted,
        bool isOpen,
        bool isOverdue)
    {
        var state = StateFor(snapshot, departmentId);
        Assert.Equal(isCompleted, state.IsCompletedForDepartment);
        Assert.Equal(isOpen, state.IsOpenForDepartment);
        Assert.Equal(isOverdue, state.IsOverdueForDepartment);
        return state;
    }

    private static DepartmentTransactionPerformanceState StateFor(TransactionReportSnapshot snapshot, int departmentId) =>
        Assert.Single(snapshot.DepartmentPerformanceStates, state => state.DepartmentId == departmentId);

    private static TransactionReportSnapshot Map(
        IReadOnlyList<InstitutionalReportSnapshotQuery.AssignmentRow> assignments,
        DateTime? closedAt = null) =>
        InstitutionalReportSnapshotQuery.MapRowToSnapshot(
            new InstitutionalReportSnapshotQuery.SnapshotRow
            {
                Id = 100,
                InternalTrackingNumber = "PERF-100",
                IncomingNumber = "IN-100",
                IncomingDate = new DateTime(2026, 8, 1),
                Subject = "اختبار أداء الإدارات",
                Priority = Priority.Normal,
                Status = TransactionStatus.PartiallyReplied,
                RequiresResponse = true,
                ResponseCompleted = false,
                ClosedAt = closedAt,
                CreatedAt = new DateTime(2026, 8, 1),
                Assignments = assignments.ToList(),
                OutgoingDepartments = [],
            },
            EvaluationDate);

    private static InstitutionalReportSnapshotQuery.AssignmentRow Assignment(
        int id,
        int departmentId,
        DateTime? dueDate = null,
        DateTime? replyDate = null,
        AssignmentStatus? status = null,
        bool requiresReply = true) => new()
    {
        Id = id,
        DepartmentId = departmentId,
        DepartmentName = $"الإدارة {departmentId}",
        AssignedDate = new DateTime(2026, 8, 1),
        CreatedAt = new DateTime(2026, 8, 1, 8, 0, 0),
        RequiresReply = requiresReply,
        ReplyStatus = replyDate.HasValue ? ReplyStatus.Replied : ReplyStatus.Pending,
        Status = status ?? (replyDate.HasValue ? AssignmentStatus.Completed : AssignmentStatus.Active),
        DueDate = dueDate,
        ReplyDate = replyDate,
    };
}
