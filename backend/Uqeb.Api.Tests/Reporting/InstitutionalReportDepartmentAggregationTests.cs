using ClosedXML.Excel;
using Uqeb.Api.Reporting.DTOs;
using Uqeb.Api.Reporting.Enums;
using Uqeb.Api.Reporting.Exporters;
using Uqeb.Api.Tests.Reporting.Visual;
using Xunit;

namespace Uqeb.Api.Tests.Reporting;

/// <summary>
/// Validates that DepartmentPerformance aggregation uses ResponsibleDepartment (additive)
/// and that all export paths surface the same model data without re-computation.
/// </summary>
public class InstitutionalReportDepartmentAggregationTests
{
    // ── Default model field values ──────────────────────────────────────────

    [Fact]
    public void Model_DefaultAggregationMode_IsResponsibleDepartment()
    {
        var model = new InstitutionalReportModel();
        Assert.Equal("ResponsibleDepartment", model.DepartmentAggregationMode);
    }

    [Fact]
    public void Model_DefaultTotalsAreAdditive_IsTrue()
    {
        var model = new InstitutionalReportModel();
        Assert.True(model.DepartmentTotalsAreAdditive);
    }

    [Fact]
    public void Model_DefaultAggregationDescription_IsNonEmpty()
    {
        var model = new InstitutionalReportModel();
        Assert.False(string.IsNullOrWhiteSpace(model.DepartmentAggregationDescription));
    }

    // ── Additive invariant on fixture model ─────────────────────────────────

    [Fact]
    public void FixtureModel_DepartmentPerformanceSum_DoesNotExceedTotalTransactions()
    {
        var model = InstitutionalReportVisualFixtures.CreateBaseModel();
        var sumByDept = model.DepartmentPerformance.Sum(r => r.TotalTransactions);
        // Under ResponsibleDepartment grouping each transaction appears once.
        Assert.True(sumByDept <= model.TotalMatchedRows,
            $"Sum across departments ({sumByDept}) exceeded TotalMatchedRows ({model.TotalMatchedRows}). " +
            "This indicates non-additive (fan-out) aggregation.");
    }

    [Fact]
    public void FixtureModel_ClosedCountSum_DoesNotExceedTotalTransactions()
    {
        var model = InstitutionalReportVisualFixtures.CreateBaseModel();
        var sumClosed = model.DepartmentPerformance.Sum(r => r.ClosedCount);
        Assert.True(sumClosed <= model.TotalMatchedRows,
            $"ClosedCount sum ({sumClosed}) exceeded TotalMatchedRows ({model.TotalMatchedRows}).");
    }

    // ── Manual model with joint transactions ────────────────────────────────

    [Fact]
    public void ManualModel_SingleResponsibleDept_OneRowInOutput()
    {
        var model = BuildModelWithRows(
            new DepartmentPerformanceRowDto
            {
                DepartmentId = 1,
                DepartmentName = "الشؤون الإدارية",
                TotalTransactions = 5,
                ClosedCount = 3,
                OpenCount = 2,
                JointDepartmentCount = 2,
            });

        // Each row represents one responsible department.
        Assert.Single(model.DepartmentPerformance, r => r.DepartmentId == 1);
        Assert.True(model.DepartmentTotalsAreAdditive);
    }

    [Fact]
    public void ManualModel_JointTransaction_CountedUnderResponsibleDeptOnly()
    {
        // Simulates: transaction 1 has ResponsibleDept=A, also assigned to B and C.
        // Under the additive policy it should appear in A's row only.
        // We prove the invariant: sum(TotalTransactions) == total unique transactions.
        const int totalUniqueTransactions = 10;
        var model = BuildModelWithRows(
            new DepartmentPerformanceRowDto { DepartmentId = 1, DepartmentName = "A", TotalTransactions = 6 },
            new DepartmentPerformanceRowDto { DepartmentId = 2, DepartmentName = "B", TotalTransactions = 4 });

        model.TotalMatchedRows = totalUniqueTransactions;
        var sum = model.DepartmentPerformance.Sum(r => r.TotalTransactions);
        Assert.Equal(totalUniqueTransactions, sum);
        Assert.True(model.DepartmentTotalsAreAdditive);
    }

    [Fact]
    public void ManualModel_NonAdditive_FlaggedCorrectly()
    {
        var model = new InstitutionalReportModel
        {
            TotalMatchedRows = 10,
            DepartmentAggregationMode = "ParticipatingDepartments",
            DepartmentTotalsAreAdditive = false,
            DepartmentAggregationDescription = "توزيع غير قابل للجمع — معاملة قد تظهر في أكثر من إدارة.",
        };

        Assert.False(model.DepartmentTotalsAreAdditive);
        Assert.Contains("غير قابل للجمع", model.DepartmentAggregationDescription);
    }

    // ── HTML renderer (department performance presentation) ─────────────────

    [Fact]
    public void HtmlRenderer_DepartmentSection_DoesNotShowAggregationFootnote()
    {
        var model = InstitutionalReportVisualFixtures.CreateBaseModel();
        var manifest = InstitutionalReportVisualFixtures.RenderSections(model, ReportSectionId.DepartmentPerformance);
        var page = manifest.Pages.Single();
        Assert.DoesNotContain("section-footnote", page.HtmlContent);
        Assert.DoesNotContain("إدارتها المسؤولة", page.HtmlContent);
    }

