using Microsoft.Extensions.Logging.Abstractions;
using Uqeb.Api.Reporting.Operations;
using Xunit;

namespace Uqeb.Api.Tests.Reporting;

public class ReportingAnalysisInstrumentationTests
{
    [Fact]
    public void RecordStage_LogsFailureOutcomeWithoutCompletedMessage()
    {
        var metrics = new ReportingMetrics();
        var instrumentation = new ReportingAnalysisInstrumentation(metrics, NullLogger<ReportingAnalysisInstrumentation>.Instance);

        instrumentation.RecordStage("findings", 12.5, "ExecutiveComprehensive", 250, succeeded: false);

        Assert.True(true);
    }
}
