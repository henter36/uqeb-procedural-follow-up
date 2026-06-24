using System.Text.Json;
using Uqeb.Api.Reporting.DTOs;
using Uqeb.Api.Reporting.Enums;
using Uqeb.Api.Reporting.Rendering;
using Uqeb.Api.Reporting.Services;
using Uqeb.Api.Tests.Reporting.Visual;
using Xunit;

namespace Uqeb.Api.Tests.Reporting;

public class InstitutionalReportRendererTests
{
    private const string TestReportTitle = "تقرير متابعة المعاملات المتأخرة";
    private const string TestReportId = "REP-2026-000125";

    private readonly InstitutionalReportRenderer _renderer = new();

    [Fact]
    public void BuildExportManifest_PreservesOperationalMetadata()
    {
        var source = CreateSourceManifest(3);
        source.TotalMatchedRows = 12_500;
        source.ExportedDetailRows = 500;
        source.DetailRowsTruncated = true;
        source.DetailPartsCount = 3;
        source.LoadedDetailRows = 500;
        source.TemplateVersion = "2026.06.1";
        source.Stylesheet = "body { margin: 0; }";
        source.OverflowAction = DetailOverflowAction.SplitPdf;
        source.FileFingerprint = "fp-test";
        source.IsSummaryOnly = false;

        var result = _renderer.BuildExportManifest(source, [1, 2], new ReportExportRequestDto
        {
            IncludePartialCover = true,
            IncludePartialManifest = true,
            PageNumberingMode = PageNumberingMode.Restart,
        });

        Assert.Equal(source.LoadedDetailRows, result.LoadedDetailRows);
        Assert.Equal(source.TemplateVersion, result.TemplateVersion);
        Assert.Equal(source.TotalMatchedRows, result.TotalMatchedRows);
        Assert.Equal(source.ExportedDetailRows, result.ExportedDetailRows);
        Assert.Equal(source.DetailPartsCount, result.DetailPartsCount);
        Assert.Equal(source.Stylesheet, result.Stylesheet);
        Assert.Equal(source.OverflowAction, result.OverflowAction);
        Assert.Equal(source.FileFingerprint, result.FileFingerprint);
        Assert.Equal(source.IsSummaryOnly, result.IsSummaryOnly);
        Assert.Equal(source.ReportTitle, result.ReportTitle);
        Assert.Equal(source.ReportId, result.ReportId);
        Assert.True(result.IsPartialExport);
    }

    [Fact]
    public void BuildExportManifest_PreservesAnalysisPayload()
    {
        var model = InstitutionalReportVisualFixtures.CreateBaseModel();
        var source = InstitutionalReportVisualFixtures.RenderSections(
            model,
            ReportSectionId.KeyPerformanceIndicators,
            ReportSectionId.SignificantFindings);

        var result = _renderer.BuildExportManifest(source, [source.Pages[0].OriginalPageNumber], new ReportExportRequestDto
        {
            IncludePartialCover = true,
            IncludePartialManifest = true,
        });

        Assert.Same(source.Analysis, result.Analysis);
    }

    [Fact]
    public void BuildExportManifest_PreservesOperationalMetadata_WithOriginalNumbering()
    {
        var source = CreateSourceManifest(4);
        source.TotalMatchedRows = 100;
        source.ExportedDetailRows = 100;
        source.LoadedDetailRows = 100;
        source.TemplateVersion = "2026.06.1";

        var result = _renderer.BuildExportManifest(source, [2, 3], new ReportExportRequestDto
        {
            PageNumberingMode = PageNumberingMode.Original,
        });

        Assert.Equal(source.LoadedDetailRows, result.LoadedDetailRows);
        Assert.Equal(source.TemplateVersion, result.TemplateVersion);
        Assert.Equal(source.ReportTitle, result.ReportTitle);
        Assert.Equal(source.ReportId, result.ReportId);
        Assert.Equal(2, result.Pages[0].RenderedPageNumber);
        Assert.Equal(3, result.Pages[1].RenderedPageNumber);
    }

