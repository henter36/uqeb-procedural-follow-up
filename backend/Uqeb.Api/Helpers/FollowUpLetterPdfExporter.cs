using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using Uqeb.Api.Models.Letters;
using Uqeb.Api.Services;

namespace Uqeb.Api.Helpers;

public interface IFollowUpLetterPdfExporter
{
    byte[] GeneratePdf(FollowUpLetterDocumentModel document);
}

public sealed class FollowUpLetterPdfExporter : IFollowUpLetterPdfExporter
{
    private const string FontFamily = "Tahoma";
    private readonly IOrganizationBrandLogoProvider _logoProvider;

    static FollowUpLetterPdfExporter()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public FollowUpLetterPdfExporter(IOrganizationBrandLogoProvider logoProvider)
    {
        _logoProvider = logoProvider;
    }

    public byte[] GeneratePdf(FollowUpLetterDocumentModel document)
    {
        var logoBytes = ResolveLogoBytes(document.LogoPath);
        var bodyLines = SplitBodyLines(document.Body);

        return Document.Create(doc =>
        {
            doc.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(40);
                page.ContentFromRightToLeft();
                page.DefaultTextStyle(x => x.FontFamily(FontFamily).FontSize(12));
                page.Header().Element(header => RenderHeader(header, document, logoBytes));
                page.Content().Element(content => RenderBody(content, document, bodyLines));
                page.Footer().Element(RenderFooter);
            });
        }).GeneratePdf();
    }

    public static byte[] ToPdf(string body, DateTime today)
    {
        var document = new FollowUpLetterDocumentModel
        {
            Body = body,
            GregorianDate = today.ToString("dd/MM/yyyy"),
            HijriDate = HijriDateFormatter.Format(today) ?? string.Empty,
            Title = "خطاب تعقيب",
        };

        var bodyLines = SplitBodyLines(body);

        return Document.Create(doc =>
        {
            doc.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(40);
                page.ContentFromRightToLeft();
                page.DefaultTextStyle(x => x.FontFamily(FontFamily).FontSize(12));
                page.Content().Column(col =>
                {
                    col.Spacing(6);
                    col.Item().AlignRight().Text(document.Title).FontSize(18).Bold();
                    col.Item().AlignRight().Text($"التاريخ: {document.GregorianDate}").FontSize(10);
                    col.Item().PaddingTop(16);
                    RenderBodyLines(col, bodyLines);
                });
            });
        }).GeneratePdf();
    }

    private byte[]? ResolveLogoBytes(string? explicitLogoPath) =>
        _logoProvider.TryGetLogoBytes(explicitLogoPath);

    private static string[] SplitBodyLines(string body) =>
        body.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');

    private static void RenderHeader(IContainer container, FollowUpLetterDocumentModel document, byte[]? logoBytes)
    {
        container.Column(header =>
        {
            header.Spacing(6);
            header.Item().Row(row =>
            {
                row.RelativeItem().Column(col =>
                {
                    col.Item().AlignRight().Text(document.Title).FontSize(18).Bold();
                    col.Item().AlignRight().Text($"التاريخ: {document.GregorianDate}").FontSize(10);
                    col.Item().AlignRight().Text($"التاريخ الهجري: {document.HijriDate}").FontSize(10);
                    col.Item().AlignRight().Text($"رقم الخطاب: {document.LetterNumber}").FontSize(10);
                });

                if (logoBytes != null)
                    row.ConstantItem(120).AlignLeft().Image(logoBytes).FitArea();
            });

            header.Item().PaddingTop(8).AlignRight().Text($"إلى: {document.Recipient}").SemiBold();
            header.Item().AlignRight().Text($"الموضوع: {document.Subject}").SemiBold();
            header.Item().AlignRight().Text(document.FollowUpSequenceText).FontSize(10);
            header.Item().PaddingTop(8);
        });
    }

    private static void RenderBody(IContainer container, FollowUpLetterDocumentModel document, string[] bodyLines)
    {
        container.Column(col =>
        {
            col.Spacing(4);
            RenderBodyLines(col, bodyLines);

            if (!string.IsNullOrWhiteSpace(document.SenderDepartment))
                col.Item().PaddingTop(24).AlignRight().Text(document.SenderDepartment);

            if (!string.IsNullOrWhiteSpace(document.Footer))
                col.Item().AlignRight().Text(document.Footer).FontSize(10);

            col.Item().PaddingTop(48).Height(72).Border(1).BorderColor(Colors.Grey.Lighten2);
        });
    }

    private static void RenderBodyLines(ColumnDescriptor col, string[] bodyLines)
    {
        foreach (var line in bodyLines)
        {
            if (string.IsNullOrWhiteSpace(line))
                col.Item().Height(8);
            else
                col.Item().AlignRight().Text(line);
        }
    }

    private static void RenderFooter(IContainer container)
    {
        container.AlignCenter().Text(text =>
        {
            text.Span("صفحة ");
            text.CurrentPageNumber();
            text.Span(" / ");
            text.TotalPages();
        });
    }
}
