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
            : $"<footer class=\"letter-print-meta\">{WebUtility.HtmlEncode(document.Footer)}</footer>";

        var title = string.IsNullOrWhiteSpace(document.Title)
            ? string.Empty
            : $"<h1 class=\"letter-title\">{WebUtility.HtmlEncode(document.Title)}</h1>";

        var orgName = !string.IsNullOrWhiteSpace(document.OrganizationName)
            ? $"<div class=\"org-sub-text\">{WebUtility.HtmlEncode(document.OrganizationName)}</div>"
            : string.Empty;

        var signatoryPosition = !string.IsNullOrWhiteSpace(document.SignatoryTitle)
            ? $"<div class=\"signature-title\">{WebUtility.HtmlEncode(document.SignatoryTitle)}</div>"
            : string.Empty;
        var signatoryRank = !string.IsNullOrWhiteSpace(document.SignatoryRank)
            ? $"<div class=\"signature-rank\">{WebUtility.HtmlEncode(document.SignatoryRank)}</div>"
            : string.Empty;
        var signatoryName = !string.IsNullOrWhiteSpace(document.SignatoryName)
            ? $"<div class=\"signature-name\">{WebUtility.HtmlEncode(document.SignatoryName)}</div>"
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
              <div class="letter-frame" aria-hidden="true"></div>
              <div class="letter-content">
                {title}
                <section class="letter-body">
                  {string.Concat(lines)}
                </section>
                <section class="letter-signature">
                  <div class="signature-box">
                    {signatoryPosition}
                    {signatoryRank}
                    {signatoryName}
                  </div>
                </section>
              </div>
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
               "<style id=\"uqeb-official-letter-css\">" + OfficialLetterCss + "</style>" +
               "</head>" +
               $"<body>{content}</body>" +
               "</html>";
    }

    public const string OfficialLetterCss =
        "@page{size:A4;margin:16mm 18mm;}" +
        ":root{--uqeb-green:#123f2f;--uqeb-gold:#b38b2e;--uqeb-ink:#17201c;--uqeb-muted:#66736d;--uqeb-line:#d9ded8;}" +
        "*{box-sizing:border-box;}" +
        "html,body{margin:0;padding:0;direction:rtl;text-align:right;}" +
        "body{font-family:Tahoma,'Segoe UI',Arial,sans-serif;color:var(--uqeb-ink);background:#f4f6f4;}" +
        // Fixed A4 height; margin moved to screen-only rule so print never inherits 18px bottom.
        // overflow:hidden clips any content that overruns the fixed page boundary.
        // No break-after on the article itself — page breaks are handled by .page-break only,
        // so the last letter never generates a trailing blank page.
        ".official-letter{width:210mm;height:297mm;margin:0 auto;background:#fff;position:relative;padding:10mm 14mm 0;overflow:hidden;text-align:right;}" +
        // No border-bottom here: the frame's top border at 48mm IS the separator line.
        ".letter-header{position:relative;z-index:2;display:grid;grid-template-columns:1fr 42mm 1fr;align-items:start;padding-bottom:4mm;margin-bottom:0;break-inside:avoid;page-break-inside:avoid;}" +
        // Decorative frame: top = padding-top(10mm) + logo(34mm) + header-pb(4mm) = 48mm,
        // aligning the frame's top border exactly with the header's bottom edge.
        ".letter-frame{position:absolute;top:48mm;right:8mm;left:8mm;bottom:14mm;border:1.5px solid var(--uqeb-green);box-sizing:border-box;pointer-events:none;z-index:1;}" +
        ".letter-content{position:relative;z-index:2;padding:8mm 4mm 0;}" +
        ".header-identity{font-size:13px;line-height:1.8;color:var(--uqeb-ink);}" +
        ".kingdom-text,.ministry-text{font-weight:700;}" +
        ".org-text{color:var(--uqeb-green);font-weight:700;}" +
        ".org-sub-text{color:var(--uqeb-muted);font-size:12px;}" +
        ".header-logo{display:flex;justify-content:center;}" +
        ".logo{width:34mm;height:34mm;max-width:34mm;max-height:34mm;object-fit:contain;}" +
        ".header-empty{}" +
        ".letter-title{font-size:19px;text-align:center;color:var(--uqeb-green);margin:0 0 6mm;}" +
        ".letter-body{font-size:15px;line-height:1.55;margin:4mm 0 7mm;text-align:right;}" +
        ".letter-paragraph{margin:0 0 1.8mm;white-space:pre-wrap;text-align:right;}" +
        ".letter-paragraph.spacer{height:2mm;margin:0;}" +
        ".letter-signature{width:70mm;margin-right:auto;margin-left:0;margin-top:10mm;text-align:right;break-inside:avoid;page-break-inside:avoid;}" +
        ".signature-box{display:inline-grid;grid-template-columns:1fr;width:max-content;max-width:70mm;direction:rtl;row-gap:1.5mm;}" +
        ".signature-title{font-weight:700;font-size:14px;line-height:1.6;white-space:nowrap;justify-self:stretch;text-align:right;}" +
        ".signature-rank{font-weight:700;font-size:14px;line-height:1.6;white-space:nowrap;justify-self:start;text-align:right;}" +
        ".signature-name{font-weight:700;font-size:14px;line-height:1.6;white-space:nowrap;justify-self:center;text-align:center;}" +
        ".letter-print-meta{position:absolute;right:10mm;left:10mm;bottom:6mm;font-size:10px;color:#555;line-height:1.4;text-align:center;z-index:2;}" +
        ".page-break{break-before:page;page-break-before:always;}" +
        ".no-print{display:none!important;}" +
        "@media screen{body{padding:18px;}.official-letter{margin-bottom:18px;box-shadow:0 16px 40px rgba(0,0,0,.10);}}" +
        // Print: fill printable area exactly — A4(297mm) - top(16mm) - bottom(16mm) = 265mm.
        // margin:0 and overflow:hidden prevent any blank trailing page.
        "@media print{body{background:#fff;padding:0;margin:0;}.official-letter{width:auto;height:265mm;margin:0;overflow:hidden;box-shadow:none;}.no-print{display:none!important;}}";
}
