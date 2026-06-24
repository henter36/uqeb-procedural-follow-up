using Uqeb.Api.Reporting.DTOs;
using Uqeb.Api.Reporting.Enums;
using Uqeb.Api.Reporting.Services;
using Xunit;

namespace Uqeb.Api.Tests.Reporting;

public class InstitutionalReportRendererTests
{
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
        Assert.True(result.IsPartialExport);
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
        Assert.Contains("الصفحة 1 من 1", html);
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
            ReportId = "REP-TEST",
            TotalPages = pageCount,
            Pages = pages,
        };
    }

    private static int CountOccurrences(string source, string value) =>
        source.Split(value, StringSplitOptions.None).Length - 1;
}
