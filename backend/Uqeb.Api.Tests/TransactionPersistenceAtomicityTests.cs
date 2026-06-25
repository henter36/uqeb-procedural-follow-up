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
            cache);

        return (service, db, counter, cache);
    }

    private static CreateTransactionRequest BuildCreateRequest(params int[] outgoingDepartmentIds) =>
        new()
        {
            IncomingNumber = "IN-1001",
            IncomingDate = DateTime.UtcNow.Date,
            Subject = "معاملة اختبار",
            IncomingSourceType = IncomingSourceType.External.ToString(),
            IncomingFromPartyId = 1,
            OutgoingNumber = "OUT-1001",
            OutgoingDate = DateTime.UtcNow.Date,
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
