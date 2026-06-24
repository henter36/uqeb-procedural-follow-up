using Uqeb.Api.Reporting.Operations;
using Xunit;

namespace Uqeb.Api.Tests.Reporting;

public class ReportingStructuredLogTests
{
    [Fact]
    public void AuditPayload_DoesNotIncludeSensitiveReportContentMarkers()
    {
        var request = new Uqeb.Api.Reporting.DTOs.ReportExportRequestDto
        {
            BuildRequest = new Uqeb.Api.Reporting.DTOs.ReportBuildRequestDto
            {
                Title = "عنوان سري",
                Filters = new Uqeb.Api.Reporting.DTOs.ReportFiltersDto
                {
                    Search = "موضوع حساس",
                },
            },
        };

        var payload = ReportingAuditWriter.FormatPayloadForTest(
            ReportingAuditEvents.ExportStarted,
            request,
            10,
            "corr",
            safeReason: null,
            fingerprint: null);

        Assert.DoesNotContain("عنوان سري", payload, StringComparison.Ordinal);
        Assert.DoesNotContain("موضوع حساس", payload, StringComparison.Ordinal);
        Assert.Contains("institutional_report.export_started", payload, StringComparison.Ordinal);
    }
}
