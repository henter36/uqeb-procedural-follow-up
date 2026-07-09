using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using Uqeb.Api.Data;
using Uqeb.Api.Models.Entities;
using Uqeb.Api.Models.Enums;
using Uqeb.Api.Reporting.DTOs;
using Uqeb.Api.Reporting.Enums;
using Uqeb.Api.Reporting.Exporters;
using Uqeb.Api.Reporting.Services;
using Xunit;

namespace Uqeb.Api.Tests.Reporting;

public class InstitutionalReportPeriodBurdenTests
{
    [Fact]
    public async Task ComprehensiveReport_SeparatesPeriodIncomingFromCarriedOpenBalance()
    {
        var today = DateTime.UtcNow.Date;
        var periodFrom = today.AddDays(-20);
        var periodTo = today.AddDays(-5);
        var (db, user, factory) = await CreateSeededContextAsync();

        db.Transactions.Add(BuildTransaction(user, "OLD-OPEN", today.AddDays(-800), TransactionStatus.New));
        db.Transactions.Add(BuildTransaction(user, "PERIOD-OPEN", today.AddDays(-10), TransactionStatus.New));
        db.Transactions.Add(BuildTransaction(user, "FUTURE", today.AddDays(-2), TransactionStatus.New));
        db.Transactions.Add(BuildTransaction(
            user,
            "CLOSED-BEFORE",
            today.AddDays(-40),
            TransactionStatus.Closed,
            closedAt: today.AddDays(-25),
            responseCompleted: true));
        db.Transactions.Add(BuildTransaction(
            user,
            "CLOSED-AFTER-PERIOD",
            today.AddDays(-40),
            TransactionStatus.Closed,
            closedAt: today.AddDays(-3),
            responseCompleted: true));
        await db.SaveChangesAsync();

        var model = await InstitutionalReportServiceTestHelpers.CreateService(factory)
            .BuildReportModelAsync(BuildRequest(periodFrom, periodTo));

        Assert.Equal("1", SummaryValue(model, "periodIncoming"));
        Assert.Equal("2", SummaryValue(model, "carriedOpenBalance"));
        Assert.Equal("3", SummaryValue(model, "activeBurden"));
        Assert.Contains(model.Transactions, t => t.TrackingNumber == "OLD-OPEN");
        Assert.Contains(model.Transactions, t => t.TrackingNumber == "PERIOD-OPEN");
        Assert.Contains(model.Transactions, t => t.TrackingNumber == "CLOSED-AFTER-PERIOD");
        Assert.DoesNotContain(model.Transactions, t => t.TrackingNumber == "FUTURE");
        Assert.DoesNotContain(model.Transactions, t => t.TrackingNumber == "CLOSED-BEFORE");
    }

    [Fact]
    public async Task ComprehensiveReport_ExcludesPrePeriodProcedurallyCompleteTransactionFromFinalScope()
    {
        var today = DateTime.UtcNow.Date;
        var periodFrom = today.AddDays(-20);
        var periodTo = today.AddDays(-5);
        var (db, user, factory) = await CreateSeededContextAsync();
        var department = new Department { Name = "الإدارة المختبرة", NameNormalized = "الإدارة المختبرة" };
        var transaction = BuildTransaction(user, "PROCEDURAL-DONE", today.AddDays(-40), TransactionStatus.New);
        transaction.Assignments.Add(new Assignment
        {
            Department = department,
            AssignedDate = today.AddDays(-39),
            RequiresReply = true,
            ReplyStatus = ReplyStatus.Replied,
            ReplyDate = periodTo.AddDays(-1),
            Status = AssignmentStatus.Completed,
            CreatedById = user.Id,
        });

        db.Departments.Add(department);
        db.Transactions.Add(transaction);
        await db.SaveChangesAsync();

        var model = await InstitutionalReportServiceTestHelpers.CreateService(factory)
            .BuildReportModelAsync(BuildRequest(periodFrom, periodTo));

        Assert.Empty(model.Transactions);
        Assert.Equal("0", SummaryValue(model, "periodIncoming"));
        Assert.Equal("0", SummaryValue(model, "carriedOpenBalance"));
        Assert.Equal("0", SummaryValue(model, "activeBurden"));
        Assert.Equal(0, model.TotalMatchedRows);
    }

