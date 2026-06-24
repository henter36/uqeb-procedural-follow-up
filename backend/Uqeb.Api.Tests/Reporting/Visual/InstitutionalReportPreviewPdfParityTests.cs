using Uqeb.Api.Reporting.DTOs;
using Uqeb.Api.Reporting.Enums;
using Uqeb.Api.Reporting.Services;
using Xunit;

namespace Uqeb.Api.Tests.Reporting.Visual;

public class InstitutionalReportPreviewPdfParityTests
{
    [Fact]
    public void PreviewHtml_UsesSameStylesheetAndSectionOrderAsPdfDocument()
    {
        var model = InstitutionalReportVisualFixtures.CreateBaseModel();
        var manifest = InstitutionalReportVisualFixtures.RenderAllSections(model);
        var previewHtml = InstitutionalReportRenderer.RenderHtmlDocument(manifest);

        Assert.Contains("REP-2026-000125", previewHtml);
        Assert.Contains("--report-primary", previewHtml);
        Assert.Contains("الملخص التنفيذي", previewHtml);
        Assert.Contains("أداء الإدارات", previewHtml);
        Assert.Contains("المعاملات التفصيلية", previewHtml);
        Assert.DoesNotContain("الصفحة 0 من 0", previewHtml);
        Assert.Equal(manifest.TotalPages, manifest.Pages.Count);

        var sectionOrder = manifest.Pages.Select(p => p.SectionId).ToList();
        Assert.Equal(ReportSectionId.Cover, sectionOrder[0]);
        Assert.Equal(ReportSectionId.ReportMetadata, sectionOrder[^1]);
    }

    [Fact]
    public void PartialExportManifest_DoesNotUseZeroPageNumbers()
    {
        var model = InstitutionalReportVisualFixtures.CreateBaseModel();
        var source = InstitutionalReportVisualFixtures.RenderAllSections(model);
        var renderer = new InstitutionalReportRenderer();
        var exportManifest = renderer.BuildExportManifest(source, [2, 3], new ReportExportRequestDto
        {
            IncludePartialCover = true,
            IncludePartialManifest = true,
            PageNumberingMode = PageNumberingMode.Restart,
        });

        foreach (var page in exportManifest.Pages)
        {
            Assert.True(page.RenderedPageNumber >= 1);
            Assert.DoesNotContain("0 من 0", page.HtmlContent);
        }
    }
}
