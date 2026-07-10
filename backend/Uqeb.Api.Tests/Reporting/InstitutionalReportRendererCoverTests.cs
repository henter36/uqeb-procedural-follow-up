using System.Text.RegularExpressions;
using Uqeb.Api.Reporting.DTOs;
using Uqeb.Api.Reporting.Enums;
using Uqeb.Api.Reporting.Services;
using Uqeb.Api.Tests.Reporting.Visual;
using Xunit;

namespace Uqeb.Api.Tests.Reporting;

public class InstitutionalReportRendererCoverTests
{
    private readonly InstitutionalReportRenderer _renderer = new();

    [Fact]
    public void RenderManifest_Cover_KeepsReportPageWrapper()
    {
        var model = InstitutionalReportVisualFixtures.CreateBaseModel();

        var manifest = new InstitutionalReportRenderer()
            .RenderManifest(model, [ReportSectionId.Cover]);

        var page = Assert.Single(manifest.Pages);

        Assert.Contains("class=\"report-page report-page--standard-portrait\"", page.HtmlContent);
        Assert.DoesNotContain("REP-2026-000125", page.HtmlContent);
        Assert.Single(Regex.Matches(page.HtmlContent, "class=\"report-page"));
    }

    [Fact]
    public void RenderManifest_CoverUsesTitleAndPeriodOnly()
    {
        var model = BuildModel();
        var manifest = _renderer.RenderManifest(model,
        [
            ReportSectionId.Cover,
            ReportSectionId.ExecutiveSummary,
        ]);

        Assert.Equal(2, manifest.TotalPages);
        var cover = Assert.Single(manifest.Pages, p => p.SectionId == ReportSectionId.Cover);
        Assert.Contains("<h1 class=\"cover-title\">تقرير المتابعة الإجرائية</h1>", cover.HtmlContent);
        Assert.DoesNotContain("<h1 class=\"cover-title\">تقرير اختبار</h1>", cover.HtmlContent);
        Assert.Contains("<div class=\"cover-period\">الفترة: من 2026-01-01 إلى 2026-06-01</div>", cover.HtmlContent);
        Assert.DoesNotContain("تاريخ الإصدار", cover.HtmlContent);
        Assert.DoesNotContain("رقم التقرير", cover.HtmlContent);
        Assert.DoesNotContain("REP-2026-000010", cover.HtmlContent);
        Assert.DoesNotContain("تقرير اختبار", cover.HtmlContent);
        Assert.DoesNotContain("VERIFY", cover.HtmlContent);
        Assert.DoesNotContain("report-header", cover.HtmlContent);
        Assert.DoesNotContain("report-footer", cover.HtmlContent);
        Assert.DoesNotContain("إجمالي الصفحات", cover.HtmlContent);
        Assert.DoesNotContain("معرف التحقق", cover.HtmlContent);
        Assert.DoesNotContain("البصمة", cover.HtmlContent);
        Assert.DoesNotContain("cover-accent", cover.HtmlContent);
        Assert.DoesNotContain("0 من 0", cover.HtmlContent);
    }

    [Fact]
    public void RenderManifest_MetadataShowsSinglePageCount()
    {
        var model = BuildModel();
        var manifest = _renderer.RenderManifest(model, [ReportSectionId.ReportMetadata]);

        Assert.Equal(1, manifest.TotalPages);
        var cover = Assert.Single(manifest.Pages);
        Assert.Contains("إجمالي الصفحات", cover.HtmlContent);
        Assert.Contains(">1<", cover.HtmlContent);
    }

    [Fact]
    public void BuildExportManifest_PartialExport_UsesExportedPageCountInCoverWhenPresent()
    {
        var model = BuildModel();
        var source = _renderer.RenderManifest(model,
        [
            ReportSectionId.Cover,
            ReportSectionId.ExecutiveSummary,
            ReportSectionId.DepartmentPerformance,
        ]);

        var exportManifest = _renderer.BuildExportManifest(source, [2], new ReportExportRequestDto
        {
            IncludePartialCover = true,
            PageNumberingMode = PageNumberingMode.Restart,
        });

        Assert.Equal(2, exportManifest.TotalPages);
        var partialCover = Assert.Single(exportManifest.Pages, p => p.SectionId == ReportSectionId.PartialCover);
        Assert.Contains("الصفحة 1 من 2", partialCover.HtmlContent);
        Assert.DoesNotContain("0 من 0", partialCover.HtmlContent);
    }

    private static InstitutionalReportModel BuildModel() => new()
    {
        Metadata = new ReportMetadataDto
        {
            ReportNumber = "REP-2026-000010",
            ReportTypeName = "تقرير",
            Title = "تقرير اختبار",
            IssueDate = new DateTime(2026, 6, 1),
            PeriodFrom = new DateTime(2026, 1, 1),
            PeriodTo = new DateTime(2026, 6, 1),
            VerificationId = "VERIFY",
            GeneratedAt = new DateTime(2026, 6, 1, 12, 0, 0, DateTimeKind.Utc),
        },
        Summary = new ExecutiveSummaryDto
        {
            KpiCards = [new KpiCardDto { Key = "total", Title = "إجمالي", Value = "1" }],
            ExecutiveNarrative = "نص",
        },
        DepartmentPerformance =
        [
            new DepartmentPerformanceRowDto
            {
                DepartmentName = "إدارة",
                TotalTransactions = 1,
                RatingLabel = "جيد",
            },
        ],
    };
}
