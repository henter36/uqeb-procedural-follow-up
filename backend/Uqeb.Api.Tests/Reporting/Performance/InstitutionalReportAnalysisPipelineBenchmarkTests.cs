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
    private static readonly DateTime PeriodFrom = new(2026, 1, 1);
    private static readonly DateTime PeriodTo = new(2026, 6, 15);
    private static readonly string[] ExpectedStages =
    [
        "kpis",
        "critical_cases",
        "departments",
        "external_parties",
        "categories",
        "priorities",
        "bottlenecks",
        "data_quality",
        "completeness_rate",
        "findings",
        "recommendations",
        "time_series",
        "executive_insights",
        "methodology"
    ];

    private readonly ITestOutputHelper _output;

    public InstitutionalReportAnalysisPipelineBenchmarkTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public void AnalysisPipeline_DoesNotWriteArtifacts_WhenBenchmarkDisabled()
    {
        var artifactPath = GetArtifactPath(100);
        if (File.Exists(artifactPath))
            File.Delete(artifactPath);

        RunBenchmarkScenario(100, writeArtifact: false);

        Assert.False(File.Exists(artifactPath));
    }

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

        var capture = RunBenchmarkScenario(snapshotCount, writeArtifact: true);
        var artifactPath = GetArtifactPath(snapshotCount);
        Assert.True(File.Exists(artifactPath));

        using var document = JsonDocument.Parse(File.ReadAllText(artifactPath));
        var root = document.RootElement;
        Assert.Equal("reporting-analysis-pipeline", root.GetProperty("scenarioId").GetString());
        Assert.Equal(snapshotCount, root.GetProperty("snapshotCount").GetInt32());
        Assert.True(root.GetProperty("totalMilliseconds").GetDouble() >= 0);
        Assert.Equal(PeriodFrom.ToString("O"), root.GetProperty("periodFrom").GetString());
        Assert.Equal(PeriodTo.ToString("O"), root.GetProperty("periodTo").GetString());

        var stages = root.GetProperty("stages");
        foreach (var expectedStage in ExpectedStages)
        {
            Assert.True(stages.TryGetProperty(expectedStage, out _), $"Missing stage artifact: {expectedStage}");
            Assert.True(stages.GetProperty(expectedStage).GetDouble() >= 0);
        }

        Assert.Equal(ExpectedStages.Length, stages.EnumerateObject().Count());
        Assert.True(capture.LastTotalSucceeded);
        Assert.All(capture.StageOutcomes.Values, outcome => Assert.Equal("success", outcome));
        _output.WriteLine($"Wrote analysis pipeline benchmark artifact: {artifactPath}");
    }

    private CapturingReportingAnalysisInstrumentation RunBenchmarkScenario(int snapshotCount, bool writeArtifact)
    {
        var capture = new CapturingReportingAnalysisInstrumentation();
        var snapshots = CreateSnapshots(snapshotCount);
        Assert.All(snapshots, snapshot =>
        {
            Assert.InRange(snapshot.IncomingDate, PeriodFrom, PeriodTo);
        });

        var metrics = InstitutionalReportMetricsCalculator.Calculate(snapshots, PeriodTo);

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
                        DateFrom = PeriodFrom,
                        DateTo = PeriodTo
                    }
                },
                Metadata = new ReportMetadataDto
                {
                    GeneratedAt = PeriodTo,
                    PeriodFrom = PeriodFrom,
                    PeriodTo = PeriodTo,
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

        if (writeArtifact && IsBenchmarkEnabled())
        {
            var artifact = new
            {
                scenarioId = "reporting-analysis-pipeline",
                snapshotCount,
                periodFrom = PeriodFrom,
                periodTo = PeriodTo,
                totalMilliseconds = capture.LastTotalMilliseconds,
                stages = capture.StageMilliseconds
            };

            var root = PerformanceBaselineCatalog.ResolveFromRoot(PerformanceBaselineCatalog.ArtifactsRelativeDirectory);
            Directory.CreateDirectory(root);
            File.WriteAllText(GetArtifactPath(snapshotCount), JsonSerializer.Serialize(artifact, new JsonSerializerOptions { WriteIndented = true }));
        }

        return capture;
    }

    private static string GetArtifactPath(int snapshotCount)
    {
        var root = PerformanceBaselineCatalog.ResolveFromRoot(PerformanceBaselineCatalog.ArtifactsRelativeDirectory);
        return Path.Combine(root, $"analysis-pipeline-{snapshotCount}.json");
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
                IncomingDate = PeriodFrom.AddDays(i % 30),
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
        public Dictionary<string, string> StageOutcomes { get; } = new(StringComparer.Ordinal);
        public double LastTotalMilliseconds { get; private set; }
        public bool LastTotalSucceeded { get; private set; }

        public void RecordStage(string stage, double milliseconds, string reportType, int snapshotCount, bool succeeded = true)
        {
            StageMilliseconds[stage] = milliseconds;
            StageOutcomes[stage] = succeeded ? "success" : "failed";
        }

        public void RecordTotal(double milliseconds, string reportType, int snapshotCount, bool succeeded = true)
        {
            LastTotalMilliseconds = milliseconds;
            LastTotalSucceeded = succeeded;
        }
    }
}
