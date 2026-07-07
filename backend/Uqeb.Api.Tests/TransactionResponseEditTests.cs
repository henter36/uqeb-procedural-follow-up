using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Uqeb.Api.Data;
using Uqeb.Api.DTOs.Transactions;
using Uqeb.Api.Models.Entities;
using Uqeb.Api.Models.Enums;
using Uqeb.Api.Services;
using Xunit;

namespace Uqeb.Api.Tests;

public class TransactionResponseEditTests
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
        await db.SaveChangesAsync();

        var service = new TransactionService(
            db,
            new AuditService(db),
            new StubTrackingNumberService(),
            new TestCacheInvalidation(),
            new RecurringTransactionTemplateService(db, new AuditService(db), new StubTrackingNumberService()));

        return (service, db);
    }

    private static async Task<Transaction> SeedCompletedResponseTransactionAsync(AppDbContext db, int id = 1)
    {
        var incomingDate = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var t = new Transaction
        {
            Id = id,
            InternalTrackingNumber = $"UQEB-{id}",
            IncomingNumber = $"IN-{id}",
            IncomingDate = incomingDate,
            Subject = "معاملة",
            IncomingSourceType = IncomingSourceType.External,
            IncomingFrom = "جهة",
            RequiresResponse = true,
            ResponseType = ResponseType.External,
            ResponseCompleted = true,
            ResponseCompletedDate = new DateTime(2026, 1, 10, 0, 0, 0, DateTimeKind.Utc),
            ResponseSummary = "الملخص الأصلي",
            OutgoingNumber = "OUT-1",
            OutgoingDate = new DateTime(2026, 1, 10, 0, 0, 0, DateTimeKind.Utc),
            Priority = Priority.Normal,
            Status = TransactionStatus.ResponseCompleted,
            CreatedById = 1,
            CreatedAt = incomingDate
        };
        db.Transactions.Add(t);
        await db.SaveChangesAsync();
        return t;
    }

    [Fact]
    public async Task EditResponseAsync_updates_the_same_transaction_record_without_creating_a_duplicate()
    {
        var (service, db) = await CreateServiceAsync(nameof(EditResponseAsync_updates_the_same_transaction_record_without_creating_a_duplicate));
        await SeedCompletedResponseTransactionAsync(db);

        var result = await service.EditResponseAsync(1, new CompleteResponseRequest
        {
            ResponseDate = new DateTime(2026, 1, 12, 0, 0, 0, DateTimeKind.Utc),
            ResponseSummary = "الملخص المعدَّل",
            OutgoingNumber = "OUT-2",
            OutgoingDate = new DateTime(2026, 1, 12, 0, 0, 0, DateTimeKind.Utc),
        }, new TestCurrentUser(UserRole.Admin));

        Assert.NotNull(result);
        Assert.Equal(1, await db.Transactions.CountAsync());
        var updated = await db.Transactions.SingleAsync(t => t.Id == 1);
        Assert.Equal("الملخص المعدَّل", updated.ResponseSummary);
        Assert.Equal(new DateTime(2026, 1, 12, 0, 0, 0, DateTimeKind.Utc), updated.ResponseCompletedDate);
        Assert.Equal("OUT-2", updated.OutgoingNumber);
        Assert.Equal(new DateTime(2026, 1, 12, 0, 0, 0, DateTimeKind.Utc), updated.OutgoingDate);
        Assert.True(updated.ResponseCompleted);
    }

    [Fact]
    public async Task EditResponseAsync_changing_response_date_updates_completion_date()
    {
        var (service, db) = await CreateServiceAsync(nameof(EditResponseAsync_changing_response_date_updates_completion_date));
        await SeedCompletedResponseTransactionAsync(db);

        var newDate = new DateTime(2026, 1, 20, 0, 0, 0, DateTimeKind.Utc);
        await service.EditResponseAsync(1, new CompleteResponseRequest
        {
            ResponseDate = newDate,
            ResponseSummary = "تحديث التاريخ",
            OutgoingNumber = "OUT-1",
            OutgoingDate = newDate,
        }, new TestCurrentUser(UserRole.Admin));

        var updated = await db.Transactions.SingleAsync(t => t.Id == 1);
        Assert.Equal(newDate, updated.ResponseCompletedDate);
    }

    [Fact]
    public async Task EditResponseAsync_throws_when_response_was_never_completed()
    {
        var (service, db) = await CreateServiceAsync(nameof(EditResponseAsync_throws_when_response_was_never_completed));
        db.Transactions.Add(new Transaction
        {
            Id = 1,
            InternalTrackingNumber = "UQEB-1",
            IncomingNumber = "IN-1",
            IncomingDate = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            Subject = "معاملة",
            IncomingSourceType = IncomingSourceType.External,
            IncomingFrom = "جهة",
            RequiresResponse = true,
            ResponseType = ResponseType.External,
            ResponseCompleted = false,
            Priority = Priority.Normal,
            Status = TransactionStatus.InProgress,
            CreatedById = 1,
            CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
        });
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.EditResponseAsync(1, new CompleteResponseRequest
            {
                ResponseDate = new DateTime(2026, 1, 5, 0, 0, 0, DateTimeKind.Utc),
                ResponseSummary = "محاولة تعديل قبل التسجيل",
            }, new TestCurrentUser(UserRole.Admin)));
    }

    [Fact]
    public async Task EditResponseAsync_rejects_department_user()
    {
        var (service, db) = await CreateServiceAsync(nameof(EditResponseAsync_rejects_department_user));
        await SeedCompletedResponseTransactionAsync(db);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            service.EditResponseAsync(1, new CompleteResponseRequest
            {
                ResponseDate = new DateTime(2026, 1, 12, 0, 0, 0, DateTimeKind.Utc),
                ResponseSummary = "محاولة غير مصرح بها",
            }, new TestCurrentUser(UserRole.DepartmentUser, userId: 2, departmentId: 10)));
    }

    [Fact]
    public async Task EditResponseAsync_rejects_missing_response_summary()
    {
        var (service, db) = await CreateServiceAsync(nameof(EditResponseAsync_rejects_missing_response_summary));
        await SeedCompletedResponseTransactionAsync(db);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.EditResponseAsync(1, new CompleteResponseRequest
            {
                ResponseDate = new DateTime(2026, 1, 12, 0, 0, 0, DateTimeKind.Utc),
                ResponseSummary = "   ",
            }, new TestCurrentUser(UserRole.Admin)));
    }

    [Fact]
    public async Task EditResponseAsync_returns_null_when_transaction_missing()
    {
        var (service, _) = await CreateServiceAsync(nameof(EditResponseAsync_returns_null_when_transaction_missing));

        var result = await service.EditResponseAsync(999, new CompleteResponseRequest
        {
            ResponseDate = new DateTime(2026, 1, 12, 0, 0, 0, DateTimeKind.Utc),
            ResponseSummary = "غير موجود",
        }, new TestCurrentUser(UserRole.Admin));

        Assert.Null(result);
    }

    [Fact]
    public async Task EditResponseAsync_clears_outgoing_fields_when_response_type_does_not_require_outgoing()
    {
        var (service, db) = await CreateServiceAsync(nameof(EditResponseAsync_clears_outgoing_fields_when_response_type_does_not_require_outgoing));
        var t = await SeedCompletedResponseTransactionAsync(db);
        t.ResponseType = ResponseType.Internal;
        await db.SaveChangesAsync();

        await service.EditResponseAsync(1, new CompleteResponseRequest
        {
            ResponseDate = new DateTime(2026, 1, 12, 0, 0, 0, DateTimeKind.Utc),
            ResponseSummary = "إفادة داخلية لا تتطلب صادر",
            OutgoingNumber = "ينبغي تجاهله",
            OutgoingDate = new DateTime(2026, 1, 12, 0, 0, 0, DateTimeKind.Utc),
        }, new TestCurrentUser(UserRole.Admin));

        var updated = await db.Transactions.SingleAsync(x => x.Id == 1);
        Assert.Null(updated.OutgoingNumber);
        Assert.Null(updated.OutgoingDate);
    }

    [Fact]
    public async Task EditResponseAsync_stores_trimmed_outgoing_fields_when_response_type_requires_outgoing()
    {
        var (service, db) = await CreateServiceAsync(nameof(EditResponseAsync_stores_trimmed_outgoing_fields_when_response_type_requires_outgoing));
        await SeedCompletedResponseTransactionAsync(db);

        var outgoingDate = new DateTime(2026, 1, 15, 0, 0, 0, DateTimeKind.Utc);
        await service.EditResponseAsync(1, new CompleteResponseRequest
        {
            ResponseDate = new DateTime(2026, 1, 15, 0, 0, 0, DateTimeKind.Utc),
            ResponseSummary = "إفادة خارجية",
            OutgoingNumber = "  OUT-99  ",
            OutgoingDate = outgoingDate,
        }, new TestCurrentUser(UserRole.Admin));

        var updated = await db.Transactions.SingleAsync(x => x.Id == 1);
        Assert.Equal("OUT-99", updated.OutgoingNumber);
        Assert.Equal(outgoingDate, updated.OutgoingDate);
    }

    [Theory]
    [InlineData(TransactionStatus.Closed)]
    [InlineData(TransactionStatus.Cancelled)]
    [InlineData(TransactionStatus.Archived)]
    public async Task EditResponseAsync_rejects_terminal_transaction_statuses(TransactionStatus status)
    {
        var (service, db) = await CreateServiceAsync($"{nameof(EditResponseAsync_rejects_terminal_transaction_statuses)}_{status}");
        var t = await SeedCompletedResponseTransactionAsync(db);
        t.Status = status;
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.EditResponseAsync(1, new CompleteResponseRequest
            {
                ResponseDate = new DateTime(2026, 1, 12, 0, 0, 0, DateTimeKind.Utc),
                ResponseSummary = "محاولة تعديل معاملة منتهية",
                OutgoingNumber = "OUT-1",
                OutgoingDate = new DateTime(2026, 1, 12, 0, 0, 0, DateTimeKind.Utc),
            }, new TestCurrentUser(UserRole.Admin)));
    }

    [Fact]
    public async Task EditResponseAsync_allows_supervisor_role()
    {
        var (service, db) = await CreateServiceAsync(nameof(EditResponseAsync_allows_supervisor_role));
        await SeedCompletedResponseTransactionAsync(db);

        var result = await service.EditResponseAsync(1, new CompleteResponseRequest
        {
            ResponseDate = new DateTime(2026, 1, 12, 0, 0, 0, DateTimeKind.Utc),
            ResponseSummary = "تعديل من قبل مشرف",
            OutgoingNumber = "OUT-3",
            OutgoingDate = new DateTime(2026, 1, 12, 0, 0, 0, DateTimeKind.Utc),
        }, new TestCurrentUser(UserRole.Supervisor));

        Assert.NotNull(result);
        var updated = await db.Transactions.SingleAsync(t => t.Id == 1);
        Assert.Equal("تعديل من قبل مشرف", updated.ResponseSummary);
    }

    [Fact]
    public async Task EditResponseAsync_rejects_missing_outgoing_number_when_response_type_requires_outgoing()
    {
        var (service, db) = await CreateServiceAsync(nameof(EditResponseAsync_rejects_missing_outgoing_number_when_response_type_requires_outgoing));
        await SeedCompletedResponseTransactionAsync(db);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.EditResponseAsync(1, new CompleteResponseRequest
            {
                ResponseDate = new DateTime(2026, 1, 12, 0, 0, 0, DateTimeKind.Utc),
                ResponseSummary = "إفادة خارجية بدون رقم صادر",
                OutgoingNumber = "   ",
                OutgoingDate = new DateTime(2026, 1, 12, 0, 0, 0, DateTimeKind.Utc),
            }, new TestCurrentUser(UserRole.Admin)));
    }

    [Fact]
    public async Task EditResponseAsync_rejects_missing_outgoing_date_when_response_type_requires_outgoing()
    {
        var (service, db) = await CreateServiceAsync(nameof(EditResponseAsync_rejects_missing_outgoing_date_when_response_type_requires_outgoing));
        await SeedCompletedResponseTransactionAsync(db);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.EditResponseAsync(1, new CompleteResponseRequest
            {
                ResponseDate = new DateTime(2026, 1, 12, 0, 0, 0, DateTimeKind.Utc),
                ResponseSummary = "إفادة خارجية بدون تاريخ صادر",
                OutgoingNumber = "OUT-1",
                OutgoingDate = null,
            }, new TestCurrentUser(UserRole.Admin)));
    }

    [Fact]
    public async Task EditResponseAsync_creates_audit_log_entry_with_old_and_new_values()
    {
        var (service, db) = await CreateServiceAsync(nameof(EditResponseAsync_creates_audit_log_entry_with_old_and_new_values));
        await SeedCompletedResponseTransactionAsync(db);

        await service.EditResponseAsync(1, new CompleteResponseRequest
        {
            ResponseDate = new DateTime(2026, 1, 12, 0, 0, 0, DateTimeKind.Utc),
            ResponseSummary = "ملخص جديد للتدقيق",
            OutgoingNumber = "OUT-AUDIT",
            OutgoingDate = new DateTime(2026, 1, 12, 0, 0, 0, DateTimeKind.Utc),
        }, new TestCurrentUser(UserRole.Admin, userId: 1));

        var auditLog = await db.AuditLogs.SingleAsync(a => a.Action == AuditAction.EditResponse && a.TransactionId == 1);
        Assert.Equal(1, auditLog.UserId);
        Assert.Contains("OUT-1", auditLog.OldValue);
        Assert.Contains("OUT-AUDIT", auditLog.NewValue);
        Assert.NotEqual(auditLog.OldValue, auditLog.NewValue);
    }

    private static async Task<Transaction> SeedTransactionReadyForResponseAsync(AppDbContext db, int id = 1, ResponseType responseType = ResponseType.External)
    {
        var incomingDate = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var t = new Transaction
        {
            Id = id,
            InternalTrackingNumber = $"UQEB-{id}",
            IncomingNumber = $"IN-{id}",
            IncomingDate = incomingDate,
            Subject = "معاملة",
            IncomingSourceType = IncomingSourceType.External,
            IncomingFrom = "جهة",
            RequiresResponse = true,
            ResponseType = responseType,
            ResponseCompleted = false,
            Priority = Priority.Normal,
            Status = TransactionStatus.Assigned,
            CreatedById = 1,
            CreatedAt = incomingDate
        };
        db.Transactions.Add(t);
        await db.SaveChangesAsync();
        return t;
    }

    [Fact]
    public async Task CompleteResponseAsync_persists_the_exact_entered_response_date_not_today()
    {
        // Regression guard: registering a response for a date other than "today" (e.g. a
        // backfilled/late-recorded response) must save that exact date, never DateTime.UtcNow
        // or GetSaudiToday() as a substitute.
        var (service, db) = await CreateServiceAsync(nameof(CompleteResponseAsync_persists_the_exact_entered_response_date_not_today));
        await SeedTransactionReadyForResponseAsync(db, responseType: ResponseType.Internal);

        var enteredDate = new DateTime(2026, 1, 15, 0, 0, 0, DateTimeKind.Utc);
        var result = await service.CompleteResponseAsync(1, new CompleteResponseRequest
        {
            ResponseDate = enteredDate,
            ResponseSummary = "ملخص الإفادة",
        }, new TestCurrentUser(UserRole.Admin));

        Assert.NotNull(result);
        var updated = await db.Transactions.SingleAsync(t => t.Id == 1);
        Assert.Equal(enteredDate, updated.ResponseCompletedDate);
        Assert.NotNull(updated.ResponseCompletedDate);
        Assert.NotEqual(DateTime.UtcNow.Date, updated.ResponseCompletedDate.Value.Date);
    }

    [Fact]
    public async Task CompleteResponseAsync_persists_the_exact_entered_outgoing_date_not_today()
    {
        var (service, db) = await CreateServiceAsync(nameof(CompleteResponseAsync_persists_the_exact_entered_outgoing_date_not_today));
        await SeedTransactionReadyForResponseAsync(db, responseType: ResponseType.External);

        var enteredResponseDate = new DateTime(2026, 1, 15, 0, 0, 0, DateTimeKind.Utc);
        var enteredOutgoingDate = new DateTime(2026, 1, 14, 0, 0, 0, DateTimeKind.Utc);
        var result = await service.CompleteResponseAsync(1, new CompleteResponseRequest
        {
            ResponseDate = enteredResponseDate,
            ResponseSummary = "ملخص الإفادة",
            OutgoingNumber = "OUT-99",
            OutgoingDate = enteredOutgoingDate,
        }, new TestCurrentUser(UserRole.Admin));

        Assert.NotNull(result);
        var updated = await db.Transactions.SingleAsync(t => t.Id == 1);
        Assert.Equal(enteredOutgoingDate, updated.OutgoingDate);
        Assert.NotNull(updated.OutgoingDate);
        Assert.NotEqual(DateTime.UtcNow.Date, updated.OutgoingDate.Value.Date);
    }

    [Fact]
    public async Task CompleteResponseAsync_rejects_missing_response_date()
    {
        var (service, db) = await CreateServiceAsync(nameof(CompleteResponseAsync_rejects_missing_response_date));
        await SeedTransactionReadyForResponseAsync(db, responseType: ResponseType.Internal);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.CompleteResponseAsync(1, new CompleteResponseRequest
            {
                ResponseDate = default,
                ResponseSummary = "ملخص بدون تاريخ",
            }, new TestCurrentUser(UserRole.Admin)));
    }

    [Fact]
    public async Task CompleteResponseAsync_rejects_missing_outgoing_date_when_response_type_requires_outgoing()
    {
        var (service, db) = await CreateServiceAsync(nameof(CompleteResponseAsync_rejects_missing_outgoing_date_when_response_type_requires_outgoing));
        await SeedTransactionReadyForResponseAsync(db, responseType: ResponseType.External);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.CompleteResponseAsync(1, new CompleteResponseRequest
            {
                ResponseDate = new DateTime(2026, 1, 15, 0, 0, 0, DateTimeKind.Utc),
                ResponseSummary = "ملخص",
                OutgoingNumber = "OUT-1",
                OutgoingDate = null,
            }, new TestCurrentUser(UserRole.Admin)));
    }

    [Fact]
    public async Task CompleteResponseAsync_rejects_future_response_date()
    {
        var (service, db) = await CreateServiceAsync(nameof(CompleteResponseAsync_rejects_future_response_date));
        await SeedTransactionReadyForResponseAsync(db, responseType: ResponseType.Internal);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.CompleteResponseAsync(1, new CompleteResponseRequest
            {
                ResponseDate = DateTime.UtcNow.AddDays(5),
                ResponseSummary = "ملخص بتاريخ مستقبلي",
            }, new TestCurrentUser(UserRole.Admin)));
    }
}
