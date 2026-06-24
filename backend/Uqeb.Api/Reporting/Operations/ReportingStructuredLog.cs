namespace Uqeb.Api.Reporting.Operations;

public static class ReportingEventIds
{
    public const int GenerationStarted = 4100;
    public const int ModelBuilt = 4101;
    public const int ExportStarted = 4102;
    public const int ExportCompleted = 4103;
    public const int ExportFailed = 4104;
    public const int ExportCancelled = 4105;
    public const int ExportRejected = 4106;
    public const int TempCleanupFailed = 4107;
    public const int ChromiumUnavailable = 4108;
}

public static class ReportingStructuredLog
{
    public static void LogExportStarted(
        ILogger logger,
        string? correlationId,
        string reportType,
        string format,
        int? matchedRows)
    {
        logger.LogInformation(
            ReportingEventIds.ExportStarted,
            "Report export started. CorrelationId={CorrelationId} ReportType={ReportType} Format={Format} MatchedRows={MatchedRows}",
            correlationId,
            reportType,
            format,
            matchedRows);
    }

    public static void LogExportCompleted(
        ILogger logger,
        string? correlationId,
        string reportType,
        string format,
        int exportedRows,
        long durationMs,
        long fileSizeBytes,
        int partsCount)
    {
        logger.LogInformation(
            ReportingEventIds.ExportCompleted,
            "Report export completed. CorrelationId={CorrelationId} ReportType={ReportType} Format={Format} ExportedRows={ExportedRows} DurationMs={DurationMs} FileSizeBytes={FileSizeBytes} PartsCount={PartsCount} Result=success",
            correlationId,
            reportType,
            format,
            exportedRows,
            durationMs,
            fileSizeBytes,
            partsCount);
    }

    public static void LogExportFailed(
        ILogger logger,
        string? correlationId,
        string reportType,
        string format,
        string result,
        long durationMs)
    {
        logger.LogWarning(
            ReportingEventIds.ExportFailed,
            "Report export failed. CorrelationId={CorrelationId} ReportType={ReportType} Format={Format} Result={Result} DurationMs={DurationMs}",
            correlationId,
            reportType,
            format,
            result,
            durationMs);
    }

    public static void LogExportCancelled(
        ILogger logger,
        string? correlationId,
        string reportType,
        string format,
        long durationMs)
    {
        logger.LogInformation(
            ReportingEventIds.ExportCancelled,
            "Report export cancelled. CorrelationId={CorrelationId} ReportType={ReportType} Format={Format} DurationMs={DurationMs} Result=cancelled",
            correlationId,
            reportType,
            format,
            durationMs);
    }

    public static void LogExportRejected(
        ILogger logger,
        string? correlationId,
        string format,
        string errorCode)
    {
        logger.LogWarning(
            ReportingEventIds.ExportRejected,
            "Report export rejected. CorrelationId={CorrelationId} Format={Format} Result=rejected ErrorCode={ErrorCode}",
            correlationId,
            format,
            errorCode);
    }
}
