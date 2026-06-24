using Uqeb.Api.Reporting.DTOs;
using Uqeb.Api.Reporting.Services;

namespace Uqeb.Api.Reporting.Operations;

public interface IReportingExportGuard
{
    Task<ReportingExportScope> BeginExportAsync(
        ReportExportRequestDto request,
        CancellationToken cancellationToken);
}

public sealed class ReportingExportGuard(
    IReportingExportAdmissionService admission,
    IReportingExportResourceGuard resourceGuard,
    IReportingExportScopeFactory scopeFactory,
    IReportingCorrelationIdProvider correlationIdProvider,
    IReportingExportConcurrencyGate concurrencyGate) : IReportingExportGuard
{
    public async Task<ReportingExportScope> BeginExportAsync(
        ReportExportRequestDto request,
        CancellationToken cancellationToken)
    {
        var exportOptions = InstitutionalReportExportOptionsResolver.Resolve(request);
        var format = exportOptions.Format;
        var correlationId = correlationIdProvider.GetCorrelationId();

        await admission.AdmitAsync(format, request, correlationId, cancellationToken);

        try
        {
            var (sessionDirectory, timeoutSource) = resourceGuard.BeginSession(cancellationToken);
            var session = new ReportingExportSessionContext(
                request,
                format,
                correlationId,
                sessionDirectory,
                timeoutSource);

            return scopeFactory.Create(session);
        }
        catch
        {
            concurrencyGate.Release(format);
            throw;
        }
    }
}
