using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Uqeb.Api.Reporting.DTOs;
using Uqeb.Api.Reporting.Enums;
using Uqeb.Api.Reporting.Exporters;
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

        var bytes = new InstitutionalReportDocxExporter().Export(model, manifest, new ReportExportRequestDto());
        Assert.NotEmpty(bytes);

        using var zip = new System.IO.Compression.ZipArchive(new MemoryStream(bytes));
        var entry = zip.GetEntry("word/document.xml");
        Assert.NotNull(entry);
        using var reader = new StreamReader(entry!.Open());
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

        var bytes = new InstitutionalReportDocxExporter().Export(model, manifest, new ReportExportRequestDto());

        using var stream = new MemoryStream(bytes);
        using var document = WordprocessingDocument.Open(stream, false);
        var body = document.MainDocumentPart?.Document.Body;
        Assert.NotNull(body);
        Assert.NotEmpty(body!.Elements<Paragraph>());
    }
}
