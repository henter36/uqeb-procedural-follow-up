using System.Diagnostics.Metrics;
using Uqeb.Api.Reporting.Enums;

namespace Uqeb.Api.Reporting.Operations;

public interface IReportingMetrics
{
    IDisposable TrackActiveExport();
    void RecordRequest(string format, string reportType, string result, string? overflowAction = null);
    void RecordRejected(string format, string reportType);
    void RecordCancellation(string format, string reportType);
    void RecordFailure(string format, string reportType, string result);
    void RecordBuildDuration(double milliseconds, string reportType);
    void RecordRenderDuration(double milliseconds, string format, string reportType);
    void RecordExportDuration(double milliseconds, string format, string reportType, string result);
    void RecordExportFileSize(long bytes, string format);
    void RecordExportRows(int rows, string format);
    void RecordPdfPages(int pages, string reportType);
    void RecordPdfParts(int parts, string reportType);
    void RecordTempCleanupFailure();
    void RecordChromiumLaunchFailure();
}

public sealed class ReportingMetrics : IReportingMetrics
{
    private const string ReportTypeTagName = "report_type";
    private const string FormatTagName = "format";
    private const string ResultTagName = "result";
    private const string OverflowActionTagName = "overflow_action";

    private static readonly Meter Meter = new("Uqeb.Reporting", "1.0.0");

    private readonly Counter<long> _requestsTotal = Meter.CreateCounter<long>("reporting_requests_total");
    private readonly UpDownCounter<long> _requestsActive = Meter.CreateUpDownCounter<long>("reporting_requests_active");
    private readonly Counter<long> _requestsRejectedTotal = Meter.CreateCounter<long>("reporting_requests_rejected_total");
    private readonly Histogram<double> _buildDurationMs = Meter.CreateHistogram<double>("reporting_build_duration_ms");
    private readonly Histogram<double> _renderDurationMs = Meter.CreateHistogram<double>("reporting_render_duration_ms");
    private readonly Histogram<double> _exportDurationMs = Meter.CreateHistogram<double>("reporting_export_duration_ms");
    private readonly Histogram<long> _exportFileSizeBytes = Meter.CreateHistogram<long>("reporting_export_file_size_bytes");
    private readonly Histogram<int> _exportRows = Meter.CreateHistogram<int>("reporting_export_rows");
    private readonly Histogram<int> _pdfPages = Meter.CreateHistogram<int>("reporting_pdf_pages");
    private readonly Histogram<int> _pdfParts = Meter.CreateHistogram<int>("reporting_pdf_parts");
    private readonly Counter<long> _failuresTotal = Meter.CreateCounter<long>("reporting_failures_total");
    private readonly Counter<long> _cancellationsTotal = Meter.CreateCounter<long>("reporting_cancellations_total");
    private readonly Counter<long> _tempCleanupFailuresTotal = Meter.CreateCounter<long>("reporting_temp_cleanup_failures_total");
    private readonly Counter<long> _chromiumLaunchFailuresTotal = Meter.CreateCounter<long>("reporting_chromium_launch_failures_total");

    public IDisposable TrackActiveExport() => new ActiveExportScope(_requestsActive);

    private sealed class ActiveExportScope : IDisposable
    {
        private readonly UpDownCounter<long> _counter;

        public ActiveExportScope(UpDownCounter<long> counter)
        {
            _counter = counter;
            _counter.Add(1);
        }

        public void Dispose() => _counter.Add(-1);
    }

    public void RecordRequest(string format, string reportType, string result, string? overflowAction = null)
    {
        var tags = BuildTags(format, reportType, result, overflowAction);
        _requestsTotal.Add(1, tags);
    }

    public void RecordRejected(string format, string reportType)
    {
        _requestsRejectedTotal.Add(1, BuildTags(format, reportType, "rejected", null));
    }

    public void RecordCancellation(string format, string reportType)
    {
        _cancellationsTotal.Add(1, BuildTags(format, reportType, "cancelled", null));
    }

    public void RecordFailure(string format, string reportType, string result)
    {
        _failuresTotal.Add(1, BuildTags(format, reportType, result, null));
    }

    public void RecordBuildDuration(double milliseconds, string reportType) =>
        _buildDurationMs.Record(milliseconds, new KeyValuePair<string, object?>(ReportTypeTagName, reportType));

    public void RecordRenderDuration(double milliseconds, string format, string reportType) =>
        _renderDurationMs.Record(milliseconds, BuildTags(format, reportType, "success", null));

    public void RecordExportDuration(double milliseconds, string format, string reportType, string result) =>
        _exportDurationMs.Record(milliseconds, BuildTags(format, reportType, result, null));

    public void RecordExportFileSize(long bytes, string format) =>
        _exportFileSizeBytes.Record(bytes, new KeyValuePair<string, object?>(FormatTagName, format));

    public void RecordExportRows(int rows, string format) =>
        _exportRows.Record(rows, new KeyValuePair<string, object?>(FormatTagName, format));

    public void RecordPdfPages(int pages, string reportType) =>
        _pdfPages.Record(pages, new KeyValuePair<string, object?>(ReportTypeTagName, reportType));

    public void RecordPdfParts(int parts, string reportType) =>
        _pdfParts.Record(parts, new KeyValuePair<string, object?>(ReportTypeTagName, reportType));

    public void RecordTempCleanupFailure() => _tempCleanupFailuresTotal.Add(1);

    public void RecordChromiumLaunchFailure() => _chromiumLaunchFailuresTotal.Add(1);

    private static KeyValuePair<string, object?>[] BuildTags(
        string format,
        string reportType,
        string result,
        string? overflowAction)
    {
        var tags = new List<KeyValuePair<string, object?>>
        {
            new(FormatTagName, format),
            new(ReportTypeTagName, reportType),
            new(ResultTagName, result),
        };

        if (!string.IsNullOrWhiteSpace(overflowAction))
            tags.Add(new(OverflowActionTagName, overflowAction));

        return tags.ToArray();
    }

    public static string FormatLabel(ExportFormat format) => format.ToString().ToLowerInvariant();
}
