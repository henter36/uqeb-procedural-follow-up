namespace Uqeb.Api.Reporting.Services;

internal static class ReportDepartmentNameNormalizer
{
    public const string UndefinedDepartmentLabel = "غير محدد";

    public static bool IsUndefined(string? value)
    {
        var normalized = (value ?? string.Empty).Trim();
        return normalized.Length == 0
            || normalized is "—" or "-" or "غير محددة" or "غير محدد";
    }

    public static string Normalize(string? value) =>
        IsUndefined(value) ? UndefinedDepartmentLabel : value!.Trim();

    public static string DataQualityOwnerOrDepartment(string? value) =>
        IsUndefined(value) ? "مالك البيانات" : value!.Trim();
}
