using Microsoft.EntityFrameworkCore;
using Uqeb.Api.Data;
using Uqeb.Api.Models.Entities;
using Uqeb.Api.Models.Enums;
using Uqeb.Api.Reporting.Configuration;
using Uqeb.Api.Reporting.DTOs;
using Uqeb.Api.Reporting.Enums;
using Uqeb.Api.Reporting.Services;
using Xunit;

namespace Uqeb.Api.Tests.Reporting;

public class InstitutionalReportMetricsSeparationTests
{
    private const int DetailLimit = 2;

    [Fact]
    public async Task BuildReportModelAsync_ComputesKpisFromFullDatasetWhileDetailsRespectLimit()
    {
        var dbFactory = await CreateSeededFactoryAsync(closedCount: 3, openCount: 2);
        var service = InstitutionalReportServiceTestHelpers.CreateService(
            dbFactory,
            new ReportingOptions { MaxPdfDetailRows = DetailLimit });

        var model = await service.BuildReportModelAsync(new ReportBuildRequestDto
        {
            ReportType = InstitutionalReportType.ExecutiveComprehensive,
            SectionIds = [ReportSectionId.ExecutiveSummary, ReportSectionId.TransactionDetails],
        });

        Assert.Equal(5, model.TotalMatchedRows);
        Assert.Equal(2, model.ExportedDetailRows);
        Assert.True(model.DetailRowsTruncated);
        Assert.Equal(3, model.DetailPartsCount);
        Assert.Equal(2, model.Transactions.Count);

        Assert.Equal("5", NormalizeKpiValue(model.Summary.KpiCards.First(c => c.Key == "total").Value));
        Assert.Equal("3", NormalizeKpiValue(model.Summary.KpiCards.First(c => c.Key == "closed").Value));
        Assert.Equal("2", NormalizeKpiValue(model.Summary.KpiCards.First(c => c.Key == "open").Value));
    }

    [Fact]
    public async Task RenderPreviewAsync_ExecutiveSummaryReflectsFullDatasetWhenDetailsAreLimited()
    {
        var dbFactory = await CreateSeededFactoryAsync(closedCount: 3, openCount: 2);
        var service = InstitutionalReportServiceTestHelpers.CreateService(
            dbFactory,
            new ReportingOptions { MaxPdfDetailRows = DetailLimit });

        var manifest = await service.RenderPreviewAsync(new ReportBuildRequestDto
        {
            ReportType = InstitutionalReportType.ExecutiveComprehensive,
            SectionIds = [ReportSectionId.ExecutiveSummary, ReportSectionId.TransactionDetails],
        });

        Assert.Equal(5, manifest.TotalMatchedRows);
        Assert.Equal(2, manifest.ExportedDetailRows);
        Assert.True(manifest.DetailRowsTruncated);

        var summaryPage = Assert.Single(manifest.Pages, p => p.SectionId == ReportSectionId.ExecutiveSummary);
        Assert.Contains("5", summaryPage.HtmlContent);
        Assert.Contains("3", summaryPage.HtmlContent);

        var detailPages = manifest.Pages.Where(p => p.SectionId == ReportSectionId.TransactionDetails).ToList();
        Assert.Contains(detailPages, p => p.PageTitle.Contains("تنبيه"));
        Assert.Contains(detailPages, p => p.HtmlContent.Contains("5") && p.HtmlContent.Contains("2"));
    }

    [Fact]
    public async Task ExportAsync_SummaryOnly_KpisReflectFullDatasetWhileExportedDetailRowsAreZero()
    {
        var dbFactory = await CreateSeededFactoryAsync(closedCount: 3, openCount: 2);
        var service = InstitutionalReportServiceTestHelpers.CreateService(
            dbFactory,
            new ReportingOptions { MaxPdfDetailRows = DetailLimit });

        var result = await service.ExportAsync(new ReportExportRequestDto
        {
            ExportFormat = ExportFormat.Pdf,
            ExportMode = ExportMode.FullReport,
            DetailOverflowAction = DetailOverflowAction.SummaryOnly,
            BuildRequest = new ReportBuildRequestDto
            {
                ReportType = InstitutionalReportType.ExecutiveComprehensive,
                SectionIds = [ReportSectionId.ExecutiveSummary, ReportSectionId.TransactionDetails],
            },
        });

        Assert.Equal(5, result.Manifest.TotalMatchedRows);
        Assert.Equal(0, result.Manifest.ExportedDetailRows);
        Assert.True(result.Manifest.DetailRowsTruncated);
        Assert.DoesNotContain(ReportSectionId.TransactionDetails, result.Manifest.Pages.Select(p => p.SectionId));
        Assert.Contains(result.Manifest.Pages, p => p.PageTitle.Contains("تنبيه"));
    }

    private static string NormalizeKpiValue(string value) =>
        value.Replace(",", string.Empty).Replace("٬", string.Empty).Trim();

    private static async Task<IDbContextFactory<AppDbContext>> CreateSeededFactoryAsync(
        int closedCount,
        int openCount)
    {
        var dbName = $"metrics-sep-{Guid.NewGuid():N}";
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;
        var dbFactory = new TestDbContextFactory(options);

        await using var db = dbFactory.CreateDbContext();
        var user = new User
        {
            Username = "metrics-sep-test",
            PasswordHash = "hash",
            FullName = "Metrics Separation Test",
            Role = UserRole.Admin,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var today = DateTime.UtcNow.Date;
        var total = closedCount + openCount;
        for (var i = 1; i <= total; i++)
        {
            var isOpen = i <= openCount;
            db.Transactions.Add(new Transaction
            {
                InternalTrackingNumber = $"INT-{i:D4}",
                IncomingNumber = $"IN-{i:D4}",
                IncomingDate = today.AddDays(-i),
                Subject = $"معاملة {i}",
                IncomingFrom = "جهة",
                Status = isOpen ? TransactionStatus.New : TransactionStatus.Closed,
                ClosedAt = isOpen ? null : today.AddDays(-i),
                Priority = Priority.Normal,
                CreatedById = user.Id,
                CreatedAt = DateTime.UtcNow,
            });
        }

        await db.SaveChangesAsync();
        return dbFactory;
    }
}
