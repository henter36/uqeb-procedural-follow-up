using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Playwright;
using System.Text.RegularExpressions;
using Uqeb.Api.Reporting.DTOs;
using Uqeb.Api.Reporting.Enums;
using Uqeb.Api.Reporting.Exporters;
using Uqeb.Api.Reporting.Operations;
using Uqeb.Api.Tests.Reporting.Visual;
using Uqeb.Api.Reporting.Services;
using Xunit;

namespace Uqeb.Api.Tests.Reporting;

[Collection(PlaywrightTestCollection.Name)]
public class InstitutionalReportPlaywrightPdfExporterTests
{
    private static bool RequirePlaywrightInCi =>
        string.Equals(Environment.GetEnvironmentVariable("REQUIRE_PLAYWRIGHT_TESTS"), "1", StringComparison.Ordinal);

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

    private static async Task EnsurePlaywrightAvailableAsync()
    {
        if (await IsPlaywrightAvailableAsync())
            return;

        if (RequirePlaywrightInCi)
            Assert.Fail("Playwright Chromium is required in CI but is not available.");
    }

    [Fact]
    public async Task ExportAsync_ProducesSearchablePdf_WithArabicTableStructure()
    {
        await EnsurePlaywrightAvailableAsync();
        if (!await IsPlaywrightAvailableAsync())
            return;

        var renderer = new InstitutionalReportRenderer();
        var manifest = BuildManifestWithDepartmentTable();
        var html = InstitutionalReportRenderer.RenderHtmlDocument(manifest);

        Assert.Contains("report-table", html);
        Assert.Contains("أداء الإدارات", html);
        Assert.Contains("Uqeb Report Arabic", html);

        await using var exporter = CreateExporter();
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
        await EnsurePlaywrightAvailableAsync();
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

        await using var exporter = CreateExporter();
        var pdf = await exporter.ExportAsync(exportManifest, InstitutionalReportRenderer.RenderHtmlDocument(exportManifest));
        Assert.True(pdf.Length > 1_000);
    }

    [Fact]
    public async Task ExportAsync_FullReport_PhysicalPageCountMatchesManifestAndUsesWideProfiles()
    {
        await EnsurePlaywrightAvailableAsync();
        if (!await IsPlaywrightAvailableAsync())
            return;

        var model = InstitutionalReportVisualFixtures.CreateBaseModel();
        var manifest = InstitutionalReportVisualFixtures.RenderAllSections(model);
        var html = InstitutionalReportRenderer.RenderHtmlDocument(manifest);

        await using var exporter = CreateExporter();
        var pdf = await exporter.ExportAsync(manifest, html);
        var pageCount = CountPdfPages(pdf);
        var mediaBoxes = ExtractMediaBoxes(pdf);
        var artifactPath = WritePdfArtifact("full-report.actual.pdf", pdf);

        Assert.Equal(manifest.TotalPages, pageCount);
        Assert.Equal(manifest.TotalPages, mediaBoxes.Count);
        Assert.Contains(mediaBoxes, box => Math.Abs(box.WidthPoints - MmToPoints(210)) < 6);
        Assert.Contains(mediaBoxes, box => box.WidthPoints > MmToPoints(300));
        Assert.True(File.Exists(artifactPath));
    }

    [Fact]
    public async Task ExportAsync_CoverPdf_HasNoQrPlaceholderAndFooterStaysOnSinglePhysicalPage()
    {
        await EnsurePlaywrightAvailableAsync();
        if (!await IsPlaywrightAvailableAsync())
            return;

        var model = InstitutionalReportVisualFixtures.CreateBaseModel();
        var manifest = InstitutionalReportVisualFixtures.RenderSections(model, ReportSectionId.Cover);
        var html = InstitutionalReportRenderer.RenderHtmlDocument(manifest);

        Assert.DoesNotContain("qr-box", html);
        Assert.DoesNotContain(">QR", html);
        Assert.Contains("معرف التحقق", html);
        Assert.Equal(1, CountOccurrences(html, "<footer class=\"report-footer"));

        await using var exporter = CreateExporter();
        var pdf = await exporter.ExportAsync(manifest, html);

        Assert.Equal(1, CountPdfPages(pdf));
    }

