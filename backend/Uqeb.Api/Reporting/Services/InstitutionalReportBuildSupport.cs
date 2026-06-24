using Uqeb.Api.Reporting.DTOs;

namespace Uqeb.Api.Reporting.Services;

public interface IInstitutionalReportBuildSupport
{
    Task<int> CountMatchingTransactionsAsync(ReportBuildRequestDto request, CancellationToken ct);

    Task<InstitutionalReportModel> BuildInternalAsync(
        ReportBuildRequestDto request,
        CancellationToken ct,
        ReportAssemblyOptions options);
}
