using System.Globalization;
using System.Text.RegularExpressions;
using Uqeb.Api.Models.Letters;

namespace Uqeb.Api.Helpers;

public static class FollowUpLetterVariableReplacer
{
    public static string Render(string template, IReadOnlyDictionary<string, string?> values)
    {
        var content = template;
        foreach (var pair in values)
        {
            var token = "{" + pair.Key + "}";
            content = content.Replace(token, pair.Value ?? string.Empty, StringComparison.Ordinal);
        }

        content = Regex.Replace(content, @"\{([A-Za-z0-9_]+)\}", string.Empty);
        return LetterTemplateRenderer.CleanupEmptyReferencePatterns(content);
    }

    public static Dictionary<string, string?> BuildValues(FollowUpLetterRenderContext ctx)
    {
        var today = ctx.TodayLocal;
        var incoming = ctx.IncomingDateLocal;
        var followUp = ctx.FollowUpDateLocal;
        var assignment = ctx.AssignmentDateLocal;
        var due = ctx.DueDateLocal;

        return new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["TransactionId"] = ctx.TransactionId.ToString(CultureInfo.InvariantCulture),
            ["TransactionNumber"] = ctx.TransactionId.ToString(CultureInfo.InvariantCulture),
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
