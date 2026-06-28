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

    private static InstitutionalReportPlaywrightPdfExporter CreateExporter() =>
        new(
            new ReadyChromiumProbe(),
            NullLogger<InstitutionalReportPlaywrightPdfExporter>.Instance);

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
