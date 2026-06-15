using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Uqeb.Api.Helpers;

public static class FollowUpLetterPdfExporter
{
    static FollowUpLetterPdfExporter()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public static byte[] ToPdf(string body, DateTime today)
    {
        var fontFamily = "Tahoma";
        var lines = body.Replace("\r\n", "\n").Split('\n');

        return Document.Create(doc =>
        {
            doc.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(40);
                page.ContentFromRightToLeft();
                page.DefaultTextStyle(x => x.FontFamily(fontFamily).FontSize(12));

                page.Content().Column(col =>
                {
                    col.Spacing(6);
                    col.Item().AlignRight().Text("خطاب تعقيب").FontSize(18).Bold();
                    col.Item().AlignRight().Text($"التاريخ: {today:yyyy-MM-dd}").FontSize(10);
                    col.Item().PaddingTop(16);

                    foreach (var line in lines)
                    {
                        if (string.IsNullOrWhiteSpace(line))
                            col.Item().Height(8);
                        else
                            col.Item().AlignRight().Text(line);
                    }
                });
            });
        }).GeneratePdf();
    }
}