    [Fact]
    public void BuildExportManifest_RenumbersPartialCoverAndManifestPages()
    {
        var source = CreateSourceManifest(3);
        var request = new ReportExportRequestDto
        {
            IncludePartialCover = true,
            IncludePartialManifest = true,
            PageNumberingMode = PageNumberingMode.Restart,
        };

        var manifest = _renderer.BuildExportManifest(source, [1, 2], request);

        Assert.Equal(4, manifest.Pages.Count);
        Assert.Equal(1, manifest.Pages[0].RenderedPageNumber);
        Assert.Equal(2, manifest.Pages[1].RenderedPageNumber);
        Assert.DoesNotContain("الصفحة 0 من 0", manifest.Pages[0].HtmlContent);
        Assert.DoesNotContain("الصفحة 0 من 0", manifest.Pages[1].HtmlContent);
        Assert.Contains("الصفحة 1 من 4", manifest.Pages[0].HtmlContent);
        Assert.Contains("الصفحة 4 من 4", manifest.Pages[^1].HtmlContent);
    }

    [Fact]
    public void RenderManifest_UsesRealReportTitleAndReportIdInFooter()
    {
        var model = InstitutionalReportVisualFixtures.CreateBaseModel(title: TestReportTitle);
        model.Metadata.ReportNumber = TestReportId;

        var manifest = _renderer.RenderManifest(model, [ReportSectionId.Cover, ReportSectionId.ExecutiveSummary]);
        var html = InstitutionalReportRenderer.RenderHtmlDocument(manifest);

        Assert.Equal(TestReportTitle, manifest.ReportTitle);
        Assert.Equal(TestReportId, manifest.ReportId);
        Assert.Contains($"""<span class="footer-title">{TestReportTitle}</span>""", html);
        Assert.Contains($"""<span class="footer-id">{TestReportId}</span>""", html);
        Assert.DoesNotContain($"""<span class="footer-title">{TestReportId}</span>""", html);
    }

    [Theory]
    [InlineData(PageNumberingMode.Restart)]
    [InlineData(PageNumberingMode.Original)]
    public void BuildExportManifest_PartialExportKeepsRealFooterTitleAndReportId(PageNumberingMode numberingMode)
    {
        var source = CreateSourceManifest(3);
        var manifest = _renderer.BuildExportManifest(source, [2], new ReportExportRequestDto
        {
            IncludePartialCover = true,
            IncludePartialManifest = true,
            PageNumberingMode = numberingMode,
        });

        Assert.Equal(source.ReportTitle, manifest.ReportTitle);
        Assert.Equal(source.ReportId, manifest.ReportId);
        Assert.Contains(manifest.Pages, page => page.SectionId == ReportSectionId.PartialCover);
        Assert.Contains(manifest.Pages, page => page.SectionId == ReportSectionId.PartialManifest);
        Assert.All(manifest.Pages, page =>
        {
            Assert.Contains($"""<span class="footer-title">نسخة جزئية — {source.ReportTitle}</span>""", page.HtmlContent);
            Assert.Contains($"""<span class="footer-id">{source.ReportId}</span>""", page.HtmlContent);
            Assert.DoesNotContain($"""<span class="footer-title">نسخة جزئية — {source.ReportId}</span>""", page.HtmlContent);
        });
    }

    [Fact]
    public void InjectFooter_ReplacesExistingFooterInsteadOfDuplicating()
    {
        var source = CreateSourceManifest(1);
        var request = new ReportExportRequestDto
        {
            PageNumberingMode = PageNumberingMode.Restart,
        };

        var manifest = _renderer.BuildExportManifest(source, [1], request);
        var html = manifest.Pages[0].HtmlContent;

        Assert.Equal(1, CountOccurrences(html, "<footer class=\"report-footer\">"));
        Assert.Contains($"""<span class="footer-title">{source.ReportTitle}</span>""", html);
        Assert.Contains($"""<span class="footer-id">{source.ReportId}</span>""", html);
        Assert.DoesNotContain($"""<span class="footer-title">{source.ReportId}</span>""", html);
        Assert.Contains("الصفحة 1 من 1", html);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void BuildExportManifest_UsesDefaultTitleFallbackWithoutUsingReportId(string? reportTitle)
    {
        var source = CreateSourceManifest(1);
        source.ReportTitle = reportTitle!;

        var manifest = _renderer.BuildExportManifest(source, [1], new ReportExportRequestDto());
        var html = manifest.Pages.Single().HtmlContent;

        Assert.Equal("تقرير المتابعة الإجرائية للمعاملات", manifest.ReportTitle);
        Assert.Contains("""<span class="footer-title">تقرير المتابعة الإجرائية للمعاملات</span>""", html);
        Assert.Contains($"""<span class="footer-id">{source.ReportId}</span>""", html);
        Assert.DoesNotContain($"""<span class="footer-title">{source.ReportId}</span>""", html);
    }

    [Fact]
    public void RenderedReportManifestDto_SerializesReportTitleAndReportIdSeparately()
    {
        var manifest = new RenderedReportManifestDto
        {
            ReportTitle = TestReportTitle,
            ReportId = TestReportId,
        };

        var json = JsonSerializer.Serialize(manifest, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        });

        using var document = JsonDocument.Parse(json);
        Assert.Equal(TestReportTitle, document.RootElement.GetProperty("reportTitle").GetString());
        Assert.Equal(TestReportId, document.RootElement.GetProperty("reportId").GetString());
    }

