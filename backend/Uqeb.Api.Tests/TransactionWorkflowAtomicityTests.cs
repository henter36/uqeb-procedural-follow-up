using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Uqeb.Api.Data;
using Uqeb.Api.DTOs.Transactions;
using Uqeb.Api.Helpers;
using Uqeb.Api.Models.Entities;
using Uqeb.Api.Models.Enums;
using Uqeb.Api.Services;
using Xunit;

namespace Uqeb.Api.Tests;

public class TransactionWorkflowAtomicityTests
{
    private sealed class SaveChangesCounterInterceptor : SaveChangesInterceptor
    {
        public int Count { get; private set; }

        public void Reset() => Count = 0;

        public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
        {
            Count++;
            return base.SavingChanges(eventData, result);
        }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            Count++;
            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }

    private sealed class StubTrackingNumberService : ITrackingNumberService
    {
        private int _counter;

        public Task<string> GenerateNextAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult($"UQEB-2026-{Interlocked.Increment(ref _counter):D5}");
    }

    private sealed class TestCacheInvalidation : ICacheInvalidationService
    {
        public int TransactionChangeInvalidations { get; private set; }

        public void ResetInvalidations() => TransactionChangeInvalidations = 0;

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
        public void InvalidateOnTransactionChange() => TransactionChangeInvalidations++;
        public void InvalidateReferenceData() { }
    }

    private sealed class TestCurrentUser : ICurrentUserService
    {
        public TestCurrentUser(int userId, UserRole role, int? departmentId = null)
        {
            UserId = userId;
            Role = role;
            DepartmentId = departmentId;
        }

        public int UserId { get; }
        public string Username => "tester";
        public UserRole Role { get; }
        public int? DepartmentId { get; }
        public bool IsAuthenticated => true;
    }

    private static (AppDbContext Db, SaveChangesCounterInterceptor Counter) CreateDb(string name)
    {
        var counter = new SaveChangesCounterInterceptor();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(name)
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .AddInterceptors(counter)
            .Options;
        return (new AppDbContext(options), counter);
    }

    private static async Task<(TransactionService Service, AppDbContext Db, SaveChangesCounterInterceptor Counter, TestCacheInvalidation Cache)> CreateServiceAsync(string dbName)
    {
        var (db, counter) = CreateDb(dbName);
        var cache = new TestCacheInvalidation();
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
        db.Categories.Add(new Category { Id = 1, Name = "عام", NameNormalized = "عام", IsActive = true });
        db.ExternalParties.Add(new ExternalParty { Id = 1, Name = "جهة", NameNormalized = "جهة", IsActive = true });
        db.Departments.AddRange(
            new Department { Id = 10, Name = "المالية", NameNormalized = "المالية", IsActive = true },
            new Department { Id = 11, Name = "الموارد", NameNormalized = "الموارد", IsActive = true },
            new Department { Id = 99, Name = "غير مرتبطة", NameNormalized = "غير مرتبطة", IsActive = true });
        await db.SaveChangesAsync();
        counter.Reset();

        var service = new TransactionService(
            db,
            new AuditService(db),
            new StubTrackingNumberService(),
            cache);

        return (service, db, counter, cache);
    }

    private static CreateTransactionRequest BuildCreateRequest(params int[] outgoingDepartmentIds) =>
        new()
        {
            IncomingNumber = "IN-WF-1001",
            IncomingDate = DateTime.UtcNow.Date,
            Subject = "معاملة سير عمل",
            IncomingSourceType = IncomingSourceType.External.ToString(),
            IncomingFromPartyId = 1,
            OutgoingNumber = "OUT-WF-1001",
            OutgoingDate = DateTime.UtcNow.Date,
            OutgoingDepartmentIds = outgoingDepartmentIds.ToList(),
            ResponseType = ResponseType.External.ToString(),
            ResponseDueDays = 30,
            Priority = Priority.Normal.ToString(),
            CategoryId = 1
        };

    [Fact]
    public async Task CancelAsync_authorization_failure_does_not_invalidate_cache()
    {
        var (service, db, _, cache) = await CreateServiceAsync(nameof(CancelAsync_authorization_failure_does_not_invalidate_cache));
        var created = await service.CreateAsync(BuildCreateRequest(10), userId: 1);
        cache.ResetInvalidations();

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            service.CancelAsync(created!.Id, userId: 2, role: UserRole.DepartmentUser));

