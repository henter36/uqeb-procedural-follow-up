using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Uqeb.Api.Data;
using Uqeb.Api.DTOs.Reports;
using Uqeb.Api.Models.Entities;
using Uqeb.Api.Models.Enums;
using Uqeb.Api.Services;
using Xunit;

namespace Uqeb.Api.Tests;

public class ReportQueryConsolidationTests
{
    private sealed class CountingDbContextFactory : IDbContextFactory<AppDbContext>
    {
        private readonly DbContextOptions<AppDbContext> _options;

        public CountingDbContextFactory(DbContextOptions<AppDbContext> options) => _options = options;

        public int CreateCount { get; private set; }

        public AppDbContext CreateDbContext()
        {
            CreateCount++;
            return new AppDbContext(_options);
        }

        public ValueTask<AppDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
        {
            CreateCount++;
            return ValueTask.FromResult(new AppDbContext(_options));
        }
    }

    private static async Task<(ReportService Service, AppDbContext Db, CountingDbContextFactory Factory)> CreateServiceAsync(string dbName)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(dbName)
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        var db = new AppDbContext(options);
        db.Departments.Add(new Department { Id = 10, Name = "المالية", NameNormalized = "المالية", IsActive = true });
        db.Transactions.Add(new Transaction
        {
            Id = 1,
            InternalTrackingNumber = "UQEB-2026-00001",
            IncomingNumber = "IN-1",
            IncomingDate = DateTime.UtcNow.AddDays(-5),
            Subject = "مفتوحة",
            IncomingSourceType = IncomingSourceType.External,
            IncomingFrom = "جهة",
            RequiresResponse = true,
            ResponseCompleted = false,
            ResponseDueDate = DateTime.UtcNow.AddDays(-1),
            Status = TransactionStatus.WaitingForReply,
            Priority = Priority.Normal,
            CreatedAt = DateTime.UtcNow.AddDays(-5)
        });
        db.Transactions.Add(new Transaction
        {
            Id = 2,
            InternalTrackingNumber = "UQEB-2026-00002",
            IncomingNumber = "IN-2",
            IncomingDate = DateTime.UtcNow.AddDays(-3),
            Subject = "مغلقة",
            IncomingSourceType = IncomingSourceType.External,
            IncomingFrom = "جهة",
            RequiresResponse = false,
            Status = TransactionStatus.Closed,
            ClosedAt = DateTime.UtcNow.AddDays(-1),
            Priority = Priority.Normal,
            CreatedAt = DateTime.UtcNow.AddDays(-3)
        });
        db.Assignments.Add(new Assignment
        {
            TransactionId = 1,
            DepartmentId = 10,
            AssignedDate = DateTime.UtcNow.AddDays(-2),
            RequiresReply = true,
            ReplyStatus = ReplyStatus.Pending,
            Status = AssignmentStatus.Active,
            DueDate = DateTime.UtcNow.AddDays(-1)
        });
        await db.SaveChangesAsync();

        var factory = new CountingDbContextFactory(options);
        var service = new ReportService(db, factory);
        return (service, db, factory);
    }

    private static (ReportService Service, AppDbContext Db, CountingDbContextFactory Factory) CreateEmptyService(string dbName)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(dbName)
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        var db = new AppDbContext(options);
        var factory = new CountingDbContextFactory(options);
        return (new ReportService(db, factory), db, factory);
    }

    [Fact]
    public async Task GetPageSummaryAsync_uses_single_aggregate_query()
    {
        var (service, _, factory) = await CreateServiceAsync(nameof(GetPageSummaryAsync_uses_single_aggregate_query));

        var counts = await service.GetPageSummaryAsync();

        Assert.Equal(1, factory.CreateCount);
        Assert.Equal(1, counts.ResponseRequired);
        Assert.Equal(1, counts.OverdueResponses);
        Assert.Equal(1, counts.OpenAssignments);
        Assert.Equal(1, counts.Open);
    }

    [Fact]
    public async Task GetDashboardAsync_keeps_waiting_and_partial_counts_separate()
    {
        var (service, db, _) = await CreateServiceAsync(nameof(GetDashboardAsync_keeps_waiting_and_partial_counts_separate));
        db.Transactions.Add(new Transaction
        {
            Id = 3,
            InternalTrackingNumber = "UQEB-2026-00003",
            IncomingNumber = "IN-3",
            IncomingDate = DateTime.UtcNow.AddDays(-2),
            Subject = "رد جزئي",
            IncomingSourceType = IncomingSourceType.External,
            IncomingFrom = "جهة",
            RequiresResponse = false,
            Status = TransactionStatus.PartiallyReplied,
            Priority = Priority.Normal,
            CreatedAt = DateTime.UtcNow.AddDays(-2)
        });
        await db.SaveChangesAsync();

        var dashboard = await service.GetDashboardAsync();

        Assert.Equal(1, dashboard.WaitingForReply);
        Assert.Equal(1, dashboard.PartiallyReplied);
    }

    [Fact]
    public async Task GetPageSummaryAsync_does_not_count_response_due_today_as_overdue()
    {
        var (service, db, _) = CreateEmptyService(nameof(GetPageSummaryAsync_does_not_count_response_due_today_as_overdue));
        var now = DateTime.UtcNow;
        db.Transactions.Add(new Transaction
        {
            InternalTrackingNumber = "UQEB-2026-DUE-TODAY",
            IncomingNumber = "IN-DUE-TODAY",
            IncomingDate = now.AddDays(-2),
            Subject = "استحقاق اليوم",
            IncomingSourceType = IncomingSourceType.External,
            IncomingFrom = "جهة",
            RequiresResponse = true,
            ResponseCompleted = false,
            ResponseDueDate = now.Date,
            Status = TransactionStatus.WaitingForReply,
            Priority = Priority.Normal,
            CreatedAt = now.AddDays(-2)
        });
        await db.SaveChangesAsync();

        var counts = await service.GetPageSummaryAsync();

        Assert.Equal(0, counts.OverdueResponses);
        Assert.Equal(0, counts.Overdue);
    }

    [Fact]
    public async Task GetPageSummaryAsync_counts_closed_after_due_date_as_overdue()
    {
        var (service, db, _) = CreateEmptyService(nameof(GetPageSummaryAsync_counts_closed_after_due_date_as_overdue));
        var dueDate = DateTime.UtcNow.Date.AddDays(-5);
        db.Transactions.Add(new Transaction
        {
            InternalTrackingNumber = "UQEB-2026-CLOSED-LATE",
            IncomingNumber = "IN-CLOSED-LATE",
            IncomingDate = dueDate.AddDays(-10),
            Subject = "مغلقة متأخرة",
            IncomingSourceType = IncomingSourceType.External,
            IncomingFrom = "جهة",
            RequiresResponse = true,
            ResponseCompleted = true,
            ResponseCompletedDate = dueDate.AddDays(2),
            ResponseDueDate = dueDate,
            Status = TransactionStatus.Closed,
            ClosedAt = dueDate.AddDays(2),
            Priority = Priority.Normal,
            CreatedAt = dueDate.AddDays(-10)
        });
        await db.SaveChangesAsync();

        var counts = await service.GetPageSummaryAsync();

        Assert.Equal(1, counts.OverdueResponses);
        Assert.Equal(1, counts.Overdue);
    }
}
