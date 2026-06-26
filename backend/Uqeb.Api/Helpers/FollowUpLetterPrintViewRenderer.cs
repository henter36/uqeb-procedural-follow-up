using System.Net;
using System.Text;
using Uqeb.Api.Configuration;
using Uqeb.Api.Models.Letters;

namespace Uqeb.Api.Helpers;

public static class FollowUpLetterPrintViewRenderer
{
    // Overload kept for call sites that provide documents without options (defaults to safe limits).
    public static string Render(IReadOnlyList<FollowUpLetterDocumentModel> documents, string? title = null)
        => Render(documents, new FollowUpLettersOptions(), title);

    public static string Render(
        IReadOnlyList<FollowUpLetterDocumentModel> documents,
        FollowUpLettersOptions options,
        string? title = null)
    {
        if (documents.Count > options.MaxLettersPerPrintView)
            throw new ArgumentException(
                $"عدد الخطابات ({documents.Count}) يتجاوز الحد الأقصى المسموح به ({options.MaxLettersPerPrintView}) لعرض الطباعة.");

        if (documents.Count == 0)
            return BuildShell(title ?? "طباعة خطابات التعقيب", string.Empty);

        var body = new StringBuilder();
        for (var i = 0; i < documents.Count; i++)
        {
            if (i > 0)
                body.Append("<div class=\"page-break\"></div>");

            body.Append(RenderDocument(documents[i], options.MaxBodyLinesPerLetter, options.MaxBodyCharactersPerLetter));
        }

        return BuildShell(title ?? "طباعة خطابات التعقيب", body.ToString());
    }

    private static string RenderDocument(FollowUpLetterDocumentModel document, int maxBodyLines, int maxBodyChars)
    {
        var logo = $"<img class=\"logo\" src=\"{OrganizationBrandingPaths.LogoApiUrl}\" alt=\"\" />";

        var rawBody = document.Body ?? string.Empty;
        if (rawBody.Length > maxBodyChars)
            throw new ArgumentException(
                $"نص الخطاب يتجاوز الحد الأقصى ({maxBodyChars} حرف).");

        var bodyLines = rawBody
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split('\n');

        if (bodyLines.Length > maxBodyLines)
            throw new ArgumentException(
                $"عدد أسطر الخطاب ({bodyLines.Length}) يتجاوز الحد الأقصى ({maxBodyLines} سطر).");

        var lines = bodyLines.Select(line => string.IsNullOrWhiteSpace(line)
            ? "<p class=\"letter-paragraph spacer\">&nbsp;</p>"
            : $"<p class=\"letter-paragraph\">{WebUtility.HtmlEncode(line)}</p>");

        var footerContent = string.IsNullOrWhiteSpace(document.Footer)
            ? string.Empty
            : $"<footer class=\"letter-footer\">{WebUtility.HtmlEncode(document.Footer)}</footer>";

        var title = string.IsNullOrWhiteSpace(document.Title)
            ? string.Empty
            : $"<h1 class=\"letter-title\">{WebUtility.HtmlEncode(document.Title)}</h1>";

        var orgName = !string.IsNullOrWhiteSpace(document.OrganizationName)
            ? $"<div class=\"org-sub-text\">{WebUtility.HtmlEncode(document.OrganizationName)}</div>"
            : string.Empty;

        var signatoryPosition = !string.IsNullOrWhiteSpace(document.SignatoryTitle)
            ? $"<div class=\"signatory-position\">{WebUtility.HtmlEncode(document.SignatoryTitle)}</div>"
            : string.Empty;
        var signatoryRank = !string.IsNullOrWhiteSpace(document.SignatoryRank)
            ? $"<div class=\"signatory-rank\">{WebUtility.HtmlEncode(document.SignatoryRank)}</div>"
            : string.Empty;
        var signatoryName = !string.IsNullOrWhiteSpace(document.SignatoryName)
            ? $"<div class=\"signatory-name\">{WebUtility.HtmlEncode(document.SignatoryName)}</div>"
            : string.Empty;

        return $"""
            <article class="official-letter">
              <header class="letter-header">
                <div class="header-identity">
                  <div class="kingdom-text">المملكة العربية السعودية</div>
                  <div class="ministry-text">وزارة الداخلية</div>
                  <div class="org-text">المديرية العامة للسجون</div>
                  {orgName}
                </div>
                <div class="header-logo">
                  {logo}
                </div>
                <div class="header-empty"></div>
              </header>
              {title}
              <section class="letter-salutation">
                <p class="salutation-line">سعادة/ {WebUtility.HtmlEncode(document.Recipient)}</p>
                <p class="salutation-greeting">السلام عليكم ورحمة الله وبركاته،،</p>
              </section>
              <p class="letter-subject-line"><strong>الموضوع: {WebUtility.HtmlEncode(document.Subject)}</strong></p>
              <section class="letter-body">
                {string.Concat(lines)}
              </section>
              <footer class="letter-signature">
                {signatoryPosition}
                {signatoryRank}
                {signatoryName}
              </footer>
              {footerContent}
            </article>
            """;
    }

