using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using Uqeb.Api.Reporting.DTOs;

namespace Uqeb.Api.Reporting.Exporters;

public sealed class InstitutionalReportPdfExporter
{
    static InstitutionalReportPdfExporter()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public byte[] Export(RenderedReportManifestDto manifest, ReportExportRequestDto request)
    {
        var font = "Tahoma";
        return Document.Create(document =>
        {
            foreach (var page in manifest.Pages)
            {
                document.Page(pageDescriptor =>
                {
                    pageDescriptor.Size(PageSizes.A4);
                    pageDescriptor.Margin(28);
                    pageDescriptor.ContentFromRightToLeft();
                    pageDescriptor.DefaultTextStyle(x => x.FontFamily(font).FontSize(10));

                    pageDescriptor.Header().BorderBottom(1).BorderColor("#D9E1DD").PaddingBottom(6).Row(row =>
                    {
                        row.RelativeItem().Text("الهيئة العامة للمتابعة الإجرائية").Bold().FontColor("#123F32");
                        row.RelativeItem().AlignLeft().Text(page.SectionName).FontColor("#2F6B58");
                    });

                    pageDescriptor.Content().PaddingTop(8).Column(col =>
                    {
                        col.Item().Text(page.PageTitle).FontSize(16).Bold().FontColor("#123F32");
                        col.Item().PaddingTop(8).Text(StripHtml(page.HtmlContent)).LineHeight(1.5f);
                    });

                    pageDescriptor.Footer().BorderTop(1).BorderColor("#D9E1DD").PaddingTop(4).Row(row =>
                    {
                        var note = manifest.IsPartialExport ? "نسخة جزئية — " : string.Empty;
                        row.RelativeItem().Text($"{note}الصفحة {page.RenderedPageNumber} من {manifest.TotalPages}").FontSize(9);
                        row.RelativeItem().AlignLeft().Text("تقرير المتابعة الإجرائية للمعاملات").FontSize(9);
                    });
                });
            }
        }).GeneratePdf();
    }

    private static string StripHtml(string html)
    {
        if (string.IsNullOrWhiteSpace(html)) return string.Empty;
        var text = System.Text.RegularExpressions.Regex.Replace(html, "<[^>]+>", " ");
        return System.Net.WebUtility.HtmlDecode(text).Replace("  ", " ").Trim();
    }
}
