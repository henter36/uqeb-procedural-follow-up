using System.Text.Json;
using Uqeb.Api.Models.Enums;
using Uqeb.Api.Reporting.Configuration;
using Uqeb.Api.Reporting.DTOs;
using Uqeb.Api.Reporting.Enums;
using Uqeb.Api.Reporting.Models;
using Uqeb.Api.Reporting.Operations;
using Uqeb.Api.Reporting.Services;
using Uqeb.Api.Tests.Performance.Fixtures;
using Xunit;
using Xunit.Abstractions;

namespace Uqeb.Api.Tests.Reporting.Performance;

public class InstitutionalReportAnalysisPipelineBenchmarkTests
{
    private readonly ITestOutputHelper _output;

    public InstitutionalReportAnalysisPipelineBenchmarkTests(ITestOutputHelper output) => _output = output;

    [Theory]
    [InlineData(100)]
    [InlineData(1_000)]
    public void AnalysisPipeline_WritesStageArtifacts_WhenBenchmarkEnabled(int snapshotCount)
    {
        if (!IsBenchmarkEnabled())
        {
            _output.WriteLine("Skipping analysis pipeline benchmark; set RUN_REPORTING_ANALYSIS_BENCHMARK=1.");
            return;
        }

        var capture = new CapturingReportingAnalysisInstrumentation();
        var snapshots = CreateSnapshots(snapshotCount);
        var metrics = InstitutionalReportMetricsCalculator.Calculate(snapshots, new DateTime(2026, 6, 15));

        _ = InstitutionalReportAnalysisService.Build(
            new InstitutionalReportAnalysisService.InstitutionalReportAnalysisInput
            {
                Request = new ReportBuildRequestDto
                {
                    ReportType = InstitutionalReportType.ExecutiveComprehensive,
                    SectionIds =
                    [
                        ReportSectionId.KeyPerformanceIndicators,
                        ReportSectionId.SignificantFindings,
                        ReportSectionId.DepartmentPerformance
                    ],
                    Filters = new ReportFiltersDto
                    {
                        DateFrom = new DateTime(2026, 1, 1),
                        DateTo = new DateTime(2026, 6, 15)
                    }
                },
                Metadata = new ReportMetadataDto
                {
                    GeneratedAt = new DateTime(2026, 6, 15),
                    PeriodFrom = new DateTime(2026, 1, 1),
                    PeriodTo = new DateTime(2026, 6, 15),
                    Title = "Analysis pipeline benchmark",
                    ReportNumber = "REP-BENCH"
                },
                Filters = new ReportFiltersDto(),
                CurrentMetrics = metrics,
                CurrentSnapshots = snapshots,
                PreviousMetrics = null,
                PreviousSnapshots = [],
                Options = new ReportingAnalysisOptions(),
                DetailLimit = 500,
                DetailRowsTruncated = false
            },
            capture);

        var artifact = new
        {
            scenarioId = "reporting-analysis-pipeline",
            snapshotCount,
            totalMilliseconds = capture.LastTotalMilliseconds,
            stages = capture.StageMilliseconds
        };

        var root = PerformanceBaselineCatalog.ResolveFromRoot(PerformanceBaselineCatalog.ArtifactsRelativeDirectory);
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, $"analysis-pipeline-{snapshotCount}.json");
        File.WriteAllText(path, JsonSerializer.Serialize(artifact, new JsonSerializerOptions { WriteIndented = true }));
        _output.WriteLine($"Wrote analysis pipeline benchmark artifact: {path}");
    }

    private static bool IsBenchmarkEnabled() =>
        string.Equals(Environment.GetEnvironmentVariable("RUN_REPORTING_ANALYSIS_BENCHMARK"), "1", StringComparison.Ordinal);

    private static List<TransactionReportSnapshot> CreateSnapshots(int count)
    {
        var snapshots = new List<TransactionReportSnapshot>(count);
        for (var i = 0; i < count; i++)
        {
            snapshots.Add(new TransactionReportSnapshot
            {
                TransactionId = i + 1,
                TrackingNumber = $"INT-{i + 1:D6}",
                IncomingNumber = $"IN-{i + 1:D6}",
                IncomingDate = new DateTime(2026, 6, 1).AddDays(i % 30),
                Subject = $"Benchmark {i + 1}",
                IncomingParty = "Benchmark party",
                CategoryName = "Benchmark",
                Priority = i % 3 == 0 ? Priority.Urgent : Priority.Normal,
                Status = i % 2 == 0 ? TransactionStatus.New : TransactionStatus.Closed,
                ResponsibleDepartment = i % 2 == 0 ? "Dept A" : "Dept B",
                ResponsibleDepartmentId = i % 2 == 0 ? 1 : 2,
                IsOpen = i % 2 == 0,
                IsClosed = i % 2 != 0,
                IsOverdue = i % 5 == 0,
                ElapsedDays = i % 20,
                ActiveAssignmentCount = i % 4,
                PendingReplyAssignmentCount = i % 4,
            });
        }

        return snapshots;
    }

    private sealed class CapturingReportingAnalysisInstrumentation : IReportingAnalysisInstrumentation
    {
        public Dictionary<string, double> StageMilliseconds { get; } = new(StringComparer.Ordinal);
        public double LastTotalMilliseconds { get; private set; }

        public void RecordStage(string stage, double milliseconds, string reportType, int snapshotCount) =>
            StageMilliseconds[stage] = milliseconds;

        public void RecordTotal(double milliseconds, string reportType, int snapshotCount) =>
            LastTotalMilliseconds = milliseconds;
    }
}
