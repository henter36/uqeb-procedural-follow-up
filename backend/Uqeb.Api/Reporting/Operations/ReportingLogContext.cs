namespace Uqeb.Api.Reporting.Operations;

public sealed record ReportingLogContext(
    string? CorrelationId,
    string ReportType,
    string Format);

public sealed record ReportingExportCompletedLog(
    ReportingLogContext Context,
    int ExportedRows,
    long DurationMs,
    long FileSizeBytes,
    int PartsCount,
    string Fingerprint);

public sealed record ReportingExportFailedLog(
    ReportingLogContext Context,
    string Result,
    long DurationMs);

public sealed record ReportingExportCancelledLog(
    ReportingLogContext Context,
    long DurationMs);
