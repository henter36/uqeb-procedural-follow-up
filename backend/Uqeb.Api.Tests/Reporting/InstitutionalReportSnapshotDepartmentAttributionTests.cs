using Uqeb.Api.Models.Enums;
using Uqeb.Api.Reporting.Services;
using Xunit;

namespace Uqeb.Api.Tests.Reporting;

public class InstitutionalReportSnapshotDepartmentAttributionTests
{
    private static readonly DateTime Today = new(2026, 7, 20);

    [Fact]
    public void OpenTransaction_WithActiveAssignment_UsesActiveDepartment()
    {
        var snapshot = Map([Assignment(1, 10, "الإدارة النشطة", AssignmentStatus.Active, Today.AddDays(-1))]);

        Assert.Equal(10, snapshot.ResponsibleDepartmentId);
        Assert.Equal("الإدارة النشطة", snapshot.ResponsibleDepartment);
        Assert.Equal([10], snapshot.AssignmentDepartmentIds);
    }

    [Fact]
    public void ClosedTransaction_WithCompletedAssignmentOnly_UsesCompletedDepartment()
    {
        var snapshot = Map([Assignment(1, 20, "الإدارة المكتملة", AssignmentStatus.Completed, Today.AddDays(-5))]);

        Assert.Equal(20, snapshot.ResponsibleDepartmentId);
        Assert.Equal("الإدارة المكتملة", snapshot.ResponsibleDepartment);
        Assert.Equal([20], snapshot.AssignmentDepartmentIds);
    }

    [Fact]
    public void Transaction_WithActiveAndCompletedAssignments_PrefersActiveDepartment()
    {
        var snapshot = Map([
            Assignment(1, 20, "الإدارة المكتملة", AssignmentStatus.Completed, Today),
            Assignment(2, 10, "الإدارة النشطة", AssignmentStatus.Active, Today.AddDays(-10), requiresReply: true),
        ]);

        Assert.Equal(10, snapshot.ResponsibleDepartmentId);
        Assert.Equal("الإدارة النشطة", snapshot.ResponsibleDepartment);
        Assert.Equal(1, snapshot.ActiveAssignmentCount);
        Assert.Equal(1, snapshot.PendingReplyAssignmentCount);
    }

    [Fact]
    public void Transaction_WithCancelledAndCompletedAssignments_UsesCompletedDepartment()
    {
        var snapshot = Map([
            Assignment(1, 10, "إدارة ملغاة", AssignmentStatus.Cancelled, Today),
            Assignment(2, 20, "إدارة مكتملة", AssignmentStatus.Completed, Today.AddDays(-1)),
        ]);

        Assert.Equal(20, snapshot.ResponsibleDepartmentId);
        Assert.Equal("إدارة مكتملة", snapshot.ResponsibleDepartment);
        Assert.Equal([20], snapshot.AssignmentDepartmentIds);
    }

    [Fact]
    public void Transaction_WithCancelledAssignmentOnly_HasNoResponsibleDepartment()
    {
        var snapshot = Map([Assignment(1, 10, "إدارة ملغاة", AssignmentStatus.Cancelled, Today)]);

        Assert.Null(snapshot.ResponsibleDepartmentId);
        Assert.Equal("—", snapshot.ResponsibleDepartment);
        Assert.Empty(snapshot.AssignmentDepartmentIds);
    }

    [Fact]
    public void ResponsibleDepartmentSelection_IsDeterministicRegardlessOfInputOrder()
    {
        var firstOrder = new[]
        {
            Assignment(1, 10, "إدارة أ", AssignmentStatus.Completed, Today.AddDays(-2)),
            Assignment(3, 30, "إدارة ج", AssignmentStatus.Completed, Today.AddDays(-1)),
            Assignment(2, 20, "إدارة ب", AssignmentStatus.Completed, Today.AddDays(-1)),
        };
        var secondOrder = firstOrder.Reverse().ToArray();

        var firstSnapshot = Map(firstOrder);
        var secondSnapshot = Map(secondOrder);

        Assert.Equal(30, firstSnapshot.ResponsibleDepartmentId);
        Assert.Equal(firstSnapshot.ResponsibleDepartmentId, secondSnapshot.ResponsibleDepartmentId);
        Assert.Equal(firstSnapshot.ResponsibleDepartment, secondSnapshot.ResponsibleDepartment);
    }

    [Fact]
    public void CompletedAssignmentSelection_UsesLatestAssignedDate()
    {
        var snapshot = Map([
            Assignment(1, 10, "إدارة أقدم", AssignmentStatus.Completed, Today.AddDays(-3)),
            Assignment(2, 20, "إدارة أحدث", AssignmentStatus.Completed, Today.AddDays(-1)),
        ]);

        Assert.Equal(20, snapshot.ResponsibleDepartmentId);
        Assert.Equal("إدارة أحدث", snapshot.ResponsibleDepartment);
    }

    [Fact]
    public void CompletedAssignmentSelection_UsesCreatedAtThenId_WhenAssignedDateMatches()
    {
        var assignedDate = Today.AddDays(-1);
        var firstCreatedAt = assignedDate.AddHours(1);
        var latestCreatedAt = assignedDate.AddHours(2);

        var createdAtSnapshot = Map([
            Assignment(1, 10, "إدارة أقدم إنشاء", AssignmentStatus.Completed, assignedDate, createdAt: firstCreatedAt),
            Assignment(2, 20, "إدارة أحدث إنشاء", AssignmentStatus.Completed, assignedDate, createdAt: latestCreatedAt),
        ]);
        var idSnapshot = Map([
            Assignment(2, 20, "إدارة رقم أقل", AssignmentStatus.Completed, assignedDate, createdAt: latestCreatedAt),
            Assignment(3, 30, "إدارة رقم أعلى", AssignmentStatus.Completed, assignedDate, createdAt: latestCreatedAt),
        ]);

        Assert.Equal(20, createdAtSnapshot.ResponsibleDepartmentId);
        Assert.Equal(30, idSnapshot.ResponsibleDepartmentId);
    }

    [Fact]
    public void EmptyAssignments_DoNotUseDepartmentIdZero()
    {
        var snapshot = Map([]);

        Assert.Null(snapshot.ResponsibleDepartmentId);
        Assert.DoesNotContain(0, snapshot.AssignmentDepartmentIds);
    }

    private static InstitutionalReportSnapshotQuery.AssignmentRow Assignment(
        int id,
        int departmentId,
        string departmentName,
        AssignmentStatus status,
        DateTime assignedDate,
        DateTime? createdAt = null,
        bool requiresReply = false) => new()
    {
        Id = id,
        DepartmentId = departmentId,
        DepartmentName = departmentName,
        AssignedDate = assignedDate,
        CreatedAt = createdAt ?? assignedDate.AddHours(1),
        RequiresReply = requiresReply,
        ReplyStatus = ReplyStatus.Pending,
        Status = status,
    };

    private static Uqeb.Api.Reporting.Models.TransactionReportSnapshot Map(
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
                Status = TransactionStatus.New,
                RequiresResponse = false,
                ResponseCompleted = false,
                CreatedAt = Today.AddDays(-7),
                Assignments = assignments.ToList(),
                OutgoingDepartments = [],
            },
            Today);
}
