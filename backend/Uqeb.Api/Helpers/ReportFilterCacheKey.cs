using Uqeb.Api.DTOs.Reports;

namespace Uqeb.Api.Helpers;

public static class ReportFilterCacheKey
{
    public static string Build(ReportFilterRequest? filter)
    {
        if (filter == null)
            return "default";

        return string.Join('|',
            filter.DateFrom?.ToString("O") ?? "",
            filter.DateTo?.ToString("O") ?? "",
            filter.Status ?? "",
            filter.CategoryId?.ToString() ?? "",
            filter.DepartmentId?.ToString() ?? "",
            filter.IncomingPartyId?.ToString() ?? "",
            filter.OutgoingPartyId?.ToString() ?? "",
            filter.OutgoingDepartmentId?.ToString() ?? "",
            filter.IncomingSourceType ?? "",
            filter.Search ?? "");
    }
}
