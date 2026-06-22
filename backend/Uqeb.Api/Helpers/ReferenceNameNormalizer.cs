using System.Globalization;
using System.Text.RegularExpressions;

namespace Uqeb.Api.Helpers;

public static partial class ReferenceNameNormalizer
{
    public const string EmptyNameMessage = "الاسم مطلوب";
    public const string EmptyUsernameMessage = "اسم المستخدم مطلوب";

    public static string NormalizeKey(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var collapsed = WhitespaceRegex().Replace(value.Trim(), " ");
        return collapsed.ToLower(CultureInfo.InvariantCulture);
    }

    public static string FormatDisplayName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        return WhitespaceRegex().Replace(value.Trim(), " ");
    }

    public static string RequireDisplayName(string? value, string emptyMessage = EmptyNameMessage)
    {
        var formatted = FormatDisplayName(value);
        if (string.IsNullOrEmpty(formatted))
            throw new EmptyReferenceNameException(emptyMessage);

        return formatted;
    }

    public static string RequireNormalizedKey(string? value, string emptyMessage = EmptyNameMessage)
    {
        var display = RequireDisplayName(value, emptyMessage);
        return NormalizeKey(display);
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();
}
