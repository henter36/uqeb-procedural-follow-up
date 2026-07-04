using Microsoft.EntityFrameworkCore;
using Uqeb.Api.Data;
using Uqeb.Api.Models.Entities;
using Uqeb.Api.Models.Enums;
using Uqeb.Api.Reporting.DTOs;
using Uqeb.Api.Reporting.Enums;
using Xunit;

namespace Uqeb.Api.Tests.Reporting;

/// <summary>
/// The overdue report answers "what's open and overdue as of the period end", not "what
/// arrived during the period and is now overdue". These tests pin down that a transaction
/// that arrived before the requested period, but is still open and overdue as of DateTo,
/// must not disappear just because a later period was requested — while the general/
/// comprehensive report keeps filtering strictly by IncomingDate, unaffected.
/// All dates are relative to DateTime.UtcNow so the suite never rots into a time bomb.
/// </summary>
public class InstitutionalReportOverdueAnchorTests
{
    private static async Task<(AppDbContext Db, User User, TestDbContextFactory Factory)> SeedBaseAsync(string dbName)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;
        var db = new AppDbContext(options);
        await db.Database.EnsureCreatedAsync();

        var user = new User
        {
            Username = "overdue-anchor-tester",
            PasswordHash = "hash",
            FullName = "Overdue Anchor Tester",
            Role = UserRole.Admin,
            IsActive = true,
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        return (db, user, new TestDbContextFactory(options));
    }

    private static Transaction BuildTransaction(
        User user,
        string trackingNumber,
        DateTime incomingDate,
        TransactionStatus status,
        DateTime? responseDueDate,
        DateTime? closedAt = null,
        bool responseCompleted = false) => new()
    {
        InternalTrackingNumber = trackingNumber,
        IncomingNumber = $"IN-{trackingNumber}",
        IncomingDate = incomingDate,
        Subject = $"معاملة اختبار {trackingNumber}",
        Status = status,
        RequiresResponse = responseDueDate.HasValue,
        ResponseDueDate = responseDueDate,
        ResponseCompleted = responseCompleted,
        ClosedAt = closedAt,
        CreatedById = user.Id,
    };

    private static ReportBuildRequestDto BuildRequest(
        InstitutionalReportType reportType, DateTime periodFrom, DateTime periodTo) => new()
    {
        ReportType = reportType,
        SectionIds = [ReportSectionId.TransactionDetails],
        Filters = new ReportFiltersDto
        {
            DateFrom = periodFrom,
            DateTo = periodTo,
        },
    };

    [Fact]
    public async Task OverdueReport_IncludesOpenOverdueTransaction_IncomingBeforePeriodStart()
    {
        // Scenario 1: arrived well before the period, still open and overdue as of DateTo.
        var (db, user, factory) = await SeedBaseAsync($"overdue-anchor-old-{Guid.NewGuid():N}");
        var today = DateTime.UtcNow.Date;
        var periodFrom = today.AddDays(-20);
        var periodTo = today.AddDays(-5);

        db.Transactions.Add(BuildTransaction(
            user, "OLD-OVERDUE",
            incomingDate: today.AddDays(-40),
            status: TransactionStatus.New,
            responseDueDate: today.AddDays(-35)));
        await db.SaveChangesAsync();

        var service = InstitutionalReportServiceTestHelpers.CreateService(factory);
        var model = await service.BuildReportModelAsync(
            BuildRequest(InstitutionalReportType.OverdueTransactions, periodFrom, periodTo));

        Assert.Equal(1, model.TotalMatchedRows);
        Assert.Contains(model.Transactions, t => t.TrackingNumber == "OLD-OVERDUE");
    }

