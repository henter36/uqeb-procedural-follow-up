namespace Uqeb.Api.Reporting.Operations;

public static class ReportingStructuredLog
{
    public static void LogExportStarted(
        ILogger logger,
        ReportingLogContext context,
        int? matchedRows)
    {
        logger.LogInformation(
            ReportingEventIds.ExportStarted,
            "Report export started. CorrelationId={CorrelationId} ReportType={ReportType} Format={Format} MatchedRows={MatchedRows}",
            context.CorrelationId,
            context.ReportType,
            context.Format,
            matchedRows);
    }

    public static void LogExportCompleted(
        ILogger logger,
        ReportingExportCompletedLog completed)
    {
        logger.LogInformation(
            ReportingEventIds.ExportCompleted,
            "Report export completed. CorrelationId={CorrelationId} ReportType={ReportType} Format={Format} ExportedRows={ExportedRows} DurationMs={DurationMs} FileSizeBytes={FileSizeBytes} PartsCount={PartsCount} Result=success",
            completed.Context.CorrelationId,
            completed.Context.ReportType,
            completed.Context.Format,
            completed.ExportedRows,
            completed.DurationMs,
            completed.FileSizeBytes,
            completed.PartsCount);
    }

    public static void LogExportFailed(
        ILogger logger,
        ReportingExportFailedLog failed)
    {
        logger.LogWarning(
            ReportingEventIds.ExportFailed,
            "Report export failed. CorrelationId={CorrelationId} ReportType={ReportType} Format={Format} Result={Result} DurationMs={DurationMs}",
            failed.Context.CorrelationId,
            failed.Context.ReportType,
            failed.Context.Format,
            failed.Result,
            failed.DurationMs);
    }

    public static void LogExportCancelled(
        ILogger logger,
        ReportingExportCancelledLog cancelled)
    {
        logger.LogInformation(
            ReportingEventIds.ExportCancelled,
            "Report export cancelled. CorrelationId={CorrelationId} ReportType={ReportType} Format={Format} DurationMs={DurationMs} Result=cancelled",
            cancelled.Context.CorrelationId,
            cancelled.Context.ReportType,
            cancelled.Context.Format,
            cancelled.DurationMs);
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
