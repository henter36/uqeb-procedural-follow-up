using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Uqeb.Api.Data;
using Uqeb.Api.DTOs.Transactions;
using Uqeb.Api.Models.Entities;
using Uqeb.Api.Models.Enums;
using Uqeb.Api.Services;
using Xunit;

namespace Uqeb.Api.Tests;

public class AssignmentResponseDatesTests
{
    private sealed class StubTrackingNumberService : ITrackingNumberService
    {
        public Task<string> GenerateNextAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult("UQEB-2026-00001");
    }

    private sealed class TestCacheInvalidation : ICacheInvalidationService
    {
        public string DashboardSummaryKey => "dashboard";
        public string BuildDashboardSummaryKey() => DashboardSummaryKey;
        public string BuildDashboardFullKey() => "dashboard:full";
        public TimeSpan DashboardCacheDuration => TimeSpan.FromMinutes(1);
        public TimeSpan ReportsPageSummaryCacheDuration => TimeSpan.FromMinutes(1);
        public TimeSpan ReferenceDataCacheDuration => TimeSpan.FromMinutes(1);
        public string BuildReportsPageSummaryKey(DTOs.Reports.ReportFilterRequest? filter) => "reports";
        public string BuildDepartmentsKey(bool activeOnly) => $"departments-{activeOnly}";
        public string BuildCategoriesKey(bool activeOnly) => $"categories-{activeOnly}";
        public string BuildExternalPartiesKey(bool activeOnly) => $"parties-{activeOnly}";
        public void InvalidateOnTransactionChange() { }
        public void InvalidateReferenceData() { }
    }

    private sealed class TestCurrentUser : ICurrentUserService
    {
        public TestCurrentUser(UserRole role, int userId = 1, int? departmentId = null)
        {
            Role = role;
            UserId = userId;
            DepartmentId = departmentId;
        }

        public int UserId { get; }
        public string Username => "tester";
        public UserRole Role { get; }
        public int? DepartmentId { get; }
        public bool IsAuthenticated => true;
    }

    private static async Task<(TransactionService Service, AppDbContext Db)> CreateServiceAsync(string dbName)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(dbName)
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        var db = new AppDbContext(options);

        db.Users.Add(new User { Id = 1, Username = "admin", PasswordHash = "h", FullName = "Admin", Role = UserRole.Admin, IsActive = true });
        db.Users.Add(new User { Id = 2, Username = "dept", PasswordHash = "h", FullName = "Dept", Role = UserRole.DepartmentUser, DepartmentId = 10, IsActive = true });
        db.Departments.Add(new Department { Id = 10, Name = "المالية", NameNormalized = "المالية", IsActive = true });
        db.Departments.Add(new Department { Id = 20, Name = "الموارد", NameNormalized = "الموارد", IsActive = true });
        await db.SaveChangesAsync();

        var service = new TransactionService(
            db,
            new AuditService(db),
            new StubTrackingNumberService(),
            new TestCacheInvalidation());