    [Fact]
    public async Task OverdueReport_IncludesOpenOverdueTransaction_IncomingWithinPeriod()
    {
        // Scenario 2: arrived inside the period and is overdue — must keep working as before.
        var (db, user, factory) = await SeedBaseAsync($"overdue-anchor-current-{Guid.NewGuid():N}");
        var today = DateTime.UtcNow.Date;
        var periodFrom = today.AddDays(-20);
        var periodTo = today.AddDays(-5);

        db.Transactions.Add(BuildTransaction(
            user, "CURRENT-OVERDUE",
            incomingDate: today.AddDays(-15),
            status: TransactionStatus.New,
            responseDueDate: today.AddDays(-10)));
        await db.SaveChangesAsync();

        var service = InstitutionalReportServiceTestHelpers.CreateService(factory);
        var model = await service.BuildReportModelAsync(
            BuildRequest(InstitutionalReportType.OverdueTransactions, periodFrom, periodTo));

        Assert.Equal(1, model.TotalMatchedRows);
        Assert.Contains(model.Transactions, t => t.TrackingNumber == "CURRENT-OVERDUE");
    }

    [Fact]
    public async Task OverdueReport_ExcludesTransaction_IncomingAfterPeriodEnd()
    {
        // Scenario 3: arrived after DateTo — future relative to the report period — excluded
        // even though it would look overdue if evaluated against the real "today".
        var (db, user, factory) = await SeedBaseAsync($"overdue-anchor-future-{Guid.NewGuid():N}");
        var today = DateTime.UtcNow.Date;
        var periodFrom = today.AddDays(-20);
        var periodTo = today.AddDays(-5);

        db.Transactions.Add(BuildTransaction(
            user, "FUTURE-FOR-PERIOD",
            incomingDate: today.AddDays(-2),
            status: TransactionStatus.New,
            responseDueDate: today.AddDays(-1)));
        await db.SaveChangesAsync();

        var service = InstitutionalReportServiceTestHelpers.CreateService(factory);
        var model = await service.BuildReportModelAsync(
            BuildRequest(InstitutionalReportType.OverdueTransactions, periodFrom, periodTo));

        Assert.Equal(0, model.TotalMatchedRows);
        Assert.DoesNotContain(model.Transactions, t => t.TrackingNumber == "FUTURE-FOR-PERIOD");
    }

    [Fact]
    public async Task OverdueReport_ExcludesTransaction_ClosedAfterDeadline()
    {
        // Scenario 4: closed late — that's a distinct "completed after the deadline" signal,
        // not a currently-open overdue one, so it must not appear in the overdue report.
        var (db, user, factory) = await SeedBaseAsync($"overdue-anchor-closed-late-{Guid.NewGuid():N}");
        var today = DateTime.UtcNow.Date;
        var periodFrom = today.AddDays(-20);
        var periodTo = today.AddDays(-5);

        db.Transactions.Add(BuildTransaction(
            user, "CLOSED-LATE",
            incomingDate: today.AddDays(-25),
            status: TransactionStatus.Closed,
            responseDueDate: today.AddDays(-20),
            closedAt: today.AddDays(-10),
            responseCompleted: true));
        await db.SaveChangesAsync();

        var service = InstitutionalReportServiceTestHelpers.CreateService(factory);
        var model = await service.BuildReportModelAsync(
            BuildRequest(InstitutionalReportType.OverdueTransactions, periodFrom, periodTo));

        Assert.Equal(0, model.TotalMatchedRows);
        Assert.DoesNotContain(model.Transactions, t => t.TrackingNumber == "CLOSED-LATE");
    }

