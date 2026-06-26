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

        var title = string.IsNullOrWhiteSpace(document.Title)
            ? string.Empty
            : $"<h1 class=\"letter-title\">{WebUtility.HtmlEncode(document.Title)}</h1>";

        return $"""
            <article class="official-letter">
              <header class="letter-header">
                <div class="brand">
                  {logo}
                  <div class="brand-name">{WebUtility.HtmlEncode(document.OrganizationName)}</div>
                </div>
                <div class="meta-panel">
                  <div><span>رقم الخطاب</span><strong>{WebUtility.HtmlEncode(document.LetterNumber)}</strong></div>
                  <div><span>التاريخ الميلادي</span><strong>{WebUtility.HtmlEncode(document.GregorianDate)}</strong></div>
                  <div><span>التاريخ الهجري</span><strong>{WebUtility.HtmlEncode(document.HijriDate)}</strong></div>
                </div>
              </header>
              {title}
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
                <div class="signature-space" aria-hidden="true"></div>
                <div class="signatory-name">{WebUtility.HtmlEncode(document.SignatoryName)}</div>
                <div class="signatory-title">{WebUtility.HtmlEncode(document.SignatoryTitle)}</div>
              </footer>
              <footer class="letter-footer">
                {WebUtility.HtmlEncode(document.Footer)}
              </footer>
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
        ".letter-header{display:flex;align-items:flex-start;justify-content:space-between;gap:18mm;border-bottom:2px solid var(--uqeb-green);padding-bottom:8mm;margin-bottom:9mm;break-inside:avoid;page-break-inside:avoid;}" +
        ".brand{display:flex;align-items:center;gap:12px;min-width:45%;}" +
        ".logo{width:120px;max-width:120px;max-height:64px;object-fit:contain;}" +
        ".brand-name{font-size:17px;font-weight:700;color:var(--uqeb-green);line-height:1.6;}" +
        ".meta-panel{min-width:68mm;border:1px solid var(--uqeb-line);border-top:4px solid var(--uqeb-gold);padding:9px 12px;background:#fbfcfb;}" +
        ".meta-panel div{display:flex;justify-content:space-between;gap:16px;font-size:12px;line-height:1.8;border-bottom:1px solid #edf0ec;}" +
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
        ".letter-signature{width:72mm;margin-right:auto;text-align:center;break-inside:avoid;page-break-inside:avoid;}" +
        ".signature-label{color:var(--uqeb-muted);font-size:12px;margin-bottom:3mm;}" +
        ".signature-space{height:24mm;border-bottom:1px solid var(--uqeb-line);margin-bottom:4mm;}" +
        ".signatory-name{font-weight:700;min-height:8mm;}" +
        ".signatory-title{color:var(--uqeb-muted);font-size:13px;min-height:7mm;}" +
        ".letter-footer{position:absolute;left:17mm;right:17mm;bottom:9mm;border-top:1px solid var(--uqeb-line);padding-top:4mm;color:var(--uqeb-muted);font-size:11px;text-align:center;}" +
        ".page-break{break-before:page;page-break-before:always;}" +
        ".no-print{display:none!important;}" +
        "@media screen{body{padding:18px;}.official-letter{box-shadow:0 16px 40px rgba(0,0,0,.10);}}" +
        "@media print{body{background:#fff;padding:0;}.official-letter{width:auto;min-height:265mm;margin:0;border:1.5px solid var(--uqeb-green);outline:0;box-shadow:none;}.no-print{display:none!important;}}";
}
