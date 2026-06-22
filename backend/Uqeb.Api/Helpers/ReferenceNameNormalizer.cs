using System.Globalization;
using System.Text.RegularExpressions;

namespace Uqeb.Api.Helpers;

public static partial class ReferenceNameNormalizer
{
    public static string NormalizeKey(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var collapsed = WhitespaceRegex().Replace(value.Trim(), " ");
        return collapsed.ToLower(CultureInfo.InvariantCulture);
    }

    public static string FormatDisplayName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        return WhitespaceRegex().Replace(value.Trim(), " ");
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();
}
