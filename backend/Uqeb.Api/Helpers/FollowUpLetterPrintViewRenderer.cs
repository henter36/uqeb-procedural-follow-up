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

        // Optional footer
        var footerContent = string.IsNullOrWhiteSpace(document.Footer)
            ? string.Empty
            : $"<footer class=\"letter-footer\">{WebUtility.HtmlEncode(document.Footer)}</footer>";

        // Signature: simple block without the large empty rectangle
        var signatoryName = !string.IsNullOrWhiteSpace(document.SignatoryName)
            ? $"<div class=\"signatory-name\">{WebUtility.HtmlEncode(document.SignatoryName)}</div>"
            : string.Empty;
        var signatoryTitle = !string.IsNullOrWhiteSpace(document.SignatoryTitle)
            ? $"<div class=\"signatory-title\">{WebUtility.HtmlEncode(document.SignatoryTitle)}</div>"
            : string.Empty;

        // Formal organization header (right side of header)
        var orgName = !string.IsNullOrWhiteSpace(document.OrganizationName)
            ? $"<div class=\"brand-dept\">{WebUtility.HtmlEncode(document.OrganizationName)}</div>"
            : string.Empty;

        // Hijri date row is optional
        var hijriRow = !string.IsNullOrWhiteSpace(document.HijriDate)
            ? $"<div><span>التاريخ الهجري</span><strong>{WebUtility.HtmlEncode(document.HijriDate)}</strong></div>"
            : string.Empty;

        return $"""
            <article class="official-letter">
              <header class="letter-header">
                <div class="brand">
                  {logo}
                  <div class="brand-text">
                    <div class="brand-kingdom">المملكة العربية السعودية</div>
                    <div class="brand-ministry">وزارة الداخلية</div>
                    <div class="brand-org">المديرية العامة للسجون</div>
                    {orgName}
                  </div>
                </div>
                <div class="meta-panel">
                  <div><span>رقم الخطاب</span><strong>{WebUtility.HtmlEncode(document.LetterNumber)}</strong></div>
                  <div><span>التاريخ الميلادي</span><strong>{WebUtility.HtmlEncode(document.GregorianDate)}</strong></div>
                  {hijriRow}
                </div>
              </header>
              <section class="letter-recipient block">
                <div class="block-label">المخاطب</div>
                <div class="block-value">{WebUtility.HtmlEncode(document.Recipient)}</div>
              </section>
              <section class="letter-subject block">
                <div class="block-label">الموضوع</div>
                <div class="block-value">{WebUtility.HtmlEncode(document.Subject)}</div>
              </section>
              <section class="letter-body">
                {string.Concat(lines)}
              </section>
              <footer class="letter-signature">
                <div class="signature-label">التوقيع</div>
                <div class="signature-line" aria-hidden="true"></div>
                {signatoryName}
                {signatoryTitle}
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
        ".official-letter{width:210mm;min-height:297mm;margin:0 auto 18px;background:#fff;border:1.5px solid var(--uqeb-green);outline:5px solid rgba(179,139,46,.16);padding:18mm 17mm 16mm;break-after:page;page-break-after:always;position:relative;}" +
        ".official-letter:last-child{break-after:auto;page-break-after:auto;}" +
        // Header: logo + org hierarchy on right, meta panel on left
        ".letter-header{display:flex;align-items:flex-start;justify-content:space-between;gap:14mm;border-bottom:2px solid var(--uqeb-green);padding-bottom:8mm;margin-bottom:9mm;break-inside:avoid;page-break-inside:avoid;}" +
        ".brand{display:flex;align-items:flex-start;gap:10px;min-width:50%;}" +
        ".logo{width:80px;max-width:80px;max-height:80px;object-fit: contain;flex-shrink:0;}" +
        ".brand-text{display:flex;flex-direction:column;gap:1px;}" +
        ".brand-kingdom{font-size:13px;font-weight:700;color:var(--uqeb-ink);line-height:1.7;}" +
        ".brand-ministry{font-size:12px;color:var(--uqeb-muted);line-height:1.7;}" +
        ".brand-org{font-size:12px;color:var(--uqeb-muted);line-height:1.7;}" +
        ".brand-dept{font-size:13px;font-weight:700;color:var(--uqeb-green);line-height:1.7;margin-top:3px;}" +
        ".meta-panel{min-width:62mm;border:1px solid var(--uqeb-line);border-top:4px solid var(--uqeb-gold);padding:9px 12px;background:#fbfcfb;}" +
        ".meta-panel div{display:flex;justify-content:space-between;gap:12px;font-size:12px;line-height:1.8;border-bottom:1px solid #edf0ec;}" +
        ".meta-panel div:last-child{border-bottom:0;}" +
        ".meta-panel span{color:var(--uqeb-muted);}" +
        ".meta-panel strong{font-weight:700;color:var(--uqeb-ink);text-align:left;}" +
        ".letter-title{font-size:19px;text-align:center;color:var(--uqeb-green);margin:0 0 8mm;}" +
        ".block{display:grid;grid-template-columns:28mm 1fr;border:1px solid var(--uqeb-line);margin-bottom:5mm;break-inside:avoid;page-break-inside:avoid;}" +
        ".block-label{background:#f5f6f1;color:var(--uqeb-green);font-weight:700;padding:10px 12px;border-left:1px solid var(--uqeb-line);}" +
        ".block-value{padding:10px 12px;line-height:1.8;}" +
        ".letter-body{font-size:16px;line-height:2.05;margin:10mm 0 14mm;}" +
        ".letter-paragraph{margin:0 0 4.5mm;white-space:pre-wrap;}" +
        ".letter-paragraph.spacer{height:4mm;margin:0;}" +
        // Signature: simple block with just a thin line (no large empty rectangle)
        ".letter-signature{width:72mm;margin-right:auto;text-align:center;border-top:1.5px solid var(--uqeb-ink);padding-top:5mm;margin-top:18mm;break-inside:avoid;page-break-inside:avoid;}" +
        ".signature-label{color:var(--uqeb-muted);font-size:11px;margin-bottom:2mm;}" +
        ".signature-line{display:none;}" +
        ".signatory-name{font-weight:700;font-size:14px;}" +
        ".signatory-title{color:var(--uqeb-muted);font-size:12px;margin-top:2px;}" +
        ".letter-footer{position:absolute;left:17mm;right:17mm;bottom:9mm;border-top:1px solid var(--uqeb-line);padding-top:4mm;color:var(--uqeb-muted);font-size:11px;text-align:center;}" +
        ".page-break{break-before:page;page-break-before:always;}" +
        ".no-print{display:none!important;}" +
        "@media screen{body{padding:18px;}.official-letter{box-shadow:0 16px 40px rgba(0,0,0,.10);}}" +
        "@media print{body{background:#fff;padding:0;}.official-letter{width:auto;min-height:265mm;margin:0;border:1.5px solid var(--uqeb-green);outline:0;box-shadow:none;}.no-print{display:none!important;}}";
}
