using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Uqeb.Api.Data;
using Uqeb.Api.DTOs.Transactions;
using Uqeb.Api.Models.Entities;
using Uqeb.Api.Models.Enums;
using Uqeb.Api.Services;
using Xunit;

namespace Uqeb.Api.Tests;

public class TransactionWorkspaceReadModelTests
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
        db.Users.Add(new User
        {
            Id = 1,
            Username = "admin",
            PasswordHash = "hash",
            FullName = "Admin",
            Role = UserRole.Admin,
            IsActive = true
        });
        db.Users.Add(new User
        {
            Id = 2,
            Username = "dept-user",
            PasswordHash = "hash",
            FullName = "Dept User",
            Role = UserRole.DepartmentUser,
            DepartmentId = 10,
            IsActive = true
        });
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

    private static async Task<Transaction> SeedTransactionAsync(AppDbContext db, int id = 100)
    {
        var transaction = new Transaction
        {
            Id = id,
            InternalTrackingNumber = $"UQEB-2026-{id:00000}",
            IncomingNumber = $"IN-{id}",
            IncomingDate = DateTime.UtcNow.AddDays(-7),
            Subject = "معاملة مساحة العمل",
            IncomingSourceType = IncomingSourceType.External,
            IncomingFrom = "جهة",
            RequiresResponse = true,
            ResponseType = ResponseType.External,
            ResponseDueDate = DateTime.UtcNow.AddDays(3),
            ResponseDueDays = 10,
            Priority = Priority.Normal,
            Status = TransactionStatus.InProgress,
            CreatedById = 1,
            CreatedAt = DateTime.UtcNow.AddDays(-7)
        };
        db.Transactions.Add(transaction);
        await db.SaveChangesAsync();
        return transaction;
    }

    [Fact]
    public async Task GetWorkspaceAsync_returns_null_when_transaction_missing()
    {
        var (service, _) = await CreateServiceAsync(nameof(GetWorkspaceAsync_returns_null_when_transaction_missing));
        var result = await service.GetWorkspaceAsync(999, new TestCurrentUser(UserRole.Admin));
        Assert.Null(result);
    }

    [Fact]
    public async Task GetWorkspaceAsync_returns_null_when_department_user_has_no_assignment()
    {
        var (service, db) = await CreateServiceAsync(nameof(GetWorkspaceAsync_returns_null_when_department_user_has_no_assignment));
        await SeedTransactionAsync(db);

        var result = await service.GetWorkspaceAsync(100, new TestCurrentUser(UserRole.DepartmentUser, userId: 2, departmentId: 10));
        Assert.Null(result);
    }

    [Fact]
    public async Task SearchAsync_DepartmentUser_ReturnsOnlyOwnDepartmentTransactions()
    {
        var (service, db) = await CreateServiceAsync(nameof(SearchAsync_DepartmentUser_ReturnsOnlyOwnDepartmentTransactions));
        var own = await SeedTransactionAsync(db, 101);
        var other = await SeedTransactionAsync(db, 102);
        var cancelled = await SeedTransactionAsync(db, 103);
        cancelled.Status = TransactionStatus.Cancelled;
        db.Assignments.AddRange(
            new Assignment
            {
                TransactionId = own.Id,
                DepartmentId = 10,
                AssignedDate = DateTime.UtcNow,
                RequiresReply = true,
                ReplyStatus = ReplyStatus.Pending,
                Status = AssignmentStatus.Active,
                CreatedById = 1
            },
            new Assignment
            {
                TransactionId = other.Id,
                DepartmentId = 20,
                AssignedDate = DateTime.UtcNow,
                RequiresReply = true,
                ReplyStatus = ReplyStatus.Pending,
                Status = AssignmentStatus.Active,
                CreatedById = 1
            },
            new Assignment
            {
                TransactionId = cancelled.Id,
                DepartmentId = 10,
                AssignedDate = DateTime.UtcNow,
                RequiresReply = true,
                ReplyStatus = ReplyStatus.Pending,
                Status = AssignmentStatus.Cancelled,
                CreatedById = 1
            });
        await db.SaveChangesAsync();

        var result = await service.SearchAsync(
            new TransactionSearchRequest { Page = 1, PageSize = 20 },
            new TestCurrentUser(UserRole.DepartmentUser, userId: 2, departmentId: 10));

        Assert.Contains(result.Items, tx => tx.Id == own.Id);
        Assert.DoesNotContain(result.Items, tx => tx.Id == other.Id);
        Assert.DoesNotContain(result.Items, tx => tx.Id == cancelled.Id);
    }

    [Fact]
    public async Task SearchAsync_DefaultStatusScope_ReturnsActiveOnly()
    {
        var (service, db) = await CreateServiceAsync(nameof(SearchAsync_DefaultStatusScope_ReturnsActiveOnly));
        var active = await SeedTransactionAsync(db, 110);
        var closed = await SeedTransactionAsync(db, 111);
        var cancelled = await SeedTransactionAsync(db, 112);
        var archived = await SeedTransactionAsync(db, 113);
        closed.Status = TransactionStatus.Closed;
        cancelled.Status = TransactionStatus.Cancelled;
        archived.Status = TransactionStatus.Archived;
        await db.SaveChangesAsync();

        var result = await service.SearchAsync(
            new TransactionSearchRequest { Page = 1, PageSize = 20 },
            new TestCurrentUser(UserRole.Admin));

        Assert.Contains(result.Items, tx => tx.Id == active.Id);
        Assert.DoesNotContain(result.Items, tx => tx.Id == closed.Id);
        Assert.DoesNotContain(result.Items, tx => tx.Id == cancelled.Id);
        Assert.DoesNotContain(result.Items, tx => tx.Id == archived.Id);
    }

    [Fact]
    public async Task SearchAsync_ActiveStatusScope_ExcludesClosedCancelledAndArchived()
    {
        var (service, db) = await CreateServiceAsync(nameof(SearchAsync_ActiveStatusScope_ExcludesClosedCancelledAndArchived));
        var active = await SeedTransactionAsync(db, 114);
        var closed = await SeedTransactionAsync(db, 115);
        var cancelled = await SeedTransactionAsync(db, 116);
        var archived = await SeedTransactionAsync(db, 117);
        closed.Status = TransactionStatus.Closed;
        cancelled.Status = TransactionStatus.Cancelled;
        archived.Status = TransactionStatus.Archived;
        await db.SaveChangesAsync();

        var result = await service.SearchAsync(
            new TransactionSearchRequest { StatusScope = "active", Page = 1, PageSize = 20 },
            new TestCurrentUser(UserRole.Admin));

        Assert.Contains(result.Items, tx => tx.Id == active.Id);
        Assert.DoesNotContain(result.Items, tx => tx.Id == closed.Id);
        Assert.DoesNotContain(result.Items, tx => tx.Id == cancelled.Id);
        Assert.DoesNotContain(result.Items, tx => tx.Id == archived.Id);
    }

    [Fact]
    public async Task SearchAsync_ClosedStatusScope_ReturnsClosedOnly()
    {
        var (service, db) = await CreateServiceAsync(nameof(SearchAsync_ClosedStatusScope_ReturnsClosedOnly));
        var active = await SeedTransactionAsync(db, 116);
        var closed = await SeedTransactionAsync(db, 117);
        closed.Status = TransactionStatus.Closed;
        await db.SaveChangesAsync();

        var result = await service.SearchAsync(
            new TransactionSearchRequest { StatusScope = "closed", Page = 1, PageSize = 20 },
            new TestCurrentUser(UserRole.Admin));

        Assert.DoesNotContain(result.Items, tx => tx.Id == active.Id);
        Assert.Contains(result.Items, tx => tx.Id == closed.Id);
        Assert.All(result.Items, tx => Assert.Equal(nameof(TransactionStatus.Closed), tx.Status));
    }

    [Fact]
    public async Task SearchAsync_AllStatusScope_ReturnsActiveAndClosed()
    {
        var (service, db) = await CreateServiceAsync(nameof(SearchAsync_AllStatusScope_ReturnsActiveAndClosed));
        var active = await SeedTransactionAsync(db, 118);
        var closed = await SeedTransactionAsync(db, 119);
        closed.Status = TransactionStatus.Closed;
        await db.SaveChangesAsync();

        var result = await service.SearchAsync(
            new TransactionSearchRequest { StatusScope = "all", Page = 1, PageSize = 20 },
            new TestCurrentUser(UserRole.Admin));

        Assert.Contains(result.Items, tx => tx.Id == active.Id);
        Assert.Contains(result.Items, tx => tx.Id == closed.Id);
    }

    [Fact]
    public async Task SearchAsync_DepartmentUser_AllStatusScope_ReturnsOnlyOwnDepartmentTransactions()
    {
        var (service, db) = await CreateServiceAsync(nameof(SearchAsync_DepartmentUser_AllStatusScope_ReturnsOnlyOwnDepartmentTransactions));
        var ownClosed = await SeedTransactionAsync(db, 120);
        var otherClosed = await SeedTransactionAsync(db, 121);
        ownClosed.Status = TransactionStatus.Closed;
        otherClosed.Status = TransactionStatus.Closed;
        db.Assignments.AddRange(
            new Assignment
            {
                TransactionId = ownClosed.Id,
                DepartmentId = 10,
                AssignedDate = DateTime.UtcNow,
                RequiresReply = true,
                ReplyStatus = ReplyStatus.Pending,
                Status = AssignmentStatus.Active,
                CreatedById = 1
            },
            new Assignment
            {
                TransactionId = otherClosed.Id,
                DepartmentId = 20,
                AssignedDate = DateTime.UtcNow,
                RequiresReply = true,
                ReplyStatus = ReplyStatus.Pending,
                Status = AssignmentStatus.Active,
                CreatedById = 1
            });
        await db.SaveChangesAsync();

        var result = await service.SearchAsync(
            new TransactionSearchRequest { StatusScope = "all", Page = 1, PageSize = 20 },
            new TestCurrentUser(UserRole.DepartmentUser, userId: 2, departmentId: 10));

        Assert.Contains(result.Items, tx => tx.Id == ownClosed.Id);
        Assert.DoesNotContain(result.Items, tx => tx.Id == otherClosed.Id);
    }

    [Fact]
    public async Task SearchAsync_InvalidStatusScope_ThrowsInvalidOperation()
    {
        var (service, db) = await CreateServiceAsync(nameof(SearchAsync_InvalidStatusScope_ThrowsInvalidOperation));
        await SeedTransactionAsync(db, 122);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.SearchAsync(
                new TransactionSearchRequest { StatusScope = "invalid", Page = 1, PageSize = 20 },
                new TestCurrentUser(UserRole.Admin)));

        Assert.Equal("نطاق حالة المعاملات غير صالح.", exception.Message);
    }

    [Fact]
    public async Task SearchAsync_DepartmentUserWithoutDepartment_ThrowsUnauthorized()
    {
        var (service, _) = await CreateServiceAsync(nameof(SearchAsync_DepartmentUserWithoutDepartment_ThrowsUnauthorized));

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            service.SearchAsync(
                new TransactionSearchRequest { Page = 1, PageSize = 20 },
                new TestCurrentUser(UserRole.DepartmentUser, userId: 2)));
    }

    [Fact]
    public async Task GetBasicByIdAsync_DepartmentUserCannotOpenOtherDepartmentTransaction()
    {
        var (service, db) = await CreateServiceAsync(nameof(GetBasicByIdAsync_DepartmentUserCannotOpenOtherDepartmentTransaction));
        var other = await SeedTransactionAsync(db, 104);
        db.Assignments.Add(new Assignment
        {
            TransactionId = other.Id,
            DepartmentId = 20,
            AssignedDate = DateTime.UtcNow,
            RequiresReply = true,
            ReplyStatus = ReplyStatus.Pending,
            Status = AssignmentStatus.Active,
            CreatedById = 1
        });
        await db.SaveChangesAsync();

        var result = await service.GetBasicByIdAsync(
            other.Id,
            new TestCurrentUser(UserRole.DepartmentUser, userId: 2, departmentId: 10));

        Assert.Null(result);
    }

    [Fact]
    public async Task GetBasicByIdAsync_DepartmentUserWithoutDepartment_ThrowsUnauthorized()
    {
        var (service, db) = await CreateServiceAsync(nameof(GetBasicByIdAsync_DepartmentUserWithoutDepartment_ThrowsUnauthorized));
        var transaction = await SeedTransactionAsync(db, 105);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            service.GetBasicByIdAsync(
                transaction.Id,
                new TestCurrentUser(UserRole.DepartmentUser, userId: 2)));
    }

    [Fact]
    public async Task GetWorkspaceAsync_returns_consolidated_payload_for_admin()
    {
        var (service, db) = await CreateServiceAsync(nameof(GetWorkspaceAsync_returns_consolidated_payload_for_admin));
        var transaction = await SeedTransactionAsync(db);

        db.Assignments.Add(new Assignment
        {
            TransactionId = transaction.Id,
            DepartmentId = 10,
            AssignedDate = DateTime.UtcNow.AddDays(-2),
            RequiresReply = true,
            ReplyStatus = ReplyStatus.Pending,
            Status = AssignmentStatus.Active,
            DueDate = DateTime.UtcNow.AddDays(1),
            CreatedById = 1
        });
        db.FollowUps.Add(new FollowUp
        {
            TransactionId = transaction.Id,
            FollowUpDate = DateTime.UtcNow.AddDays(-1),
            RequiresReply = false,
            ReplyStatus = ReplyStatus.Pending,
            CreatedById = 1
        });
        db.Attachments.Add(new Attachment
        {
            TransactionId = transaction.Id,
            OriginalFileName = "doc.pdf",
            StoredFileName = "stored.pdf",
            FilePath = "/tmp/doc.pdf",
            ContentType = "application/pdf",
            FileSize = 100,
            UploadedById = 1,
            UploadedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var result = await service.GetWorkspaceAsync(transaction.Id, new TestCurrentUser(UserRole.Admin));

        Assert.NotNull(result);
        Assert.Equal(transaction.Id, result!.Transaction.Id);
        Assert.Single(result.Assignments);
        Assert.Single(result.FollowUps);
        Assert.Single(result.Attachments);
        Assert.True(result.TemporalFacts.IsOpen);
        Assert.True(result.AllowedActions.CanEdit);
        Assert.True(result.AllowedActions.ShowMutationActions);
        Assert.True(result.AllowedActions.CanRegisterResponse);
    }

    [Fact]
    public async Task GetWorkspaceAsync_includes_data_for_department_user_with_assignment()
    {
        var (service, db) = await CreateServiceAsync(nameof(GetWorkspaceAsync_includes_data_for_department_user_with_assignment));
        var transaction = await SeedTransactionAsync(db);
        db.Assignments.Add(new Assignment
        {
            TransactionId = transaction.Id,
            DepartmentId = 10,
            AssignedDate = DateTime.UtcNow.AddDays(-1),
            RequiresReply = true,
            ReplyStatus = ReplyStatus.Pending,
            Status = AssignmentStatus.Active,
            CreatedById = 1
        });
        await db.SaveChangesAsync();

        var result = await service.GetWorkspaceAsync(
            transaction.Id,
            new TestCurrentUser(UserRole.DepartmentUser, userId: 2, departmentId: 10));

        Assert.NotNull(result);
        Assert.Single(result!.Assignments);
        Assert.False(result.AllowedActions.ShowMutationActions);
        Assert.False(result.AllowedActions.CanReply);
    }

    [Fact]
    public async Task GetWorkspaceAsync_does_not_include_audit_logs()
    {
        var (service, db) = await CreateServiceAsync(nameof(GetWorkspaceAsync_does_not_include_audit_logs));
        var transaction = await SeedTransactionAsync(db);
        db.AuditLogs.Add(new AuditLog
        {
            TransactionId = transaction.Id,
            UserId = 1,
            Action = AuditAction.Create,
            EntityName = "Transaction",
            EntityId = transaction.Id,
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var result = await service.GetWorkspaceAsync(transaction.Id, new TestCurrentUser(UserRole.Admin));

        Assert.NotNull(result);
        Assert.Empty(result!.Transaction.AuditLogs);
    }
}
