using System.Text.RegularExpressions;

namespace Uqeb.Api.Helpers;

public static class LetterTemplateRenderer
{
    public static string Render(
        string template,
        string? incomingNumber,
        DateTime? incomingDate,
        string? subject,
        string? targetEntity,
        DateTime today)
    {
        var number = incomingNumber?.Trim() ?? "";
        var dateStr = incomingDate.HasValue ? incomingDate.Value.ToString("yyyy-MM-dd") : "";
        var subjectText = subject?.Trim() ?? "";
        var target = targetEntity?.Trim() ?? "";
        var todayStr = today.ToString("yyyy-MM-dd");

        var content = template
            .Replace("{IncomingNumber}", number)
            .Replace("{IncomingDate}", dateStr)
            .Replace("{Subject}", subjectText)
            .Replace("{TargetEntity}", target)
            .Replace("{TodayDate}", todayStr);

        return CleanupEmptyReferencePatterns(content);
    }

    internal static string CleanupEmptyReferencePatterns(string content)
    {
        content = Regex.Replace(content, @"رقم\s+وتاريخ\s+", "", RegexOptions.Multiline);
        content = Regex.Replace(content, @"إشارةً إلى المعاملة رقم\s+وتاريخ\s+", "إشارةً إلى المعاملة ", RegexOptions.Multiline);
        content = Regex.Replace(content, @"إشارةً إلى المعاملة رقم\s+بشأن:", "إشارةً إلى المعاملة بشأن:", RegexOptions.Multiline);
        content = Regex.Replace(content, @"إشارةً إلى المعاملة\s+وتاريخ\s+", "إشارةً إلى المعاملة بتاريخ ", RegexOptions.Multiline);
        content = Regex.Replace(content, @"[ \t]{2,}", " ");
        content = Regex.Replace(content, @"\n{3,}", "\n\n");
        return content.Trim();
    }
}
