using ClosedXML.Excel;
using Uqeb.Api.Reporting.DTOs;
using Uqeb.Api.Reporting.Enums;
using Uqeb.Api.Reporting.Exporters;
using Uqeb.Api.Tests.Reporting.Visual;
using Xunit;

namespace Uqeb.Api.Tests.Reporting;

public class InstitutionalReportXlsxExporterTests
{
    [Fact]
    public void Export_IncludesAnalyticalWorksheetsFromUnifiedModel()
    {
        var model = InstitutionalReportVisualFixtures.CreateBaseModel();
        var manifest = InstitutionalReportVisualFixtures.RenderSections(
            model,
            ReportSectionId.KeyPerformanceIndicators,
            ReportSectionId.SignificantFindings,
            ReportSectionId.CriticalCases,
            ReportSectionId.ExternalPartyAnalysis,
            ReportSectionId.ClassificationAndPriorityAnalysis,
            ReportSectionId.DelayAndBottleneckAnalysis,
            ReportSectionId.DataQuality,
            ReportSectionId.RecommendationsAndActionPlan,
            ReportSectionId.MethodologyAndDefinitions);

        var bytes = InstitutionalReportXlsxExporter.Export(model, manifest, new ReportExportRequestDto());

        using var workbook = new XLWorkbook(new MemoryStream(bytes));
        Assert.True(workbook.TryGetWorksheet("KPIs", out var kpis));
        Assert.True(workbook.TryGetWorksheet("Findings", out var findings));
        Assert.True(workbook.TryGetWorksheet("Critical Cases", out var criticalCases));
        Assert.True(workbook.TryGetWorksheet("Recommendations", out var recommendations));
        Assert.True(workbook.TryGetWorksheet("Methodology", out var methodology));
        Assert.Equal("إجمالي المعاملات", kpis!.Cell(2, 1).GetString());
        Assert.Contains("ارتفاع نسبة التأخر", findings!.Cell(2, 2).GetString());
        Assert.Equal("IN-0001", criticalCases!.Cell(2, 2).GetString());
        Assert.Contains("مراجعة المعاملات المتأخرة", recommendations!.Cell(2, 3).GetString());
        Assert.Contains("AverageFirstActionHours", methodology!.Cell(12, 2).GetString());
    }
}