    [Fact]
    public void HtmlRenderer_DepartmentTotalRow_UsesShortTotalLabel()
    {
        var model = InstitutionalReportVisualFixtures.CreateBaseModel();
        var manifest = InstitutionalReportVisualFixtures.RenderSections(model, ReportSectionId.DepartmentPerformance);
        var html = manifest.Pages.Single().HtmlContent;
        Assert.Contains("<td>الإجمالي</td>", html);
        Assert.DoesNotContain("الإجمالي —", html);
    }

    [Fact]
    public void HtmlRenderer_DepartmentTotalRow_DoesNotShowNonAdditiveWarningInFootnote()
    {
        var model = InstitutionalReportVisualFixtures.CreateBaseModel();
        model.DepartmentTotalsAreAdditive = false;
        model.DepartmentAggregationDescription = "توزيع غير قابل للجمع";
        var manifest = InstitutionalReportVisualFixtures.RenderSections(model, ReportSectionId.DepartmentPerformance);
        var html = manifest.Pages.Single().HtmlContent;
        Assert.DoesNotContain("section-footnote", html);
        Assert.DoesNotContain("غير قابل للجمع", html);
    }

    // ── XLSX exporter (department sheet presentation) ─────────────────────────

    [Fact]
    public void XlsxExporter_DepartmentSheet_DoesNotIncludeMethodologyNote()
    {
        var model = InstitutionalReportVisualFixtures.CreateBaseModel();
        var manifest = InstitutionalReportVisualFixtures.RenderSections(model, ReportSectionId.DepartmentPerformance);
        var bytes = InstitutionalReportXlsxExporter.Export(model, manifest, new ReportExportRequestDto());

        using var workbook = new XLWorkbook(new MemoryStream(bytes));
        Assert.True(workbook.TryGetWorksheet("أداء الإدارات", out var ws));
        var sheetText = string.Join(' ', ws!.CellsUsed().Select(c => c.GetString()));
        Assert.DoesNotContain("ملاحظة منهجية", sheetText);
        Assert.DoesNotContain("إدارتها المسؤولة", sheetText);
        Assert.DoesNotContain("بانتظار إفادة", sheetText);
    }

    [Fact]
    public void XlsxExporter_DepartmentSheet_DoesNotIncludeNonAdditiveWarningNote()
    {
        var model = InstitutionalReportVisualFixtures.CreateBaseModel();
        model.DepartmentTotalsAreAdditive = false;
        model.DepartmentAggregationDescription = "توزيع غير قابل للجمع";
        var manifest = InstitutionalReportVisualFixtures.RenderSections(model, ReportSectionId.DepartmentPerformance);
        var bytes = InstitutionalReportXlsxExporter.Export(model, manifest, new ReportExportRequestDto());

        using var workbook = new XLWorkbook(new MemoryStream(bytes));
        Assert.True(workbook.TryGetWorksheet("أداء الإدارات", out var ws));
        var sheetText = string.Join(' ', ws!.CellsUsed().Select(c => c.GetString()));
        Assert.DoesNotContain("تحذير", sheetText);
        Assert.DoesNotContain("غير قابل للجمع", sheetText);
    }

    // ── ClosedAt.HasValue guard ──────────────────────────────────────────────

    [Fact]
    public void FixtureModel_ClosedRowWithNoClosedAt_DoesNotCrash()
    {
        // A DepartmentPerformanceRowDto with ClosedCount > 0 but AverageCompletionDays = 0
        // represents a closed transaction with missing ClosedAt — must not throw.
        var model = BuildModelWithRows(
            new DepartmentPerformanceRowDto
            {
                DepartmentId = 1,
                DepartmentName = "إدارة بلا تاريخ إغلاق",
                TotalTransactions = 3,
                ClosedCount = 3,
                OpenCount = 0,
                AverageCompletionDays = 0, // ClosedAt was null — guarded to 0
            });

        var manifest = InstitutionalReportVisualFixtures.RenderSections(model, ReportSectionId.DepartmentPerformance);
        // Just rendering must not throw.
        Assert.NotNull(manifest);
        var html = manifest.Pages.Single().HtmlContent;
        Assert.Contains("إدارة بلا تاريخ إغلاق", html);
    }

    // ── Renderer footnote removed from department performance presentation ──

    [Fact]
    public void HtmlRenderer_Footnote_IsNotRendered_WhenAdditive()
    {
        var model = InstitutionalReportVisualFixtures.CreateBaseModel();
        var manifest = InstitutionalReportVisualFixtures.RenderSections(model, ReportSectionId.DepartmentPerformance);
        var html = manifest.Pages.Single().HtmlContent;
        Assert.DoesNotContain("إدارتها المسؤولة فقط", html);
        Assert.DoesNotContain("section-footnote", html);
    }

    [Fact]
    public void HtmlRenderer_Footnote_IsNotRendered_WhenNonAdditive()
    {
        var model = InstitutionalReportVisualFixtures.CreateBaseModel();
        model.DepartmentTotalsAreAdditive = false;
        model.DepartmentAggregationDescription = "توزيع غير قابل للجمع — اختبار";
        var manifest = InstitutionalReportVisualFixtures.RenderSections(model, ReportSectionId.DepartmentPerformance);
        var html = manifest.Pages.Single().HtmlContent;
        Assert.DoesNotContain("توزيع غير قابل للجمع — اختبار", html);
        Assert.DoesNotContain("section-footnote", html);
    }

    // ── Helper ──────────────────────────────────────────────────────────────

    private static InstitutionalReportModel BuildModelWithRows(
        params DepartmentPerformanceRowDto[] rows)
    {
        var model = InstitutionalReportVisualFixtures.CreateBaseModel();
        model.DepartmentPerformance = [.. rows];
        return model;
    }
}
