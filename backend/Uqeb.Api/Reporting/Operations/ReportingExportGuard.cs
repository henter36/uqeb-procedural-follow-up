using System.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Uqeb.Api.Middleware;
using Uqeb.Api.Reporting.Configuration;
using Uqeb.Api.Reporting.DTOs;
using Uqeb.Api.Reporting.Enums;
using Uqeb.Api.Reporting.Services;
using Uqeb.Api.Services;

namespace Uqeb.Api.Reporting.Operations;

public interface IReportingExportGuard
{
    Task<ReportingExportScope> BeginExportAsync(
        ReportExportRequestDto request,
        CancellationToken cancellationToken);
}

public sealed class ReportingExportScope : IAsyncDisposable
{
    private readonly IReportingExportConcurrencyGate _concurrencyGate;
    private readonly IReportingTempFileManager _tempFileManager;
    private readonly IReportingMetrics _metrics;
    private readonly IAuditService _audit;
    private readonly ICurrentUserService _currentUser;
    private readonly ILogger _logger;
    private readonly ReportExportRequestDto _request;
    private readonly ExportFormat _format;
    private readonly string? _correlationId;
    private readonly Stopwatch _stopwatch = Stopwatch.StartNew();
    private readonly IDisposable _activeMetric;
    private bool _completed;
    private bool _released;

    internal ReportingExportScope(
        IReportingExportConcurrencyGate concurrencyGate,
        IReportingTempFileManager tempFileManager,
        IReportingMetrics metrics,
        IAuditService audit,
        ICurrentUserService currentUser,
        ILogger logger,
        ReportExportRequestDto request,
        ExportFormat format,
        string? correlationId,
        string sessionDirectory,
        CancellationTokenSource timeoutSource)
    {
        _concurrencyGate = concurrencyGate;
        _tempFileManager = tempFileManager;
        _metrics = metrics;
        _audit = audit;
        _currentUser = currentUser;
        _logger = logger;
        _request = request;
        _format = format;
        _correlationId = correlationId;
        SessionDirectory = sessionDirectory;
        TimeoutSource = timeoutSource;
        Token = timeoutSource.Token;
        _activeMetric = metrics.TrackActiveExport();
    }

    public string SessionDirectory { get; }
    public CancellationTokenSource TimeoutSource { get; }
    public CancellationToken Token { get; }

    public void MarkCompleted() => _completed = true;

    public async ValueTask DisposeAsync()
    {
        if (!_released)
        {
            _concurrencyGate.Release(_format);
            _released = true;
        }

        _activeMetric.Dispose();
        _tempFileManager.CleanupSession(SessionDirectory);
        TimeoutSource.Dispose();

        if (!_completed)
        {
            _metrics.RecordCancellation(
                ReportingMetrics.FormatLabel(_format),
                _request.BuildRequest.ReportType.ToString());
        }
    }

    internal async Task LogStartedAsync(int? matchedRows)
    {
        ReportingStructuredLog.LogExportStarted(
            _logger,
            _correlationId,
            _request.BuildRequest.ReportType.ToString(),
            ReportingMetrics.FormatLabel(_format),
            matchedRows);
        await ReportingAuditWriter.LogExportStartedAsync(
            _audit,
            _currentUser.UserId,
            _request,
            matchedRows,
            _correlationId);
    }

    internal async Task LogCompletedAsync(int exportedRows, long fileSizeBytes, int partsCount, string fingerprint)
    {
        _completed = true;
        var durationMs = _stopwatch.ElapsedMilliseconds;
        ReportingStructuredLog.LogExportCompleted(
            _logger,
            _correlationId,
            _request.BuildRequest.ReportType.ToString(),
            ReportingMetrics.FormatLabel(_format),
            exportedRows,
            durationMs,
            fileSizeBytes,
            partsCount);
        _metrics.RecordExportDuration(
            durationMs,
            ReportingMetrics.FormatLabel(_format),
            _request.BuildRequest.ReportType.ToString(),
            "success");
        _metrics.RecordRequest(
            ReportingMetrics.FormatLabel(_format),
            _request.BuildRequest.ReportType.ToString(),
            "success");
        await ReportingAuditWriter.LogExportCompletedAsync(
            _audit,
            _currentUser.UserId,
            _request,
            exportedRows,
            fingerprint,
            _correlationId);
    }