    [Fact]
    public void RenderManifest_AssignsExpectedPdfProfilesBySection()
    {
        var model = InstitutionalReportVisualFixtures.CreateBaseModel();
        var manifest = _renderer.RenderManifest(model,
        [
            ReportSectionId.Cover,
            ReportSectionId.ExecutiveSummary,
            ReportSectionId.IndicatorsDashboard,
            ReportSectionId.DepartmentPerformance,
            ReportSectionId.TransactionDetails,
            ReportSectionId.ReportMetadata,
        ]);

        Assert.Equal("StandardPortrait", manifest.Pages.Single(p => p.SectionId == ReportSectionId.Cover).PdfProfileName);
        Assert.Equal("StandardPortrait", manifest.Pages.Single(p => p.SectionId == ReportSectionId.ExecutiveSummary).PdfProfileName);
        Assert.Equal("StandardLandscape", manifest.Pages.Single(p => p.SectionId == ReportSectionId.IndicatorsDashboard).PdfProfileName);
        Assert.Equal("WideLandscape", manifest.Pages.Single(p => p.SectionId == ReportSectionId.DepartmentPerformance).PdfProfileName);
        Assert.All(
            manifest.Pages.Where(p => p.SectionId == ReportSectionId.TransactionDetails),
            page => Assert.Equal("ExtraWideLandscape", page.PdfProfileName));
        Assert.Equal("StandardPortrait", manifest.Pages.Single(p => p.SectionId == ReportSectionId.ReportMetadata).PdfProfileName);
    }

    [Fact]
    public void RenderManifest_UsesTableSpecificClassesAndReadableDateOrder()
    {
        var model = InstitutionalReportVisualFixtures.CreateBaseModel();
        var manifest = _renderer.RenderManifest(model,
        [
            ReportSectionId.DepartmentPerformance,
            ReportSectionId.TransactionDetails,
            ReportSectionId.ReportMetadata,
        ]);
        var html = InstitutionalReportRenderer.RenderHtmlDocument(manifest);

        Assert.Contains("report-table--departments", html);
        Assert.Contains("report-table--transactions", html);
        Assert.Contains("cell--id", html);
        Assert.Contains("cell--date", html);
        Assert.Contains("الفترة من 2026-01-01 إلى 2026-06-15", html);
        Assert.Contains("<dt>الفترة</dt><dd>من 2026-01-01 إلى 2026-06-15</dd>", html);
    }

