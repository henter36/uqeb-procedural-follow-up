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
        var logoSrc = !string.IsNullOrWhiteSpace(document.LogoPath)
            ? document.LogoPath
            : OrganizationBrandingPaths.LogoApiUrl;
        var logoHtml = $"<img class=\"letter-logo\" src=\"{logoSrc}\" alt=\"الشعار\" />";

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
            ? $"<div class=\"official-letter-office\">{WebUtility.HtmlEncode(document.OrganizationName)}</div>"
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
              <header class="official-letter-header">
                <div class="official-letter-basmala">بسم الله الرحمن الرحيم</div>
                <div class="official-letter-logo-wrap">
                  {logoHtml}
                </div>
                <div class="official-letter-agency">
                  <div>المملكة العربية السعودية</div>
                  <div>وزارة الداخلية</div>
                  <div>المديرية العامة للسجون</div>
                  <div>(169)</div>
                  <div>مكتب المدير العام/ المتابعه الاجرائية</div>
                  {orgName}
                </div>
              </header>
              <div class="official-letter-frame" aria-hidden="true"></div>
              <div class="official-letter-body">
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

        public const string OfficialLetterCss = """
@page{size:A4 portrait;margin:0;}
:root{--uqeb-green:#123f2f;--uqeb-gold:#b38b2e;--uqeb-ink:#17201c;--uqeb-muted:#66736d;--uqeb-line:#d9ded8;}
*{box-sizing:border-box;}
html,body{margin:0;padding:0;direction:rtl;text-align:right;}
body{font-family:Tahoma,'Segoe UI',Arial,sans-serif;color:var(--uqeb-ink);background:#f4f6f4;}

.official-letter{width:210mm;min-height:297mm;margin:0 auto;background:#fff;position:relative;direction:rtl;text-align:right;}

.official-letter-header{position:relative;height:41mm;}

.official-letter-basmala{
  position:absolute;
  top:3.8mm;
  left:0;
  right:0;
  text-align:center;
  font-size:13pt;
  line-height:1.1;
  white-space:nowrap;
}

.official-letter-logo-wrap{
  position:absolute;
  top:9.4mm;
  left:0;
  right:0;
  display:flex;
  justify-content:center;
  align-items:flex-start;
}

.letter-logo{
  width:39mm;
  max-width:39mm;
  height:auto;
  max-height:26mm;
  object-fit:contain;
  display:block;
  print-color-adjust:exact;
  -webkit-print-color-adjust:exact;
}

.official-letter-agency{
  position:absolute;
  top:4.5mm;
  right:7.5mm;
  width:68mm;
  text-align:center;
  font-size:7.6pt;
  line-height:1.4;
}

.official-letter-agency>div{
  font-weight:700;
}

.official-letter-office{
  font-size:6.8pt;
  margin-top:.6mm;
  font-weight:600;
}

.official-letter-frame{
  position:absolute;
  top:37mm;
  right:8.0mm;
  left:8.3mm;
  bottom:10.2mm;
  border:0.75pt solid #111;
  border-radius:24mm;
  background:#fff;
  box-sizing:border-box;
  pointer-events:none;
  z-index:1;
  overflow:visible;
}

.official-letter-body{
  position:relative;
  z-index:2;
  padding:14mm 16mm 16mm 16mm;
  direction:rtl;
  text-align:right;
  box-sizing:border-box;
}

.letter-title{
  font-size:19px;
  text-align:center;
  color:var(--uqeb-green);
  margin:0 0 6mm;
}

.letter-body{
  font-size:17px;
  line-height:1.55;
  margin:4mm 0 7mm;
  text-align:right;
}

.letter-paragraph{
  margin:0 0 1.8mm;
  white-space:pre-wrap;
  text-align:right;
}

.letter-paragraph.spacer{
  height:2mm;
  margin:0;
}

.letter-signature{
  width:70mm;
  margin-right:auto;
  margin-left:0;
  margin-top:10mm;
  text-align:right;
  break-inside:avoid;
  page-break-inside:avoid;
}

.signature-box{
  display:inline-grid;
  grid-template-columns:1fr;
  width:max-content;
  max-width:70mm;
  direction:rtl;
  row-gap:1.5mm;
}

.signature-title{
  font-weight:700;
  font-size:14px;
  line-height:1.6;
  white-space:nowrap;
  justify-self:stretch;
  text-align:right;
}

.signature-rank{
  font-weight:700;
  font-size:14px;
  line-height:1.6;
  white-space:nowrap;
  justify-self:start;
  text-align:right;
}

.signature-name{
  font-weight:700;
  font-size:14px;
  line-height:1.6;
  white-space:nowrap;
  justify-self:center;
  text-align:center;
}

.letter-print-meta{
  position:absolute;
  right:10mm;
  left:10mm;
  bottom:6mm;
  font-size:10px;
  color:#555;
  line-height:1.4;
  text-align:center;
  z-index:2;
}

.page-break{
  break-before:page;
  page-break-before:always;
}

.no-print{
  display:none!important;
}

@media screen{
  body{padding:18px;}
  .official-letter{margin-bottom:18px;box-shadow:0 16px 40px rgba(0,0,0,.10);}
}

@media print{
  body{background:#fff;padding:0;margin:0;}
  .official-letter{width:210mm;min-height:297mm;margin:0;box-shadow:none;overflow:visible;}
  .official-letter-frame{overflow:visible;}
  .letter-logo{print-color-adjust:exact;-webkit-print-color-adjust:exact;}
  .no-print{display:none!important;}
}
""";
}