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
        var ctx = new FollowUpLetterRenderContext
        {
            TransactionId = 0,
            IncomingNumber = incomingNumber,
            IncomingDateLocal = incomingDate,
            Subject = subject,
            TargetEntity = targetEntity,
            TodayLocal = today,
            FollowUpSequence = 1,
            FollowUpSequenceText = FollowUpSequenceCalculator.ToArabicText(1),
        };

        return FollowUpLetterVariableReplacer.Render(template, FollowUpLetterVariableReplacer.BuildValues(ctx));
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