    [Fact]
    public void RenderManifest_RendersAnalyticalSectionsFromUnifiedAnalysisModel()
    {
        var model = InstitutionalReportVisualFixtures.CreateBaseModel();
        var manifest = _renderer.RenderManifest(model,
        [
            ReportSectionId.KeyPerformanceIndicators,
            ReportSectionId.SignificantFindings,
            ReportSectionId.CriticalCases,
            ReportSectionId.TimeTrends,
            ReportSectionId.ExternalPartyAnalysis,
            ReportSectionId.ClassificationAndPriorityAnalysis,
            ReportSectionId.DelayAndBottleneckAnalysis,
            ReportSectionId.DataQuality,
            ReportSectionId.RecommendationsAndActionPlan,
            ReportSectionId.MethodologyAndDefinitions,
        ]);
        var html = InstitutionalReportRenderer.RenderHtmlDocument(manifest);

        Assert.Equal(10, manifest.Pages.Count);
        Assert.Same(model.Analysis, manifest.Analysis);
        Assert.Contains("مؤشرات الأداء الرئيسية", html);
        Assert.Contains("ارتفاع نسبة التأخر", html);
        Assert.Contains("معاملة حرجة متأخرة", html);
        Assert.Contains("جهة حكومية", html);
        Assert.Contains("إفادة أو تكليف إدارة معلق", html);
        Assert.Contains("نسبة اكتمال البيانات", html);
        Assert.Contains("مراجعة المعاملات المتأخرة حسب الإدارات الأعلى أثرًا", html);
        Assert.Contains("AverageFirstActionHours", html);
    }

    [Fact]
    public void PdfProfiles_DefineWideReadablePageSizes()
    {
        Assert.True(InstitutionalReportPdfProfiles.StandardLandscape.WidthMm > InstitutionalReportPdfProfiles.StandardPortrait.WidthMm);
        Assert.True(InstitutionalReportPdfProfiles.WideLandscape.WidthMm > InstitutionalReportPdfProfiles.StandardLandscape.WidthMm);
        Assert.True(InstitutionalReportPdfProfiles.ExtraWideLandscape.WidthMm >= InstitutionalReportPdfProfiles.WideLandscape.WidthMm);
        Assert.True(InstitutionalReportPdfProfiles.ExtraWideLandscape.TableFontSizePx >= 9);
    }

    [Fact]
    public void WidestProfile_SelectsLandscapeOverPortraitByWidth()
    {
        var profile = InstitutionalReportPdfProfiles.WidestProfile(
        [
            InstitutionalReportPdfProfiles.StandardPortrait.Name,
            InstitutionalReportPdfProfiles.StandardLandscape.Name,
        ]);

        Assert.Same(InstitutionalReportPdfProfiles.StandardLandscape, profile);
    }

    [Theory]
    [InlineData("WideLandscape", "ExtraWideLandscape")]
    [InlineData("ExtraWideLandscape", "WideLandscape")]
    public void WidestProfile_SelectsLargestWidthRegardlessOfInputOrder(string first, string second)
    {
        var profile = InstitutionalReportPdfProfiles.WidestProfile([first, second]);

        Assert.Same(InstitutionalReportPdfProfiles.ExtraWideLandscape, profile);
    }

    [Fact]
    public void WidestProfile_HandlesUnknownAndEmptyInputsDeterministically()
    {
        var unknown = InstitutionalReportPdfProfiles.WidestProfile(["does-not-exist"]);
        var empty = InstitutionalReportPdfProfiles.WidestProfile([]);
        var tiedFallbacks = InstitutionalReportPdfProfiles.WidestProfile(["unknown-a", "unknown-b"]);

        Assert.Same(InstitutionalReportPdfProfiles.StandardPortrait, unknown);
        Assert.Same(InstitutionalReportPdfProfiles.StandardPortrait, empty);
        Assert.Same(InstitutionalReportPdfProfiles.StandardPortrait, tiedFallbacks);
    }

    private static RenderedReportManifestDto CreateSourceManifest(int pageCount)
    {
        var pages = Enumerable.Range(1, pageCount)
            .Select(i => new RenderedReportPageDto
            {
                OriginalPageNumber = i,
                RenderedPageNumber = i,
                SectionId = ReportSectionId.ExecutiveSummary,
                SectionName = $"قسم {i}",
                PageTitle = $"صفحة {i}",
                HtmlContent = $"""
                    <section class="report-page" data-page="{i}">
                      <main>محتوى {i}</main>
                      <footer class="report-footer"><span>الصفحة {i} من {pageCount}</span><span>تقرير</span></footer>
                    </section>
                    """,
            })
            .ToList();

        return new RenderedReportManifestDto
        {
            ReportTitle = TestReportTitle,
            ReportId = "REP-TEST",
            TotalPages = pageCount,
            Pages = pages,
        };
    }

    private static int CountOccurrences(string source, string value) =>
        source.Split(value, StringSplitOptions.None).Length - 1;
}