    [Theory]
    [InlineData(ReportSectionId.Cover)]
    [InlineData(ReportSectionId.ExecutiveSummary)]
    [InlineData(ReportSectionId.IndicatorsDashboard)]
    [InlineData(ReportSectionId.DepartmentPerformance)]
    [InlineData(ReportSectionId.RisksAndAlerts)]
    [InlineData(ReportSectionId.ExecutiveRecommendations)]
    [InlineData(ReportSectionId.TransactionDetails)]
    [InlineData(ReportSectionId.ReportMetadata)]
    public async Task ExportAsync_Section_PhysicalPageCountMatchesManifest(ReportSectionId section)
    {
        await EnsurePlaywrightAvailableAsync();
        if (!await IsPlaywrightAvailableAsync())
            return;

        var model = InstitutionalReportVisualFixtures.CreateBaseModel(truncated: section == ReportSectionId.TransactionDetails);
        var manifest = InstitutionalReportVisualFixtures.RenderSections(model, section);
        var html = InstitutionalReportRenderer.RenderHtmlDocument(manifest);

        await using var exporter = CreateExporter();
        var pdf = await exporter.ExportAsync(manifest, html);
        WritePdfArtifact($"{section}.actual.pdf", pdf);

        Assert.Equal(manifest.TotalPages, CountPdfPages(pdf));
    }

    [Fact]
    public async Task ExportAsync_DepartmentTransactions_PhysicalPageCountMatchesManifestWithComputedRowsPerPage()
    {
        await EnsurePlaywrightAvailableAsync();
        if (!await IsPlaywrightAvailableAsync())
            return;

        // 60 rows comfortably spans multiple pages under the new geometry-computed row-per-page
        // capacity (~24/page for ExtraWideLandscape), proving manifest.TotalPages (computed from the
        // same Chunk(rowsPerPage) the physical PDF is rendered from) stays in sync with the actual
        // rendered PDF page count for this report type specifically.
        var rows = InstitutionalReportVisualFixtures.CreateTransactions(60);
        foreach (var row in rows)
        {
            row.MatchedDepartments =
            [
                new TransactionDetailDepartmentRelationDto { DepartmentId = 20, DepartmentName = "الإدارة ب", Relation = "إحالة" },
            ];
        }

        var model = InstitutionalReportVisualFixtures.CreateBaseModel(totalMatched: 60, exportedRows: 60);
        model.Metadata.ReportType = InstitutionalReportType.DepartmentTransactions;
        model.Transactions = rows;

        var manifest = InstitutionalReportVisualFixtures.RenderSections(model, ReportSectionId.TransactionDetails);
        var html = InstitutionalReportRenderer.RenderHtmlDocument(manifest);

        await using var exporter = CreateExporter();
        var pdf = await exporter.ExportAsync(manifest, html);
        WritePdfArtifact("department-transactions.actual.pdf", pdf);

        Assert.True(manifest.TotalPages > 1);
        Assert.Equal(manifest.TotalPages, CountPdfPages(pdf));
    }

    [Fact]
    public async Task ExportAsync_DepartmentTransactions_NormalDataset_ShowsMoreThanTenRowsOnFirstDetailPage()
    {
        await EnsurePlaywrightAvailableAsync();
        if (!await IsPlaywrightAvailableAsync())
            return;

        // 20 typical (non-truncated, non-grouped) transactions - the common case the density fix
        // targets. Mirrors real-world row content: a mix of short and one deliberately long subject
        // (from CreateTransactions), plus a matched-department/relation cell like production rows.
        var rows = InstitutionalReportVisualFixtures.CreateTransactions(20);
        foreach (var row in rows)
        {
            row.MatchedDepartments =
            [
                new TransactionDetailDepartmentRelationDto { DepartmentId = 20, DepartmentName = "الإدارة ب", Relation = "إحالة" },
            ];
        }

        var model = InstitutionalReportVisualFixtures.CreateBaseModel(totalMatched: 20, exportedRows: 20);
        model.Metadata.ReportType = InstitutionalReportType.DepartmentTransactions;
        model.Transactions = rows;

        var manifest = InstitutionalReportVisualFixtures.RenderSections(model, ReportSectionId.TransactionDetails);
        var html = InstitutionalReportRenderer.RenderHtmlDocument(manifest);

        await using var exporter = CreateExporter();
        var pdf = await exporter.ExportAsync(manifest, html);
        WritePdfArtifact("department-transactions-normal.actual.pdf", pdf);

        // Physical PDF page count must still match the manifest's chunk-derived page count (no
        // silent overflow onto extra physical pages from an over-optimistic row-per-page estimate).
        Assert.Equal(manifest.TotalPages, CountPdfPages(pdf));

        var firstPageRowCount = Regex.Matches(manifest.Pages[0].HtmlContent, "class=\"cell--number\"").Count;
        Assert.True(firstPageRowCount > 10, $"Expected more than 10 rows on the first detail page, found {firstPageRowCount}.");
    }

