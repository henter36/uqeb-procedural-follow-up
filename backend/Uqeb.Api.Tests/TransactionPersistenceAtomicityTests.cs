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

public class TransactionPersistenceAtomicityTests
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

    private static (AppDbContext Db, SaveChangesCounterInterceptor Counter) CreateDb(string name, params SaveChangesInterceptor[] extraInterceptors)
    {
        var counter = new SaveChangesCounterInterceptor();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(name)
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .AddInterceptors(new SaveChangesInterceptor[] { counter }.Concat(extraInterceptors).ToArray())
            .Options;
        return (new AppDbContext(options), counter);
    }

    private static async Task<(TransactionService Service, AppDbContext Db, SaveChangesCounterInterceptor Counter, TestCacheInvalidation Cache)> CreateServiceAsync(
        string dbName,
        ITrackingNumberService? trackingNumbers = null,
        params SaveChangesInterceptor[] extraInterceptors)
    {
        var (db, counter) = CreateDb(dbName, extraInterceptors);
        var cache = new TestCacheInvalidation();
        var user = new User
        {
            Id = 1,
            Username = "admin",
            PasswordHash = "hash",
            FullName = "Admin",
            Role = UserRole.Admin,
            IsActive = true
        };
        var category = new Category { Id = 1, Name = "عام", NameNormalized = "عام", IsActive = true };
        var party = new ExternalParty { Id = 1, Name = "جهة", NameNormalized = "جهة", IsActive = true };
        var deptA = new Department { Id = 10, Name = "المالية", NameNormalized = "المالية", IsActive = true };
        var deptB = new Department { Id = 11, Name = "الموارد", NameNormalized = "الموارد", IsActive = true };
        db.Users.Add(user);
        db.Categories.Add(category);
        db.ExternalParties.Add(party);
        db.Departments.AddRange(deptA, deptB);
        await db.SaveChangesAsync();
        counter.Reset();

        var service = new TransactionService(
            db,
            new AuditService(db),
            trackingNumbers ?? new StubTrackingNumberService(),
            cache,
            new RecurringTransactionTemplateService(db, new AuditService(db), new StubTrackingNumberService()));

        return (service, db, counter, cache);
    }

    private static DateTime SaudiToday() => DateTime.UtcNow.AddHours(3).Date;

    private static CreateTransactionRequest BuildCreateRequest(params int[] outgoingDepartmentIds) =>
        new()
        {
            IncomingNumber = "IN-1001",
            IncomingDate = SaudiToday(),
            Subject = "معاملة اختبار",
            IncomingSourceType = IncomingSourceType.External.ToString(),
            IncomingFromPartyId = 1,
            OutgoingNumber = "OUT-1001",
            OutgoingDate = SaudiToday(),
            OutgoingDepartmentIds = outgoingDepartmentIds.ToList(),
            ResponseType = ResponseType.External.ToString(),
            ResponseDueDays = 7,
            Priority = Priority.Normal.ToString(),
            CategoryId = 1
        };

    [Fact]
    public void AuditService_TrackLog_does_not_persist_until_save()
    {
        var (db, counter) = CreateDb(nameof(AuditService_TrackLog_does_not_persist_until_save));
        var audit = new AuditService(db);

        audit.TrackLog(1, AuditAction.Create, "Transaction", 1, 1, null, "{}");

        Assert.Equal(0, counter.Count);
        Assert.Equal(0, db.AuditLogs.Count());
    }

    [Fact]
    public async Task CreateAsync_with_outgoing_departments_persists_related_data_with_bounded_save_changes()
    {
        var (service, db, counter, cache) = await CreateServiceAsync(nameof(CreateAsync_with_outgoing_departments_persists_related_data_with_bounded_save_changes));

        var savesBefore = counter.Count;
        var created = await service.CreateAsync(BuildCreateRequest(10, 11), userId: 1);
        var savesDuringCreate = counter.Count - savesBefore;

        Assert.NotNull(created);
        Assert.InRange(savesDuringCreate, 1, 2);
        Assert.Equal(1, cache.TransactionChangeInvalidations);
        Assert.Equal(1, await db.Transactions.CountAsync());
        Assert.Equal(2, await db.TransactionOutgoingDepartments.CountAsync());
        Assert.Equal(2, await db.Assignments.CountAsync());
        Assert.True(await db.AuditLogs.AnyAsync(a => a.Action == AuditAction.Create && a.TransactionId == created.Id));
        Assert.True(await db.AuditLogs.AnyAsync(a => a.EntityName == "TransactionOutgoingDepartments"));
        Assert.True(await db.AuditLogs.AnyAsync(a => a.Action == AuditAction.AddAssignment));
    }

    [Fact]
    public async Task CreateAsync_with_outgoing_date_different_from_incoming_date_dates_auto_referrals_with_outgoing_date()
    {
        var (service, db, _, _) = await CreateServiceAsync(
            nameof(CreateAsync_with_outgoing_date_different_from_incoming_date_dates_auto_referrals_with_outgoing_date));

        var request = BuildCreateRequest(10, 11);
        request.IncomingDate = SaudiToday().AddDays(-5);
        request.OutgoingDate = SaudiToday();

        var created = await service.CreateAsync(request, userId: 1);

        var assignments = await db.Assignments.Where(a => a.TransactionId == created.Id).ToListAsync();
        Assert.Equal(2, assignments.Count);
        Assert.NotNull(request.OutgoingDate);
        Assert.All(assignments, a => Assert.Equal(request.OutgoingDate.Value.Date, a.AssignedDate.Date));
        Assert.All(assignments, a => Assert.NotEqual(request.IncomingDate.Date, a.AssignedDate.Date));
    }

    [Fact]
    public async Task CreateAsync_with_response_due_date_before_outgoing_date_keeps_auto_referral_due_date_from_preceding_assigned_date()
    {
        // ResponseDueDate is computed relative to IncomingDate. When OutgoingDate lands after
        // it, blindly copying ResponseDueDate onto the auto-referral's DueDate would produce
        // DueDate < AssignedDate — an invalid assignment state. The due date must instead be
        // recomputed relative to the resolved AssignedDate (OutgoingDate), same as
        // AddAssignmentAsync's own invariant.
        var (service, db, _, _) = await CreateServiceAsync(
            nameof(CreateAsync_with_response_due_date_before_outgoing_date_keeps_auto_referral_due_date_from_preceding_assigned_date));

        var request = BuildCreateRequest(10, 11);
        request.IncomingDate = SaudiToday().AddDays(-10);
        request.OutgoingDate = SaudiToday();
        request.ResponseDueDays = 7;

        var created = await service.CreateAsync(request, userId: 1);

        var assignments = await db.Assignments.Where(a => a.TransactionId == created.Id).ToListAsync();
        Assert.Equal(2, assignments.Count);
        Assert.NotNull(request.OutgoingDate);
        Assert.All(assignments, a => Assert.Equal(request.OutgoingDate.Value.Date, a.AssignedDate.Date));
        Assert.All(assignments, a => Assert.True(!a.DueDate.HasValue || a.DueDate.Value.Date >= a.AssignedDate.Date));
        Assert.All(assignments, a =>
        {
            Assert.NotNull(a.DueDate);
            Assert.Equal(request.OutgoingDate.Value.Date.AddDays(7), a.DueDate.Value.Date);
        });
    }

    [Fact]
    public async Task UpdateAsync_with_outgoing_department_change_uses_single_save_changes()
    {
        var (service, db, counter, cache) = await CreateServiceAsync(nameof(UpdateAsync_with_outgoing_department_change_uses_single_save_changes));
        var created = await service.CreateAsync(BuildCreateRequest(10), userId: 1);
        counter.Reset();
        cache.ResetInvalidations();

        await service.UpdateAsync(
            created!.Id,
            new UpdateTransactionRequest { OutgoingDepartmentIds = new List<int> { 10, 11 } },
            userId: 1,
            role: UserRole.Admin);

        Assert.Equal(1, counter.Count);
        Assert.Equal(1, cache.TransactionChangeInvalidations);
        Assert.Equal(2, await db.TransactionOutgoingDepartments.CountAsync());
        Assert.Equal(2, await db.Assignments.CountAsync());
        Assert.True(await db.AuditLogs.AnyAsync(a => a.Action == AuditAction.Update && a.EntityName == "Transaction"));
    }

    [Fact]
    public async Task CreateAsync_duplicate_incoming_number_throws_without_persisting_transaction()
    {
        var (service, db, counter, _) = await CreateServiceAsync(nameof(CreateAsync_duplicate_incoming_number_throws_without_persisting_transaction));
        await service.CreateAsync(BuildCreateRequest(10), userId: 1);
        counter.Reset();

        await Assert.ThrowsAsync<DuplicateIncomingNumberException>(() =>
            service.CreateAsync(BuildCreateRequest(10), userId: 1));

        Assert.Equal(0, counter.Count);
        Assert.Equal(1, await db.Transactions.CountAsync());
    }

    [Fact]
    public async Task CreateAsync_future_incoming_date_returns_field_validation()
    {
        var (service, _, _, _) = await CreateServiceAsync(nameof(CreateAsync_future_incoming_date_returns_field_validation));
        var request = BuildCreateRequest(10);
        var today = SaudiToday();
        request.IncomingDate = today.AddDays(1);

        var ex = await Assert.ThrowsAsync<FieldValidationException>(() => service.CreateAsync(request, userId: 1));

        Assert.Equal("لا يمكن أن يكون التاريخ بعد تاريخ اليوم.", ex.FieldErrors[nameof(CreateTransactionRequest.IncomingDate)]);
    }

    [Fact]
    public async Task CreateAsync_missing_incoming_date_returns_field_validation()
    {
        var (service, _, _, _) = await CreateServiceAsync(nameof(CreateAsync_missing_incoming_date_returns_field_validation));
        var request = BuildCreateRequest(10);
        request.IncomingDate = default;

        var ex = await Assert.ThrowsAsync<FieldValidationException>(() => service.CreateAsync(request, userId: 1));

        Assert.Equal("تاريخ المعاملة مطلوب", ex.FieldErrors[nameof(CreateTransactionRequest.IncomingDate)]);
    }

    [Fact]
    public async Task CreateAsync_future_outgoing_date_returns_field_validation()
    {
        var (service, _, _, _) = await CreateServiceAsync(nameof(CreateAsync_future_outgoing_date_returns_field_validation));
        var request = BuildCreateRequest(10);
        var today = SaudiToday();
        request.OutgoingDate = today.AddDays(1);

        var ex = await Assert.ThrowsAsync<FieldValidationException>(() => service.CreateAsync(request, userId: 1));

        Assert.Equal("لا يمكن أن يكون التاريخ بعد تاريخ اليوم.", ex.FieldErrors[nameof(CreateTransactionRequest.OutgoingDate)]);
    }

    [Fact]
    public async Task CreateAsync_outgoing_date_before_incoming_date_returns_field_validation()
    {
        var (service, _, _, _) = await CreateServiceAsync(nameof(CreateAsync_outgoing_date_before_incoming_date_returns_field_validation));
        var request = BuildCreateRequest(10);
        var today = SaudiToday();
        request.IncomingDate = today;
        request.OutgoingDate = today.AddDays(-1);

        var ex = await Assert.ThrowsAsync<FieldValidationException>(() => service.CreateAsync(request, userId: 1));

        Assert.Equal("تاريخ الصادر لا يمكن أن يكون قبل تاريخ المعاملة.", ex.FieldErrors[nameof(CreateTransactionRequest.OutgoingDate)]);
    }

    [Fact]
    public async Task CreateAsync_outgoing_number_without_outgoing_date_returns_specific_field_validation()
    {
        var (service, _, _, _) = await CreateServiceAsync(nameof(CreateAsync_outgoing_number_without_outgoing_date_returns_specific_field_validation));
        var request = BuildCreateRequest(10);
        request.OutgoingDate = null;

        var ex = await Assert.ThrowsAsync<FieldValidationException>(() => service.CreateAsync(request, userId: 1));

        Assert.Equal("تاريخ الصادر مطلوب عند إدخال رقم الصادر.", ex.FieldErrors[nameof(CreateTransactionRequest.OutgoingDate)]);
    }

    [Fact]
    public async Task UpdateAsync_outgoing_date_before_incoming_date_returns_field_validation()
    {
        var (service, _, _, _) = await CreateServiceAsync(nameof(UpdateAsync_outgoing_date_before_incoming_date_returns_field_validation));
        var created = await service.CreateAsync(BuildCreateRequest(10), userId: 1);

        var ex = await Assert.ThrowsAsync<FieldValidationException>(() =>
            service.UpdateAsync(
                created.Id,
                new UpdateTransactionRequest { OutgoingDate = SaudiToday().AddDays(-1) },
                userId: 1,
                role: UserRole.Admin));

        Assert.Equal("تاريخ الصادر لا يمكن أن يكون قبل تاريخ المعاملة.", ex.FieldErrors[nameof(CreateTransactionRequest.OutgoingDate)]);
    }

    [Fact]
    public async Task UpdateAsync_outgoing_number_without_outgoing_date_returns_specific_field_validation()
    {
        var (service, _, _, _) = await CreateServiceAsync(nameof(UpdateAsync_outgoing_number_without_outgoing_date_returns_specific_field_validation));
        var request = BuildCreateRequest();
        request.OutgoingNumber = null;
        request.OutgoingDate = null;
        request.OutgoingDepartmentIds = [];
        var created = await service.CreateAsync(request, userId: 1);

        var ex = await Assert.ThrowsAsync<FieldValidationException>(() =>
            service.UpdateAsync(
                created.Id,
                new UpdateTransactionRequest { OutgoingNumber = "OUT-2001" },
                userId: 1,
                role: UserRole.Admin));

        Assert.Equal("تاريخ الصادر مطلوب عند إدخال رقم الصادر.", ex.FieldErrors[nameof(CreateTransactionRequest.OutgoingDate)]);
    }

    [Fact]
    public async Task UpdateAsync_partial_outgoing_data_without_departments_returns_field_validation()
    {
        var (service, _, _, _) = await CreateServiceAsync(nameof(UpdateAsync_partial_outgoing_data_without_departments_returns_field_validation));
        var request = BuildCreateRequest();
        request.OutgoingNumber = null;
        request.OutgoingDate = null;
        request.OutgoingDepartmentIds = [];
        var created = await service.CreateAsync(request, userId: 1);

        var ex = await Assert.ThrowsAsync<FieldValidationException>(() =>
            service.UpdateAsync(
                created.Id,
                new UpdateTransactionRequest
                {
                    OutgoingNumber = "OUT-2001",
                    OutgoingDate = SaudiToday(),
                    OutgoingDepartmentIds = [],
                },
                userId: 1,
                role: UserRole.Admin));

        Assert.Equal(
            "عند إدخال أي بيان من بيانات الصادر يجب إكمال رقم الصادر وتاريخ الصادر والإدارة الصادر لها.",
            ex.FieldErrors[nameof(CreateTransactionRequest.OutgoingDepartmentIds)]);
    }

    [Fact]
    public async Task UpdateAsync_complete_outgoing_data_succeeds()
    {
        var (service, db, _, _) = await CreateServiceAsync(nameof(UpdateAsync_complete_outgoing_data_succeeds));
        var request = BuildCreateRequest();
        request.OutgoingNumber = null;
        request.OutgoingDate = null;
        request.OutgoingDepartmentIds = [];
        var created = await service.CreateAsync(request, userId: 1);

        await service.UpdateAsync(
            created.Id,
            new UpdateTransactionRequest
            {
                OutgoingNumber = "OUT-2001",
                OutgoingDate = SaudiToday(),
                OutgoingDepartmentIds = [10],
            },
            userId: 1,
            role: UserRole.Admin);

        var transaction = await db.Transactions.Include(t => t.OutgoingDepartments).SingleAsync(t => t.Id == created.Id);
        Assert.Equal("OUT-2001", transaction.OutgoingNumber);
        Assert.Equal(SaudiToday(), transaction.OutgoingDate);
        Assert.Equal(10, Assert.Single(transaction.OutgoingDepartments).DepartmentId);
    }

    [Fact]
    public async Task CreateAsync_with_outgoing_departments_links_all_audits_to_transaction_and_entities()
    {
        var (service, db, _, _) = await CreateServiceAsync(nameof(CreateAsync_with_outgoing_departments_links_all_audits_to_transaction_and_entities));

        var created = await service.CreateAsync(BuildCreateRequest(10, 11), userId: 1);
        var audits = await db.AuditLogs.Where(a => a.TransactionId == created!.Id).ToListAsync();

        Assert.NotEmpty(audits);
        Assert.All(audits, audit => Assert.Equal(created.Id, audit.TransactionId));

        var assignmentAudits = audits.Where(a => a.EntityName == "Assignment").ToList();
        Assert.NotEmpty(assignmentAudits);
        Assert.All(assignmentAudits, audit => Assert.NotNull(audit.EntityId));
        Assert.All(assignmentAudits, audit => Assert.True(audit.EntityId > 0));
    }

    [Fact]
    public async Task UpdateAsync_field_change_tracks_audit_without_intermediate_save()
    {
        var (service, db, counter, _) = await CreateServiceAsync(nameof(UpdateAsync_field_change_tracks_audit_without_intermediate_save));
        var created = await service.CreateAsync(BuildCreateRequest(10), userId: 1);
        counter.Reset();
        var auditCountBefore = await db.AuditLogs.CountAsync();

        await service.UpdateAsync(
            created!.Id,
            new UpdateTransactionRequest { Priority = Priority.Urgent.ToString() },
            userId: 1,
            role: UserRole.Admin);

        Assert.Equal(1, counter.Count);
        Assert.Equal(auditCountBefore + 2, await db.AuditLogs.CountAsync());
        Assert.Contains(await db.AuditLogs.ToListAsync(), a =>
            a.Action == AuditAction.Update && a.OldValue == Priority.Normal.ToString() && a.NewValue == Priority.Urgent.ToString());
    }

    [Fact]
    public async Task CreateAsync_does_not_attach_unrelated_pending_audit_log()
    {
        var (service, db, _, _) = await CreateServiceAsync(nameof(CreateAsync_does_not_attach_unrelated_pending_audit_log));
        var auditService = new AuditService(db);
        var unrelated = auditService.TrackLog(
            1,
            AuditAction.Update,
            "Department",
            99,
            transactionId: null,
            oldValue: null,
            newValue: """{"name":"other"}""");

        var created = await service.CreateAsync(BuildCreateRequest(10), userId: 1);

        Assert.Null(unrelated.TransactionId);
        Assert.Equal(99, unrelated.EntityId);
        Assert.DoesNotContain(
            await db.AuditLogs.Where(a => a.TransactionId == created!.Id).ToListAsync(),
            audit => audit.EntityName == "Department" && audit.EntityId == 99);
        Assert.True(await db.AuditLogs.AnyAsync(a => a.Action == AuditAction.Create && a.TransactionId == created.Id));
    }

}
