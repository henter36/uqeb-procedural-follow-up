using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Uqeb.Api.Data;
using Uqeb.Api.DTOs.Reports;
using Uqeb.Api.Models.Entities;
using Uqeb.Api.Models.Enums;
using Uqeb.Api.Services;
using Xunit;

namespace Uqeb.Api.Tests;

/// <summary>
/// Seeded scenario (all "now"-relative offsets, so this suite never depends on which
/// calendar date it actually runs on):
///
/// - Department 10 (A): owns T1, T2, T3, T5. Never appears in any Assignment/OutgoingDepartment
///   row anywhere - a pure "owner only" department.
/// - Department 20 (B): never owns anything. Referred-to on T1 (TransactionOutgoingDepartment).
///   Has Assignments on T1 (pending, overdue), T2 (replied, but no approved DepartmentResponse -
///   a deliberate attribution mismatch), T3 (TWO duplicate Assignment rows - the same divergence
///   the real AddAssignmentAsync path can produce - due soon), and T6 (replied AND has an
///   Approved DepartmentResponse - fully consistent). A pure "responsible/referred only" department.
/// - Department 40 (D): owns T4 only, with no Assignments/OutgoingDepartments at all anywhere -
///   the cleanest possible "owner only" case.
/// - Department 50 (E): never owns anything; has one Assignment (T5, no due date) - the cleanest
///   possible "responsible only" case, and proves a null DueDate is never counted as overdue/due soon.
/// - T6 has no owning department at all (external source) and is touched by department 20 via
///   both an Assignment AND a DepartmentResponse - a single department in multiple roles, which
///   must NOT be counted as a "multi-department" obligation.
/// </summary>
public class DepartmentObligationSnapshotServiceTests
{
    private sealed class TestDbContextFactory : IDbContextFactory<AppDbContext>
    {
        private readonly DbContextOptions<AppDbContext> _options;
        public TestDbContextFactory(DbContextOptions<AppDbContext> options) => _options = options;
        public AppDbContext CreateDbContext() => new(_options);
    }

    private static DbContextOptions<AppDbContext> CreateOptions(string dbName) =>
        new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(dbName)
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

    private static ReportService CreateService(DbContextOptions<AppDbContext> options) =>
        new(new AppDbContext(options), new TestDbContextFactory(options));

