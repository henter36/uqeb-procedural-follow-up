using Microsoft.Extensions.Logging;

namespace Uqeb.Api.Reporting.Operations;

public interface IReportingAnalysisInstrumentation
{
    void RecordStage(
        string stage,
        double milliseconds,
        string reportType,
        int snapshotCount);

    void RecordTotal(
        double milliseconds,
        string reportType,
        int snapshotCount);
}

public sealed class ReportingAnalysisInstrumentation(
    IReportingMetrics metrics,
    ILogger<ReportingAnalysisInstrumentation> logger) : IReportingAnalysisInstrumentation
{
    public void RecordStage(
        string stage,
        double milliseconds,
        string reportType,
        int snapshotCount)
    {
        metrics.RecordAnalysisStageDuration(milliseconds, stage, reportType);
        logger.LogDebug(
            "Institutional report analysis stage completed. AnalysisStage={AnalysisStage} DurationMs={DurationMs} SnapshotCount={SnapshotCount} ReportType={ReportType}",
            stage,
            milliseconds,
            snapshotCount,
            reportType);
    }

    public void RecordTotal(
        double milliseconds,
        string reportType,
        int snapshotCount)
    {
        metrics.RecordAnalysisDuration(milliseconds, reportType, snapshotCount);
        logger.LogDebug(
            "Institutional report analysis completed. AnalysisStage=total DurationMs={DurationMs} SnapshotCount={SnapshotCount} ReportType={ReportType}",
            milliseconds,
            snapshotCount,
            reportType);
    }
}

public sealed class NullReportingAnalysisInstrumentation : IReportingAnalysisInstrumentation
{
    public static NullReportingAnalysisInstrumentation Instance { get; } = new();

    public void RecordStage(string stage, double milliseconds, string reportType, int snapshotCount)
    {
    }

    public void RecordTotal(double milliseconds, string reportType, int snapshotCount)
    {
    }
}
