using Microsoft.Extensions.Logging;
using Uqeb.Api.Reporting.Configuration;
using Uqeb.Api.Reporting.Operations;
using Uqeb.Api.Reporting.Services;
using Xunit;

namespace Uqeb.Api.Tests.Reporting;

public class ReportingAnalysisInstrumentationTests
{
    [Fact]
    public void RecordStage_LogsFailureOutcomeWithoutCompletedMessage()
    {
        var logger = new ListLogger();
        var instrumentation = new ReportingAnalysisInstrumentation(new ReportingMetrics(), logger);

        instrumentation.RecordStage("findings", 12.5, "ExecutiveComprehensive", 250, succeeded: false);

        Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Warning, logger.Entries[0].Level);
        Assert.Contains("failed", logger.Entries[0].Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("completed", logger.Entries[0].Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RecordStage_LogsSuccessOutcomeOnce()
    {
        var logger = new ListLogger();
        var instrumentation = new ReportingAnalysisInstrumentation(new ReportingMetrics(), logger);

        instrumentation.RecordStage("findings", 8.0, "ExecutiveComprehensive", 3, succeeded: true);

        Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Debug, logger.Entries[0].Level);
        Assert.Contains("completed", logger.Entries[0].Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RecordTotal_DoesNotRecordAnalysisDurationWhenFailed()
    {
        var metrics = new CapturingReportingMetrics();
        var instrumentation = new ReportingAnalysisInstrumentation(metrics, new ListLogger());

        instrumentation.RecordTotal(42.0, "ExecutiveComprehensive", 250, succeeded: false);

        Assert.Empty(metrics.AnalysisDurationCalls);
    }

    [Fact]
    public void RecordTotal_RecordsAnalysisDurationWhenSucceeded()
    {
        var metrics = new CapturingReportingMetrics();
        var instrumentation = new ReportingAnalysisInstrumentation(metrics, new ListLogger());

        instrumentation.RecordTotal(42.0, "ExecutiveComprehensive", 250, succeeded: true);

        Assert.Single(metrics.AnalysisDurationCalls);
        Assert.Equal(250, metrics.AnalysisDurationCalls[0].SnapshotCount);
        Assert.Equal("101-1000", metrics.AnalysisDurationCalls[0].Bucket);
    }

    private sealed class ListLogger : ILogger<ReportingAnalysisInstrumentation>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            Entries.Add((logLevel, formatter(state, exception)));

        private sealed class NullScope : IDisposable
        {
            public static NullScope Instance { get; } = new();

            public void Dispose()
            {
            }
        }
    }

    private sealed class CapturingReportingMetrics : IReportingMetrics
    {
        public List<(string Stage, bool Succeeded)> StageDurationCalls { get; } = [];

        public List<(int SnapshotCount, string Bucket)> AnalysisDurationCalls { get; } = [];

        public IDisposable TrackActiveExport() => throw new NotSupportedException();

        public void RecordRequest(string format, string reportType, string result, string? overflowAction = null) =>
            throw new NotSupportedException();

        public void RecordRejected(string format, string reportType) => throw new NotSupportedException();

        public void RecordCancellation(string format, string reportType) => throw new NotSupportedException();

        public void RecordFailure(string format, string reportType, string result) => throw new NotSupportedException();

        public void RecordBuildDuration(double milliseconds, string reportType) => throw new NotSupportedException();

        public void RecordAnalysisStageDuration(double milliseconds, string stage, string reportType, bool succeeded = true) =>
            StageDurationCalls.Add((stage, succeeded));

        public void RecordAnalysisDuration(double milliseconds, string reportType, int snapshotCount) =>
            AnalysisDurationCalls.Add((snapshotCount, ReportingMetrics.BucketSnapshotCount(snapshotCount)));

        public void RecordRenderDuration(double milliseconds, string format, string reportType) =>
            throw new NotSupportedException();

        public void RecordExportDuration(double milliseconds, string format, string reportType, string result) =>
            throw new NotSupportedException();

        public void RecordExportFileSize(long bytes, string format) => throw new NotSupportedException();

        public void RecordExportRows(int rows, string format) => throw new NotSupportedException();

        public void RecordPdfPages(int pages, string reportType) => throw new NotSupportedException();

        public void RecordPdfParts(int parts, string reportType) => throw new NotSupportedException();

        public void RecordTempCleanupFailure() => throw new NotSupportedException();

        public void RecordChromiumLaunchFailure() => throw new NotSupportedException();

        public void RecordRolloutDecision(
            ReportingRolloutEnforcementMode enforcementMode,
            ReportingRolloutDecision decision) =>
            throw new NotSupportedException();
    }
}
