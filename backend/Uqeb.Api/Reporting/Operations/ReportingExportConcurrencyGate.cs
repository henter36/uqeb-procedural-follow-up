using Microsoft.Extensions.Options;
using Uqeb.Api.Reporting.Configuration;
using Uqeb.Api.Reporting.Enums;

namespace Uqeb.Api.Reporting.Operations;

public interface IReportingExportConcurrencyGate
{
    Task AcquireAsync(ExportFormat format, CancellationToken cancellationToken);
    void Release(ExportFormat format);
    bool HasCapacity(ExportFormat format);
    int ActivePdfExports { get; }
    int ActiveNonPdfExports { get; }
}

public sealed class ReportingExportConcurrencyGate : IReportingExportConcurrencyGate, IDisposable
{
    private readonly ReportingOptions _options;
    private readonly SemaphoreSlim _pdfGate;
    private readonly SemaphoreSlim _nonPdfGate;
    private int _activePdf;
    private int _activeNonPdf;

    public ReportingExportConcurrencyGate(IOptions<ReportingOptions> options)
    {
        _options = options.Value;
        _pdfGate = new SemaphoreSlim(_options.MaxConcurrentPdfExports, _options.MaxConcurrentPdfExports);
        _nonPdfGate = new SemaphoreSlim(_options.MaxConcurrentNonPdfExports, _options.MaxConcurrentNonPdfExports);
    }

    public int ActivePdfExports => Volatile.Read(ref _activePdf);
    public int ActiveNonPdfExports => Volatile.Read(ref _activeNonPdf);

    public bool HasCapacity(ExportFormat format) =>
        format == ExportFormat.Pdf ? _pdfGate.CurrentCount > 0 : _nonPdfGate.CurrentCount > 0;

    public async Task AcquireAsync(ExportFormat format, CancellationToken cancellationToken)
    {
        var gate = format == ExportFormat.Pdf ? _pdfGate : _nonPdfGate;
        var waitTimeout = TimeSpan.FromSeconds(_options.ExportConcurrencyWaitSeconds);

        if (!await gate.WaitAsync(waitTimeout, cancellationToken))
        {
            throw new ReportingExportRejectedException(
                ReportingErrorCodes.ConcurrencyLimit,
                "عدد عمليات تصدير التقارير المتزامنة ممتلئ. حاول لاحقًا.",
                StatusCodes.Status429TooManyRequests,
                retryAfterSeconds: Math.Max(1, _options.ExportConcurrencyWaitSeconds));
        }

        if (format == ExportFormat.Pdf)
            Interlocked.Increment(ref _activePdf);
        else
            Interlocked.Increment(ref _activeNonPdf);
    }

    public void Release(ExportFormat format)
    {
        if (format == ExportFormat.Pdf)
        {
            Interlocked.Decrement(ref _activePdf);
            _pdfGate.Release();
            return;
        }

        Interlocked.Decrement(ref _activeNonPdf);
        _nonPdfGate.Release();
    }

    public void Dispose()
    {
        _pdfGate.Dispose();
        _nonPdfGate.Dispose();
    }
}
