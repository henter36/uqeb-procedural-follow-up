using Uqeb.Api.Reporting.DTOs;
using Uqeb.Api.Reporting.Enums;

namespace Uqeb.Api.Reporting.Operations;

public sealed record ReportingExportSessionContext(
    ReportExportRequestDto Request,
    ExportFormat Format,
    string? CorrelationId,
    string SessionDirectory,
    CancellationTokenSource TimeoutSource)
{
    public CancellationToken Token => TimeoutSource.Token;

    public ReportingLogContext ToLogContext() => new(
        CorrelationId,
        Request.BuildRequest.ReportType.ToString(),
        ReportingMetrics.FormatLabel(Format));
}
