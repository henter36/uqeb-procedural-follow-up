using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using System.Text.Json;
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
        db.Users.Add(new User
        {
            Id = 3,
            Username = "supervisor",
            PasswordHash = "hash",
            FullName = "Supervisor",
            Role = UserRole.Supervisor,
            IsActive = true
        });
        db.Users.Add(new User
        {
            Id = 4,
            Username = "data-entry",
            PasswordHash = "hash",
            FullName = "Data Entry",
            Role = UserRole.DataEntry,
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
            cache,
            new RecurringTransactionTemplateService(db, new AuditService(db), new StubTrackingNumberService()));

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

    // Mirrors TransactionService.GetSaudiToday() (UTC+3) so this is always strictly
    // after "today" regardless of the UTC time of day the test happens to run at;
    // DateTime.UtcNow.Date.AddDays(1) is not always future once Saudi time has
    // already rolled over to the next day (UTC 21:00-24:00).
    private static DateTime SaudiToday() => DateTime.UtcNow.AddHours(3).Date;

    private static DateTime DayAfterSaudiToday() => DateTime.UtcNow.AddHours(3).Date.AddDays(1);

    private static async Task PrepareTransactionForCloseAsync(AppDbContext db, int transactionId)
    {
        var transaction = await db.Transactions
            .Include(t => t.Assignments)
            .SingleAsync(t => t.Id == transactionId);

        transaction.ResponseCompleted = true;
        transaction.ResponseCompletedDate = DateTime.UtcNow.Date;

        foreach (var assignment in transaction.Assignments.Where(a => a.Status == AssignmentStatus.Active))
        {
            assignment.RequiresReply = true;
            assignment.ReplyStatus = ReplyStatus.Replied;
        }

        await db.SaveChangesAsync();
    }

    private static async Task<DepartmentResponse> AddDepartmentResponseAsync(
        AppDbContext db,
        int transactionId,
        int departmentId,
        int submittedByUserId,
        DepartmentResponseStatus status)
    {
        var response = new DepartmentResponse
        {
            TransactionId = transactionId,
            DepartmentId = departmentId,
            ResponseText = "إفادة مكتملة",
            Status = status,
            SubmittedByUserId = submittedByUserId,
            SubmittedAt = status == DepartmentResponseStatus.Draft ? null : DateTime.UtcNow,
            ReviewedByUserId = status == DepartmentResponseStatus.Approved ? 1 : null,
            ReviewedAt = status == DepartmentResponseStatus.Approved ? DateTime.UtcNow : null,
        };
        db.DepartmentResponses.Add(response);
        await db.SaveChangesAsync();
        return response;
    }

    private static async Task AddDepartmentResponseAuditAsync(
        AppDbContext db,
        int transactionId,
        int responseId,
        int userId,
        AuditAction action)
    {
        db.AuditLogs.Add(new AuditLog
        {
            TransactionId = transactionId,
            EntityName = "DepartmentResponse",
            EntityId = responseId,
            UserId = userId,
            Action = action,
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
    }

    private static async Task<(TransactionDetailDto Created, DateTime ClosedAt, DateTime ResponseCompletedDate)> CreateClosedTransactionAsync(
        TransactionService service,
        AppDbContext db)
    {
        var created = await service.CreateAsync(BuildCreateRequest(10), userId: 1);
        var closedAt = SaudiToday().AddDays(-1);
        var responseCompletedDate = closedAt.AddDays(-1);
        var transaction = await db.Transactions.SingleAsync(t => t.Id == created.Id);
        transaction.IncomingDate = closedAt.AddDays(-10);
        transaction.ResponseCompleted = true;
        transaction.ResponseCompletedDate = responseCompletedDate;
        transaction.Status = TransactionStatus.Closed;
        transaction.ClosedAt = closedAt;
        await db.SaveChangesAsync();

        return (created, closedAt, responseCompletedDate);
    }

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
            LetterNumber = "OUT-WF-1001",
            RequiredAction = "متابعة",
            ReplyDueDays = 5
        }, userId: 1);

        Assert.InRange(counter.Count, 1, 2);
        Assert.Equal(1, cache.TransactionChangeInvalidations);
        Assert.Equal(TransactionStatus.WaitingForReply, await db.Transactions.Where(t => t.Id == created.Id).Select(t => t.Status).FirstAsync());
        Assert.Equal("OUT-WF-1001", await db.Assignments.Where(a => a.Id == assignment.Id).Select(a => a.LetterNumber).FirstAsync());
        Assert.True(await db.AuditLogs.AnyAsync(a => a.Action == AuditAction.AddAssignment && a.EntityId == assignment.Id));
    }

    [Fact]
    public async Task ReplyAssignmentAsync_missing_reply_date_returns_field_validation()
    {
        var (service, _, counter, cache) = await CreateServiceAsync(nameof(ReplyAssignmentAsync_missing_reply_date_returns_field_validation));
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

        var ex = await Assert.ThrowsAsync<FieldValidationException>(() =>
            service.ReplyAssignmentAsync(
                created.Id,
                assignment.Id,
                new ReplyAssignmentRequest { ReplyDate = default, ReplySummary = "تم" },
                new TestCurrentUser(1, UserRole.Admin)));

        Assert.Equal("تاريخ إنجاز الإدارة مطلوب.", ex.FieldErrors[nameof(ReplyAssignmentRequest.ReplyDate)]);
        Assert.Equal(0, cache.TransactionChangeInvalidations);
        Assert.Equal(0, counter.Count);
    }

    [Fact]
    public async Task ReplyAssignmentAsync_null_request_returns_validation_error()
    {
        var (service, _, counter, cache) = await CreateServiceAsync(nameof(ReplyAssignmentAsync_null_request_returns_validation_error));
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

        var ex = await Assert.ThrowsAsync<FieldValidationException>(() =>
            service.ReplyAssignmentAsync(
                created.Id,
                assignment.Id,
                null!,
                new TestCurrentUser(1, UserRole.Admin)));

        Assert.Equal("بيانات طلب الرد مطلوبة.", ex.FieldErrors[nameof(ReplyAssignmentRequest)]);
        Assert.Equal(0, cache.TransactionChangeInvalidations);
        Assert.Equal(0, counter.Count);
    }

    [Fact]
    public async Task AddAssignmentAsync_missing_assigned_date_returns_field_validation()
    {
        var (service, _, counter, cache) = await CreateServiceAsync(nameof(AddAssignmentAsync_missing_assigned_date_returns_field_validation));
        var created = await service.CreateAsync(BuildCreateRequest(10), userId: 1);
        counter.Reset();
        cache.ResetInvalidations();

        var ex = await Assert.ThrowsAsync<FieldValidationException>(() =>
            service.AddAssignmentAsync(created!.Id, new CreateAssignmentRequest
            {
                DepartmentId = 11,
                AssignedDate = default,
                RequiredAction = "متابعة",
                ReplyDueDays = 5
            }, userId: 1));

        Assert.Equal("تاريخ الإحالة مطلوب.", ex.FieldErrors[nameof(CreateAssignmentRequest.AssignedDate)]);
        Assert.Equal(0, cache.TransactionChangeInvalidations);
        Assert.Equal(0, counter.Count);
    }

    [Fact]
    public async Task AddAssignmentAsync_future_assigned_date_returns_field_validation()
    {
        var (service, _, counter, cache) = await CreateServiceAsync(nameof(AddAssignmentAsync_future_assigned_date_returns_field_validation));
        var created = await service.CreateAsync(BuildCreateRequest(10), userId: 1);
        counter.Reset();
        cache.ResetInvalidations();

        var ex = await Assert.ThrowsAsync<FieldValidationException>(() =>
            service.AddAssignmentAsync(created!.Id, new CreateAssignmentRequest
            {
                DepartmentId = 11,
                AssignedDate = DayAfterSaudiToday(),
                RequiredAction = "متابعة",
                ReplyDueDays = 5
            }, userId: 1));

        Assert.Equal("لا يمكن أن يكون التاريخ بعد تاريخ اليوم.", ex.FieldErrors[nameof(CreateAssignmentRequest.AssignedDate)]);
        Assert.Equal(0, cache.TransactionChangeInvalidations);
        Assert.Equal(0, counter.Count);
    }

    [Fact]
    public async Task AddAssignmentAsync_allows_future_due_date()
    {
        var (service, db, _, _) = await CreateServiceAsync(nameof(AddAssignmentAsync_allows_future_due_date));
        var created = await service.CreateAsync(BuildCreateRequest(10), userId: 1);
        var dueDate = DateTime.UtcNow.Date.AddDays(3);

        var assignment = await service.AddAssignmentAsync(created!.Id, new CreateAssignmentRequest
        {
            DepartmentId = 11,
            AssignedDate = DateTime.UtcNow.Date,
            RequiredAction = "متابعة",
            DueDate = dueDate
        }, userId: 1);

        Assert.Equal(dueDate, await db.Assignments.Where(a => a.Id == assignment.Id).Select(a => a.DueDate).SingleAsync());
    }

    [Fact]
    public async Task ReplyAssignmentAsync_future_reply_date_returns_field_validation()
    {
        var (service, _, counter, cache) = await CreateServiceAsync(nameof(ReplyAssignmentAsync_future_reply_date_returns_field_validation));
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

        var ex = await Assert.ThrowsAsync<FieldValidationException>(() =>
            service.ReplyAssignmentAsync(
                created.Id,
                assignment.Id,
                new ReplyAssignmentRequest { ReplyDate = DayAfterSaudiToday(), ReplySummary = "تم" },
                new TestCurrentUser(1, UserRole.Admin)));

        Assert.Equal("لا يمكن أن يكون التاريخ بعد تاريخ اليوم.", ex.FieldErrors[nameof(ReplyAssignmentRequest.ReplyDate)]);
        Assert.Equal(0, cache.TransactionChangeInvalidations);
        Assert.Equal(0, counter.Count);
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
    public async Task ReplyAssignmentAsync_department_user_cannot_reply_even_for_own_department()
    {
        var (service, _, counter, cache) = await CreateServiceAsync(nameof(ReplyAssignmentAsync_department_user_cannot_reply_even_for_own_department));
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
                new TestCurrentUser(2, UserRole.DepartmentUser, departmentId: 10)));

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
    public async Task CloseAsync_preserves_existing_closed_at_and_audits_it()
    {
        var (service, db, _, _) = await CreateServiceAsync(nameof(CloseAsync_preserves_existing_closed_at_and_audits_it));
        var created = await service.CreateAsync(BuildCreateRequest(10), userId: 1);
        await PrepareTransactionForCloseAsync(db, created.Id);
        await AddDepartmentResponseAsync(db, created.Id, 10, submittedByUserId: 1, DepartmentResponseStatus.Approved);
        var preservedClosedAt = SaudiToday().AddDays(-1);
        var transaction = await db.Transactions.SingleAsync(t => t.Id == created.Id);
        transaction.IncomingDate = preservedClosedAt.AddDays(-10);
        transaction.ResponseCompletedDate = preservedClosedAt.AddDays(-1);
        transaction.ClosedAt = preservedClosedAt;
        await db.SaveChangesAsync();

        await service.CloseAsync(created.Id, userId: 1, role: UserRole.Admin);

        var persisted = await db.Transactions.SingleAsync(t => t.Id == created.Id);
        Assert.Equal(TransactionStatus.Closed, persisted.Status);
        Assert.Equal(preservedClosedAt, persisted.ClosedAt);

        var audit = await db.AuditLogs.SingleAsync(a => a.Action == AuditAction.Close && a.TransactionId == created.Id);
        Assert.Contains(preservedClosedAt.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture), audit.NewValue);
    }

    [Fact]
    public async Task CloseAsync_sets_saudi_today_when_closed_at_is_missing()
    {
        var (service, db, _, _) = await CreateServiceAsync(nameof(CloseAsync_sets_saudi_today_when_closed_at_is_missing));
        var created = await service.CreateAsync(BuildCreateRequest(10), userId: 1);
        await PrepareTransactionForCloseAsync(db, created.Id);
        await AddDepartmentResponseAsync(db, created.Id, 10, submittedByUserId: 1, DepartmentResponseStatus.Approved);
        var today = SaudiToday();
        var transaction = await db.Transactions.SingleAsync(t => t.Id == created.Id);
        transaction.IncomingDate = today.AddDays(-10);
        transaction.ResponseCompletedDate = today.AddDays(-1);
        transaction.ClosedAt = null;
        await db.SaveChangesAsync();

        await service.CloseAsync(created.Id, userId: 1, role: UserRole.Admin);

        Assert.Equal(today, await db.Transactions.Where(t => t.Id == created.Id).Select(t => t.ClosedAt).SingleAsync());
    }

    [Fact]
    public async Task CloseAsync_rejects_closed_at_before_incoming_date()
    {
        var (service, db, _, _) = await CreateServiceAsync(nameof(CloseAsync_rejects_closed_at_before_incoming_date));
        var created = await service.CreateAsync(BuildCreateRequest(10), userId: 1);
        await PrepareTransactionForCloseAsync(db, created.Id);
        await AddDepartmentResponseAsync(db, created.Id, 10, submittedByUserId: 1, DepartmentResponseStatus.Approved);
        var today = SaudiToday();
        var transaction = await db.Transactions.SingleAsync(t => t.Id == created.Id);
        transaction.IncomingDate = today;
        transaction.ResponseCompletedDate = today;
        transaction.ClosedAt = today.AddDays(-1);
        await db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.CloseAsync(created.Id, userId: 1, role: UserRole.Admin));

        Assert.Equal("تاريخ إغلاق المعاملة لا يمكن أن يسبق تاريخ الوارد.", ex.Message);
        Assert.NotEqual(TransactionStatus.Closed, await db.Transactions.Where(t => t.Id == created.Id).Select(t => t.Status).SingleAsync());
    }

    [Fact]
    public async Task CloseAsync_rejects_closed_at_before_response_completed_date()
    {
        var (service, db, _, _) = await CreateServiceAsync(nameof(CloseAsync_rejects_closed_at_before_response_completed_date));
        var created = await service.CreateAsync(BuildCreateRequest(10), userId: 1);
        await PrepareTransactionForCloseAsync(db, created.Id);
        await AddDepartmentResponseAsync(db, created.Id, 10, submittedByUserId: 1, DepartmentResponseStatus.Approved);
        var today = SaudiToday();
        var transaction = await db.Transactions.SingleAsync(t => t.Id == created.Id);
        transaction.IncomingDate = today.AddDays(-10);
        transaction.ResponseCompletedDate = today;
        transaction.ClosedAt = today.AddDays(-1);
        await db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.CloseAsync(created.Id, userId: 1, role: UserRole.Admin));

        Assert.Equal("تاريخ إغلاق المعاملة لا يمكن أن يسبق تاريخ الإفادة.", ex.Message);
        Assert.NotEqual(TransactionStatus.Closed, await db.Transactions.Where(t => t.Id == created.Id).Select(t => t.Status).SingleAsync());
    }

    [Fact]
    public async Task CloseAsync_ignores_stale_response_completed_date_when_response_is_not_required()
    {
        var (service, db, _, _) = await CreateServiceAsync(nameof(CloseAsync_ignores_stale_response_completed_date_when_response_is_not_required));
        var created = await service.CreateAsync(BuildCreateRequest(10), userId: 1);
        var closedAt = SaudiToday().AddDays(-1);
        var transaction = await db.Transactions
            .Include(t => t.Assignments)
            .SingleAsync(t => t.Id == created.Id);
        transaction.IncomingDate = closedAt.AddDays(-10);
        transaction.RequiresResponse = false;
        transaction.ResponseType = ResponseType.None;
        transaction.ResponseDueDays = null;
        transaction.ResponseDueDate = null;
        transaction.ResponseCompletedDate = closedAt.AddDays(1);
        transaction.ClosedAt = closedAt;
        foreach (var assignment in transaction.Assignments)
            assignment.Status = AssignmentStatus.Completed;
        await db.SaveChangesAsync();

        await service.CloseAsync(created.Id, userId: 1, role: UserRole.Admin);

        var persisted = await db.Transactions.SingleAsync(t => t.Id == created.Id);
        Assert.Equal(TransactionStatus.Closed, persisted.Status);
        Assert.Equal(closedAt, persisted.ClosedAt);
    }

    [Fact]
    public async Task UpdateAsync_status_closed_resolves_closed_at_using_same_rules()
    {
        var (service, db, _, _) = await CreateServiceAsync(nameof(UpdateAsync_status_closed_resolves_closed_at_using_same_rules));
        var created = await service.CreateAsync(BuildCreateRequest(10), userId: 1);
        await PrepareTransactionForCloseAsync(db, created.Id);
        await AddDepartmentResponseAsync(db, created.Id, 10, submittedByUserId: 1, DepartmentResponseStatus.Approved);
        var preservedClosedAt = SaudiToday().AddDays(-1);
        var transaction = await db.Transactions.SingleAsync(t => t.Id == created.Id);
        transaction.IncomingDate = preservedClosedAt.AddDays(-10);
        transaction.ResponseCompletedDate = preservedClosedAt.AddDays(-1);
        transaction.ClosedAt = preservedClosedAt;
        await db.SaveChangesAsync();

        await service.UpdateAsync(
            created.Id,
            new UpdateTransactionRequest { Status = TransactionStatus.Closed.ToString() },
            userId: 1,
            role: UserRole.Admin);

        var persisted = await db.Transactions.SingleAsync(t => t.Id == created.Id);
        Assert.Equal(TransactionStatus.Closed, persisted.Status);
        Assert.Equal(preservedClosedAt, persisted.ClosedAt);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UpdateAsync_status_closed_audits_resolved_closed_at(bool hasExistingClosedAt)
    {
        var (service, db, _, _) = await CreateServiceAsync($"{nameof(UpdateAsync_status_closed_audits_resolved_closed_at)}_{hasExistingClosedAt}");
        var created = await service.CreateAsync(BuildCreateRequest(10), userId: 1);
        await PrepareTransactionForCloseAsync(db, created.Id);
        await AddDepartmentResponseAsync(db, created.Id, 10, submittedByUserId: 1, DepartmentResponseStatus.Approved);
        var today = SaudiToday();
        var existingClosedAt = hasExistingClosedAt ? today.AddDays(-1) : (DateTime?)null;
        var expectedClosedAt = existingClosedAt ?? today;
        var transaction = await db.Transactions.SingleAsync(t => t.Id == created.Id);
        transaction.IncomingDate = today.AddDays(-10);
        transaction.ResponseCompletedDate = today.AddDays(-2);
        transaction.ClosedAt = existingClosedAt;
        await db.SaveChangesAsync();

        await service.UpdateAsync(
            created.Id,
            new UpdateTransactionRequest { Status = TransactionStatus.Closed.ToString() },
            userId: 1,
            role: UserRole.Admin);

        var persisted = await db.Transactions.SingleAsync(t => t.Id == created.Id);
        Assert.Equal(TransactionStatus.Closed, persisted.Status);
        Assert.Equal(expectedClosedAt, persisted.ClosedAt);

        var audit = await db.AuditLogs
            .Where(a => a.Action == AuditAction.Update && a.TransactionId == created.Id)
            .OrderBy(a => a.Id)
            .LastAsync();
        using var oldJson = JsonDocument.Parse(audit.OldValue!);
        using var newJson = JsonDocument.Parse(audit.NewValue!);
        var oldClosedAt = oldJson.RootElement.GetProperty("ClosedAt");
        if (existingClosedAt.HasValue)
            Assert.Equal(existingClosedAt.Value, oldClosedAt.GetDateTime());
        else
            Assert.Equal(JsonValueKind.Null, oldClosedAt.ValueKind);
        Assert.Equal(expectedClosedAt.Date, newJson.RootElement.GetProperty("ClosedAt").GetDateTime().Date);
    }

    [Fact]
    public async Task UpdateAsync_status_closed_validation_failure_does_not_close_or_change_closed_at()
    {
        var (service, db, _, _) = await CreateServiceAsync(nameof(UpdateAsync_status_closed_validation_failure_does_not_close_or_change_closed_at));
        var created = await service.CreateAsync(BuildCreateRequest(10), userId: 1);
        await PrepareTransactionForCloseAsync(db, created.Id);
        await AddDepartmentResponseAsync(db, created.Id, 10, submittedByUserId: 1, DepartmentResponseStatus.Approved);
        var today = SaudiToday();
        var invalidClosedAt = today.AddDays(-1);
        var transaction = await db.Transactions.SingleAsync(t => t.Id == created.Id);
        transaction.IncomingDate = today.AddDays(-10);
        transaction.ResponseCompletedDate = today;
        transaction.ClosedAt = invalidClosedAt;
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.UpdateAsync(
                created.Id,
                new UpdateTransactionRequest { Status = TransactionStatus.Closed.ToString() },
                userId: 1,
                role: UserRole.Admin));

        var persisted = await db.Transactions.SingleAsync(t => t.Id == created.Id);
        Assert.NotEqual(TransactionStatus.Closed, persisted.Status);
        Assert.Equal(invalidClosedAt, persisted.ClosedAt);
    }

    [Fact]
    public async Task UpdateAsync_reopening_closed_transaction_to_in_progress_clears_closed_at()
    {
        var (service, db, _, _) = await CreateServiceAsync(nameof(UpdateAsync_reopening_closed_transaction_to_in_progress_clears_closed_at));
        var (created, _, _) = await CreateClosedTransactionAsync(service, db);

        await service.UpdateAsync(
            created.Id,
            new UpdateTransactionRequest { Status = TransactionStatus.InProgress.ToString() },
            userId: 1,
            role: UserRole.Admin);

        var persisted = await db.Transactions.SingleAsync(t => t.Id == created.Id);
        Assert.Equal(TransactionStatus.InProgress, persisted.Status);
        Assert.Null(persisted.ClosedAt);
    }

    [Fact]
    public async Task UpdateAsync_reopening_closed_transaction_to_response_completed_clears_closed_at()
    {
        var (service, db, _, _) = await CreateServiceAsync(nameof(UpdateAsync_reopening_closed_transaction_to_response_completed_clears_closed_at));
        var (created, _, _) = await CreateClosedTransactionAsync(service, db);

        await service.UpdateAsync(
            created.Id,
            new UpdateTransactionRequest { Status = TransactionStatus.ResponseCompleted.ToString() },
            userId: 1,
            role: UserRole.Admin);

        var persisted = await db.Transactions.SingleAsync(t => t.Id == created.Id);
        Assert.Equal(TransactionStatus.ResponseCompleted, persisted.Status);
        Assert.Null(persisted.ClosedAt);
    }

    [Fact]
    public async Task UpdateAsync_closed_to_closed_preserves_closed_at()
    {
        var (service, db, _, _) = await CreateServiceAsync(nameof(UpdateAsync_closed_to_closed_preserves_closed_at));
        var (created, closedAt, _) = await CreateClosedTransactionAsync(service, db);

        await service.UpdateAsync(
            created.Id,
            new UpdateTransactionRequest { Status = TransactionStatus.Closed.ToString() },
            userId: 1,
            role: UserRole.Admin);

        var persisted = await db.Transactions.SingleAsync(t => t.Id == created.Id);
        Assert.Equal(TransactionStatus.Closed, persisted.Status);
        Assert.Equal(closedAt, persisted.ClosedAt);
    }

    [Fact]
    public async Task UpdateAsync_closed_to_archived_preserves_closed_at()
    {
        var (service, db, _, _) = await CreateServiceAsync(nameof(UpdateAsync_closed_to_archived_preserves_closed_at));
        var (created, closedAt, _) = await CreateClosedTransactionAsync(service, db);

        await service.UpdateAsync(
            created.Id,
            new UpdateTransactionRequest { Status = TransactionStatus.Archived.ToString() },
            userId: 1,
            role: UserRole.Admin);

        var persisted = await db.Transactions.SingleAsync(t => t.Id == created.Id);
        Assert.Equal(TransactionStatus.Archived, persisted.Status);
        Assert.Equal(closedAt, persisted.ClosedAt);
    }

    [Fact]
    public async Task UpdateAsync_closed_to_cancelled_preserves_closed_at()
    {
        var (service, db, _, _) = await CreateServiceAsync(nameof(UpdateAsync_closed_to_cancelled_preserves_closed_at));
        var (created, closedAt, _) = await CreateClosedTransactionAsync(service, db);

        await service.UpdateAsync(
            created.Id,
            new UpdateTransactionRequest { Status = TransactionStatus.Cancelled.ToString() },
            userId: 1,
            role: UserRole.Admin);

        var persisted = await db.Transactions.SingleAsync(t => t.Id == created.Id);
        Assert.Equal(TransactionStatus.Cancelled, persisted.Status);
        Assert.Equal(closedAt, persisted.ClosedAt);
    }

    [Fact]
    public async Task UpdateAsync_reopened_workspace_does_not_expose_old_closed_at_as_completion_date()
    {
        var (service, db, _, _) = await CreateServiceAsync(nameof(UpdateAsync_reopened_workspace_does_not_expose_old_closed_at_as_completion_date));
        var (created, closedAt, responseCompletedDate) = await CreateClosedTransactionAsync(service, db);

        var updated = await service.UpdateAsync(
            created.Id,
            new UpdateTransactionRequest { Status = TransactionStatus.InProgress.ToString() },
            userId: 1,
            role: UserRole.Admin);
        var workspace = await service.GetWorkspaceAsync(created.Id, new TestCurrentUser(1, UserRole.Admin));

        Assert.NotNull(updated);
        Assert.NotEqual(closedAt, updated.CompletionDate);
        Assert.Equal(responseCompletedDate, updated.CompletionDate);
        Assert.NotNull(workspace);
        Assert.NotEqual(closedAt, workspace.Transaction.CompletionDate);
        Assert.Equal(responseCompletedDate, workspace.Transaction.CompletionDate);
    }

    [Fact]
    public async Task CloseAsync_DepartmentUserDraftResponse_Blocks()
    {
        var (service, db, _, cache) = await CreateServiceAsync(nameof(CloseAsync_DepartmentUserDraftResponse_Blocks));
        var created = await service.CreateAsync(BuildCreateRequest(10), userId: 1);
        await PrepareTransactionForCloseAsync(db, created!.Id);
        await AddDepartmentResponseAsync(db, created.Id, 10, submittedByUserId: 2, DepartmentResponseStatus.Draft);
        cache.ResetInvalidations();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.CloseAsync(created.Id, userId: 1, role: UserRole.Admin));

        Assert.Contains("توجد إفادة محفوظة كمسودة ولم تُرسل أو تُعتمد بعد.", ex.Message);
        Assert.Contains("المالية", ex.Message);
        Assert.Equal(0, cache.TransactionChangeInvalidations);
    }

    [Fact]
    public async Task CloseAsync_DepartmentUserSubmittedResponse_Blocks()
    {
        var (service, db, _, cache) = await CreateServiceAsync(nameof(CloseAsync_DepartmentUserSubmittedResponse_Blocks));
        var created = await service.CreateAsync(BuildCreateRequest(10), userId: 1);
        await PrepareTransactionForCloseAsync(db, created!.Id);
        await AddDepartmentResponseAsync(db, created.Id, 10, submittedByUserId: 2, DepartmentResponseStatus.SubmittedForReview);
        cache.ResetInvalidations();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.CloseAsync(created.Id, userId: 1, role: UserRole.Admin));

        Assert.Contains("توجد إفادة مرسلة للمراجعة لكنها لم تُعتمد بعد.", ex.Message);
        Assert.Contains("المالية", ex.Message);
        Assert.Equal(0, cache.TransactionChangeInvalidations);
    }

    [Fact]
    public async Task CloseAsync_DepartmentUserApprovedResponse_AllowsClose()
    {
        var (service, db, _, cache) = await CreateServiceAsync(nameof(CloseAsync_DepartmentUserApprovedResponse_AllowsClose));
        var created = await service.CreateAsync(BuildCreateRequest(10), userId: 1);
        await PrepareTransactionForCloseAsync(db, created!.Id);
        await AddDepartmentResponseAsync(db, created.Id, 10, submittedByUserId: 2, DepartmentResponseStatus.Approved);
        cache.ResetInvalidations();

        var closed = await service.CloseAsync(created.Id, userId: 1, role: UserRole.Admin);

        Assert.True(closed);
        Assert.Equal(1, cache.TransactionChangeInvalidations);
        Assert.Equal(TransactionStatus.Closed, await db.Transactions.Where(t => t.Id == created.Id).Select(t => t.Status).FirstAsync());
    }

    [Fact]
    public async Task CloseAsync_AdminCreatedResponseWithoutApproval_AllowsClose()
    {
        var (service, db, _, cache) = await CreateServiceAsync(nameof(CloseAsync_AdminCreatedResponseWithoutApproval_AllowsClose));
        var created = await service.CreateAsync(BuildCreateRequest(10), userId: 1);
        await PrepareTransactionForCloseAsync(db, created!.Id);
        await AddDepartmentResponseAsync(db, created.Id, 10, submittedByUserId: 1, DepartmentResponseStatus.Draft);
        cache.ResetInvalidations();

        var closed = await service.CloseAsync(created.Id, userId: 1, role: UserRole.Admin);

        Assert.True(closed);
        Assert.Equal(1, cache.TransactionChangeInvalidations);
    }

    [Fact]
    public async Task CloseAsync_SupervisorCreatedResponseWithoutApproval_AllowsClose()
    {
        var (service, db, _, cache) = await CreateServiceAsync(nameof(CloseAsync_SupervisorCreatedResponseWithoutApproval_AllowsClose));
        var created = await service.CreateAsync(BuildCreateRequest(10), userId: 1);
        await PrepareTransactionForCloseAsync(db, created!.Id);
        await AddDepartmentResponseAsync(db, created.Id, 10, submittedByUserId: 3, DepartmentResponseStatus.Draft);
        cache.ResetInvalidations();

        var closed = await service.CloseAsync(created.Id, userId: 1, role: UserRole.Admin);

        Assert.True(closed);
        Assert.Equal(1, cache.TransactionChangeInvalidations);
    }

    [Fact]
    public async Task CloseAsync_NormalUserCreatedResponseWithoutApproval_Blocks()
    {
        var (service, db, _, cache) = await CreateServiceAsync(nameof(CloseAsync_NormalUserCreatedResponseWithoutApproval_Blocks));
        var created = await service.CreateAsync(BuildCreateRequest(10), userId: 1);
        await PrepareTransactionForCloseAsync(db, created!.Id);
        await AddDepartmentResponseAsync(db, created.Id, 10, submittedByUserId: 2, DepartmentResponseStatus.Draft);
        cache.ResetInvalidations();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.CloseAsync(created.Id, userId: 1, role: UserRole.Admin));

        Assert.Contains("توجد إفادة محفوظة كمسودة ولم تُرسل أو تُعتمد بعد.", ex.Message);
        Assert.Equal(0, cache.TransactionChangeInvalidations);
    }

    [Fact]
    public async Task CloseAsync_ResponseForDifferentDepartment_Blocks()
    {
        var (service, db, _, cache) = await CreateServiceAsync(nameof(CloseAsync_ResponseForDifferentDepartment_Blocks));
        var created = await service.CreateAsync(BuildCreateRequest(10), userId: 1);
        await PrepareTransactionForCloseAsync(db, created!.Id);
        await AddDepartmentResponseAsync(db, created.Id, 11, submittedByUserId: 1, DepartmentResponseStatus.Approved);
        cache.ResetInvalidations();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.CloseAsync(created.Id, userId: 1, role: UserRole.Admin));

        Assert.Contains("الإفادة المسجلة لا تخص الإدارة المطلوبة.", ex.Message);
        Assert.Contains("المالية", ex.Message);
        Assert.DoesNotContain("الموارد", ex.Message);
        Assert.Equal(0, cache.TransactionChangeInvalidations);
    }

    [Fact]
    public async Task CloseAsync_DataEntryCreatedResponseWithoutApproval_AllowsClose()
    {
        var (service, db, _, cache) = await CreateServiceAsync(nameof(CloseAsync_DataEntryCreatedResponseWithoutApproval_AllowsClose));
        var created = await service.CreateAsync(BuildCreateRequest(10), userId: 1);
        await PrepareTransactionForCloseAsync(db, created!.Id);
        await AddDepartmentResponseAsync(db, created.Id, 10, submittedByUserId: 4, DepartmentResponseStatus.Draft);
        cache.ResetInvalidations();

        var closed = await service.CloseAsync(created.Id, userId: 1, role: UserRole.Admin);

        Assert.True(closed);
        Assert.Equal(1, cache.TransactionChangeInvalidations);
    }

    [Fact]
    public async Task CloseAsync_Transaction2011RegressionShape_AllowsPrivilegedDepartmentResponseAudit()
    {
        var (service, db, _, cache) = await CreateServiceAsync(
            nameof(CloseAsync_Transaction2011RegressionShape_AllowsPrivilegedDepartmentResponseAudit));
        var created = await service.CreateAsync(BuildCreateRequest(10), userId: 1);
        await PrepareTransactionForCloseAsync(db, created!.Id);
        var response = await AddDepartmentResponseAsync(
            db,
            created.Id,
            10,
            submittedByUserId: 2,
            DepartmentResponseStatus.Draft);
        await AddDepartmentResponseAuditAsync(
            db,
            created.Id,
            response.Id,
            userId: 4,
            AuditAction.DepartmentResponseUpdated);
        cache.ResetInvalidations();

        var closed = await service.CloseAsync(created.Id, userId: 1, role: UserRole.Admin);

        Assert.True(closed);
        Assert.Equal(1, cache.TransactionChangeInvalidations);
    }

    [Fact]
    public async Task CloseAsync_DepartmentUserAuditWithoutApproval_Blocks()
    {
        var (service, db, _, cache) = await CreateServiceAsync(nameof(CloseAsync_DepartmentUserAuditWithoutApproval_Blocks));
        var created = await service.CreateAsync(BuildCreateRequest(10), userId: 1);
        await PrepareTransactionForCloseAsync(db, created!.Id);
        var response = await AddDepartmentResponseAsync(
            db,
            created.Id,
            10,
            submittedByUserId: 2,
            DepartmentResponseStatus.Draft);
        await AddDepartmentResponseAuditAsync(
            db,
            created.Id,
            response.Id,
            userId: 2,
            AuditAction.DepartmentResponseUpdated);
        cache.ResetInvalidations();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.CloseAsync(created.Id, userId: 1, role: UserRole.Admin));

        Assert.Contains("توجد إفادة محفوظة كمسودة ولم تُرسل أو تُعتمد بعد.", ex.Message);
        Assert.Equal(0, cache.TransactionChangeInvalidations);
    }

    [Fact]
    public async Task CloseAsync_IgnoresInactiveAssignments()
    {
        var (service, db, _, cache) = await CreateServiceAsync(nameof(CloseAsync_IgnoresInactiveAssignments));
        var created = await service.CreateAsync(BuildCreateRequest(11), userId: 1);
        var assignment = await db.Assignments.SingleAsync(a => a.TransactionId == created!.Id && a.DepartmentId == 11);
        assignment.RequiresReply = true;
        assignment.ReplyStatus = ReplyStatus.Replied;
        assignment.Status = AssignmentStatus.Completed;
        var transaction = await db.Transactions.SingleAsync(t => t.Id == created.Id);
        transaction.ResponseCompleted = true;
        transaction.ResponseCompletedDate = DateTime.UtcNow.Date;
        await PrepareTransactionForCloseAsync(db, created.Id);
        cache.ResetInvalidations();

        var closed = await service.CloseAsync(created.Id, userId: 1, role: UserRole.Admin);

        Assert.True(closed);
        Assert.Equal(1, cache.TransactionChangeInvalidations);
    }

    [Fact]
    public async Task CloseAsync_MultipleActiveDepartments_RequiresAllSatisfied()
    {
        var (service, db, _, cache) = await CreateServiceAsync(nameof(CloseAsync_MultipleActiveDepartments_RequiresAllSatisfied));
        var created = await service.CreateAsync(BuildCreateRequest(10, 11), userId: 1);
        await PrepareTransactionForCloseAsync(db, created!.Id);
        await AddDepartmentResponseAsync(db, created.Id, 10, submittedByUserId: 1, DepartmentResponseStatus.Draft);
        cache.ResetInvalidations();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.CloseAsync(created.Id, userId: 1, role: UserRole.Admin));

        Assert.Contains("لا توجد إفادة مسجلة من الإدارة المطلوبة.", ex.Message);
        Assert.Contains("الموارد", ex.Message);
        Assert.Equal(0, cache.TransactionChangeInvalidations);
    }

    [Fact]
    public async Task CloseAsync_MixedResponses_AdminOneDepartmentApprovedOtherDepartment_AllowsOnlyWhenAllSatisfied()
    {
        var (service, db, _, cache) = await CreateServiceAsync(nameof(CloseAsync_MixedResponses_AdminOneDepartmentApprovedOtherDepartment_AllowsOnlyWhenAllSatisfied));
        var created = await service.CreateAsync(BuildCreateRequest(10, 11), userId: 1);
        await PrepareTransactionForCloseAsync(db, created!.Id);
        await AddDepartmentResponseAsync(db, created.Id, 10, submittedByUserId: 1, DepartmentResponseStatus.Draft);
        await AddDepartmentResponseAsync(db, created.Id, 11, submittedByUserId: 2, DepartmentResponseStatus.Approved);
        cache.ResetInvalidations();

        var closed = await service.CloseAsync(created.Id, userId: 1, role: UserRole.Admin);

        Assert.True(closed);
        Assert.Equal(1, cache.TransactionChangeInvalidations);
    }

    [Fact]
    public async Task CloseAsync_fails_when_approved_response_revoked_before_save()
    {
        // Simulates the race-condition scenario where a dept response was Approved when
        // the close was initiated but was revoked by a concurrent write before the save.
        // ValidateCanCloseAsync now runs inside CommitWorkflowMutationAsync (inside the DB
        // transaction), so it reads the current state and correctly rejects the close.
        var (service, db, _, cache) = await CreateServiceAsync(
            nameof(CloseAsync_fails_when_approved_response_revoked_before_save));
        var created = await service.CreateAsync(BuildCreateRequest(10), userId: 1);

        var assignment = await db.Assignments.SingleAsync(a => a.TransactionId == created!.Id && a.DepartmentId == 10);
        assignment.RequiresReply = true;
        assignment.ReplyStatus = ReplyStatus.Replied;
        assignment.Status = AssignmentStatus.Active;
        var transaction = await db.Transactions.SingleAsync(t => t.Id == created.Id);
        transaction.ResponseCompleted = true;
        transaction.ResponseCompletedDate = DateTime.UtcNow.Date;

        // Response exists but was revoked back to Submitted (concurrent write scenario)
        db.DepartmentResponses.Add(new DepartmentResponse
        {
            TransactionId = created!.Id,
            DepartmentId = 10,
            ResponseText = "إفادة",
            Status = DepartmentResponseStatus.SubmittedForReview,
            SubmittedByUserId = 2,
            SubmittedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
        cache.ResetInvalidations();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.CloseAsync(created.Id, userId: 1, role: UserRole.Admin));

        Assert.Contains("توجد إفادة مرسلة للمراجعة لكنها لم تُعتمد بعد.", ex.Message);
        Assert.Contains("المالية", ex.Message);
        Assert.Equal(0, cache.TransactionChangeInvalidations);
        var finalStatus = await db.Transactions
            .Where(t => t.Id == created.Id)
            .Select(t => t.Status)
            .FirstAsync();
        Assert.NotEqual(TransactionStatus.Closed, finalStatus);
    }
}
