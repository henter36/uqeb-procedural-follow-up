using System.Text.RegularExpressions;

namespace Uqeb.Api.Helpers;

public static partial class FollowUpLetterPlaceholderRegex
{
    public const int MatchTimeoutMilliseconds = 200;

    [GeneratedRegex(@"\{([A-Za-z0-9_]+)\}", RegexOptions.NonBacktracking | RegexOptions.CultureInvariant, MatchTimeoutMilliseconds)]
    private static partial Regex PlaceholderPattern();

    public static MatchCollection MatchAll(string content) => PlaceholderPattern().Matches(content);

    public static string RemoveRemaining(string content) => PlaceholderPattern().Replace(content, string.Empty);

    public static string Replace(string content, MatchEvaluator evaluator) => PlaceholderPattern().Replace(content, evaluator);
}
