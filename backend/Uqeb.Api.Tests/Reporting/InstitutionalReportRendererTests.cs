using Uqeb.Api.Reporting.DTOs;
using Uqeb.Api.Reporting.Enums;
using Uqeb.Api.Reporting.Services;
using Xunit;

namespace Uqeb.Api.Tests.Reporting;

public class InstitutionalReportRendererTests
{
    private readonly InstitutionalReportRenderer _renderer = new();

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
