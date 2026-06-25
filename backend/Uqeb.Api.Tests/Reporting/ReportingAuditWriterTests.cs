using Uqeb.Api.Models.Enums;
using Uqeb.Api.Reporting.DTOs;
using Uqeb.Api.Reporting.Enums;
using Uqeb.Api.Reporting.Operations;
using Uqeb.Api.Services;
using Xunit;

namespace Uqeb.Api.Tests.Reporting;

public class ReportingAuditWriterTests
{
    [Theory]
    [InlineData(ReportingAuditEvents.ExportStarted)]
    [InlineData(ReportingAuditEvents.ExportCompleted)]
    [InlineData(ReportingAuditEvents.ExportFailed)]
    [InlineData(ReportingAuditEvents.ExportCancelled)]
    [InlineData(ReportingAuditEvents.ExportRejected)]
    public async Task LogEvents_UseInstitutionalReportResourceType(string eventName)
    {
        var audit = new CapturingAuditService();
        var request = CreateRequest();
        var userId = 7;

        switch (eventName)
        {
            case ReportingAuditEvents.ExportStarted:
                await ReportingAuditWriter.LogExportStartedAsync(audit, userId, request, 10, "corr");
                break;
            case ReportingAuditEvents.ExportCompleted:
                await ReportingAuditWriter.LogExportCompletedAsync(audit, userId, request, 10, "fp", "corr");
                break;
            case ReportingAuditEvents.ExportFailed:
                await ReportingAuditWriter.LogExportFailedAsync(audit, userId, request, "export_failed", "corr");
                break;
            case ReportingAuditEvents.ExportCancelled:
                await ReportingAuditWriter.LogExportCancelledAsync(audit, userId, request, "corr");
                break;
            case ReportingAuditEvents.ExportRejected:
                await ReportingAuditWriter.LogExportRejectedAsync(audit, userId, request, ReportingErrorCodes.ConcurrencyLimit, "corr");
                break;
        }

        Assert.Equal(ReportingAuditWriter.ResourceType, audit.EntityName);
        Assert.Contains(eventName, audit.NewValue, StringComparison.Ordinal);
    }

    [Fact]
    public void FormatPayloadForTest_DoesNotIncludeSensitiveReportContent()
    {
        var request = CreateRequest();
        request.BuildRequest.Title = "عنوان سري";
        request.BuildRequest.Filters.Search = "موضوع حساس";

        var payload = ReportingAuditWriter.FormatPayloadForTest(
            ReportingAuditEvents.ExportStarted,
            request,
            10,
            "corr");

        Assert.DoesNotContain("عنوان سري", payload, StringComparison.Ordinal);
        Assert.DoesNotContain("موضوع حساس", payload, StringComparison.Ordinal);
        Assert.Contains("institutional_report.export_started", payload, StringComparison.Ordinal);
    }

    private static ReportExportRequestDto CreateRequest() => new()
    {
        ExportFormat = ExportFormat.Pdf,
        ExportMode = ExportMode.FullReport,
        BuildRequest = new ReportBuildRequestDto
        {
            ReportType = InstitutionalReportType.ExecutiveComprehensive,
        },
    };

    private sealed class CapturingAuditService : IAuditService
    {
        public string? EntityName { get; private set; }
        public string? NewValue { get; private set; }

        public void TrackLog(
            int userId,
            AuditAction action,
            string? entityName,
            int? entityId,
            int? transactionId,
            string? oldValue,
            string? newValue)
        {
            EntityName = entityName;
            NewValue = newValue;
        }

        public Task LogAsync(
            int userId,
            AuditAction action,
            string? entityName,
            int? entityId,
            int? transactionId,
            string? oldValue,
            string? newValue)
        {
            EntityName = entityName;
            NewValue = newValue;
            return Task.CompletedTask;
        }
    }
}
