using System.Diagnostics;
using System.Runtime;
using System.Text.Json;
using Uqeb.Api.Reporting.Enums;
using Uqeb.Api.Reporting.Exporters;
using Uqeb.Api.Reporting.Operations;
using Uqeb.Api.Reporting.Services;
using Uqeb.Api.Tests.Reporting.Visual;
using Xunit;
using Xunit.Abstractions;

namespace Uqeb.Api.Tests.Reporting.Performance;

public class ReportingAcceptanceBenchmarkTests
{
    private static readonly int[] DatasetSizes = [1_000, 5_000, 10_000, 20_000, 50_000];
    private readonly ITestOutputHelper _output;

    public ReportingAcceptanceBenchmarkTests(ITestOutputHelper output) => _output = output;

    [Theory]
    [InlineData(1_000)]
    [InlineData(5_000)]
    public void SmallBenchmark_WritesArtifacts_WhenAcceptanceEnabled(int datasetSize)
    {
        if (!IsAcceptanceEnabled())
        {
            _output.WriteLine("Skipping benchmark; set RUN_REPORTING_ACCEPTANCE=1 to execute.");
            return;
        }

        var result = RunScenario(datasetSize, ExportFormat.Xlsx);
        ReportingBenchmarkArtifactWriter.Append(result);
        Assert.True(result.Success, result.FailureReason);
    }

    [Theory]
    [InlineData(10_000)]
    [InlineData(20_000)]
    public void LargeBenchmark_WritesArtifacts_WhenLargeAcceptanceEnabled(int datasetSize)
    {
        if (!IsLargeAcceptanceEnabled())
        {
            _output.WriteLine("Skipping large benchmark; set RUN_REPORTING_ACCEPTANCE_LARGE=1 to execute.");
            return;
        }

        var result = RunScenario(datasetSize, ExportFormat.Xlsx);
        ReportingBenchmarkArtifactWriter.Append(result);
        Assert.True(result.Success, result.FailureReason);
    }

    private static bool IsAcceptanceEnabled() =>
        string.Equals(Environment.GetEnvironmentVariable("RUN_REPORTING_ACCEPTANCE"), "1", StringComparison.Ordinal);

    private static bool IsLargeAcceptanceEnabled() =>
        string.Equals(Environment.GetEnvironmentVariable("RUN_REPORTING_ACCEPTANCE_LARGE"), "1", StringComparison.Ordinal);

    internal static ReportingBenchmarkResult RunScenario(int datasetSize, ExportFormat format)
    {
        var result = new ReportingBenchmarkResult
        {
            DatasetSize = datasetSize,
            Format = format.ToString(),
            ManagedHeapBeforeBytes = GC.GetTotalMemory(forceFullCollection: true),
        };

        var stopwatch = Stopwatch.StartNew();
        try
        {
            var model = InstitutionalReportVisualFixtures.CreateBaseModel(
                totalMatched: datasetSize,
                exportedRows: datasetSize);
            model.Transactions = InstitutionalReportVisualFixtures.CreateTransactions(datasetSize);
            result.MatchedRows = model.TotalMatchedRows;
            result.LoadedRows = model.Transactions.Count;
            result.ExportedRows = model.ExportedDetailRows;

            var buildWatch = Stopwatch.StartNew();
            var manifest = InstitutionalReportVisualFixtures.RenderSections(model, ReportSectionId.TransactionDetails);
            buildWatch.Stop();
            result.BuildDurationMs = buildWatch.ElapsedMilliseconds;

            var renderWatch = Stopwatch.StartNew();
            _ = InstitutionalReportRenderer.RenderHtmlDocument(manifest);
            renderWatch.Stop();
            result.RenderDurationMs = renderWatch.ElapsedMilliseconds;

            var exportWatch = Stopwatch.StartNew();
            var bytes = format switch
            {
                ExportFormat.Docx => InstitutionalReportDocxExporter.Export(model, manifest, new()),
                ExportFormat.Xlsx => InstitutionalReportXlsxExporter.Export(model, manifest, new()),
                _ => InstitutionalReportXlsxExporter.Export(model, manifest, new()),
            };
            exportWatch.Stop();
            result.ExportDurationMs = exportWatch.ElapsedMilliseconds;
            result.OutputFileSizeBytes = bytes.LongLength;
            result.Success = bytes.Length > 0;
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.FailureReason = ex.GetType().Name;
        }
        finally
        {
            stopwatch.Stop();
            result.TotalDurationMs = stopwatch.ElapsedMilliseconds;
            result.PeakWorkingSetBytes = Process.GetCurrentProcess().PeakWorkingSet64;
            result.ManagedHeapAfterBytes = GC.GetTotalMemory(forceFullCollection: false);
            result.Gen0Collections = GC.CollectionCount(0);
            result.Gen1Collections = GC.CollectionCount(1);
            result.Gen2Collections = GC.CollectionCount(2);
        }

        return result;
    }
}