    private static async Task SeedAsync(DbContextOptions<AppDbContext> options)
    {
        using var db = new AppDbContext(options);
        var now = DateTime.UtcNow;

        db.Users.Add(new User { Id = 1, Username = "admin", PasswordHash = "h", FullName = "Admin", Role = UserRole.Admin, IsActive = true });
        db.Departments.Add(new Department { Id = 10, Name = "الإدارة أ", NameNormalized = "الإدارة أ", IsActive = true });
        db.Departments.Add(new Department { Id = 20, Name = "الإدارة ب", NameNormalized = "الإدارة ب", IsActive = true });
        db.Departments.Add(new Department { Id = 40, Name = "الإدارة د", NameNormalized = "الإدارة د", IsActive = true });
        db.Departments.Add(new Department { Id = 50, Name = "الإدارة هـ", NameNormalized = "الإدارة هـ", IsActive = true });
        db.Categories.Add(new Category { Id = 1, Name = "معاملات", NameNormalized = "معاملات", IsActive = true });
        await db.SaveChangesAsync();

        Transaction NewTransaction(int id, int? owningDepartmentId, DateTime incomingDate) => new()
        {
            Id = id,
            InternalTrackingNumber = $"UQEB-2026-{id:00000}",
            IncomingNumber = $"IN-{id:0000}",
            IncomingDate = incomingDate,
            Subject = $"معاملة {id}",
            IncomingSourceType = owningDepartmentId.HasValue ? IncomingSourceType.Internal : IncomingSourceType.External,
            IncomingFrom = owningDepartmentId.HasValue ? null : "جهة خارجية",
            IncomingFromDepartmentId = owningDepartmentId,
            CategoryId = 1,
            Priority = Priority.Normal,
            ResponseType = ResponseType.None,
            Status = TransactionStatus.InProgress,
            CreatedById = 1,
            CreatedAt = incomingDate,
        };

        var t1 = NewTransaction(1, 10, new DateTime(2026, 1, 5, 0, 0, 0, DateTimeKind.Utc));
        var t2 = NewTransaction(2, 10, new DateTime(2026, 1, 6, 0, 0, 0, DateTimeKind.Utc));
        var t3 = NewTransaction(3, 10, new DateTime(2026, 1, 7, 0, 0, 0, DateTimeKind.Utc));
        var t4 = NewTransaction(4, 40, new DateTime(2026, 1, 8, 0, 0, 0, DateTimeKind.Utc));
        var t5 = NewTransaction(5, 10, new DateTime(2026, 1, 9, 0, 0, 0, DateTimeKind.Utc));
        var t6 = NewTransaction(6, null, new DateTime(2026, 1, 10, 0, 0, 0, DateTimeKind.Utc));
        db.Transactions.AddRange(t1, t2, t3, t4, t5, t6);
        await db.SaveChangesAsync();

        // T1: owner=10, referred-to=20, assignment pending+overdue for 20, a Draft response for 20.
        db.TransactionOutgoingDepartments.Add(new TransactionOutgoingDepartment { TransactionId = 1, DepartmentId = 20, CreatedById = 1 });
        db.Assignments.Add(new Assignment
        {
            TransactionId = 1, DepartmentId = 20, Status = AssignmentStatus.Active, RequiresReply = true,
            ReplyStatus = ReplyStatus.Pending, AssignedDate = now.AddDays(-5), DueDate = now.AddDays(-2), CreatedById = 1,
        });
        db.DepartmentResponses.Add(new DepartmentResponse
        {
            TransactionId = 1, DepartmentId = 20, Status = DepartmentResponseStatus.Draft,
            ResponseText = "مسودة", SubmittedByUserId = 1,
        });

        // T2: owner=10, assignment replied+completed for 20, but NO DepartmentResponse at all -> mismatch.
        db.Assignments.Add(new Assignment
        {
            TransactionId = 2, DepartmentId = 20, Status = AssignmentStatus.Completed, RequiresReply = true,
            ReplyStatus = ReplyStatus.Replied, AssignedDate = now.AddDays(-10), DueDate = now.AddDays(-8), CreatedById = 1,
        });

        // T3: owner=10, TWO duplicate Assignment rows for department 20 (mirrors the real divergence
        // AddAssignmentAsync can produce - no unique constraint on Assignment(TransactionId, DepartmentId)).
        db.Assignments.Add(new Assignment
        {
            TransactionId = 3, DepartmentId = 20, Status = AssignmentStatus.Active, RequiresReply = true,
            ReplyStatus = ReplyStatus.Pending, AssignedDate = now.AddDays(-3), DueDate = now.AddDays(3), CreatedById = 1,
        });
        db.Assignments.Add(new Assignment
        {
            TransactionId = 3, DepartmentId = 20, Status = AssignmentStatus.Active, RequiresReply = true,
            ReplyStatus = ReplyStatus.Pending, AssignedDate = now.AddDays(-1), DueDate = now.AddDays(3), CreatedById = 1,
        });

        // T4: owner=40 only, no Assignments/OutgoingDepartments anywhere.

        // T5: owner=10, assignment for department 50 with no due date at all.
        db.Assignments.Add(new Assignment
        {
            TransactionId = 5, DepartmentId = 50, Status = AssignmentStatus.Active, RequiresReply = true,
            ReplyStatus = ReplyStatus.Pending, AssignedDate = now.AddDays(-1), DueDate = null, CreatedById = 1,
        });

        // T6: no owner (external). Assignment replied+completed for 20, AND an Approved
        // DepartmentResponse for 20 - fully consistent, single department in two roles.
        db.Assignments.Add(new Assignment
        {
            TransactionId = 6, DepartmentId = 20, Status = AssignmentStatus.Completed, RequiresReply = true,
            ReplyStatus = ReplyStatus.Replied, AssignedDate = now.AddDays(-20), DueDate = now.AddDays(-18), CreatedById = 1,
        });
        db.DepartmentResponses.Add(new DepartmentResponse
        {
            TransactionId = 6, DepartmentId = 20, Status = DepartmentResponseStatus.Approved,
            ResponseText = "تم الاعتماد", SubmittedByUserId = 1, SubmittedAt = now.AddDays(-19),
            ReviewedByUserId = 1, ReviewedAt = now.AddDays(-18),
        });

        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Snapshot_separates_owning_department_from_responsible_and_referred_department()
    {
        var options = CreateOptions(nameof(Snapshot_separates_owning_department_from_responsible_and_referred_department));
        await SeedAsync(options);
        var service = CreateService(options);

        var snapshot = await service.GetDepartmentObligationSnapshotAsync();

        var deptA = snapshot.Departments.Single(d => d.DepartmentId == 10);
        var deptB = snapshot.Departments.Single(d => d.DepartmentId == 20);

        // Department A owns transactions but is never assigned/referred to any.
        Assert.Equal(4, deptA.OwnedCount);
        Assert.Equal(0, deptA.ResponsibleCount);
        Assert.Equal(0, deptA.ReferredCount);
        Assert.Equal("OwnerOnly", deptA.InvolvementCategory);

        // Department B is referred-to/assigned but never owns anything.
        Assert.Equal(0, deptB.OwnedCount);
        Assert.Equal(4, deptB.ResponsibleCount);
        Assert.Equal(1, deptB.ReferredCount);
        Assert.Equal("ResponsibleOrReferredOnly", deptB.InvolvementCategory);
    }

    [Fact]
    public async Task Snapshot_counts_department_A_and_department_B_differently_for_the_same_transaction()
    {
        var options = CreateOptions(nameof(Snapshot_counts_department_A_and_department_B_differently_for_the_same_transaction));
        await SeedAsync(options);
        var service = CreateService(options);

        var snapshot = await service.GetDepartmentObligationSnapshotAsync();
        var deptA = snapshot.Departments.Single(d => d.DepartmentId == 10);
        var deptB = snapshot.Departments.Single(d => d.DepartmentId == 20);

        // T1 is owned by A and referred to B: it must count toward A's OwnedCount and B's
        // ReferredCount/ResponsibleCount, never the other way around.
        Assert.NotEqual(deptA.OwnedCount, deptB.OwnedCount);
        Assert.NotEqual(deptA.ResponsibleCount, deptB.ResponsibleCount);
        Assert.True(deptA.OwnedCount > 0 && deptB.OwnedCount == 0);
        Assert.True(deptB.ResponsibleCount > 0 && deptA.ResponsibleCount == 0);
    }

    [Fact]
    public async Task Snapshot_does_not_double_count_duplicate_assignment_rows_for_the_same_department_and_transaction()
    {
        var options = CreateOptions(nameof(Snapshot_does_not_double_count_duplicate_assignment_rows_for_the_same_department_and_transaction));
        await SeedAsync(options);
        var service = CreateService(options);

        var snapshot = await service.GetDepartmentObligationSnapshotAsync();
        var deptB = snapshot.Departments.Single(d => d.DepartmentId == 20);

        // T3 has two Assignment rows for department 20. ResponsibleCount/PendingActionCount/
        // DueSoonCount must each count T3 once, not twice.
        Assert.Equal(4, deptB.ResponsibleCount); // T1, T2, T3, T6 - not 5
        Assert.Equal(2, deptB.PendingActionCount); // T1, T3 - not 3
        Assert.Equal(1, deptB.DueSoonCount); // T3 only, counted once despite two rows
    }

    [Fact]
    public async Task Snapshot_computes_overdue_and_pending_action_and_average_days_open()
    {
        var options = CreateOptions(nameof(Snapshot_computes_overdue_and_pending_action_and_average_days_open));
        await SeedAsync(options);
        var service = CreateService(options);

        var snapshot = await service.GetDepartmentObligationSnapshotAsync();
        var deptB = snapshot.Departments.Single(d => d.DepartmentId == 20);
        var deptE = snapshot.Departments.Single(d => d.DepartmentId == 50);

        Assert.Equal(1, deptB.OverdueCount); // T1 only (due 2 days ago)
        Assert.Equal(2, deptB.OpenActionCount); // T1, T3 (T2/T6 are Completed)
        Assert.Equal(2, deptB.CompletedActionCount); // T2, T6

        // Average days open for B: T1 opened 5 days ago, T3's earliest duplicate opened 3 days
        // ago (not double-weighted by its second duplicate row) -> (5 + 3) / 2 = 4.0
        Assert.Equal(4.0, deptB.AverageDaysOpenAction);

        // Department E's single assignment has no due date at all: never overdue, never due soon.
        Assert.Equal(0, deptE.OverdueCount);
        Assert.Equal(0, deptE.DueSoonCount);
        Assert.Equal(1, deptE.PendingActionCount);
        Assert.Equal(1.0, deptE.AverageDaysOpenAction);
    }

    [Fact]
    public async Task Snapshot_flags_attribution_mismatch_when_assignment_reply_and_department_response_disagree()
    {
        var options = CreateOptions(nameof(Snapshot_flags_attribution_mismatch_when_assignment_reply_and_department_response_disagree));
        await SeedAsync(options);
        var service = CreateService(options);

        var snapshot = await service.GetDepartmentObligationSnapshotAsync();
        var deptB = snapshot.Departments.Single(d => d.DepartmentId == 20);

        // T2: Assignment says Replied, but there is no DepartmentResponse at all -> mismatch.
        // T6: Assignment says Replied AND an Approved DepartmentResponse exists -> consistent, not a mismatch.
        // T1/T3: neither replied nor approved -> consistent (both still pending), not a mismatch.
        Assert.Equal(1, deptB.AttributionMismatchCount);
        Assert.Equal(2, deptB.SubmittedResponseCount); // T1 (Draft), T6 (Approved)
        Assert.Equal(1, deptB.ApprovedResponseCount); // T6 only
    }

    [Fact]
    public async Task Snapshot_does_not_count_a_single_department_in_multiple_roles_as_a_multi_department_obligation()
    {
        var options = CreateOptions(nameof(Snapshot_does_not_count_a_single_department_in_multiple_roles_as_a_multi_department_obligation));
        await SeedAsync(options);
        var service = CreateService(options);

        var snapshot = await service.GetDepartmentObligationSnapshotAsync();

        // T1, T2, T3, T5 each span two distinct departments (owner + assignee). T4 involves only
        // department 40. T6 involves only department 20, via two different roles (Assignment and
        // DepartmentResponse) - that must NOT inflate the multi-department count.
        Assert.Equal(6, snapshot.TotalDistinctObligations);
        Assert.Equal(4, snapshot.MultiDepartmentObligationsCount);
    }

    [Fact]
    public async Task Snapshot_department_filter_scopes_to_a_single_department_row()
    {
        var options = CreateOptions(nameof(Snapshot_department_filter_scopes_to_a_single_department_row));
        await SeedAsync(options);
        var service = CreateService(options);

        var snapshot = await service.GetDepartmentObligationSnapshotAsync(new DepartmentObligationSnapshotFilterRequest { DepartmentId = 20 });

        Assert.Equal(1, snapshot.TotalDepartmentsInScope);
        Assert.Equal(20, snapshot.Departments.Single().DepartmentId);
    }

    [Fact]
    public async Task Snapshot_department_filter_preserves_cross_department_totals()
    {
        var options = CreateOptions(nameof(Snapshot_department_filter_preserves_cross_department_totals));
        await SeedAsync(options);
        var service = CreateService(options);

        var snapshot = await service.GetDepartmentObligationSnapshotAsync(new DepartmentObligationSnapshotFilterRequest { DepartmentId = 20 });

        // Departments collection stays scoped to only the selected department.
        Assert.Equal(1, snapshot.TotalDepartmentsInScope);
        Assert.Equal(20, snapshot.Departments.Single().DepartmentId);

        // Department 20 (B) is involved (referred/assigned/responded) in T1, T2, T3, T6.
        Assert.Equal(4, snapshot.TotalDistinctObligations);

        // T1/T2/T3 are owned by department 10 (A) and touched by 20 (B) elsewhere -> cross-department,
        // must count as multi-department even though the returned row set is filtered to B alone.
        // T6 has no owner and is touched by 20 (B) only, via two different roles (Assignment and
        // DepartmentResponse) -> a single department in multiple roles, must NOT count as multi-department.
        Assert.Equal(3, snapshot.MultiDepartmentObligationsCount);
    }

    [Fact]
    public async Task Snapshot_returns_empty_result_when_there_is_no_data()
    {
        var options = CreateOptions(nameof(Snapshot_returns_empty_result_when_there_is_no_data));
        using (var db = new AppDbContext(options))
        {
            await db.Database.EnsureCreatedAsync();
        }
        var service = CreateService(options);

        var snapshot = await service.GetDepartmentObligationSnapshotAsync();

        Assert.Equal(0, snapshot.TotalDepartmentsInScope);
        Assert.Equal(0, snapshot.TotalDistinctObligations);
        Assert.Equal(0, snapshot.MultiDepartmentObligationsCount);
        Assert.Empty(snapshot.Departments);
    }

    [Fact]
    public async Task ExportDepartmentObligationSnapshotExcelAsync_produces_a_row_per_department_with_expected_headers()
    {
        var options = CreateOptions(nameof(ExportDepartmentObligationSnapshotExcelAsync_produces_a_row_per_department_with_expected_headers));
        await SeedAsync(options);
        var service = CreateService(options);

        var bytes = await service.ExportDepartmentObligationSnapshotExcelAsync();

        using var workbook = new XLWorkbook(new MemoryStream(bytes));
        var ws = workbook.Worksheets.Single();

        Assert.Equal("معرف الإدارة", ws.Cell(1, 1).GetString());
        Assert.Equal("الإدارة", ws.Cell(1, 2).GetString());
        Assert.Equal("المملوكة", ws.Cell(1, 3).GetString());

        var lastRow = ws.LastRowUsed();
        Assert.NotNull(lastRow);
        Assert.Equal(5, lastRow.RowNumber()); // header + 4 departments
    }
}
