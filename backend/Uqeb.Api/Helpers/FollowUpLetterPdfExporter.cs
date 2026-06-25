using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using Uqeb.Api.Configuration;
using Uqeb.Api.Models.Letters;

namespace Uqeb.Api.Helpers;

public interface IFollowUpLetterPdfExporter
{
    byte[] GeneratePdf(FollowUpLetterDocumentModel document);
}

public sealed class FollowUpLetterPdfExporter : IFollowUpLetterPdfExporter
{
    private const string FontFamily = "Tahoma";
    private readonly OrganizationBrandingOptions _branding;
    private readonly IWebHostEnvironment _environment;
    private readonly ILogger<FollowUpLetterPdfExporter> _logger;

    static FollowUpLetterPdfExporter()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public FollowUpLetterPdfExporter(
        IOptions<OrganizationBrandingOptions> branding,
        IWebHostEnvironment environment,
        ILogger<FollowUpLetterPdfExporter> logger)
    {
        _branding = branding.Value;
        _environment = environment;
        _logger = logger;
    }

    public byte[] GeneratePdf(FollowUpLetterDocumentModel document)
    {
        var logoBytes = TryLoadLogoBytes(document.LogoPath);
        var lines = document.Body.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');

        return Document.Create(doc =>
        {
            doc.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(40);
                page.ContentFromRightToLeft();
                page.DefaultTextStyle(x => x.FontFamily(FontFamily).FontSize(12));

                page.Header().Column(header =>
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
                        {
                            row.ConstantItem(120).AlignLeft().Image(logoBytes).FitArea();
                        }
                    });

                    header.Item().PaddingTop(8).AlignRight().Text($"إلى: {document.Recipient}").SemiBold();
                    header.Item().AlignRight().Text($"الموضوع: {document.Subject}").SemiBold();
                    header.Item().AlignRight().Text(document.FollowUpSequenceText).FontSize(10);
                    header.Item().PaddingTop(8);
                });

                page.Content().Column(col =>
                {
                    col.Spacing(4);
                    foreach (var line in lines)
                    {
                        if (string.IsNullOrWhiteSpace(line))
                            col.Item().Height(8);
                        else
                            col.Item().AlignRight().Text(line);
                    }

                    if (!string.IsNullOrWhiteSpace(document.SenderDepartment))
                    {
                        col.Item().PaddingTop(24).AlignRight().Text(document.SenderDepartment);
                    }

                    if (!string.IsNullOrWhiteSpace(document.Footer))
                    {
                        col.Item().AlignRight().Text(document.Footer).FontSize(10);
                    }

                    col.Item().PaddingTop(48).Height(72).Border(1).BorderColor(Colors.Grey.Lighten2);
                });

                page.Footer().AlignCenter().Text(text =>
                {
                    text.Span("صفحة ");
                    text.CurrentPageNumber();
                    text.Span(" / ");
                    text.TotalPages();
                });
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
                    foreach (var line in body.Replace("\r\n", "\n").Split('\n'))
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

    private byte[]? TryLoadLogoBytes(string? explicitLogoPath)
    {
        var candidates = new List<string>();
        if (!string.IsNullOrWhiteSpace(explicitLogoPath))
            candidates.Add(explicitLogoPath);
        if (!string.IsNullOrWhiteSpace(_branding.LogoPath))
            candidates.Add(_branding.LogoPath);

        foreach (var candidate in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var path = Path.IsPathRooted(candidate)
                ? candidate
                : Path.Combine(_environment.ContentRootPath, candidate.Replace('/', Path.DirectorySeparatorChar));

            if (!File.Exists(path))
                continue;

            try
            {
                return File.ReadAllBytes(path);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "تعذر تحميل شعار المؤسسة من {LogoPath}", path);
            }
        }

        if (candidates.Count > 0)
            _logger.LogWarning("لم يتم العثور على شعار المؤسسة في المسارات المحددة.");

        return null;
    }
}
