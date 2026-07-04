using Uqeb.Api.Reporting.DTOs;

namespace Uqeb.Api.Reporting.Services;

/// <summary>
/// Shared "top departments" ranking for the department time-series view, used by both the
/// HTML/PDF renderer and the DOCX exporter so the capped list is ranked identically in both.
/// The XLSX export is intentionally uncapped and does not use this helper.
/// </summary>
internal static class DepartmentTimeSeriesRanking
{
    /// <summary>Maximum departments shown in the capped (HTML/PDF/DOCX) views.</summary>
    internal const int TopDepartments = 10;

    internal readonly record struct RankedDepartment(
        (int? DepartmentId, string DepartmentName) Key,
        int OverdueCount,
        int OpenCount,
        int IncomingCount);

    /// <summary>
    /// Aggregates the per-period points into one row per department and orders them by
    /// Overdue, then Open, then Incoming totals across the whole window (descending).
    /// </summary>
    internal static List<RankedDepartment> RankDepartments(IEnumerable<DepartmentTimeSeriesPointDto> points) =>
        points
            .GroupBy(p => (p.DepartmentId, p.DepartmentName))
            .Select(g => new RankedDepartment(
                g.Key,
                g.Sum(p => p.OverdueCount),
                g.Sum(p => p.OpenCount),
                g.Sum(p => p.IncomingCount)))
            .OrderByDescending(x => x.OverdueCount)
            .ThenByDescending(x => x.OpenCount)
            .ThenByDescending(x => x.IncomingCount)
            .ToList();

    /// <summary>The keys of the top <see cref="TopDepartments"/> departments, for row filtering.</summary>
    internal static HashSet<(int? DepartmentId, string DepartmentName)> TopDepartmentKeys(
        IReadOnlyList<RankedDepartment> rankedDepartments) =>
        rankedDepartments
            .Take(TopDepartments)
            .Select(x => x.Key)
            .ToHashSet();

    /// <summary>Whether a period point's department is among the top-ranked departments.</summary>
    internal static bool IsTopDepartment(
        DepartmentTimeSeriesPointDto point,
        HashSet<(int? DepartmentId, string DepartmentName)> topDepartmentKeys) =>
        topDepartmentKeys.Contains((point.DepartmentId, point.DepartmentName));

    /// <summary>
    /// Department-name fallback shared by the HTML/PDF renderer and the DOCX exporter, so the
    /// department time-series view never diverges between the two on an undefined/blank name.
    /// </summary>
    internal static string NormalizeDepartmentName(string? departmentName)
    {
        var normalized = (departmentName ?? string.Empty).Trim();
        var isUndefined = normalized.Length == 0 || normalized is "—" or "-" or "غير محددة" or "غير محدد";
        return isUndefined ? "غير محدد" : normalized;
    }
}