    private static string BuildShell(string title, string content)
    {
        return "<!DOCTYPE html>" +
               "<html lang=\"ar\" dir=\"rtl\">" +
               "<head>" +
               "<meta charset=\"utf-8\" />" +
               "<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\" />" +
               $"<title>{WebUtility.HtmlEncode(title)}</title>" +
               "<style>" + OfficialLetterCss + "</style>" +
               "</head>" +
               $"<body>{content}</body>" +
               "</html>";
    }

    public const string OfficialLetterCss =
        "@page{size:A4;margin:16mm 18mm;}" +
        ":root{--uqeb-green:#123f2f;--uqeb-gold:#b38b2e;--uqeb-ink:#17201c;--uqeb-muted:#66736d;--uqeb-line:#d9ded8;}" +
        "*{box-sizing:border-box;}" +
        "html,body{margin:0;padding:0;direction:rtl;}" +
        "body{font-family:Tahoma,'Segoe UI',Arial,sans-serif;color:var(--uqeb-ink);background:#f4f6f4;}" +
        ".official-letter{width:210mm;min-height:297mm;margin:0 auto 18px;background:#fff;border:1.5px solid var(--uqeb-green);outline:5px solid rgba(179,139,46,.16);padding:14mm 17mm 14mm;break-after:page;page-break-after:always;position:relative;}" +
        ".official-letter:last-child{break-after:auto;page-break-after:auto;}" +
        ".letter-header{display:grid;grid-template-columns:1fr auto 1fr;align-items:center;border-bottom:2px solid var(--uqeb-green);padding-bottom:5mm;margin-bottom:5mm;break-inside:avoid;page-break-inside:avoid;}" +
        ".header-identity{font-size:13px;line-height:1.8;color:var(--uqeb-ink);}" +
        ".kingdom-text,.ministry-text{font-weight:700;}" +
        ".org-text{color:var(--uqeb-green);font-weight:700;}" +
        ".org-sub-text{color:var(--uqeb-muted);font-size:12px;}" +
        ".header-logo{display:flex;justify-content:center;}" +
        ".logo{width:80px;max-width:80px;max-height:64px;object-fit: contain;}" +
        ".header-empty{}" +
        ".letter-title{font-size:19px;text-align:center;color:var(--uqeb-green);margin:0 0 6mm;}" +
        ".letter-salutation{margin:4mm 0;}" +
        ".salutation-line{font-weight:700;margin:0 0 2mm;}" +
        ".salutation-greeting{margin:0 0 3mm;}" +
        ".letter-subject-line{margin:0 0 4mm;}" +
        ".letter-body{font-size:15px;line-height:1.7;margin:6mm 0 8mm;}" +
        ".letter-paragraph{margin:0 0 2.5mm;white-space:pre-wrap;}" +
        ".letter-paragraph.spacer{height:4mm;margin:0;}" +
        ".letter-signature{width:72mm;margin-right:auto;margin-top:12mm;text-align:center;break-inside:avoid;page-break-inside:avoid;}" +
        ".signatory-position{color:var(--uqeb-muted);font-size:13px;min-height:7mm;}" +
        ".signatory-rank{color:var(--uqeb-muted);font-size:13px;min-height:7mm;}" +
        ".signatory-name{font-weight:700;min-height:8mm;}" +
        ".letter-footer{position:absolute;left:17mm;right:17mm;bottom:9mm;border-top:1px solid var(--uqeb-line);padding-top:4mm;color:var(--uqeb-muted);font-size:11px;text-align:center;}" +
        ".page-break{break-before:page;page-break-before:always;}" +
        ".no-print{display:none!important;}" +
        "@media screen{body{padding:18px;}.official-letter{box-shadow:0 16px 40px rgba(0,0,0,.10);}}" +
        "@media print{body{background:#fff;padding:0;}.official-letter{width:auto;min-height:265mm;margin:0;border:1.5px solid var(--uqeb-green);outline:0;box-shadow:none;}.no-print{display:none!important;}}";
}
