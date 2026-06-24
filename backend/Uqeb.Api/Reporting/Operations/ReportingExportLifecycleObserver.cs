using Uqeb.Api.Services;

namespace Uqeb.Api.Reporting.Operations;

public interface IReportingExportLifecycleObserver
{
    Task LogStartedAsync(ReportingExportSessionContext session, int? matchedRows);

    Task LogCompletedAsync(
        ReportingExportSessionContext session,
        ReportingExportCompletedLog completed);

    Task LogFailedAsync(ReportingExportSessionContext session, ReportingExportFailedLog failed);

    Task LogCancelledAsync(ReportingExportSessionContext session, ReportingExportCancelledLog cancelled);

    Task LogRejectedAsync(ReportingExportSessionContext session, string errorCode);
}

public sealed class ReportingExportLifecycleObserver(
    IAuditService audit,
    ICurrentUserService currentUser,
    IReportingMetrics metrics,
    ILogger<ReportingExportLifecycleObserver> logger) : IReportingExportLifecycleObserver
{
    public async Task LogStartedAsync(ReportingExportSessionContext session, int? matchedRows)
    {
        var context = session.ToLogContext();
        ReportingStructuredLog.LogExportStarted(logger, context, matchedRows);
        await ReportingAuditWriter.LogExportStartedAsync(
            audit,
            currentUser.UserId,
            session.Request,
            matchedRows,
            session.CorrelationId);
    }

    public async Task LogCompletedAsync(
        ReportingExportSessionContext session,
        ReportingExportCompletedLog completed)
    {
        var context = session.ToLogContext();
        ReportingStructuredLog.LogExportCompleted(logger, completed);
        metrics.RecordExportDuration(
            completed.DurationMs,
            context.Format,
            context.ReportType,
            "success");
        metrics.RecordRequest(context.Format, context.ReportType, "success");
        await ReportingAuditWriter.LogExportCompletedAsync(
            audit,
            currentUser.UserId,
            session.Request,
            completed.ExportedRows,
            completed.Fingerprint,
            session.CorrelationId);
    }

    public async Task LogFailedAsync(ReportingExportSessionContext session, ReportingExportFailedLog failed)
    {
        var context = session.ToLogContext();
        ReportingStructuredLog.LogExportFailed(logger, failed);
        metrics.RecordFailure(context.Format, context.ReportType, failed.Result);
        await ReportingAuditWriter.LogExportFailedAsync(
            audit,
            currentUser.UserId,
            session.Request,
            failed.Result,
            session.CorrelationId);
    }

    public async Task LogCancelledAsync(ReportingExportSessionContext session, ReportingExportCancelledLog cancelled)
    {
        var context = session.ToLogContext();
        ReportingStructuredLog.LogExportCancelled(logger, cancelled);
        metrics.RecordCancellation(context.Format, context.ReportType);
        await ReportingAuditWriter.LogExportCancelledAsync(
            audit,
            currentUser.UserId,
            session.Request,
            session.CorrelationId);
    }

    public async Task LogRejectedAsync(ReportingExportSessionContext session, string errorCode)
    {
        var context = session.ToLogContext();
        ReportingStructuredLog.LogExportRejected(logger, context.CorrelationId, context.Format, errorCode);
        metrics.RecordRejected(context.Format, context.ReportType);
        await ReportingAuditWriter.LogExportRejectedAsync(
            audit,
            currentUser.UserId,
            session.Request,
            errorCode,
            session.CorrelationId);
    }
}