    internal async Task LogFailedAsync(string result)
    {
        ReportingStructuredLog.LogExportFailed(
            _logger,
            _correlationId,
            _request.BuildRequest.ReportType.ToString(),
            ReportingMetrics.FormatLabel(_format),
            result,
            _stopwatch.ElapsedMilliseconds);
        _metrics.RecordFailure(
            ReportingMetrics.FormatLabel(_format),
            _request.BuildRequest.ReportType.ToString(),
            result);
        await ReportingAuditWriter.LogExportFailedAsync(
            _audit,
            _currentUser.UserId,
            _request,
            result,
            _correlationId);
    }

    internal async Task LogCancelledAsync()
    {
        ReportingStructuredLog.LogExportCancelled(
            _logger,
            _correlationId,
            _request.BuildRequest.ReportType.ToString(),
            ReportingMetrics.FormatLabel(_format),
            _stopwatch.ElapsedMilliseconds);
        _metrics.RecordCancellation(
            ReportingMetrics.FormatLabel(_format),
            _request.BuildRequest.ReportType.ToString());
        await ReportingAuditWriter.LogExportCancelledAsync(
            _audit,
            _currentUser.UserId,
            _request,
            _correlationId);
    }

    internal async Task LogRejectedAsync(string errorCode)
    {
        ReportingStructuredLog.LogExportRejected(_logger, _correlationId, ReportingMetrics.FormatLabel(_format), errorCode);
        _metrics.RecordRejected(
            ReportingMetrics.FormatLabel(_format),
            _request.BuildRequest.ReportType.ToString());
        await ReportingAuditWriter.LogExportRejectedAsync(
            _audit,
            _currentUser.UserId,
            _request,
            errorCode,
            _correlationId);
    }
}

public sealed class ReportingExportGuard : IReportingExportGuard
{
    private readonly ReportingOptions _options;
    private readonly IReportingExportConcurrencyGate _concurrencyGate;
    private readonly IReportingTempFileManager _tempFileManager;
    private readonly IReportingMetrics _metrics;
    private readonly IAuditService _audit;
    private readonly ICurrentUserService _currentUser;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ILogger<ReportingExportGuard> _logger;

    public ReportingExportGuard(
        IOptions<ReportingOptions> options,
        IReportingExportConcurrencyGate concurrencyGate,
        IReportingTempFileManager tempFileManager,
        IReportingMetrics metrics,
        IAuditService audit,
        ICurrentUserService currentUser,
        IHttpContextAccessor httpContextAccessor,
        ILogger<ReportingExportGuard> logger)
    {
        _options = options.Value;
        _concurrencyGate = concurrencyGate;
        _tempFileManager = tempFileManager;
        _metrics = metrics;
        _audit = audit;
        _currentUser = currentUser;
        _httpContextAccessor = httpContextAccessor;
        _logger = logger;
    }

    public async Task<ReportingExportScope> BeginExportAsync(
        ReportExportRequestDto request,
        CancellationToken cancellationToken)
    {
        var exportOptions = InstitutionalReportExportOptionsResolver.Resolve(request);
        var format = exportOptions.Format;
        var correlationId = _httpContextAccessor.HttpContext?.Items[CorrelationIdMiddleware.ItemKey] as string;

        try
        {
            _tempFileManager.EnsureDiskSpaceForExport(_options.MaxTempBytesPerExport);
            await _concurrencyGate.AcquireAsync(format, cancellationToken);
        }
        catch (ReportingExportRejectedException ex)
        {
            await ReportingAuditWriter.LogExportRejectedAsync(
                _audit,
                _currentUser.UserId,
                request,
                ex.ErrorCode,
                correlationId);
            _metrics.RecordRejected(
                ReportingMetrics.FormatLabel(format),
                request.BuildRequest.ReportType.ToString());
            throw;
        }

        var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(TimeSpan.FromSeconds(_options.MaxExportDurationSeconds));
        var sessionDirectory = _tempFileManager.CreateSessionDirectory();

        return new ReportingExportScope(
            _concurrencyGate,
            _tempFileManager,
            _metrics,
            _audit,
            _currentUser,
            _logger,
            request,
            format,
            correlationId,
            sessionDirectory,
            timeoutSource);
    }
}
