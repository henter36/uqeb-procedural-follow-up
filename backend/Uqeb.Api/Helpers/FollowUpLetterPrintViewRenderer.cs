using System.Net;
using System.Text;
using Uqeb.Api.Configuration;
using Uqeb.Api.Models.Letters;

namespace Uqeb.Api.Helpers;

public static class FollowUpLetterPrintViewRenderer
{
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

        var rawBody = document.Body;
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
            ? "<p class=\"spacer\">&nbsp;</p>"
            : $"<p>{WebUtility.HtmlEncode(line)}</p>");

        return $"""
            <article class="letter">
              <header class="letter-header">
                {logo}
                <div class="meta">
                  <h1>{WebUtility.HtmlEncode(document.Title)}</h1>
                  <p>التاريخ: {WebUtility.HtmlEncode(document.GregorianDate)}</p>
                  <p>التاريخ الهجري: {WebUtility.HtmlEncode(document.HijriDate)}</p>
                  <p>رقم الخطاب: {WebUtility.HtmlEncode(document.LetterNumber)}</p>
                </div>
              </header>
              <section class="letter-recipient">
                <p><strong>إلى:</strong> {WebUtility.HtmlEncode(document.Recipient)}</p>
                <p><strong>الموضوع:</strong> {WebUtility.HtmlEncode(document.Subject)}</p>
                <p><strong>{WebUtility.HtmlEncode(document.FollowUpSequenceText)}</strong></p>
              </section>
              <section class="letter-body">
                {string.Concat(lines)}
              </section>
              <footer class="letter-footer">
                <p>{WebUtility.HtmlEncode(document.SenderDepartment)}</p>
                <p>{WebUtility.HtmlEncode(document.Footer)}</p>
                <div class="signature-space" aria-hidden="true"></div>
              </footer>
            </article>
            """;
    }

    // Overload kept for call sites that provide documents without options (defaults to safe limits).
    public static string Render(IReadOnlyList<FollowUpLetterDocumentModel> documents, string? title = null)
        => Render(documents, new FollowUpLettersOptions(), title);

    private static string BuildShell(string title, string content)
    {
        return "<!DOCTYPE html>" +
               "<html lang=\"ar\" dir=\"rtl\">" +
               "<head>" +
               "<meta charset=\"utf-8\" />" +
               "<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\" />" +
               $"<title>{WebUtility.HtmlEncode(title)}</title>" +
               "<style>" +
               "@page { size: A4; margin: 18mm; }" +
               "body { font-family: Tahoma, Arial, sans-serif; color: #111; margin: 0; }" +
               ".letter { page-break-inside: avoid; }" +
               ".page-break { page-break-before: always; break-before: page; }" +
               ".letter-header { display: flex; justify-content: space-between; align-items: flex-start; gap: 16px; margin-bottom: 24px; }" +
               ".logo { max-width: 120px; max-height: 60px; object-fit: contain; }" +
               ".meta h1 { margin: 0 0 8px; font-size: 20px; }" +
               ".meta p { margin: 2px 0; font-size: 12px; }" +
               ".letter-recipient p { margin: 6px 0; }" +
               ".letter-body p { margin: 0 0 8px; line-height: 1.8; white-space: pre-wrap; }" +
               ".letter-body .spacer { height: 8px; }" +
               ".letter-footer { margin-top: 32px; }" +
               ".signature-space { height: 80px; border-top: 1px solid #ccc; margin-top: 48px; }" +
               "@media print { .page-break { page-break-before: always; } }" +
               "</style>" +
               "</head>" +
               $"<body>{content}</body>" +
               "</html>";
    }
}
