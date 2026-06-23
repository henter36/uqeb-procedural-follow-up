using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Uqeb.Api.Reporting.DTOs;
using Uqeb.Api.Reporting.Enums;

namespace Uqeb.Api.Reporting.Exporters;

public sealed class InstitutionalReportDocxExporter
{
    public byte[] Export(InstitutionalReportModel model, RenderedReportManifestDto manifest, ReportExportRequestDto request)
    {
        using var stream = new MemoryStream();
        using (var doc = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document, true))
        {
            var mainPart = doc.AddMainDocumentPart();
            mainPart.Document = new Document(new Body());
            var body = mainPart.Document.Body!;

            if (request.ExportMode is ExportMode.SelectedPages or ExportMode.CurrentPage)
            {
                AppendParagraph(body, "اختيار الصفحات الفعلية يطبق بدقة على PDF. في Word سيتم تصدير الأقسام المقابلة لأن توزيع الصفحات قد يختلف.", bold: true);
                AppendSpacer(body);
            }

            foreach (var section in manifest.Pages.Select(p => p.SectionId).Distinct())
            {
                switch (section)
                {
                    case ReportSectionId.Cover:
                        AppendHeading(body, model.Metadata.Title);
                        AppendParagraph(body, $"الفترة من {model.Metadata.PeriodFrom:yyyy-MM-dd} إلى {model.Metadata.PeriodTo:yyyy-MM-dd}");
                        AppendParagraph(body, $"رقم التقرير: {model.Metadata.ReportNumber}");
                        AppendParagraph(body, $"نوع التقرير: {model.Metadata.ReportTypeName}");
                        AppendParagraph(body, $"تاريخ الإصدار: {model.Metadata.IssueDate:yyyy-MM-dd}");
                        break;
                    case ReportSectionId.ExecutiveSummary:
                        AppendHeading(body, "الملخص التنفيذي");
                        foreach (var card in model.Summary.KpiCards)
                            AppendParagraph(body, $"{card.Title}: {card.Value}");
                        AppendHeading(body, "التقييم التنفيذي", 2);
                        AppendParagraph(body, model.Summary.ExecutiveNarrative);
                        break;
                    case ReportSectionId.DepartmentPerformance:
                        AppendHeading(body, "أداء الإدارات");
                        foreach (var row in model.DepartmentPerformance)
                            AppendParagraph(body, $"{row.DepartmentName} — إجمالي {row.TotalTransactions} — التقييم {row.RatingLabel}");
                        break;
                    case ReportSectionId.RisksAndAlerts:
                        AppendHeading(body, "المخاطر والتنبيهات");
                        foreach (var risk in model.Risks)
                            AppendParagraph(body, $"{risk.Sequence}. {risk.Alert} ({risk.DepartmentName})");
                        break;
                    case ReportSectionId.ExecutiveRecommendations:
                        AppendHeading(body, "التوصيات التنفيذية");
                        foreach (var rec in model.Recommendations)
                            AppendParagraph(body, $"{rec.Observation} — {rec.RequiredAction} [{rec.SourceLabel}]");
                        break;
                    case ReportSectionId.TransactionDetails:
                        AppendHeading(body, "المعاملات التفصيلية");
                        foreach (var tx in model.Transactions.Take(500))
                            AppendParagraph(body, $"{tx.Sequence}. {tx.IncomingNumber} — {tx.Subject}");
                        break;
                    case ReportSectionId.ReportMetadata:
                        AppendHeading(body, "بيانات التقرير والفلاتر");
                        AppendParagraph(body, $"رقم التقرير: {model.Metadata.ReportNumber}");
                        break;
                }
                AppendSpacer(body);
            }

            mainPart.Document.Save();
        }

        return stream.ToArray();
    }

    private static void AppendHeading(Body body, string text, int level = 1)
    {
        var props = new ParagraphProperties(new BiDi(), new Justification { Val = JustificationValues.Right });
        var runProps = new RunProperties(new Bold(), new RightToLeftText(), new FontSize { Val = level == 1 ? "32" : "26" });
        body.Append(new Paragraph(props, new Run(runProps, new Text(text))));
    }

    private static void AppendParagraph(Body body, string text, bool bold = false)
    {
        var props = new ParagraphProperties(new BiDi(), new Justification { Val = JustificationValues.Right });
        var runProps = new RunProperties(new RightToLeftText());
        if (bold) runProps.Append(new Bold());
        body.Append(new Paragraph(props, new Run(runProps, new Text(text) { Space = SpaceProcessingModeValues.Preserve })));
    }

    private static void AppendSpacer(Body body) =>
        body.Append(new Paragraph(new Run(new Text(string.Empty))));
}
