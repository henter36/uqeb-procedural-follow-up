using Microsoft.Extensions.Options;
using Uqeb.Api.Reporting.Configuration;

namespace Uqeb.Api.Reporting.Operations;

public interface IReportingExportResourceGuard
{
    void EnsureDiskSpaceForExport();

    (string SessionDirectory, CancellationTokenSource TimeoutSource) BeginSession(CancellationToken cancellationToken);
}

public sealed class ReportingExportResourceGuard(
    IOptions<ReportingOptions> options,
    IReportingTempFileManager tempFileManager) : IReportingExportResourceGuard
{
    private readonly ReportingOptions _options = options.Value;

    public void EnsureDiskSpaceForExport() =>
        tempFileManager.EnsureDiskSpaceForExport(_options.MaxTempBytesPerExport);

    public (string SessionDirectory, CancellationTokenSource TimeoutSource) BeginSession(CancellationToken cancellationToken)
    {
        var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(TimeSpan.FromSeconds(_options.MaxExportDurationSeconds));
        return (tempFileManager.CreateSessionDirectory(), timeoutSource);
    }
}
