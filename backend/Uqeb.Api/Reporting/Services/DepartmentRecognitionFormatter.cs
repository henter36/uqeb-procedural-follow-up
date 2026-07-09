using Uqeb.Api.Reporting.DTOs;

namespace Uqeb.Api.Reporting.Services;

internal static class DepartmentRecognitionFormatter
{
    internal const string OutstandingType = "متميزة";
    internal const string ImprovedType = "الأكثر تحسنًا";

    internal static bool IsOutstanding(DepartmentRecognitionRowDto row) =>
        string.Equals(row.RecognitionType, OutstandingType, StringComparison.Ordinal);

    internal static bool IsImproved(DepartmentRecognitionRowDto row) =>
        string.Equals(row.RecognitionType, ImprovedType, StringComparison.Ordinal);

    internal static string FormatImprovementValue(DepartmentRecognitionRowDto row) =>
        IsImproved(row) ? $"{row.ImprovementValue:N1}" : "—";

    internal static string FormatDepartmentRecognitionRow(DepartmentRecognitionRowDto row) =>
        $"{row.DepartmentName} — {row.RecognitionType} — حجم {row.TransactionCount} — ضمن المهلة {row.OnTimeCompletionRate:N1}% — المتأخرات {row.OverdueCount} — متوسط الإنجاز {row.AverageCompletionDays:N1} يوم — التحسن {FormatImprovementValue(row)} — {row.Reason}";
}