    [Fact]
    public async Task ComprehensiveReport_CriticalCasesIncludeVeryOldOpenNormalPriorityWithoutDueDates()
    {
        var today = DateTime.UtcNow.Date;
        var periodFrom = today.AddDays(-20);
        var periodTo = today.AddDays(-5);
        var (db, user, factory) = await CreateSeededContextAsync();

        db.Transactions.Add(BuildTransaction(user, "OLD-NORMAL-NO-DUE", today.AddDays(-900), TransactionStatus.New));
        await db.SaveChangesAsync();

        var model = await InstitutionalReportServiceTestHelpers.CreateService(factory)
            .BuildReportModelAsync(BuildRequest(periodFrom, periodTo));

        var criticalCase = Assert.Single(model.Analysis.CriticalCases, c => c.ReasonCode == "OLD_OPEN_TRANSACTION");
        Assert.Equal("IN-OLD-NORMAL-NO-DUE", criticalCase.IncomingNumber);
        Assert.True(criticalCase.AgeDays >= 895);
    }

    [Fact]
    public async Task TransactionDetailsExports_DoNotDisplayInternalTrackingNumber()
    {
        var today = DateTime.UtcNow.Date;
        var periodFrom = today.AddDays(-20);
        var periodTo = today.AddDays(-5);
        var (db, user, factory) = await CreateSeededContextAsync();

        db.Transactions.Add(BuildTransaction(
            user,
            "INT-HIDE-001",
            today.AddDays(-10),
            TransactionStatus.New,
            incomingNumber: "IN-KEEP-001"));
        await db.SaveChangesAsync();

        var model = await InstitutionalReportServiceTestHelpers.CreateService(factory)
            .BuildReportModelAsync(BuildRequest(periodFrom, periodTo));
        var renderer = new InstitutionalReportRenderer();
        var manifest = renderer.RenderManifest(model, [ReportSectionId.TransactionDetails]);
        var html = string.Join('\n', manifest.Pages.Select(p => p.HtmlContent));

        Assert.DoesNotContain("INT-HIDE-001", html);
        Assert.Contains("IN-KEEP-001", html);

        var bytes = InstitutionalReportXlsxExporter.Export(model, manifest, new ReportExportRequestDto());
        using var workbook = new XLWorkbook(new MemoryStream(bytes));
        var details = workbook.Worksheet("المعاملات التفصيلية");
        var sheetText = string.Join('|', details.CellsUsed().Select(c => c.GetString()));
        Assert.DoesNotContain("INT-HIDE-001", sheetText);
        Assert.Contains("IN-KEEP-001", sheetText);
    }

    private static async Task<(AppDbContext Db, User User, TestDbContextFactory Factory)> CreateSeededContextAsync()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"period-burden-{Guid.NewGuid():N}")
            .Options;
        var db = new AppDbContext(options);
        await db.Database.EnsureCreatedAsync();
        var user = new User
        {
            Username = "period-burden-tester",
            PasswordHash = "hash",
            FullName = "Period Burden Tester",
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
        DateTime? closedAt = null,
        bool responseCompleted = false,
        string? incomingNumber = null) => new()
    {
        InternalTrackingNumber = trackingNumber,
        IncomingNumber = incomingNumber ?? $"IN-{trackingNumber}",
        IncomingDate = incomingDate,
        Subject = "معاملة اختبار",
        Priority = Priority.Normal,
        Status = status,
        RequiresResponse = false,
        ResponseCompleted = responseCompleted,
        ResponseCompletedDate = responseCompleted ? closedAt : null,
        ClosedAt = closedAt,
        CreatedById = user.Id,
    };

    private static ReportBuildRequestDto BuildRequest(DateTime periodFrom, DateTime periodTo) => new()
    {
        ReportType = InstitutionalReportType.ExecutiveComprehensive,
        SectionIds =
        [
            ReportSectionId.ExecutiveSummary,
            ReportSectionId.CriticalCases,
            ReportSectionId.TransactionDetails
        ],
        Filters = new ReportFiltersDto
        {
            DateFrom = periodFrom,
            DateTo = periodTo,
        },
    };

    private static string SummaryValue(InstitutionalReportModel model, string key) =>
        Assert.Single(model.Summary.KpiCards, c => c.Key == key).Value;
}
