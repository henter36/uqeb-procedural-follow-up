using Uqeb.Api.Reporting.Models;

namespace Uqeb.Api.Reporting.Services;

internal static class ReportDepartmentValidator
{
    public static bool HasValidDepartment(TransactionReportSnapshot snapshot) =>
        snapshot.ResponsibleDepartmentId.HasValue
        && !ReportDepartmentNameNormalizer.IsUndefined(snapshot.ResponsibleDepartment);

    public static int GetKey(TransactionReportSnapshot snapshot) =>
        snapshot.ResponsibleDepartmentId!.Value;

    public static string GetName(IEnumerable<TransactionReportSnapshot> snapshots) =>
        ReportDepartmentNameNormalizer.Normalize(snapshots.First().ResponsibleDepartment);
}
