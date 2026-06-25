using Microsoft.Extensions.Logging;

namespace Uqeb.Api.Reporting.Operations;

public interface IReportingAnalysisInstrumentation
{
    void RecordStage(
        string stage,
        double milliseconds,
        string reportType,
        int snapshotCount,
        bool succeeded = true);

    void RecordTotal(
        double milliseconds,
        string reportType,
        int snapshotCount,
        bool succeeded = true);
}

public sealed class ReportingAnalysisInstrumentation(
    IReportingMetrics metrics,
    ILogger<ReportingAnalysisInstrumentation> logger) : IReportingAnalysisInstrumentation
{
    public void RecordStage(
        string stage,
        double milliseconds,
        string reportType,
        int snapshotCount,
        bool succeeded = true)
    {
        metrics.RecordAnalysisStageDuration(milliseconds, stage, reportType, succeeded);
        if (succeeded)
        {
            logger.LogDebug(
                "Institutional report analysis stage completed. AnalysisStage={AnalysisStage} DurationMs={DurationMs} SnapshotCount={SnapshotCount} ReportType={ReportType}",
                stage,
                milliseconds,
                snapshotCount,
                reportType);
            return;
        }

        logger.LogWarning(
            "Institutional report analysis stage failed. AnalysisStage={AnalysisStage} DurationMs={DurationMs} SnapshotCount={SnapshotCount} ReportType={ReportType}",
            stage,
            milliseconds,
            snapshotCount,
            reportType);
    }

    public void RecordTotal(
        double milliseconds,
        string reportType,
        int snapshotCount,
        bool succeeded = true)
    {
        if (succeeded)
            metrics.RecordAnalysisDuration(milliseconds, reportType, snapshotCount);

        if (succeeded)
        {
            logger.LogDebug(
                "Institutional report analysis completed. AnalysisStage=total DurationMs={DurationMs} SnapshotCount={SnapshotCount} ReportType={ReportType}",
                milliseconds,
                snapshotCount,
                reportType);
            return;
        }

        logger.LogWarning(
            "Institutional report analysis failed. AnalysisStage=total DurationMs={DurationMs} SnapshotCount={SnapshotCount} ReportType={ReportType}",
            milliseconds,
            snapshotCount,
            reportType);
    }
}

public sealed class NullReportingAnalysisInstrumentation : IReportingAnalysisInstrumentation
{
    public static NullReportingAnalysisInstrumentation Instance { get; } = new();

    public void RecordStage(string stage, double milliseconds, string reportType, int snapshotCount, bool succeeded = true)
    {
    }

    public void RecordTotal(double milliseconds, string reportType, int snapshotCount, bool succeeded = true)
    {
    }
}
