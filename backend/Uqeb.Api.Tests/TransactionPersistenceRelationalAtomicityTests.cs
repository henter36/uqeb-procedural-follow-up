using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Uqeb.Api.Data;
using Uqeb.Api.DTOs.Transactions;
using Uqeb.Api.Models.Entities;
using Uqeb.Api.Models.Enums;
using Uqeb.Api.Services;
using Xunit;

namespace Uqeb.Api.Tests;

public class TransactionPersistenceRelationalAtomicityTests
{
    private sealed class ArmedSaveChangesFailureInterceptor : SaveChangesInterceptor
    {
        private int _saveCount;
        private int? _failOnSaveNumber;

        public int ObservedSaveCount { get; private set; }

        public int? FailedOnSaveNumber { get; private set; }

        public int CompletedSaveCount =>
            FailedOnSaveNumber is int failed ? failed - 1 : ObservedSaveCount;

        public void Arm(int failOnSaveNumber)
        {
            _saveCount = 0;
            ObservedSaveCount = 0;
            FailedOnSaveNumber = null;
            _failOnSaveNumber = failOnSaveNumber;
        }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (_failOnSaveNumber.HasValue)
            {
                _saveCount++;
                ObservedSaveCount = _saveCount;

                if (_saveCount == _failOnSaveNumber.Value)
                {
                    FailedOnSaveNumber = _saveCount;
                    throw new InvalidOperationException(
                        "simulated final audit persistence failure");
                }
            }

            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }

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

    private sealed record RelationalAtomicityTestContext(
        SqliteConnection Connection,
        AppDbContext Db,
        TransactionService Service,
        ArmedSaveChangesFailureInterceptor FailureInterceptor);

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

    private static async Task<RelationalAtomicityTestContext> CreateRelationalAtomicityTestContextAsync()
    {
        var failureInterceptor = new ArmedSaveChangesFailureInterceptor();
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connection)
            .AddInterceptors(failureInterceptor)
            .Options;

        var db = new AppDbContext(options);
        await db.Database.EnsureCreatedAsync();

        db.Users.Add(new User
        {
            Id = 1,
            Username = "admin",
            PasswordHash = "hash",
            FullName = "Admin",
            Role = UserRole.Admin,
            IsActive = true
        });
        db.Categories.Add(new Category { Id = 1, Name = "عام", NameNormalized = "عام", IsActive = true });
        db.ExternalParties.Add(new ExternalParty { Id = 1, Name = "جهة", NameNormalized = "جهة", IsActive = true });
        db.Departments.AddRange(
            new Department { Id = 10, Name = "المالية", NameNormalized = "المالية", IsActive = true },
            new Department { Id = 11, Name = "الموارد", NameNormalized = "الموارد", IsActive = true });
        await db.SaveChangesAsync();

        var service = new TransactionService(
            db,
            new AuditService(db),
            new StubTrackingNumberService(),
            new TestCacheInvalidation());

        return new RelationalAtomicityTestContext(connection, db, service, failureInterceptor);
    }

    [Fact]
    public async Task CreateAsync_rolls_back_transaction_when_final_audit_save_fails()
    {
        var context = await CreateRelationalAtomicityTestContextAsync();
        await using (context.Connection)
        await using (context.Db)
        {
            context.FailureInterceptor.Arm(failOnSaveNumber: 2);

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                context.Service.CreateAsync(BuildCreateRequest(10, 11), userId: 1));

            Assert.Equal("simulated final audit persistence failure", exception.Message);
            Assert.Equal(2, context.FailureInterceptor.ObservedSaveCount);
            Assert.Equal(2, context.FailureInterceptor.FailedOnSaveNumber);
            Assert.Equal(1, context.FailureInterceptor.CompletedSaveCount);

            var verificationOptions = new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite(context.Connection)
                .Options;

            await using var verificationDb = new AppDbContext(verificationOptions);

            Assert.False(await verificationDb.Transactions
                .AnyAsync(t => t.IncomingNumber == "IN-1001"));

            Assert.Equal(0, await verificationDb.TransactionOutgoingDepartments.CountAsync());
            Assert.Equal(0, await verificationDb.Assignments.CountAsync());

            Assert.False(await verificationDb.AuditLogs.AnyAsync(a =>
                a.Action == AuditAction.Create &&
                a.EntityName == "Transaction"));

            Assert.False(await verificationDb.AuditLogs.AnyAsync(a => a.TransactionId != null));
        }
    }
}
