using Uqeb.Api.Reporting.DTOs;
using Uqeb.Api.Reporting.Enums;
using Uqeb.Api.Services;

namespace Uqeb.Api.Reporting.Operations;

public interface IReportingExportAdmissionService
{
    Task AdmitAsync(
        ExportFormat format,
        ReportExportRequestDto request,
        string? correlationId,
        CancellationToken cancellationToken);
}

public sealed class ReportingExportAdmissionService(
    IReportingExportConcurrencyGate concurrencyGate,
    IReportingExportResourceGuard resourceGuard,
    IAuditService audit,
    ICurrentUserService currentUser,
    IReportingMetrics metrics) : IReportingExportAdmissionService
{
    public async Task AdmitAsync(
        ExportFormat format,
        ReportExportRequestDto request,
        string? correlationId,
        CancellationToken cancellationToken)
    {
        try
        {
            resourceGuard.EnsureDiskSpaceForExport();
            await concurrencyGate.AcquireAsync(format, cancellationToken);
        }
        catch (ReportingExportRejectedException ex)
        {
            await ReportingAuditWriter.LogExportRejectedAsync(
                audit,
                currentUser.UserId,
                request,
                ex.ErrorCode,
                correlationId);
            metrics.RecordRejected(
                ReportingMetrics.FormatLabel(format),
                request.BuildRequest.ReportType.ToString());
            throw;
        }
    }
}