    [Fact]
    public void BuildMeasuredChunks_PutsSingleOversizedRowOnItsOwnPage()
    {
        var chunks = InstitutionalReportPdfPaginationMeasurer.BuildMeasuredChunks(
            [120, 20, 20],
            firstAvailableRowsHeight: 80,
            continuationAvailableRowsHeight: 80);

        Assert.Equal([new TransactionDetailRowRange(0, 1), new TransactionDetailRowRange(1, 2)], chunks);
    }

    [Fact]
    public async Task MeasureTransactionDetails_ShortRowsUseMoreThanTwelveRowsAndLongRowsReduceCapacity()
    {
        await EnsurePlaywrightAvailableAsync();
        if (!await IsPlaywrightAvailableAsync())
            return;

        var renderer = new InstitutionalReportRenderer();
        var shortModel = CreateMeasuredTransactionModel(100, longSubjects: false);
        var longModel = CreateMeasuredTransactionModel(100, longSubjects: true);

        await using var measurer = CreateMeasurer();
        var shortChunks = await MeasureChunksAsync(renderer, measurer, shortModel);
        var longChunks = await MeasureChunksAsync(renderer, measurer, longModel);

        Assert.NotEmpty(shortChunks);
        Assert.NotEmpty(longChunks);
        Assert.True(shortChunks[0].Count > 12, $"Expected short rows to fit more than 12 rows, got {shortChunks[0].Count}.");
        Assert.True(longChunks[0].Count < shortChunks[0].Count, $"Expected long rows ({longChunks[0].Count}) to fit fewer than short rows ({shortChunks[0].Count}).");

        var finalManifest = renderer.RenderManifestWithMeasuredTransactionPages(
            shortModel,
            [ReportSectionId.TransactionDetails],
            shortChunks);
        var layout = await MeasureFinalTransactionLayoutAsync(InstitutionalReportRenderer.RenderHtmlDocument(finalManifest));
        var blankSpaceRatio = layout.AvailableRowsHeight <= 0 ? 1 : layout.UnusedRowsHeight / layout.AvailableRowsHeight;

        Assert.False(layout.OverlapsFooter, "Expected the measured detail table to stay above the footer.");
        Assert.True(blankSpaceRatio < 0.25, $"Expected short rows to use most of the page; unused ratio was {blankSpaceRatio:P1}.");

        await using var exporter = CreateExporter();
        var pdf = await exporter.ExportAsync(finalManifest, InstitutionalReportRenderer.RenderHtmlDocument(finalManifest));
        Assert.Equal(finalManifest.TotalPages, CountPdfPages(pdf));
    }

    private static InstitutionalReportPlaywrightPdfExporter CreateExporter() =>
        new(
            new ReportingPlaywrightBrowserHost(
                new ReadyChromiumProbe(),
                NullLogger<ReportingPlaywrightBrowserHost>.Instance));

    private static InstitutionalReportPdfPaginationMeasurer CreateMeasurer() =>
        new(
            new ReportingPlaywrightBrowserHost(
                new ReadyChromiumProbe(),
                NullLogger<ReportingPlaywrightBrowserHost>.Instance));

    private static async Task<IReadOnlyList<IReadOnlyList<TransactionDetailRowDto>>> MeasureChunksAsync(
        InstitutionalReportRenderer renderer,
        InstitutionalReportPdfPaginationMeasurer measurer,
        InstitutionalReportModel model)
    {
        var preflightChunks = model.Transactions
            .Select(row => (IReadOnlyList<TransactionDetailRowDto>)new[] { row })
            .ToList();
        var preflightManifest = renderer.RenderManifestWithMeasuredTransactionPages(
            model,
            [ReportSectionId.TransactionDetails],
            preflightChunks);
        return await measurer.MeasureTransactionDetailChunksAsync(
            preflightManifest,
            InstitutionalReportRenderer.RenderHtmlDocument(preflightManifest),
            model.Transactions);
    }

    private static InstitutionalReportModel CreateMeasuredTransactionModel(int rowCount, bool longSubjects)
    {
        var model = InstitutionalReportVisualFixtures.CreateBaseModel(totalMatched: rowCount, exportedRows: rowCount);
        model.Transactions = InstitutionalReportVisualFixtures.CreateTransactions(rowCount);

        foreach (var row in model.Transactions)
        {
            row.Subject = longSubjects
                ? string.Join(" ", Enumerable.Repeat($"موضوع معاملة تفصيلي طويل رقم {row.Sequence}", 12))
                : $"معاملة قصيرة {row.Sequence}";
        }

        return model;
    }

