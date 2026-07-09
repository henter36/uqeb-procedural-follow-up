using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Uqeb.Api.Reporting.DTOs;
using Uqeb.Api.Reporting.Enums;
using Uqeb.Api.Reporting.Exporters;
using Uqeb.Api.Tests.Reporting.Visual;
using Xunit;

namespace Uqeb.Api.Tests.Reporting;

public class InstitutionalReportDocxExporterTests
{
    [Fact]
    public void Export_IncludesAllTransactions_WithoutSilentTruncation()
    {
        var transactions = Enumerable.Range(1, 501)
            .Select(i => new TransactionDetailRowDto
            {
                Sequence = i,
                IncomingNumber = $"IN-{i:D4}",
                Subject = $"معاملة {i}",
            })
            .ToList();

        var model = new InstitutionalReportModel
        {
            Metadata = new ReportMetadataDto
            {
                ReportNumber = "REP-2026-000501",
                Title = "تقرير",
                ReportTypeName = "شامل",
                IssueDate = DateTime.UtcNow.Date,
                PeriodFrom = DateTime.UtcNow.Date,
                PeriodTo = DateTime.UtcNow.Date,
            },
            Transactions = transactions,
        };

        var manifest = new RenderedReportManifestDto
        {
            Pages =
            [
                new RenderedReportPageDto
                {
                    SectionId = ReportSectionId.TransactionDetails,
                    SectionName = "المعاملات التفصيلية",
                    OriginalPageNumber = 1,
                    RenderedPageNumber = 1,
                },
            ],
        };

        var bytes = InstitutionalReportDocxExporter.Export(model, manifest, new ReportExportRequestDto());
        Assert.NotEmpty(bytes);

        using var zip = new System.IO.Compression.ZipArchive(new MemoryStream(bytes));
        var entry = zip.GetEntry("word/document.xml");
        Assert.NotNull(entry);
        using var reader = new StreamReader(entry.Open());
        var xml = reader.ReadToEnd();
        Assert.Contains("IN-0501", xml);
        Assert.Contains("معاملة 501", xml);
        Assert.DoesNotContain("تنبيه", xml);
    }

    [Fact]
    public void Export_ProducesValidOpenXmlDocument()
    {
        var model = new InstitutionalReportModel
        {
            Metadata = new ReportMetadataDto
            {
                ReportNumber = "REP-2026-000001",
                Title = "تقرير",
                ReportTypeName = "شامل",
                IssueDate = DateTime.UtcNow.Date,
                PeriodFrom = DateTime.UtcNow.Date,
                PeriodTo = DateTime.UtcNow.Date,
            },
            Transactions =
            [
                new TransactionDetailRowDto
                {
                    Sequence = 1,
                    IncomingNumber = "IN-0001",
                    Subject = "معاملة",
                },
            ],
        };

        var manifest = new RenderedReportManifestDto
        {
            Pages =
            [
                new RenderedReportPageDto
                {
                    SectionId = ReportSectionId.Cover,
                    SectionName = "الغلاف",
                    OriginalPageNumber = 1,
                    RenderedPageNumber = 1,
                },
            ],
        };

        var bytes = InstitutionalReportDocxExporter.Export(model, manifest, new ReportExportRequestDto());

        using var stream = new MemoryStream(bytes);
        using var document = WordprocessingDocument.Open(stream, false);
        var body = document.MainDocumentPart?.Document.Body;
        Assert.NotNull(body);
        Assert.NotEmpty(body.Elements<Paragraph>());
    }

    [Fact]
    public void Export_IncludesAnalyticalSectionsFromUnifiedModel()
    {
        var model = InstitutionalReportVisualFixtures.CreateBaseModel();
        var manifest = InstitutionalReportVisualFixtures.RenderSections(
            model,
            ReportSectionId.KeyPerformanceIndicators,
            ReportSectionId.SignificantFindings,
            ReportSectionId.CriticalCases,
            ReportSectionId.RecommendationsAndActionPlan,
            ReportSectionId.MethodologyAndDefinitions);

        var bytes = InstitutionalReportDocxExporter.Export(model, manifest, new ReportExportRequestDto());

        using var zip = new System.IO.Compression.ZipArchive(new MemoryStream(bytes));
        var entry = zip.GetEntry("word/document.xml");
        Assert.NotNull(entry);
        using var reader = new StreamReader(entry.Open());
        var xml = reader.ReadToEnd();
        Assert.Contains("مؤشرات الأداء الرئيسية", xml);
        Assert.Contains("ارتفاع نسبة التأخر", xml);
        Assert.Contains("معاملة حرجة متأخرة", xml);
        Assert.Contains("مراجعة المعاملات المتأخرة حسب الإدارات الأعلى أثرًا", xml);
        Assert.Contains("AverageFirstActionHours", xml);
    }

    [Fact]
    public void Export_IncludesDepartmentRecognitionsSection()
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

        var bytes = InstitutionalReportDocxExporter.Export(model, manifest, new ReportExportRequestDto());

        using var zip = new System.IO.Compression.ZipArchive(new MemoryStream(bytes));
        var entry = zip.GetEntry("word/document.xml");
        Assert.NotNull(entry);
        using var reader = new StreamReader(entry.Open());
        var xml = reader.ReadToEnd();
        Assert.Contains("الإدارات المتميزة والأكثر تحسنًا", xml);
        Assert.Contains("إدارة التميز", xml);
        Assert.Contains("ارتفاع نسبة الإنجاز في الوقت", xml);
    }

    [Fact]
    public void Export_DepartmentTransactions_RendersTableWithMatchedDepartmentsAndRelationColumn()
    {
        var model = new InstitutionalReportModel
        {
            Metadata = new ReportMetadataDto
            {
                ReportNumber = "REP-2026-000601",
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
                    IncomingNumber = "IN-0001",
                    IncomingDate = DateTime.UtcNow.Date,
                    Subject = "معاملة الإدارة",
                    Status = "قيد المعالجة",
                    Priority = "عاجل",
                    MatchedDepartments =
                    [
                        new TransactionDetailDepartmentRelationDto { DepartmentId = 20, DepartmentName = "الإدارة ب", Relation = "إحالة وصادر لها" },
                    ],
                },
            ],
        };

        var manifest = new RenderedReportManifestDto
        {
            Pages =
            [
                new RenderedReportPageDto
                {
                    SectionId = ReportSectionId.TransactionDetails,
                    SectionName = "المعاملات التفصيلية",
                    OriginalPageNumber = 1,
                    RenderedPageNumber = 1,
                },
            ],
        };

        var bytes = InstitutionalReportDocxExporter.Export(model, manifest, new ReportExportRequestDto());

        using var stream = new MemoryStream(bytes);
        using var document = WordprocessingDocument.Open(stream, false);
        var body = document.MainDocumentPart?.Document.Body;
        Assert.NotNull(body);
        var table = Assert.Single(body.Elements<Table>());
        var rows = table.Elements<TableRow>().ToList();
        Assert.Equal(2, rows.Count); // header + 1 data row
        var dataRowText = string.Concat(rows[1].Descendants<Text>().Select(t => t.Text));
        Assert.Contains("IN-0001", dataRowText);
        Assert.Contains("معاملة الإدارة", dataRowText);
        Assert.Contains("الإدارة ب", dataRowText);
        Assert.Contains("إحالة وصادر لها", dataRowText);
        Assert.Contains("عاجل", dataRowText);
    }
}
