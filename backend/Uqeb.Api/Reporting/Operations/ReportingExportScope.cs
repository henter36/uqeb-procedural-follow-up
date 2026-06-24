using System.Diagnostics;

namespace Uqeb.Api.Reporting.Operations;

public interface IReportingExportScopeFactory
{
    ReportingExportScope Create(ReportingExportSessionContext session);
}

public sealed class ReportingExportScopeFactory(
    IReportingExportConcurrencyGate concurrencyGate,
    IReportingTempFileManager tempFileManager,
    IReportingExportLifecycleObserver lifecycleObserver,
    IReportingMetrics metrics) : IReportingExportScopeFactory
{
    public ReportingExportScope Create(ReportingExportSessionContext session) =>
        new(session, concurrencyGate, tempFileManager, lifecycleObserver, metrics);
}

public sealed class ReportingExportScope : IAsyncDisposable
{
    private readonly ReportingExportSessionContext _session;
    private readonly IReportingExportConcurrencyGate _concurrencyGate;
    private readonly IReportingTempFileManager _tempFileManager;
    private readonly IReportingExportLifecycleObserver _lifecycleObserver;
    private readonly IReportingMetrics _metrics;
    private readonly Stopwatch _stopwatch = Stopwatch.StartNew();
    private readonly IDisposable _activeMetric;
    private bool _completed;
    private bool _released;

    internal ReportingExportScope(
        ReportingExportSessionContext session,
        IReportingExportConcurrencyGate concurrencyGate,
        IReportingTempFileManager tempFileManager,
        IReportingExportLifecycleObserver lifecycleObserver,
        IReportingMetrics metrics)
    {
        _session = session;
        _concurrencyGate = concurrencyGate;
        _tempFileManager = tempFileManager;
        _lifecycleObserver = lifecycleObserver;
        _metrics = metrics;
        _activeMetric = metrics.TrackActiveExport();
    }

    public string SessionDirectory => _session.SessionDirectory;
    public CancellationTokenSource TimeoutSource => _session.TimeoutSource;
    public CancellationToken Token => _session.Token;

    public void MarkCompleted() => _completed = true;

    public async ValueTask DisposeAsync()
    {
        if (!_released)
        {
            _concurrencyGate.Release(_session.Format);
            _released = true;
        }

        _activeMetric.Dispose();
        _tempFileManager.CleanupSession(SessionDirectory);
        TimeoutSource.Dispose();

        if (!_completed)
        {
            var context = _session.ToLogContext();
            _metrics.RecordCancellation(context.Format, context.ReportType);
        }

        await ValueTask.CompletedTask;
    }

    public Task LogStartedAsync(int? matchedRows) =>
        _lifecycleObserver.LogStartedAsync(_session, matchedRows);

    public Task LogCompletedAsync(int exportedRows, long fileSizeBytes, int partsCount, string fingerprint)
    {
        _completed = true;
        return _lifecycleObserver.LogCompletedAsync(
            _session,
            new ReportingExportCompletedLog(
                _session.ToLogContext(),
                exportedRows,
                _stopwatch.ElapsedMilliseconds,
                fileSizeBytes,
                partsCount,
                fingerprint));
    }

    public Task LogFailedAsync(string result) =>
        _lifecycleObserver.LogFailedAsync(
            _session,
            new ReportingExportFailedLog(_session.ToLogContext(), result, _stopwatch.ElapsedMilliseconds));

    public Task LogCancelledAsync() =>
        _lifecycleObserver.LogCancelledAsync(
            _session,
            new ReportingExportCancelledLog(_session.ToLogContext(), _stopwatch.ElapsedMilliseconds));

    public Task LogRejectedAsync(string errorCode) =>
        _lifecycleObserver.LogRejectedAsync(_session, errorCode);
}
