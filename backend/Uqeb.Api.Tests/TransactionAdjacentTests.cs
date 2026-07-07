using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Uqeb.Api.Data;
using Uqeb.Api.Models.Entities;
using Uqeb.Api.Models.Enums;
using Uqeb.Api.Services;
using Xunit;

namespace Uqeb.Api.Tests;

public class TransactionAdjacentTests
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
        db.Departments.Add(new Department { Id = 10, Name = "المالية", NameNormalized = "المالية", IsActive = true });
        db.Departments.Add(new Department { Id = 20, Name = "الموارد", NameNormalized = "الموارد", IsActive = true });
        await db.SaveChangesAsync();

        var service = new TransactionService(
            db,
            new AuditService(db),
            new StubTrackingNumberService(),
            new TestCacheInvalidation(),
            new RecurringTransactionTemplateService(db, new AuditService(db), new StubTrackingNumberService()));

        return (service, db);
    }

    private static Transaction BuildTransaction(int id, DateTime incomingDate) => new()
    {
        Id = id,
        InternalTrackingNumber = $"UQEB-{id}",
        IncomingNumber = $"IN-{id}",
        IncomingDate = incomingDate,
        Subject = "معاملة",
        IncomingSourceType = IncomingSourceType.External,
        IncomingFrom = "جهة",
        Priority = Priority.Normal,
        Status = TransactionStatus.New,
        CreatedById = 1,
        CreatedAt = incomingDate
    };

    [Fact]
    public async Task GetAdjacentAsync_ReturnsNull_WhenTransactionDoesNotExist()
    {
        var (service, _) = await CreateServiceAsync(nameof(GetAdjacentAsync_ReturnsNull_WhenTransactionDoesNotExist));

        var result = await service.GetAdjacentAsync(999, new TestCurrentUser(UserRole.Admin));

        Assert.Null(result);
    }

    [Fact]
    public async Task GetAdjacentAsync_ReturnsPreviousAndNextIds_OrderedByIncomingDateThenId()
    {
        var (service, db) = await CreateServiceAsync(nameof(GetAdjacentAsync_ReturnsPreviousAndNextIds_OrderedByIncomingDateThenId));
        db.Transactions.AddRange(
            BuildTransaction(1, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)),
            BuildTransaction(2, new DateTime(2026, 1, 5, 0, 0, 0, DateTimeKind.Utc)),
            BuildTransaction(3, new DateTime(2026, 1, 10, 0, 0, 0, DateTimeKind.Utc)));
        await db.SaveChangesAsync();

        var result = await service.GetAdjacentAsync(2, new TestCurrentUser(UserRole.Admin));

        Assert.NotNull(result);
        Assert.Equal(1, result!.PreviousId);
        Assert.Equal(3, result.NextId);
    }

    [Fact]
    public async Task GetAdjacentAsync_ReturnsNullPrevious_ForEarliestTransaction()
    {
        var (service, db) = await CreateServiceAsync(nameof(GetAdjacentAsync_ReturnsNullPrevious_ForEarliestTransaction));
        db.Transactions.AddRange(
            BuildTransaction(1, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)),
            BuildTransaction(2, new DateTime(2026, 1, 5, 0, 0, 0, DateTimeKind.Utc)));
        await db.SaveChangesAsync();

        var result = await service.GetAdjacentAsync(1, new TestCurrentUser(UserRole.Admin));

        Assert.NotNull(result);
        Assert.Null(result!.PreviousId);
        Assert.Equal(2, result.NextId);
    }

    [Fact]
    public async Task GetAdjacentAsync_ReturnsNullNext_ForLatestTransaction()
    {
        var (service, db) = await CreateServiceAsync(nameof(GetAdjacentAsync_ReturnsNullNext_ForLatestTransaction));
        db.Transactions.AddRange(
            BuildTransaction(1, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)),
            BuildTransaction(2, new DateTime(2026, 1, 5, 0, 0, 0, DateTimeKind.Utc)));
        await db.SaveChangesAsync();

        var result = await service.GetAdjacentAsync(2, new TestCurrentUser(UserRole.Admin));

        Assert.NotNull(result);
        Assert.Equal(1, result!.PreviousId);
        Assert.Null(result.NextId);
    }

    [Fact]
    public async Task GetAdjacentAsync_BreaksTiesBySameIncomingDateUsingId()
    {
        var (service, db) = await CreateServiceAsync(nameof(GetAdjacentAsync_BreaksTiesBySameIncomingDateUsingId));
        var sameDate = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        db.Transactions.AddRange(
            BuildTransaction(1, sameDate),
            BuildTransaction(2, sameDate),
            BuildTransaction(3, sameDate));
        await db.SaveChangesAsync();

        var result = await service.GetAdjacentAsync(2, new TestCurrentUser(UserRole.Admin));

        Assert.NotNull(result);
        Assert.Equal(1, result!.PreviousId);
        Assert.Equal(3, result.NextId);
    }

    [Fact]
    public async Task GetAdjacentAsync_ScopesToAssignedTransactions_ForDepartmentUser()
    {
        var (service, db) = await CreateServiceAsync(nameof(GetAdjacentAsync_ScopesToAssignedTransactions_ForDepartmentUser));
        var t1 = BuildTransaction(1, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        var t2 = BuildTransaction(2, new DateTime(2026, 1, 5, 0, 0, 0, DateTimeKind.Utc));
        var t3 = BuildTransaction(3, new DateTime(2026, 1, 10, 0, 0, 0, DateTimeKind.Utc));
        db.Transactions.AddRange(t1, t2, t3);
        await db.SaveChangesAsync();

        // Only t1 and t3 are assigned to department 10; t2 is assigned to a different
        // department and must not surface as an adjacent transaction for that user.
        db.Assignments.Add(new Assignment
        {
            TransactionId = t1.Id,
            DepartmentId = 10,
            AssignedDate = t1.IncomingDate,
            RequiresReply = true,
            Status = AssignmentStatus.Active,
            CreatedById = 1,
            CreatedAt = t1.IncomingDate
        });
        db.Assignments.Add(new Assignment
        {
            TransactionId = t2.Id,
            DepartmentId = 20,
            AssignedDate = t2.IncomingDate,
            RequiresReply = true,
            Status = AssignmentStatus.Active,
            CreatedById = 1,
            CreatedAt = t2.IncomingDate
        });
        db.Assignments.Add(new Assignment
        {
            TransactionId = t3.Id,
            DepartmentId = 10,
            AssignedDate = t3.IncomingDate,
            RequiresReply = true,
            Status = AssignmentStatus.Active,
            CreatedById = 1,
            CreatedAt = t3.IncomingDate
        });
        await db.SaveChangesAsync();

        var result = await service.GetAdjacentAsync(1, new TestCurrentUser(UserRole.DepartmentUser, userId: 2, departmentId: 10));

        Assert.NotNull(result);
        Assert.Null(result!.PreviousId);
        Assert.Equal(3, result.NextId);
    }
}
