using System.Globalization;
using System.Text.RegularExpressions;
using Uqeb.Api.Models.Letters;

namespace Uqeb.Api.Helpers;

public static class FollowUpLetterVariableReplacer
{
    public static string Render(string template, IReadOnlyDictionary<string, string?> values)
    {
        var lookup = values is Dictionary<string, string?> dictionary &&
                     dictionary.Comparer.Equals(StringComparer.OrdinalIgnoreCase)
            ? values
            : new Dictionary<string, string?>(values, StringComparer.OrdinalIgnoreCase);

        try
        {
            var content = FollowUpLetterPlaceholderRegex.Replace(template, match =>
            {
                var name = match.Groups[1].Value;
                if (!FollowUpLetterVariableRegistry.IsKnown(name))
                    throw new InvalidOperationException($"متغيرات غير مدعومة في قالب الخطاب: {name}");

                return lookup.TryGetValue(name, out var value)
                    ? value ?? string.Empty
                    : match.Value;
            });

            return LetterTemplateRenderer.CleanupEmptyReferencePatterns(content);
        }
        catch (RegexMatchTimeoutException ex)
        {
            throw new InvalidOperationException("انتهت مهلة تصيير متغيرات القالب.", ex);
        }
    }

    public static Dictionary<string, string?> BuildValues(FollowUpLetterRenderContext ctx)
    {
        var today = ctx.TodayLocal;
        var incoming = ctx.IncomingDateLocal;
        var followUp = ctx.FollowUpDateLocal;
        var assignment = ctx.AssignmentDateLocal;
        var due = ctx.DueDateLocal;

        return new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["TransactionId"] = ctx.TransactionId.ToString(CultureInfo.InvariantCulture),
            ["TransactionNumber"] = string.IsNullOrWhiteSpace(ctx.TransactionNumber)
                ? ctx.TransactionId.ToString(CultureInfo.InvariantCulture)
                : ctx.TransactionNumber,
            ["IncomingNumber"] = ctx.IncomingNumber,
            ["IncomingDate"] = incoming.HasValue ? HijriDateFormatter.FormatGregorian(incoming.Value) : null,
            ["IncomingDateGregorian"] = incoming.HasValue ? HijriDateFormatter.FormatGregorianArabic(incoming.Value) : null,
            ["IncomingDateHijri"] = incoming.HasValue ? HijriDateFormatter.Format(incoming.Value) : null,
            ["Subject"] = ctx.Subject,
            ["TargetEntity"] = ctx.TargetEntity,
            ["TargetEntities"] = ctx.TargetEntities,
            ["TargetDepartments"] = ctx.TargetDepartments,
            ["AssignmentDate"] = assignment.HasValue ? HijriDateFormatter.FormatGregorian(assignment.Value) : null,
            ["DueDate"] = due.HasValue ? HijriDateFormatter.FormatGregorian(due.Value) : null,
            ["DaysOverdue"] = ctx.DaysOverdue?.ToString(CultureInfo.InvariantCulture),
            ["Priority"] = ctx.Priority,
            ["Category"] = ctx.Category,
            ["TodayDate"] = HijriDateFormatter.FormatGregorian(today),
            ["TodayDateGregorian"] = HijriDateFormatter.FormatGregorianArabic(today),
            ["TodayDateHijri"] = HijriDateFormatter.Format(today),
            ["SenderDepartment"] = ctx.SenderDepartment,
            ["PreparedBy"] = ctx.PreparedBy,
            ["FollowUpNumber"] = ctx.FollowUpNumber,
            ["FollowUpDate"] = followUp.HasValue ? HijriDateFormatter.FormatGregorian(followUp.Value) : null,
            ["FollowUpDateGregorian"] = followUp.HasValue ? HijriDateFormatter.FormatGregorianArabic(followUp.Value) : null,
            ["FollowUpDateHijri"] = followUp.HasValue ? HijriDateFormatter.Format(followUp.Value) : null,
            ["FollowUpSequence"] = ctx.FollowUpSequence.ToString(CultureInfo.InvariantCulture),
            ["FollowUpSequenceText"] = ctx.FollowUpSequenceText,
            ["ResponseDeadlineDays"] = ctx.ResponseDeadlineDays?.ToString(CultureInfo.InvariantCulture),
        };
    }
}

public sealed class FollowUpLetterRenderContext
{
    public int TransactionId { get; init; }
    public string? TransactionNumber { get; init; }
    public string? IncomingNumber { get; init; }
    public DateTime? IncomingDateLocal { get; init; }
    public string? Subject { get; init; }
    public string? TargetEntity { get; init; }
    public string? TargetEntities { get; init; }
    public string? TargetDepartments { get; init; }
    public DateTime? AssignmentDateLocal { get; init; }
    public DateTime? DueDateLocal { get; init; }
    public int? DaysOverdue { get; init; }
    public string? Priority { get; init; }
    public string? Category { get; init; }
    public DateTime TodayLocal { get; init; }
    public string? SenderDepartment { get; init; }
    public string? PreparedBy { get; init; }
    public string? FollowUpNumber { get; init; }
    public DateTime? FollowUpDateLocal { get; init; }
    public int FollowUpSequence { get; init; }
    public string FollowUpSequenceText { get; init; } = string.Empty;
    public int? ResponseDeadlineDays { get; init; }
}
