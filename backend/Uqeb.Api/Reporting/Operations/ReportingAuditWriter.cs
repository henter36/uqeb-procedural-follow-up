using Uqeb.Api.Models.Enums;
using Uqeb.Api.Reporting.DTOs;
using Uqeb.Api.Reporting.Enums;
using Uqeb.Api.Services;

namespace Uqeb.Api.Reporting.Operations;

public static class ReportingAuditEvents
{
    public const string ExportStarted = "institutional_report.export_started";
    public const string ExportCompleted = "institutional_report.export_completed";
    public const string ExportFailed = "institutional_report.export_failed";
    public const string ExportCancelled = "institutional_report.export_cancelled";
    public const string ExportRejected = "institutional_report.export_rejected";
    public const string FeatureEnabled = "institutional_report.feature_enabled";
    public const string FeatureDisabled = "institutional_report.feature_disabled";
}

public static class ReportingAuditWriter
{
    public static Task LogExportStartedAsync(
        IAuditService audit,
        int userId,
        ReportExportRequestDto request,
        int? matchedRows,
        string? correlationId) =>
        audit.LogAsync(
            userId,
            AuditAction.ExportReport,
            "InstitutionalReport",
            null,
            null,
            null,
            FormatPayload(ReportingAuditEvents.ExportStarted, request, matchedRows, correlationId));

    public static Task LogExportCompletedAsync(
        IAuditService audit,
        int userId,
        ReportExportRequestDto request,
        int exportedRows,
        string? fingerprint,
        string? correlationId) =>
        audit.LogAsync(
            userId,
            AuditAction.ExportReport,
            "InstitutionalReport",
            null,
            null,
            null,
            FormatPayload(
                ReportingAuditEvents.ExportCompleted,
                request,
                exportedRows,
                correlationId,
                fingerprint: fingerprint));

    public static Task LogExportFailedAsync(
        IAuditService audit,
        int userId,
        ReportExportRequestDto request,
        string safeReason,
        string? correlationId) =>
        audit.LogAsync(
            userId,
            AuditAction.ExportReport,
            "InstitutionalReport",
            null,
            null,
            null,
            FormatPayload(ReportingAuditEvents.ExportFailed, request, null, correlationId, safeReason));

    public static Task LogExportCancelledAsync(
        IAuditService audit,
        int userId,
        ReportExportRequestDto request,
        string? correlationId) =>
        audit.LogAsync(
            userId,
            AuditAction.ExportReport,
            "InstitutionalReport",
            null,
            null,
            null,
            FormatPayload(ReportingAuditEvents.ExportCancelled, request, null, correlationId));

    public static Task LogExportRejectedAsync(
        IAuditService audit,
        int userId,
        ReportExportRequestDto request,
        string errorCode,
        string? correlationId) =>
        audit.LogAsync(
            userId,
            AuditAction.ExportReport,
            "InstitutionalReport",
            null,
            null,
            null,
            FormatPayload(ReportingAuditEvents.ExportRejected, request, null, correlationId, errorCode));

    private static string FormatPayload(
        string eventName,
        ReportExportRequestDto request,
        int? rowCount,
        string? correlationId,
        string? safeReason = null,
        string? fingerprint = null)
    {
        var builder = new System.Text.StringBuilder();
        builder.Append("event=").Append(eventName);
        builder.Append(";reportType=").Append(request.BuildRequest.ReportType);
        builder.Append(";format=").Append(request.ExportFormat);
        builder.Append(";scope=").Append(request.ExportMode);
        if (rowCount is not null)
            builder.Append(";rows=").Append(rowCount.Value);
        if (!string.IsNullOrWhiteSpace(fingerprint))
            builder.Append(";fingerprint=").Append(fingerprint);
        if (!string.IsNullOrWhiteSpace(safeReason))
            builder.Append(";reason=").Append(safeReason);
        if (!string.IsNullOrWhiteSpace(correlationId))
            builder.Append(";correlationId=").Append(correlationId);
        return builder.ToString();
    }

    internal static string FormatPayloadForTest(
        string eventName,
        ReportExportRequestDto request,
        int? rowCount,
        string? correlationId,
        string? safeReason = null,
        string? fingerprint = null) =>
        FormatPayload(eventName, request, rowCount, correlationId, safeReason, fingerprint);
}
