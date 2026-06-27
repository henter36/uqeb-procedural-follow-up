using ClosedXML.Excel;
using Uqeb.Api.Reporting.DTOs;
using Uqeb.Api.Reporting.Enums;
using Uqeb.Api.Reporting.Exporters;
using Uqeb.Api.Tests.Reporting.Visual;
using Xunit;

namespace Uqeb.Api.Tests.Reporting;

/// <summary>
/// Proves that all export paths (HTML, XLSX, DOCX) surface values directly from
/// InstitutionalReportModel without re-computing them inside the exporter.
///
/// Test boundary note: PDF export goes through Playwright/Chromium (Category=SqlServer /
/// integration-only); it is excluded here. DOCX is verified at the raw XML level.
/// XLSX is verified at the cell level via ClosedXML. HTML is verified via string search
/// in the rendered manifest page content.
/// </summary>
public class InstitutionalReportExportParityTests
{
    // Known fixture values — must match InstitutionalReportVisualFixtures.CreateBaseModel()
    private const int FixtureTotalTransactions = 125;
    private const string FixtureDepartmentName = "الشؤون الإدارية";
    private const int FixtureDeptTotal = 40;
    private const int FixtureDeptClosed = 25;
    private const int FixtureDeptOpen = 15;
    private const int FixtureDeptOverdue = 3;

    // ── HTML preview parity ────────────────────────────────────────────────

    [Fact]
    public void HtmlPreview_DepartmentSection_ContainsDeptName_FromModel()
    {
        var model = InstitutionalReportVisualFixtures.CreateBaseModel();
        var manifest = InstitutionalReportVisualFixtures.RenderSections(model, ReportSectionId.DepartmentPerformance);
        var html = manifest.Pages.Single().HtmlContent;
        Assert.Contains(FixtureDepartmentName, html);
    }

    [Fact]
    public void HtmlPreview_DepartmentSection_ContainsDeptTotalCount_FromModel()
    {
        var model = InstitutionalReportVisualFixtures.CreateBaseModel();
        var manifest = InstitutionalReportVisualFixtures.RenderSections(model, ReportSectionId.DepartmentPerformance);
        var html = manifest.Pages.Single().HtmlContent;
        Assert.Contains($">{FixtureDeptTotal}<", html);
    }

    [Fact]
    public void HtmlPreview_ExecutiveSummary_ContainsTotalTransactions_FromModel()
    {
        var model = InstitutionalReportVisualFixtures.CreateBaseModel();
        var manifest = InstitutionalReportVisualFixtures.RenderSections(model, ReportSectionId.ExecutiveSummary);
        var html = manifest.Pages.Single().HtmlContent;
        // KPI card shows total transactions from Summary.KpiCards, which is set from model data.
        Assert.Contains(FixtureTotalTransactions.ToString("N0"), html);
    }

    [Fact]
    public void HtmlPreview_WhenDepartmentRowChanged_ReflectsNewValueNotOld()
    {
        var model = InstitutionalReportVisualFixtures.CreateBaseModel();
        var original = model.DepartmentPerformance[0];
        model.DepartmentPerformance[0] = new DepartmentPerformanceRowDto
        {
            DepartmentId = original.DepartmentId,
            DepartmentName = "إدارة المعلومات المعدّلة",
            TotalTransactions = 999,
            ClosedCount = original.ClosedCount,
            OpenCount = original.OpenCount,
            OverdueCount = original.OverdueCount,
            Rating = original.Rating,
            RatingLabel = original.RatingLabel,
        };
        var manifest = InstitutionalReportVisualFixtures.RenderSections(model, ReportSectionId.DepartmentPerformance);
        var html = manifest.Pages.Single().HtmlContent;
        Assert.Contains("إدارة المعلومات المعدّلة", html);
        Assert.Contains(">999<", html);
        Assert.DoesNotContain(FixtureDepartmentName, html);
    }

