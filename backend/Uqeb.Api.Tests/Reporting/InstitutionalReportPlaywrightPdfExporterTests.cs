using Microsoft.Playwright;
using Uqeb.Api.Reporting.DTOs;
using Uqeb.Api.Reporting.Enums;
using Uqeb.Api.Reporting.Exporters;
using Uqeb.Api.Reporting.Services;
using Xunit;

namespace Uqeb.Api.Tests.Reporting;

public class InstitutionalReportPlaywrightPdfExporterTests
{
    private static async Task<bool> IsPlaywrightAvailableAsync()
    {
        try
        {
            using var playwright = await Playwright.CreateAsync();
            await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });
            return true;
        }
        catch
        {
            return false;
        }
    }

    [Fact]
    public async Task ExportAsync_ProducesSearchablePdf_WithArabicTableStructure()
    {
        if (!await IsPlaywrightAvailableAsync())
            return;

        var renderer = new InstitutionalReportRenderer();
        var manifest = BuildManifestWithDepartmentTable();
        var html = renderer.RenderHtmlDocument(manifest);

        Assert.Contains("report-table", html);
        Assert.Contains("أداء الإدارات", html);
        Assert.Contains("Uqeb Report Arabic", html);

        await using var exporter = new InstitutionalReportPlaywrightPdfExporter();
        var pdf = await exporter.ExportAsync(manifest, html);

        Assert.NotEmpty(pdf);
        Assert.Equal(0x25, pdf[0]);
        Assert.Equal(0x50, pdf[1]);
        Assert.Equal(0x44, pdf[2]);
        Assert.Equal(0x46, pdf[3]);
        Assert.True(pdf.Length > 2_000);
    }

    [Fact]
    public async Task ExportAsync_PartialManifest_HasCorrectPageNumberingInHtml()
    {
        if (!await IsPlaywrightAvailableAsync())
            return;

        var renderer = new InstitutionalReportRenderer();
        var source = BuildManifestWithDepartmentTable();
        var exportManifest = renderer.BuildExportManifest(source, [1], new ReportExportRequestDto
        {
            IncludePartialCover = true,
            IncludePartialManifest = true,
            PageNumberingMode = PageNumberingMode.Restart,
        });

        Assert.DoesNotContain("الصفحة 0 من 0", exportManifest.Pages[0].HtmlContent);
        Assert.Equal(1, exportManifest.Pages[0].RenderedPageNumber);

        await using var exporter = new InstitutionalReportPlaywrightPdfExporter();
        var pdf = await exporter.ExportAsync(exportManifest, renderer.RenderHtmlDocument(exportManifest));
        Assert.True(pdf.Length > 1_000);
    }

    private static RenderedReportManifestDto BuildManifestWithDepartmentTable()
    {
        var renderer = new InstitutionalReportRenderer();
        var model = new InstitutionalReportModel
        {
            Metadata = new ReportMetadataDto
            {
                ReportNumber = "REP-2026-000099",
                ReportTypeName = "تقرير تجريبي",
                Title = "تقرير المتابعة الإجرائية للمعاملات",
                IssueDate = new DateTime(2026, 6, 1),
                PeriodFrom = new DateTime(2026, 1, 1),
                PeriodTo = new DateTime(2026, 6, 1),
            },
            Summary = new ExecutiveSummaryDto
            {
                KpiCards =
                [
                    new KpiCardDto { Key = "total", Title = "إجمالي المعاملات", Value = "12" },
                ],
                ExecutiveNarrative = "ملخص تنفيذي عربي للاختبار.",
            },
            DepartmentPerformance =
            [
                new DepartmentPerformanceRowDto
                {
                    DepartmentName = "الشؤون الإدارية",
                    TotalTransactions = 5,
                    RatingLabel = "جيد",
                },
            ],
        };

        return renderer.RenderManifest(model,
        [
            ReportSectionId.Cover,
            ReportSectionId.ExecutiveSummary,
            ReportSectionId.DepartmentPerformance,
        ]);
    }
}
