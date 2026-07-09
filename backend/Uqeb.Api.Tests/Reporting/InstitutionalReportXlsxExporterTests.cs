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

    [Fact]
    public void Export_IncludesDepartmentTimeSeriesWorksheet_WhenTimeTrendsSectionIsRendered()
    {
        var model = InstitutionalReportVisualFixtures.CreateBaseModel();
        var manifest = InstitutionalReportVisualFixtures.RenderSections(
            model,
            ReportSectionId.KeyPerformanceIndicators,
            ReportSectionId.TimeTrends,
            ReportSectionId.DepartmentPerformance);

        var bytes = InstitutionalReportXlsxExporter.Export(model, manifest, new ReportExportRequestDto());

        using var workbook = new XLWorkbook(new MemoryStream(bytes));
        Assert.True(workbook.TryGetWorksheet("Department Time Series", out var departmentTimeSeries));
        Assert.NotNull(departmentTimeSeries);
        AssertHeaders(departmentTimeSeries, ["الفترة", DepartmentHeader, "الوارد", "المغلق", "المفتوح", "المتأخر", "ضمن المهلة", "متوسط الإنجاز", "الإفادات المعلقة", "الردود الجزئية", "تغير التراكم"]);
        Assert.Equal("2026-06", departmentTimeSeries.Cell(2, 1).GetString());
        Assert.Equal("الشؤون الإدارية", departmentTimeSeries.Cell(2, 2).GetString());

        // Existing worksheets from the same export must not be broken by the new sheet.
        Assert.True(workbook.TryGetWorksheet("KPIs", out var kpis));
        Assert.NotNull(kpis);
        Assert.Equal("إجمالي المعاملات", kpis.Cell(2, 1).GetString());
        Assert.True(workbook.TryGetWorksheet("أداء الإدارات", out var departmentPerformance));
        Assert.NotNull(departmentPerformance);
    }

    [Fact]
    public void Export_IncludesDepartmentRecognitionsWorksheet_WhenSectionIsRendered()
    {
        var model = InstitutionalReportVisualFixtures.CreateBaseModel();
        model.Analysis.DepartmentRecognitions =
        [
            new DepartmentRecognitionRowDto
            {
                DepartmentName = "إدارة التميز",
                RecognitionType = "متميزة",
                TransactionCount = 12,
                OnTimeCompletionRate = 92,
                OverdueCount = 0,
                AverageCompletionDays = 2.4,
                ImprovementValue = 0,
                Reason = "ارتفاع نسبة الإنجاز في الوقت",
            },
        ];
        var manifest = InstitutionalReportVisualFixtures.RenderSections(
            model,
            ReportSectionId.OutstandingAndImprovedDepartments);

        var bytes = InstitutionalReportXlsxExporter.Export(model, manifest, new ReportExportRequestDto());

        using var workbook = new XLWorkbook(new MemoryStream(bytes));
        Assert.True(workbook.TryGetWorksheet("الإدارات المتميزة والتحسن", out var recognitions));
        Assert.NotNull(recognitions);
        AssertHeaders(recognitions, ["اسم الإدارة", "نوع التصنيف", "حجم المعاملات", "نسبة الإنجاز في الوقت", "عدد المتأخرات", "متوسط مدة المعالجة أو التأخير", "مقدار التحسن مقارنة بالفترة السابقة", "سبب التصنيف"]);
        Assert.Equal("إدارة التميز", recognitions.Cell(2, 1).GetString());
        Assert.Equal("متميزة", recognitions.Cell(2, 2).GetString());
    }

    [Fact]
    public void Export_OmitsDepartmentTimeSeriesWorksheet_WhenNoDataAndTimeTrendsSectionRendered()
    {
        var model = InstitutionalReportVisualFixtures.CreateBaseModel();
        model.Analysis.DepartmentTimeSeries = [];
        var manifest = InstitutionalReportVisualFixtures.RenderSections(model, ReportSectionId.TimeTrends);

        var bytes = InstitutionalReportXlsxExporter.Export(model, manifest, new ReportExportRequestDto());

        using var workbook = new XLWorkbook(new MemoryStream(bytes));
        Assert.False(workbook.TryGetWorksheet("Department Time Series", out _));
    }

    [Fact]
    public void Export_DepartmentTransactions_WritesMatchedDepartmentsRelationAndFullDepartmentColumns()
    {
        var model = new InstitutionalReportModel
        {
            Metadata = new ReportMetadataDto
            {
                ReportNumber = "REP-2026-000701",
                Title = "تقرير معاملات إدارة",
                ReportTypeName = "تقرير معاملات إدارة",
                ReportType = InstitutionalReportType.DepartmentTransactions,
                IssueDate = DateTime.UtcNow.Date,
                PeriodFrom = DateTime.UtcNow.Date,
                PeriodTo = DateTime.UtcNow.Date,
            },
            Transactions =
            [
                new TransactionDetailRowDto
                {
                    Sequence = 1,
                    TrackingNumber = "UQEB-2026-00001",
                    IncomingNumber = "IN-0001",
                    IncomingDate = DateTime.UtcNow.Date,
                    Subject = "معاملة الإدارة",
                    Status = "قيد المعالجة",
                    Priority = "عاجل",
                    MatchedDepartments =
                    [
                        new TransactionDetailDepartmentRelationDto { DepartmentId = 20, DepartmentName = "الإدارة ب", Relation = "إحالة وصادر لها" },
                    ],
                    AllAssignmentDepartments = ["الإدارة ب", "الإدارة ج"],
                    AllOutgoingDepartments = ["الإدارة ب"],
                },
            ],
        };

        var manifest = new RenderedReportManifestDto
        {
            Pages = [new RenderedReportPageDto { SectionId = ReportSectionId.TransactionDetails }],
        };

        var bytes = InstitutionalReportXlsxExporter.Export(model, manifest, new ReportExportRequestDto());

        using var workbook = new XLWorkbook(new MemoryStream(bytes));
        var ws = workbook.Worksheet("المعاملات التفصيلية");
        AssertHeaders(ws, [
            "م", "رقم الوارد", "تاريخ الوارد", "الموضوع", "الجهة",
            "الإدارة/الإدارات المطابقة", "نوع العلاقة", "رقم الصادر", "تاريخ الصادر",
            "إدارات الإحالة (الكل)", "إدارات الصادر (الكل)", "الحالة", PriorityHeader, "المهلة", "آخر إجراء",
        ]);
        Assert.Equal("الإدارة ب", ws.Cell(2, 6).GetString());
        Assert.Equal("إحالة وصادر لها", ws.Cell(2, 7).GetString());
        Assert.Equal("الإدارة ب؛ الإدارة ج", ws.Cell(2, 10).GetString());
        Assert.Equal("الإدارة ب", ws.Cell(2, 11).GetString());
    }

    private static void AssertHeaders(IXLWorksheet worksheet, IReadOnlyList<string> expectedHeaders)
    {
        for (var column = 1; column <= expectedHeaders.Count; column++)
            Assert.Equal(expectedHeaders[column - 1], worksheet.Cell(1, column).GetString());
    }
}
