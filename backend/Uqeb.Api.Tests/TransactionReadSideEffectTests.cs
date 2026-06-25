using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Uqeb.Api.Data;
using Uqeb.Api.Models.Entities;
using Uqeb.Api.Models.Enums;
using Uqeb.Api.Services;
using Xunit;

namespace Uqeb.Api.Tests;

public class TransactionReadSideEffectTests
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
        public Task<string> GenerateNextAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult("UQEB-2026-00001");
    }

    private sealed class TestCacheInvalidation : ICacheInvalidationService
    {
        public string DashboardSummaryKey => "dashboard";
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
        public int UserId => 1;
        public string Username => "admin";
        public UserRole Role => UserRole.Admin;
        public int? DepartmentId => null;
        public bool IsAuthenticated => true;
    }

    private static async Task<(TransactionService Service, AppDbContext Db, SaveChangesCounterInterceptor Counter)> CreateServiceAsync(string dbName)
    {
        var counter = new SaveChangesCounterInterceptor();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(dbName)
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .AddInterceptors(counter)
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
        db.Departments.Add(new Department { Id = 10, Name = "المالية", NameNormalized = "المالية", IsActive = true });
        await db.SaveChangesAsync();
        counter.Reset();

        var service = new TransactionService(
            db,
            new AuditService(db),
            new StubTrackingNumberService(),
            new TestCacheInvalidation());

        return (service, db, counter);
    }

    [Fact]
    public async Task GetByIdAsync_does_not_mutate_overdue_assignment_or_transaction_status()
    {
        var (service, db, counter) = await CreateServiceAsync(nameof(GetByIdAsync_does_not_mutate_overdue_assignment_or_transaction_status));
        var pastDue = DateTime.UtcNow.AddDays(-5);
        var transaction = new Transaction
        {
            InternalTrackingNumber = "UQEB-2026-00099",
            IncomingNumber = "IN-READ-1",
            IncomingDate = DateTime.UtcNow.AddDays(-10),
            Subject = "قراءة بدون آثار جانبية",
            IncomingSourceType = IncomingSourceType.External,
            IncomingFrom = "جهة",
            RequiresResponse = true,
            ResponseType = ResponseType.External,
            ResponseDueDate = pastDue,
            ResponseDueDays = 3,
            Priority = Priority.Normal,
            Status = TransactionStatus.WaitingForReply,
            CreatedById = 1,
            CreatedAt = DateTime.UtcNow.AddDays(-10)
        };
        db.Transactions.Add(transaction);
        await db.SaveChangesAsync();

        db.Assignments.Add(new Assignment
        {
            TransactionId = transaction.Id,
            DepartmentId = 10,
            AssignedDate = transaction.IncomingDate,
            RequiresReply = true,
            ReplyStatus = ReplyStatus.Pending,
            Status = AssignmentStatus.Active,
            DueDate = pastDue,
            CreatedById = 1,
            CreatedAt = DateTime.UtcNow.AddDays(-10)
        });
        await db.SaveChangesAsync();
        counter.Reset();

        var detail = await service.GetByIdAsync(transaction.Id, new TestCurrentUser());

        Assert.NotNull(detail);
        Assert.True(detail!.IsOverdue);
        Assert.Equal(0, counter.Count);
        Assert.Equal(ReplyStatus.Pending, await db.Assignments.Where(a => a.TransactionId == transaction.Id).Select(a => a.ReplyStatus).SingleAsync());
        Assert.Equal(TransactionStatus.WaitingForReply, await db.Transactions.Where(t => t.Id == transaction.Id).Select(t => t.Status).SingleAsync());
    }

    [Fact]
    public async Task GetAssignmentsAsync_reports_overdue_without_persisting_status()
    {
        var (service, db, counter) = await CreateServiceAsync(nameof(GetAssignmentsAsync_reports_overdue_without_persisting_status));
        var pastDue = DateTime.UtcNow.AddDays(-2);
        var transaction = new Transaction
        {
            InternalTrackingNumber = "UQEB-2026-00100",
            IncomingNumber = "IN-READ-2",
            IncomingDate = DateTime.UtcNow.AddDays(-7),
            Subject = "تعيينات للقراءة",
            IncomingSourceType = IncomingSourceType.External,
            IncomingFrom = "جهة",
            RequiresResponse = false,
            ResponseType = ResponseType.None,
            Priority = Priority.Normal,
            Status = TransactionStatus.WaitingForReply,
            CreatedById = 1,
            CreatedAt = DateTime.UtcNow.AddDays(-7)
        };
        db.Transactions.Add(transaction);
        await db.SaveChangesAsync();
        db.Assignments.Add(new Assignment
        {
            TransactionId = transaction.Id,
            DepartmentId = 10,
            AssignedDate = transaction.IncomingDate,
            RequiresReply = true,
            ReplyStatus = ReplyStatus.Pending,
            Status = AssignmentStatus.Active,
            DueDate = pastDue,
            CreatedById = 1,
            CreatedAt = DateTime.UtcNow.AddDays(-7)
        });
        await db.SaveChangesAsync();
        counter.Reset();

        var assignments = await service.GetAssignmentsAsync(transaction.Id, new TestCurrentUser());

        Assert.NotNull(assignments);
        Assert.Single(assignments!);
        Assert.True(assignments![0].IsOverdue);
        Assert.Equal(0, counter.Count);
        Assert.Equal(ReplyStatus.Pending, await db.Assignments.Where(a => a.TransactionId == transaction.Id).Select(a => a.ReplyStatus).SingleAsync());
    }
}
