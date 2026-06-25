using System.Text.Json;
using Uqeb.Api.Tests.Performance.Fixtures;
using Xunit;

namespace Uqeb.Api.Tests.Performance;

public class PerformanceBaselineFixtureTests
{
    [Fact]
    public void SchemaFile_IsValidJsonObject()
    {
        var schemaPath = PerformanceBaselineCatalog.ResolveFromRoot(PerformanceBaselineCatalog.SchemaRelativePath);
        Assert.True(File.Exists(schemaPath), $"Missing schema: {schemaPath}");

        using var document = JsonDocument.Parse(File.ReadAllText(schemaPath));
        Assert.Equal(JsonValueKind.Object, document.RootElement.ValueKind);
        Assert.True(document.RootElement.TryGetProperty("required", out var required));
        Assert.Equal(JsonValueKind.Array, required.ValueKind);
    }

    [Fact]
    public void TemplateRecords_IncludeRequiredBaselineScenarios()
    {
        var path = PerformanceBaselineCatalog.ResolveFromRoot(PerformanceBaselineCatalog.RecordsTemplateRelativePath);
        Assert.True(File.Exists(path), $"Missing template: {path}");

        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var records = document.RootElement.GetProperty("records");
        var scenarioIds = records.EnumerateArray()
            .Select(record => record.GetProperty("scenarioId").GetString())
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .ToHashSet(StringComparer.Ordinal);

        Assert.Contains("api-read-smoke", scenarioIds);
        Assert.Contains("reporting-export-xlsx-1000", scenarioIds);
        Assert.Contains("reporting-analysis-pipeline", scenarioIds);
    }

    [Fact]
    public void BaselineK6Scenario_ReferencesDocumentedThresholds()
    {
        var scenarioPath = PerformanceBaselineCatalog.ResolveFromRoot("tests/performance/baseline/read-smoke-baseline.js");
        var thresholdsPath = PerformanceBaselineCatalog.ResolveFromRoot("tests/performance/baseline/helpers/baseline-thresholds.js");
        var corePath = PerformanceBaselineCatalog.ResolveFromRoot("tests/performance/read-smoke.js");
        Assert.True(File.Exists(scenarioPath), $"Missing k6 baseline scenario: {scenarioPath}");
        Assert.True(File.Exists(thresholdsPath), $"Missing k6 baseline thresholds: {thresholdsPath}");
        Assert.True(File.Exists(corePath), $"Missing k6 core scenario: {corePath}");

        var scenarioContent = File.ReadAllText(scenarioPath);
        var thresholdsContent = File.ReadAllText(thresholdsPath);
        Assert.Contains("read-smoke.js", scenarioContent, StringComparison.Ordinal);
        Assert.Contains("api-read-smoke", thresholdsContent, StringComparison.Ordinal);
        Assert.Contains("p(95)<1500", thresholdsContent, StringComparison.Ordinal);
        Assert.Contains("rate<0.01", thresholdsContent, StringComparison.Ordinal);
    }
}
