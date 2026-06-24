namespace Uqeb.Api.Reporting.Helpers;

public static class ReportPageRangeParser
{
    public sealed class ParseResult
    {
        public bool IsValid { get; init; }
        public string? ErrorMessage { get; init; }
        public List<int> PageNumbers { get; init; } = [];
    }

    public static ParseResult Parse(string? expression, int totalPages)
    {
        if (string.IsNullOrWhiteSpace(expression))
            return Invalid("يجب إدخال أرقام الصفحات.");

        if (totalPages <= 0)
            return Invalid("لا توجد صفحات في التقرير.");

        var pages = new SortedSet<int>();

        foreach (var token in SplitTokens(expression))
        {
            var tokenResult = ParseToken(token, totalPages);
            if (!tokenResult.IsValid)
                return tokenResult;

            foreach (var page in tokenResult.PageNumbers)
                pages.Add(page);
        }

        return pages.Count == 0
            ? Invalid("لم يتم تحديد صفحات.")
            : Success(pages);
    }

    private static IEnumerable<string> SplitTokens(string expression) =>
        expression.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static ParseResult ParseToken(string token, int totalPages) =>
        token.Contains('-', StringComparison.Ordinal)
            ? ParseRangeToken(token, totalPages)
            : ParseSinglePageToken(token, totalPages);

    private static ParseResult ParseSinglePageToken(string token, int totalPages)
    {
        if (!int.TryParse(token, out var page))
            return Invalid($"قيمة غير صالحة: {token}");

        var validation = ValidatePage(page, totalPages);
        return validation ?? Success([page]);
    }

    private static ParseResult ParseRangeToken(string token, int totalPages)
    {
        var parts = token.Split('-', StringSplitOptions.TrimEntries);
        if (parts.Length != 2)
            return Invalid($"النطاق غير صالح: {token}");

        if (!int.TryParse(parts[0], out var start) || !int.TryParse(parts[1], out var end))
            return Invalid($"النطاق يحتوي أحرفًا غير صالحة: {token}");

        var rangeValidation = ValidateRange(start, end, token, totalPages);
        if (rangeValidation is not null)
            return rangeValidation;

        var pages = new List<int>();
        for (var page = start; page <= end; page++)
            pages.Add(page);

        return Success(pages);
    }

    private static ParseResult? ValidateRange(int start, int end, string token, int totalPages)
    {
        if (start <= 0 || end <= 0)
            return Invalid("لا يُسمح بالصفحة صفر أو بالقيم السالبة.");

        if (start > end)
            return Invalid($"النطاق العكسي غير مسموح: {token}");

        if (end > totalPages)
            return Invalid($"الصفحة {end} أكبر من إجمالي الصفحات ({totalPages}).");

        return null;
    }

    private static ParseResult? ValidatePage(int page, int totalPages)
    {
        if (page <= 0)
            return Invalid("لا يُسمح بالصفحة صفر أو بالقيم السالبة.");

        if (page > totalPages)
            return Invalid($"الصفحة {page} أكبر من إجمالي الصفحات ({totalPages}).");

        return null;
    }

    private static ParseResult Success(IEnumerable<int> pages) => new()
    {
        IsValid = true,
        PageNumbers = pages.ToList()
    };

    private static ParseResult Invalid(string message) => new()
    {
        IsValid = false,
        ErrorMessage = message
    };
}
