using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Uqeb.Api.Reporting.DTOs;
using Uqeb.Api.Reporting.Enums;

namespace Uqeb.Api.Reporting.Exporters;

public static class InstitutionalReportDocxExporter
{
    public static byte[] Export(
        InstitutionalReportModel model,
        RenderedReportManifestDto manifest,
        ReportExportRequestDto request)
    {
        using var stream = new MemoryStream();
        using (var doc = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document, true))
        {
            var mainPart = doc.AddMainDocumentPart();
            var body = new Body();
            mainPart.Document = new Document();
            mainPart.Document.AppendChild(body);

            AppendPartialExportNotice(body, request);
            foreach (var section in manifest.Pages.Select(p => p.SectionId).Distinct())
            {
                AppendSection(body, section, model);
                AppendSpacer(body);
            }

            mainPart.Document.Save();
        }

        return stream.ToArray();
    }

    private static void AppendPartialExportNotice(Body body, ReportExportRequestDto request)
    {
        if (request.ExportMode is not (ExportMode.SelectedPages or ExportMode.CurrentPage))
            return;

        AppendParagraph(
            body,
            "اختيار الصفحات الفعلية يطبق بدقة على PDF. في Word سيتم تصدير الأقسام المقابلة لأن توزيع الصفحات قد يختلف.",
            bold: true);
        AppendSpacer(body);
    }

    private static void AppendSection(Body body, ReportSectionId section, InstitutionalReportModel model)
    {
        switch (section)
        {
            case ReportSectionId.Cover:
                AppendCoverSection(body, model);
                break;
            case ReportSectionId.ExecutiveSummary:
                AppendExecutiveSummarySection(body, model);
                break;
            case ReportSectionId.DepartmentPerformance:
                AppendDepartmentPerformanceSection(body, model);
                break;
            case ReportSectionId.RisksAndAlerts:
                AppendRisksSection(body, model);
                break;
            case ReportSectionId.ExecutiveRecommendations:
                AppendRecommendationsSection(body, model);
                break;
            case ReportSectionId.TransactionDetails:
                AppendTransactionDetailsSection(body, model);
                break;
            case ReportSectionId.ReportMetadata:
                AppendMetadataSection(body, model);
                break;
        }
    }

    private static void AppendCoverSection(Body body, InstitutionalReportModel model)
    {
        AppendHeading(body, model.Metadata.Title);
        AppendParagraph(body, $"الفترة من {model.Metadata.PeriodFrom:yyyy-MM-dd} إلى {model.Metadata.PeriodTo:yyyy-MM-dd}");
        AppendParagraph(body, $"رقم التقرير: {model.Metadata.ReportNumber}");
        AppendParagraph(body, $"نوع التقرير: {model.Metadata.ReportTypeName}");
        AppendParagraph(body, $"تاريخ الإصدار: {model.Metadata.IssueDate:yyyy-MM-dd}");
    }

    private static void AppendExecutiveSummarySection(Body body, InstitutionalReportModel model)
    {
        AppendHeading(body, "الملخص التنفيذي");
        foreach (var card in model.Summary.KpiCards)
            AppendParagraph(body, $"{card.Title}: {card.Value}");
        AppendHeading(body, "التقييم التنفيذي", 2);
        AppendParagraph(body, model.Summary.ExecutiveNarrative);
    }

    private static void AppendDepartmentPerformanceSection(Body body, InstitutionalReportModel model)
    {
        AppendHeading(body, "أداء الإدارات");
        foreach (var row in model.DepartmentPerformance)
            AppendParagraph(body, $"{row.DepartmentName} — إجمالي {row.TotalTransactions} — التقييم {row.RatingLabel}");
    }

    private static void AppendRisksSection(Body body, InstitutionalReportModel model)
    {
        AppendHeading(body, "المخاطر والتنبيهات");
        foreach (var risk in model.Risks)
            AppendParagraph(body, $"{risk.Sequence}. {risk.Alert} ({risk.DepartmentName})");
    }

    private static void AppendRecommendationsSection(Body body, InstitutionalReportModel model)
    {
        AppendHeading(body, "التوصيات التنفيذية");
        foreach (var rec in model.Recommendations)
            AppendParagraph(body, $"{rec.Observation} — {rec.RequiredAction} [{rec.SourceLabel}]");
    }

    private static void AppendTransactionDetailsSection(Body body, InstitutionalReportModel model)
    {
        AppendHeading(body, "المعاملات التفصيلية");
        foreach (var tx in model.Transactions)
            AppendParagraph(body, $"{tx.Sequence}. {tx.IncomingNumber} — {tx.Subject}");
    }

    private static void AppendMetadataSection(Body body, InstitutionalReportModel model)
    {
        AppendHeading(body, "بيانات التقرير والفلاتر");
        AppendParagraph(body, $"رقم التقرير: {model.Metadata.ReportNumber}");
    }

    private static void AppendHeading(Body body, string text, int level = 1)
    {
        var paragraph = CreateParagraph();
        var run = CreateRun();
        var runProperties = new RunProperties();
        runProperties.AppendChild(new Bold());
        runProperties.AppendChild(new RightToLeftText());
        runProperties.AppendChild(new FontSize { Val = level == 1 ? "32" : "26" });
        run.AppendChild(runProperties);
        run.AppendChild(new Text(text));
        paragraph.AppendChild(run);
        body.AppendChild(paragraph);
    }

    private static void AppendParagraph(Body body, string text, bool bold = false)
    {
        var paragraph = CreateParagraph();
        var run = CreateRun();
        var runProperties = new RunProperties();
        runProperties.AppendChild(new RightToLeftText());
        if (bold)
            runProperties.AppendChild(new Bold());
        run.AppendChild(runProperties);
        run.AppendChild(new Text(text) { Space = SpaceProcessingModeValues.Preserve });
        paragraph.AppendChild(run);
        body.AppendChild(paragraph);
    }

    private static void AppendSpacer(Body body)
    {
        var paragraph = CreateParagraph();
        var run = CreateRun();
        run.AppendChild(new Text(string.Empty));
        paragraph.AppendChild(run);
        body.AppendChild(paragraph);
    }

    private static Paragraph CreateParagraph()
    {
        var paragraph = new Paragraph();
        var properties = new ParagraphProperties();
        properties.AppendChild(new BiDi());
        properties.AppendChild(new Justification { Val = JustificationValues.Right });
        paragraph.AppendChild(properties);
        return paragraph;
    }

    private static Run CreateRun() => new();
}