    private static async Task<TransactionLayoutMeasurement> MeasureFinalTransactionLayoutAsync(string html)
    {
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });
        await using var context = await browser.NewContextAsync(new BrowserNewContextOptions { Locale = "ar-SA" });
        var page = await context.NewPageAsync();
        await page.SetContentAsync(html, new PageSetContentOptions { WaitUntil = WaitUntilState.NetworkIdle });
        await page.EvaluateAsync("() => document.fonts ? document.fonts.ready : Promise.resolve()");

        return await page.EvaluateAsync<TransactionLayoutMeasurement>("""
            () => {
              const page = document.querySelector('.report-page:has(.report-table--transactions)');
              const content = page.querySelector('.report-content');
              const table = page.querySelector('.report-table--transactions');
              const footer = page.querySelector('.report-footer');
              const contentRect = content.getBoundingClientRect();
              const tableRect = table.getBoundingClientRect();
              const footerRect = footer.getBoundingClientRect();
              const headerHeight = table.querySelector('thead').getBoundingClientRect().height;
              const fixedContentHeight = Array.from(content.children)
                .filter(el => el !== table)
                .reduce((sum, el) => {
                  const rect = el.getBoundingClientRect();
                  const style = window.getComputedStyle(el);
                  return sum + rect.height + (Number.parseFloat(style.marginTop) || 0) + (Number.parseFloat(style.marginBottom) || 0);
                }, 0);
              const rowsHeight = Array.from(table.querySelectorAll('tbody tr'))
                .reduce((sum, row) => sum + row.getBoundingClientRect().height, 0);
              const availableRowsHeight = Math.max(0, contentRect.height - fixedContentHeight - headerHeight);
              return {
                overlapsFooter: tableRect.bottom > footerRect.top + 0.5,
                availableRowsHeight,
                unusedRowsHeight: Math.max(0, availableRowsHeight - rowsHeight),
              };
            }
            """);
    }

    private static int CountPdfPages(byte[] pdf)
    {
        var content = System.Text.Encoding.Latin1.GetString(pdf);
        return Regex.Matches(content, @"/Type\s*/Page(?!s)\b").Count;
    }

    private static IReadOnlyList<PdfMediaBox> ExtractMediaBoxes(byte[] pdf)
    {
        var content = System.Text.Encoding.Latin1.GetString(pdf);
        return Regex.Matches(
                content,
                @"/MediaBox\s*\[\s*0\s+0\s+(?<width>[0-9.]+)\s+(?<height>[0-9.]+)\s*\]")
            .Select(match => new PdfMediaBox(
                double.Parse(match.Groups["width"].Value, System.Globalization.CultureInfo.InvariantCulture),
                double.Parse(match.Groups["height"].Value, System.Globalization.CultureInfo.InvariantCulture)))
            .ToList();
    }

    private static double MmToPoints(double mm) => mm * 72d / 25.4d;

    private static int CountOccurrences(string source, string value) =>
        source.Split(value, StringSplitOptions.None).Length - 1;

    private static string WritePdfArtifact(string fileName, byte[] pdf)
    {
        var artifactDir = Path.Combine(Path.GetTempPath(), "uqeb-reporting-pdf-layout");
        Directory.CreateDirectory(artifactDir);
        var path = Path.Combine(artifactDir, fileName);
        File.WriteAllBytes(path, pdf);
        return path;
    }

    private sealed record PdfMediaBox(double WidthPoints, double HeightPoints);

    private sealed class TransactionLayoutMeasurement
    {
        public bool OverlapsFooter { get; set; }
        public double AvailableRowsHeight { get; set; }
        public double UnusedRowsHeight { get; set; }
    }

    private sealed class ReadyChromiumProbe : IReportingChromiumProbe
    {
        public Task<ReportingChromiumProbeResult> ProbeAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new ReportingChromiumProbeResult
            {
                State = ReportingChromiumProbeState.Ready,
                ExecutableAvailable = true,
                LaunchSuccessful = true,
                Summary = "Ready for tests.",
            });

        public Task<ReportingChromiumProbeResult> ProbeLaunchOnlyAsync(CancellationToken cancellationToken = default) =>
            ProbeAsync(cancellationToken);
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