    [Fact]
    public async Task GeneralReport_StillFiltersByIncomingDate_UnaffectedByOverdueAnchorChange()
    {
        // Scenario 5: proves the incoming-report / general-report behavior did not change —
        // a transaction that arrived before DateFrom must still be excluded from a general
        // (non-overdue) report for the same period, even though it would now show up in the
        // overdue report for that same period.
        var (db, user, factory) = await SeedBaseAsync($"overdue-anchor-general-{Guid.NewGuid():N}");
        var today = DateTime.UtcNow.Date;
        var periodFrom = today.AddDays(-20);
        var periodTo = today.AddDays(-5);

        db.Transactions.Add(BuildTransaction(
            user, "OLD-OVERDUE-GENERAL",
            incomingDate: today.AddDays(-40),
            status: TransactionStatus.New,
            responseDueDate: today.AddDays(-35)));
        db.Transactions.Add(BuildTransaction(
            user, "CURRENT-GENERAL",
            incomingDate: today.AddDays(-15),
            status: TransactionStatus.New,
            responseDueDate: today.AddDays(-10)));
        await db.SaveChangesAsync();

        var service = InstitutionalReportServiceTestHelpers.CreateService(factory);
        var model = await service.BuildReportModelAsync(
            BuildRequest(InstitutionalReportType.ExecutiveComprehensive, periodFrom, periodTo));

        Assert.Equal(1, model.TotalMatchedRows);
        Assert.DoesNotContain(model.Transactions, t => t.TrackingNumber == "OLD-OVERDUE-GENERAL");
        Assert.Contains(model.Transactions, t => t.TrackingNumber == "CURRENT-GENERAL");
    }

    [Fact]
    public async Task OverdueReport_EvaluatesOverdueStatus_AsOfDateTo_NotAsOfRealToday()
    {
        // Guards the core fix: a transaction not yet due as of the period end (DateTo) must
        // not appear just because it happens to look overdue by the real, current-day clock.
        var (db, user, factory) = await SeedBaseAsync($"overdue-anchor-asof-{Guid.NewGuid():N}");
        var today = DateTime.UtcNow.Date;
        var periodFrom = today.AddDays(-20);
        var periodTo = today.AddDays(-5);

        db.Transactions.Add(BuildTransaction(
            user, "NOT-YET-DUE-AT-PERIOD-END",
            incomingDate: today.AddDays(-10),
            status: TransactionStatus.New,
            responseDueDate: today.AddDays(-3))); // after periodTo(-5), before real today(0)
        await db.SaveChangesAsync();

        var service = InstitutionalReportServiceTestHelpers.CreateService(factory);
        var model = await service.BuildReportModelAsync(
            BuildRequest(InstitutionalReportType.OverdueTransactions, periodFrom, periodTo));

        Assert.Equal(0, model.TotalMatchedRows);
        Assert.DoesNotContain(model.Transactions, t => t.TrackingNumber == "NOT-YET-DUE-AT-PERIOD-END");
    }

    [Fact]
    public async Task OverdueReport_PreviewCountMatchesDetailRowCount()
    {
        // Requirement: count-matching and detail-row loading must never disagree.
        var (db, user, factory) = await SeedBaseAsync($"overdue-anchor-parity-{Guid.NewGuid():N}");
        var today = DateTime.UtcNow.Date;
        var periodFrom = today.AddDays(-20);
        var periodTo = today.AddDays(-5);

        db.Transactions.Add(BuildTransaction(
            user, "PARITY-1", today.AddDays(-40), TransactionStatus.New, today.AddDays(-35)));
        db.Transactions.Add(BuildTransaction(
            user, "PARITY-2", today.AddDays(-15), TransactionStatus.New, today.AddDays(-10)));
        db.Transactions.Add(BuildTransaction(
            user, "PARITY-3-FUTURE", today.AddDays(-2), TransactionStatus.New, today.AddDays(-1)));
        await db.SaveChangesAsync();

        var service = InstitutionalReportServiceTestHelpers.CreateService(factory);
        var model = await service.BuildReportModelAsync(
            BuildRequest(InstitutionalReportType.OverdueTransactions, periodFrom, periodTo));

        Assert.Equal(2, model.TotalMatchedRows);
        Assert.Equal(model.TotalMatchedRows, model.Transactions.Count);
        Assert.Equal(model.TotalMatchedRows, model.Metadata.TotalMatchingTransactions);
    }
}
