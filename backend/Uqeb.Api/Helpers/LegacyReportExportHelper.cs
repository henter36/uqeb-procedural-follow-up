using System.Text.RegularExpressions;

namespace Uqeb.Api.Helpers;

public static partial class LegacyReportExportHelper
{
    public static string SanitizeReportType(string reportType)
    {
        if (string.IsNullOrWhiteSpace(reportType))
            return "report";

        var normalized = reportType.Trim().ToLowerInvariant();
        var sanitized = ReportTypeSanitizer().Replace(normalized, "-").Trim('-');
        return string.IsNullOrEmpty(sanitized) ? "report" : sanitized;
    }

    public static string BuildExcelFileName(string reportType) =>
        $"report-{SanitizeReportType(reportType)}.xlsx";

    [GeneratedRegex(@"[^a-z0-9\-]+", RegexOptions.CultureInvariant)]
    private static partial Regex ReportTypeSanitizer();
}