    // ── XLSX parity ──────────────────────────────────────────────────────────

    [Fact]
    public void Xlsx_DepartmentSheet_RowValues_MatchModel()
    {
        var model = InstitutionalReportVisualFixtures.CreateBaseModel();
        var manifest = InstitutionalReportVisualFixtures.RenderSections(model, ReportSectionId.DepartmentPerformance);
        var bytes = InstitutionalReportXlsxExporter.Export(model, manifest, new ReportExportRequestDto());

        using var workbook = new XLWorkbook(new MemoryStream(bytes));
        Assert.True(workbook.TryGetWorksheet("أداء الإدارات", out var ws));
        Assert.Equal(FixtureDepartmentName, ws!.Cell(2, 1).GetString());
        Assert.Equal(FixtureDeptTotal, ws.Cell(2, 2).GetValue<int>());
        Assert.Equal(FixtureDeptClosed, ws.Cell(2, 3).GetValue<int>());
        Assert.Equal(FixtureDeptOpen, ws.Cell(2, 4).GetValue<int>());
        Assert.Equal(FixtureDeptOverdue, ws.Cell(2, 6).GetValue<int>());
    }

    [Fact]
    public void Xlsx_DepartmentSheet_WhenModelRowChanged_ReflectsNewValue()
    {
        var model = InstitutionalReportVisualFixtures.CreateBaseModel();
        model.DepartmentPerformance[0] = new DepartmentPerformanceRowDto
        {
            DepartmentId = model.DepartmentPerformance[0].DepartmentId,
            DepartmentName = "إدارة مُعدَّلة",
            TotalTransactions = 777,
        };
        var manifest = InstitutionalReportVisualFixtures.RenderSections(model, ReportSectionId.DepartmentPerformance);
        var bytes = InstitutionalReportXlsxExporter.Export(model, manifest, new ReportExportRequestDto());

        using var workbook = new XLWorkbook(new MemoryStream(bytes));
        Assert.True(workbook.TryGetWorksheet("أداء الإدارات", out var ws));
        Assert.Equal("إدارة مُعدَّلة", ws!.Cell(2, 1).GetString());
        Assert.Equal(777, ws.Cell(2, 2).GetValue<int>());
    }

    [Fact]
    public void Xlsx_MethodologySheet_ContainsAverageResponseDays_ProxyNote()
    {
        // The fixture uses hardcoded DeferredMetrics; we inject the AverageResponseDays note
        // to prove the exporter surfaces whatever the model carries (parity, not re-computation).
        var model = InstitutionalReportVisualFixtures.CreateBaseModel();
        model.Analysis.Methodology.DeferredMetrics.Add(
            "AverageResponseDays (تقديري): لا يوجد ResponseCompletedAt مستقل — يستخدم ClosedAt بديلاً.");
        var manifest = InstitutionalReportVisualFixtures.RenderSections(model, ReportSectionId.MethodologyAndDefinitions);
        var bytes = InstitutionalReportXlsxExporter.Export(model, manifest, new ReportExportRequestDto());

        using var workbook = new XLWorkbook(new MemoryStream(bytes));
        Assert.True(workbook.TryGetWorksheet("Methodology", out var ws));
        var allText = ws!.CellsUsed().Select(c => c.GetString()).ToList();
        var hasProxyNote = allText.Any(t => t.Contains("AverageResponseDays") && t.Contains("تقديري"));
        Assert.True(hasProxyNote,
            "Methodology sheet must surface the AverageResponseDays proxy note from model.Analysis.Methodology.DeferredMetrics.");
    }

    // ── DOCX parity ─────────────────────────────────────────────────────────

