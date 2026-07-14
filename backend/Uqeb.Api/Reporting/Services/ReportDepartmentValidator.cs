using Uqeb.Api.Reporting.Models;

namespace Uqeb.Api.Reporting.Services;

internal static class ReportDepartmentValidator
{
    public static bool HasValidDepartment(TransactionReportSnapshot snapshot) =>
        snapshot.ResponsibleDepartmentId.HasValue
        && !ReportDepartmentNameNormalizer.IsUndefined(snapshot.ResponsibleDepartment);
}
