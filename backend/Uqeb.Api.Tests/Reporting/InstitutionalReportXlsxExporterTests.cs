using ClosedXML.Excel;
using Uqeb.Api.Reporting.DTOs;
using Uqeb.Api.Reporting.Enums;
using Uqeb.Api.Reporting.Exporters;
using Uqeb.Api.Tests.Reporting.Visual;
using Xunit;

namespace Uqeb.Api.Tests.Reporting;

public class InstitutionalReportXlsxExporterTests
{
    private const string SeverityHeader = "الخطورة";
    private const string DepartmentHeader = "الإدارة";
    private const string PriorityHeader = "الأولوية";

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
        Assert.NotNull(methodology);
        Assert.Equal("إجمالي المعاملات", kpis!.Cell(2, 1).GetString());
        Assert.Contains("ارتفاع نسبة التأخر", findings!.Cell(2, 2).GetString());
        Assert.Equal("IN-0001", criticalCases!.Cell(2, 2).GetString());
        Assert.Contains("مراجعة المعاملات المتأخرة", recommendations!.Cell(2, 3).GetString());
        Assert.Contains("AverageFirstActionHours", methodology.Cell(13, 2).GetString());
        Assert.Equal("Period basis", methodology.Cell(5, 1).GetString());
        Assert.Equal("الفترة الزمنية مبنية على تاريخ الوارد.", methodology.Cell(5, 2).GetString());

        AssertHeaders(findings, ["الكود", "العنوان", "الوصف", "الدليل", SeverityHeader, "النطاق"]);
        AssertHeaders(criticalCases, ["المعرف", "رقم الوارد", "الموضوع", DepartmentHeader, "الجهة", PriorityHeader, "العمر", "أيام التأخر", "السبب", "الإجراء", SeverityHeader]);
        AssertHeaders(workbook.Worksheet("Categories"), ["التصنيف", "الإجمالي", "مفتوحة", "متأخرة", "ضمن المهلة", "متوسط الإنجاز", "إفادات معلقة"]);
        AssertHeaders(workbook.Worksheet("Priorities"), [PriorityHeader, "الإجمالي", "مفتوحة", "متأخرة", "متوسط العمر", "ضمن المهلة"]);
        AssertHeaders(workbook.Worksheet("Data Quality"), ["الكود", "الملاحظة", "العدد", "النسبة", SeverityHeader, "الحقول", "التصحيح"]);
        AssertHeaders(recommendations, [PriorityHeader, "النتيجة", "الإجراء", "المسؤول", "المدة المقترحة", "الحالة", "الدليل"]);
    }

    private static void AssertHeaders(IXLWorksheet worksheet, IReadOnlyList<string> expectedHeaders)
    {
        for (var column = 1; column <= expectedHeaders.Count; column++)
            Assert.Equal(expectedHeaders[column - 1], worksheet.Cell(1, column).GetString());
    }
}