    [Fact]
    public void Docx_DepartmentSection_ContainsDeptName_FromModel()
    {
        var model = InstitutionalReportVisualFixtures.CreateBaseModel();
        var manifest = InstitutionalReportVisualFixtures.RenderSections(model, ReportSectionId.DepartmentPerformance);
        var bytes = InstitutionalReportDocxExporter.Export(model, manifest, new ReportExportRequestDto());

        using var zip = new System.IO.Compression.ZipArchive(new MemoryStream(bytes));
        var entry = zip.GetEntry("word/document.xml");
        Assert.NotNull(entry);
        using var reader = new System.IO.StreamReader(entry!.Open());
        var xml = reader.ReadToEnd();
        Assert.Contains(FixtureDepartmentName, xml);
        Assert.Contains(FixtureDeptTotal.ToString(), xml);
    }

    [Fact]
    public void Docx_MethodologySection_ContainsAverageResponseDays_ProxyNote()
    {
        var model = InstitutionalReportVisualFixtures.CreateBaseModel();
        model.Analysis.Methodology.DeferredMetrics.Add(
            "AverageResponseDays (تقديري): لا يوجد ResponseCompletedAt مستقل — يستخدم ClosedAt بديلاً.");
        var manifest = InstitutionalReportVisualFixtures.RenderSections(model, ReportSectionId.MethodologyAndDefinitions);
        var bytes = InstitutionalReportDocxExporter.Export(model, manifest, new ReportExportRequestDto());

        using var zip = new System.IO.Compression.ZipArchive(new MemoryStream(bytes));
        var entry = zip.GetEntry("word/document.xml");
        using var reader = new System.IO.StreamReader(entry!.Open());
        var xml = reader.ReadToEnd();
        Assert.Contains("AverageResponseDays", xml);
    }

    // ── Unified-source invariant: same model → same counts across formats ────

    [Fact]
    public void AllFormats_DepartmentTotalTransactions_MatchModelValue()
    {
        var model = InstitutionalReportVisualFixtures.CreateBaseModel();
        var deptManifest = InstitutionalReportVisualFixtures.RenderSections(model, ReportSectionId.DepartmentPerformance);
        var xlsxBytes = InstitutionalReportXlsxExporter.Export(model, deptManifest, new ReportExportRequestDto());

        // HTML
        var html = deptManifest.Pages.Single().HtmlContent;
        Assert.Contains($">{FixtureDeptTotal}<", html);

        // XLSX
        using var workbook = new XLWorkbook(new MemoryStream(xlsxBytes));
        workbook.TryGetWorksheet("أداء الإدارات", out var ws);
        Assert.Equal(FixtureDeptTotal, ws!.Cell(2, 2).GetValue<int>());

        // DOCX
        var docxBytes = InstitutionalReportDocxExporter.Export(model, deptManifest, new ReportExportRequestDto());
        using var zip = new System.IO.Compression.ZipArchive(new MemoryStream(docxBytes));
        using var reader = new System.IO.StreamReader(zip.GetEntry("word/document.xml")!.Open());
        Assert.Contains(FixtureDeptTotal.ToString(), reader.ReadToEnd());
    }

    // ── KPI parity: Summary.KpiCards sourced from model, not re-derived ──────

    [Fact]
    public void HtmlPreview_ExecutiveSummary_KpiCards_AreNotRecomputed_InRenderer()
    {
        // If the renderer re-computed totals it would ignore the KpiCards we set.
        // We set a deliberately wrong card value; if the renderer takes it from the card
        // the output will contain "9999", proving pass-through (no re-computation).
        var model = InstitutionalReportVisualFixtures.CreateBaseModel();
        model.Summary.KpiCards.Add(new KpiCardDto
        {
            Key = "test_parity",
            Title = "مؤشر اختبار التطابق",
            Value = "9999"
        });
        var manifest = InstitutionalReportVisualFixtures.RenderSections(model, ReportSectionId.ExecutiveSummary);
        var html = manifest.Pages.Single().HtmlContent;
        Assert.Contains("9999", html);
        Assert.Contains("مؤشر اختبار التطابق", html);
    }
}
