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

    [Theory]
    [InlineData(PerformanceBaselineCatalog.ApiReadSmokeTemplateFile, "api-read-smoke")]
    [InlineData(PerformanceBaselineCatalog.ReportingExportTemplateFile, "reporting-export-xlsx-1000")]
    public void TemplateRecords_ExposeRequiredBaselineFields(string fileName, string expectedScenarioId)
    {
        var path = Path.Combine(
            PerformanceBaselineCatalog.ResolveFromRoot(PerformanceBaselineCatalog.RecordsRelativeDirectory),
            fileName);

        Assert.True(File.Exists(path), $"Missing template: {path}");

        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var root = document.RootElement;

        Assert.Equal(expectedScenarioId, root.GetProperty("scenarioId").GetString());
        Assert.True(root.TryGetProperty("thresholds", out _));
        Assert.True(root.TryGetProperty("metrics", out _));
        Assert.True(root.TryGetProperty("environment", out _));
    }

    [Fact]
    public void BaselineK6Scenario_ReferencesDocumentedThresholds()
    {
        var scenarioPath = PerformanceBaselineCatalog.ResolveFromRoot("tests/performance/baseline/read-smoke-baseline.js");
        var thresholdsPath = PerformanceBaselineCatalog.ResolveFromRoot("tests/performance/baseline/helpers/baseline-thresholds.js");
        Assert.True(File.Exists(scenarioPath), $"Missing k6 baseline scenario: {scenarioPath}");
        Assert.True(File.Exists(thresholdsPath), $"Missing k6 baseline thresholds: {thresholdsPath}");

        var scenarioContent = File.ReadAllText(scenarioPath);
        var thresholdsContent = File.ReadAllText(thresholdsPath);
        Assert.Contains("api-read-smoke", scenarioContent, StringComparison.Ordinal);
        Assert.Contains("api-read-smoke", thresholdsContent, StringComparison.Ordinal);
        Assert.Contains("p(95)<1500", thresholdsContent, StringComparison.Ordinal);
        Assert.Contains("rate<0.01", thresholdsContent, StringComparison.Ordinal);
    }
}
