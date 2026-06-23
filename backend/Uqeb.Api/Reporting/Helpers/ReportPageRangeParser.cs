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

        var tokens = expression.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (tokens.Length == 0)
            return Invalid("صيغة الصفحات غير صالحة.");

        var pages = new SortedSet<int>();

        foreach (var token in tokens)
        {
            if (token.Contains('-', StringComparison.Ordinal))
            {
                var parts = token.Split('-', StringSplitOptions.TrimEntries);
                if (parts.Length != 2)
                    return Invalid($"النطاق غير صالح: {token}");

                if (!int.TryParse(parts[0], out var start) || !int.TryParse(parts[1], out var end))
                    return Invalid($"النطاق يحتوي أحرفًا غير صالحة: {token}");

                if (start <= 0 || end <= 0)
                    return Invalid("لا يُسمح بالصفحة صفر أو بالقيم السالبة.");

                if (start > end)
                    return Invalid($"النطاق العكسي غير مسموح: {token}");

                if (end > totalPages)
                    return Invalid($"الصفحة {end} أكبر من إجمالي الصفحات ({totalPages}).");

                for (var page = start; page <= end; page++)
                    pages.Add(page);
            }
            else
            {
                if (!int.TryParse(token, out var page))
                    return Invalid($"قيمة غير صالحة: {token}");

                if (page <= 0)
                    return Invalid("لا يُسمح بالصفحة صفر أو بالقيم السالبة.");

                if (page > totalPages)
                    return Invalid($"الصفحة {page} أكبر من إجمالي الصفحات ({totalPages}).");

                pages.Add(page);
            }
        }

        return new ParseResult
        {
            IsValid = true,
            PageNumbers = pages.ToList()
        };
    }

    private static ParseResult Invalid(string message) => new()
    {
        IsValid = false,
        ErrorMessage = message
    };
}