        Assert.Equal(0, cache.TransactionChangeInvalidations);
        Assert.NotEqual(TransactionStatus.Cancelled, await db.Transactions.Where(t => t.Id == created.Id).Select(t => t.Status).FirstAsync());
    }

    [Fact]
    public async Task CancelAsync_persists_status_and_audit_with_bounded_save_changes()
    {
        var (service, db, counter, cache) = await CreateServiceAsync(nameof(CancelAsync_persists_status_and_audit_with_bounded_save_changes));
        var created = await service.CreateAsync(BuildCreateRequest(10), userId: 1);
        counter.Reset();
        cache.ResetInvalidations();

        var cancelled = await service.CancelAsync(created!.Id, userId: 1, role: UserRole.Admin);

        Assert.True(cancelled);
        Assert.InRange(counter.Count, 1, 2);
        Assert.Equal(1, cache.TransactionChangeInvalidations);
        Assert.Equal(TransactionStatus.Cancelled, await db.Transactions.Where(t => t.Id == created.Id).Select(t => t.Status).FirstAsync());
        Assert.True(await db.AuditLogs.AnyAsync(a => a.Action == AuditAction.Cancel && a.TransactionId == created.Id));
    }

    [Fact]
    public async Task AddAssignmentAsync_updates_status_and_audit_atomically()
    {
        var (service, db, counter, cache) = await CreateServiceAsync(nameof(AddAssignmentAsync_updates_status_and_audit_atomically));
        var created = await service.CreateAsync(BuildCreateRequest(10), userId: 1);
        counter.Reset();
        cache.ResetInvalidations();

        var assignment = await service.AddAssignmentAsync(created!.Id, new CreateAssignmentRequest
        {
            DepartmentId = 11,
            AssignedDate = DateTime.UtcNow.Date,
            RequiredAction = "متابعة",
            ReplyDueDays = 5
        }, userId: 1);

        Assert.InRange(counter.Count, 1, 2);
        Assert.Equal(1, cache.TransactionChangeInvalidations);
        Assert.Equal(TransactionStatus.WaitingForReply, await db.Transactions.Where(t => t.Id == created.Id).Select(t => t.Status).FirstAsync());
        Assert.True(await db.AuditLogs.AnyAsync(a => a.Action == AuditAction.AddAssignment && a.EntityId == assignment.Id));
    }

    [Fact]
    public async Task ReplyAssignmentAsync_authorization_failure_does_not_invalidate_cache()
    {
        var (service, _, counter, cache) = await CreateServiceAsync(nameof(ReplyAssignmentAsync_authorization_failure_does_not_invalidate_cache));
        var created = await service.CreateAsync(BuildCreateRequest(10), userId: 1);
        var assignment = await service.AddAssignmentAsync(created!.Id, new CreateAssignmentRequest
        {
            DepartmentId = 10,
            AssignedDate = DateTime.UtcNow.Date,
            RequiredAction = "متابعة",
            ReplyDueDays = 5
        }, userId: 1);
        counter.Reset();
        cache.ResetInvalidations();

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            service.ReplyAssignmentAsync(
                created.Id,
                assignment.Id,
                new ReplyAssignmentRequest { ReplyDate = DateTime.UtcNow.Date, ReplySummary = "تم" },
                new TestCurrentUser(2, UserRole.DepartmentUser, departmentId: 11)));

        Assert.Equal(0, cache.TransactionChangeInvalidations);
    }

    [Fact]
    public async Task AddFollowUpAsync_validation_failure_does_not_write_success_audit()
    {
        var (service, db, counter, cache) = await CreateServiceAsync(nameof(AddFollowUpAsync_validation_failure_does_not_write_success_audit));
        var created = await service.CreateAsync(BuildCreateRequest(10), userId: 1);
        var auditCountBefore = await db.AuditLogs.CountAsync();
        counter.Reset();
        cache.ResetInvalidations();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.AddFollowUpAsync(created!.Id, new CreateFollowUpRequest
            {
                FollowUpDate = DateTime.UtcNow.Date,
                DepartmentIds = new List<int> { 99 }
            }, userId: 1));

        Assert.Equal(auditCountBefore, await db.AuditLogs.CountAsync());
        Assert.Equal(0, cache.TransactionChangeInvalidations);
        Assert.Equal(0, counter.Count);
    }

    [Fact]
    public async Task CloseAsync_validation_failure_does_not_invalidate_cache()
    {
        var (service, _, counter, cache) = await CreateServiceAsync(nameof(CloseAsync_validation_failure_does_not_invalidate_cache));
        var created = await service.CreateAsync(BuildCreateRequest(10), userId: 1);
        cache.ResetInvalidations();
        counter.Reset();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.CloseAsync(created!.Id, userId: 1, role: UserRole.Admin));

        Assert.Equal(0, cache.TransactionChangeInvalidations);
        Assert.InRange(counter.Count, 0, 1);
    }

    [Fact]
    public async Task CloseAsync_blocks_when_required_department_response_is_not_approved()
    {
        var (service, db, _, cache) = await CreateServiceAsync(nameof(CloseAsync_blocks_when_required_department_response_is_not_approved));
        var created = await service.CreateAsync(BuildCreateRequest(10, 11), userId: 1);
        var dept10Response = new DepartmentResponse
        {
            TransactionId = created!.Id,
            DepartmentId = 10,
            ResponseText = "إفادة معتمدة",
            Status = DepartmentResponseStatus.Approved,
            SubmittedByUserId = 1,
            SubmittedAt = DateTime.UtcNow,
            ReviewedByUserId = 1,
            ReviewedAt = DateTime.UtcNow,
        };
        db.DepartmentResponses.Add(dept10Response);
        await db.SaveChangesAsync();
        cache.ResetInvalidations();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.CloseAsync(created.Id, userId: 1, role: UserRole.Admin));

        Assert.Contains("إفادات ناقصة", ex.Message);
        Assert.Contains("الموارد", ex.Message);
        Assert.Equal(0, cache.TransactionChangeInvalidations);
    }
}