        return (service, db);
    }

    private static async Task<Transaction> SeedTransactionAsync(AppDbContext db, int id = 100,
        DateTime? incomingDate = null)
    {
        var t = new Transaction
        {
            Id = id,
            InternalTrackingNumber = $"UQEB-{id}",
            IncomingNumber = $"IN-{id}",
            IncomingDate = incomingDate ?? new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            Subject = "معاملة",
            IncomingSourceType = IncomingSourceType.External,
            IncomingFrom = "جهة",
            RequiresResponse = true,
            ResponseType = ResponseType.External,
            Priority = Priority.Normal,
            Status = TransactionStatus.Assigned,
            CreatedById = 1,
            CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
        };
        db.Transactions.Add(t);
        await db.SaveChangesAsync();
        return t;
    }

    [Fact]
    public async Task GetAssignmentsAsync_includes_LetterNumber()
    {
        var (service, db) = await CreateServiceAsync(nameof(GetAssignmentsAsync_includes_LetterNumber));
        var t = await SeedTransactionAsync(db, 1);

        db.Assignments.Add(new Assignment
        {
            TransactionId = 1,
            DepartmentId = 10,
            AssignedDate = new DateTime(2026, 1, 5, 0, 0, 0, DateTimeKind.Utc),
            LetterNumber = "خ-2026/100",
            RequiresReply = true,
            ReplyStatus = ReplyStatus.Pending,
            Status = AssignmentStatus.Active,
            CreatedById = 1,
            CreatedAt = t.CreatedAt
        });
        await db.SaveChangesAsync();

        var result = await service.GetAssignmentsAsync(1, new TestCurrentUser(UserRole.Admin));

        Assert.NotNull(result);
        Assert.Single(result);
        Assert.Equal("خ-2026/100", result[0].LetterNumber);
    }

    [Fact]
    public async Task GetAssignmentsAsync_calculates_DepartmentCompletionDays_from_SubmittedAt()
    {
        var (service, db) = await CreateServiceAsync(nameof(GetAssignmentsAsync_calculates_DepartmentCompletionDays_from_SubmittedAt));
        var incomingDate = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        await SeedTransactionAsync(db, 1, incomingDate);

        db.Assignments.Add(new Assignment
        {
            TransactionId = 1,
            DepartmentId = 10,
            AssignedDate = incomingDate,
            RequiresReply = true,
            ReplyStatus = ReplyStatus.Pending,
            Status = AssignmentStatus.Active,
            CreatedById = 1,
            CreatedAt = incomingDate
        });

        var submittedAt = new DateTime(2026, 1, 11, 0, 0, 0, DateTimeKind.Utc);
        db.DepartmentResponses.Add(new DepartmentResponse
        {
            TransactionId = 1,
            DepartmentId = 10,
            ResponseText = "إفادة المالية",
            Status = DepartmentResponseStatus.SubmittedForReview,
            SubmittedByUserId = 1,
            SubmittedAt = submittedAt,
            CreatedAt = submittedAt
        });
        await db.SaveChangesAsync();

        var result = await service.GetAssignmentsAsync(1, new TestCurrentUser(UserRole.Admin));

        Assert.NotNull(result);
        Assert.Single(result);
        Assert.Equal(submittedAt, result[0].ResponseDate);
        Assert.Equal(10, result[0].DepartmentCompletionDays);
    }

    [Fact]
    public async Task GetAssignmentsAsync_DepartmentCompletionDays_is_null_when_no_response()
    {
        var (service, db) = await CreateServiceAsync(nameof(GetAssignmentsAsync_DepartmentCompletionDays_is_null_when_no_response));
        await SeedTransactionAsync(db, 1);

        db.Assignments.Add(new Assignment
        {
            TransactionId = 1,
            DepartmentId = 10,
            AssignedDate = new DateTime(2026, 1, 5, 0, 0, 0, DateTimeKind.Utc),
            RequiresReply = true,
            ReplyStatus = ReplyStatus.Pending,
            Status = AssignmentStatus.Active,
            CreatedById = 1,
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var result = await service.GetAssignmentsAsync(1, new TestCurrentUser(UserRole.Admin));

        Assert.NotNull(result);
        Assert.Single(result);
        Assert.Null(result[0].ResponseDate);
        Assert.Null(result[0].DepartmentCompletionDays);
    }

    [Fact]
    public async Task GetAssignmentsAsync_CompletionDays_is_independent_per_department()
    {
        var (service, db) = await CreateServiceAsync(nameof(GetAssignmentsAsync_CompletionDays_is_independent_per_department));
        var incomingDate = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        await SeedTransactionAsync(db, 1, incomingDate);

        db.Assignments.Add(new Assignment
        {
            TransactionId = 1,
            DepartmentId = 10,
            AssignedDate = incomingDate,
            RequiresReply = true,
            ReplyStatus = ReplyStatus.Pending,
            Status = AssignmentStatus.Active,
            CreatedById = 1,
            CreatedAt = incomingDate
        });
        db.Assignments.Add(new Assignment
        {
            TransactionId = 1,
            DepartmentId = 20,
            AssignedDate = incomingDate,
            RequiresReply = true,
            ReplyStatus = ReplyStatus.Pending,
            Status = AssignmentStatus.Active,
            CreatedById = 1,
            CreatedAt = incomingDate
        });

        db.DepartmentResponses.Add(new DepartmentResponse
        {
            TransactionId = 1,
            DepartmentId = 10,
            ResponseText = "إفادة المالية",
            Status = DepartmentResponseStatus.SubmittedForReview,
            SubmittedByUserId = 1,
            SubmittedAt = new DateTime(2026, 1, 6, 0, 0, 0, DateTimeKind.Utc),
            CreatedAt = incomingDate
        });
        db.DepartmentResponses.Add(new DepartmentResponse
        {
            TransactionId = 1,
            DepartmentId = 20,
            ResponseText = "إفادة الموارد",
            Status = DepartmentResponseStatus.SubmittedForReview,
            SubmittedByUserId = 1,
            SubmittedAt = new DateTime(2026, 1, 16, 0, 0, 0, DateTimeKind.Utc),
            CreatedAt = incomingDate
        });
        await db.SaveChangesAsync();

        var result = await service.GetAssignmentsAsync(1, new TestCurrentUser(UserRole.Admin));

        Assert.NotNull(result);
        Assert.Equal(2, result.Count);

        var fin = result.Single(r => r.DepartmentId == 10);
        var hr = result.Single(r => r.DepartmentId == 20);

        Assert.Equal(5, fin.DepartmentCompletionDays);
        Assert.Equal(15, hr.DepartmentCompletionDays);
    }

    [Fact]
    public async Task GetAssignmentsAsync_CanAdminEdit_is_true_for_Admin_false_for_others()
    {
        var (service, db) = await CreateServiceAsync(nameof(GetAssignmentsAsync_CanAdminEdit_is_true_for_Admin_false_for_others));
        await SeedTransactionAsync(db, 1);

        db.Assignments.Add(new Assignment
        {
            TransactionId = 1,
            DepartmentId = 10,
            AssignedDate = new DateTime(2026, 1, 5, 0, 0, 0, DateTimeKind.Utc),
            RequiresReply = true,
            ReplyStatus = ReplyStatus.Pending,
            Status = AssignmentStatus.Active,
            CreatedById = 1,
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var adminResult = await service.GetAssignmentsAsync(1, new TestCurrentUser(UserRole.Admin));
        var supervisorResult = await service.GetAssignmentsAsync(1, new TestCurrentUser(UserRole.Supervisor));

        Assert.True(adminResult![0].CanAdminEdit);
        Assert.False(supervisorResult![0].CanAdminEdit);
    }

    [Fact]
    public async Task AdminEditAssignmentAsync_updates_LetterNumber_and_logs_audit()
    {
        var (service, db) = await CreateServiceAsync(nameof(AdminEditAssignmentAsync_updates_LetterNumber_and_logs_audit));
        var incomingDate = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        await SeedTransactionAsync(db, 1, incomingDate);

        var assignment = new Assignment
        {
            Id = 501,
            TransactionId = 1,
            DepartmentId = 10,
            AssignedDate = incomingDate,
            RequiresReply = true,
            ReplyStatus = ReplyStatus.Pending,
            Status = AssignmentStatus.Active,
            CreatedById = 1,
            CreatedAt = incomingDate
        };
        db.Assignments.Add(assignment);
        await db.SaveChangesAsync();

        var result = await service.AdminEditAssignmentAsync(1, 501,
            new AdminEditAssignmentRequest
            {
                LetterNumber = "خ-2026/500",
                RequiredAction = "مراجعة المستندات",
            }, userId: 1);

        Assert.NotNull(result);
        Assert.Equal("خ-2026/500", result.LetterNumber);
        Assert.Equal("مراجعة المستندات", result.RequiredAction);

        var log = await db.AuditLogs.FirstOrDefaultAsync(a =>
            a.Action == AuditAction.AdminEditAssignment && a.EntityId == 501);
        Assert.NotNull(log);
        Assert.Contains("2026/500", log.NewValue);
    }

    [Fact]
    public async Task AdminEditAssignmentAsync_returns_null_for_missing_assignment()
    {
        var (service, _) = await CreateServiceAsync(nameof(AdminEditAssignmentAsync_returns_null_for_missing_assignment));

        var result = await service.AdminEditAssignmentAsync(1, 999,
            new AdminEditAssignmentRequest { LetterNumber = "خ-001" }, userId: 1);

        Assert.Null(result);
    }

    [Fact]
    public async Task AddAssignmentAsync_persists_LetterNumber()
    {
        var (service, db) = await CreateServiceAsync(nameof(AddAssignmentAsync_persists_LetterNumber));
        var incomingDate = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        await SeedTransactionAsync(db, 1, incomingDate);

        var result = await service.AddAssignmentAsync(1, new CreateAssignmentRequest
        {
            DepartmentId = 10,
            AssignedDate = incomingDate,
            LetterNumber = "خ-2026/999",
        }, userId: 1);

        Assert.Equal("خ-2026/999", result.LetterNumber);
        var persisted = await db.Assignments.FirstOrDefaultAsync(a => a.TransactionId == 1 && a.DepartmentId == 10);
        Assert.Equal("خ-2026/999", persisted?.LetterNumber);
    }
}