public sealed class ReportingBenchmarkResult
{
    public int DatasetSize { get; set; }
    public string Format { get; set; } = string.Empty;
    public int MatchedRows { get; set; }
    public int LoadedRows { get; set; }
    public int ExportedRows { get; set; }
    public int PartsCount { get; set; }
    public long BuildDurationMs { get; set; }
    public long RenderDurationMs { get; set; }
    public long ExportDurationMs { get; set; }
    public long TotalDurationMs { get; set; }
    public long PeakWorkingSetBytes { get; set; }
    public long ManagedHeapBeforeBytes { get; set; }
    public long ManagedHeapAfterBytes { get; set; }
    public int Gen0Collections { get; set; }
    public int Gen1Collections { get; set; }
    public int Gen2Collections { get; set; }
    public long OutputFileSizeBytes { get; set; }
    public int PdfPages { get; set; }
    public int TemporaryFilesCreated { get; set; }
    public int TemporaryFilesDeleted { get; set; }
    public bool Success { get; set; }
    public string? FailureReason { get; set; }
}

internal static class ReportingBenchmarkArtifactWriter
{
    private static readonly object Gate = new();

    internal static void Append(ReportingBenchmarkResult result)
    {
        var root = ResolveArtifactRoot();
        Directory.CreateDirectory(root);

        var jsonPath = Path.Combine(root, "reporting-benchmark-results.json");
        var mdPath = Path.Combine(root, "reporting-benchmark-results.md");

        lock (Gate)
        {
            var results = File.Exists(jsonPath)
                ? JsonSerializer.Deserialize<List<ReportingBenchmarkResult>>(File.ReadAllText(jsonPath)) ?? []
                : [];
            results.Add(result);
            File.WriteAllText(jsonPath, JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }));
            File.WriteAllText(mdPath, BuildMarkdown(results));
        }
    }

    private static string BuildMarkdown(IReadOnlyList<ReportingBenchmarkResult> results)
    {
        var lines = new List<string>
        {
            "# Reporting benchmark results",
            "",
            "| Dataset | Format | Build ms | Render ms | Export ms | Total ms | Peak WS | Output bytes | Success |",
            "| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: | --- |",
        };

        foreach (var result in results)
        {
            lines.Add(
                $"| {result.DatasetSize:N0} | {result.Format} | {result.BuildDurationMs} | {result.RenderDurationMs} | {result.ExportDurationMs} | {result.TotalDurationMs} | {result.PeakWorkingSetBytes} | {result.OutputFileSizeBytes} | {result.Success} |");
        }

        return string.Join(Environment.NewLine, lines);
    }

    private static string ResolveArtifactRoot()
    {
        var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (dir is not null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, ".git")))
            {
                return Path.Combine(dir.FullName, "artifacts", "reporting-acceptance");
            }

            dir = dir.Parent;
        }

        return Path.Combine(Directory.GetCurrentDirectory(), "artifacts", "reporting-acceptance");
    }
}
