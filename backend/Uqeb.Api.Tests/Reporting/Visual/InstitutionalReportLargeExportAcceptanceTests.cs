using System.Diagnostics;
using Uqeb.Api.Reporting.Configuration;
using Uqeb.Api.Reporting.Enums;
using Uqeb.Api.Reporting.Exporters;
using Uqeb.Api.Reporting.Services;
using Xunit;
using Xunit.Abstractions;

namespace Uqeb.Api.Tests.Reporting.Visual;

public class InstitutionalReportLargeExportAcceptanceTests
{
    private readonly ITestOutputHelper _output;

    public InstitutionalReportLargeExportAcceptanceTests(ITestOutputHelper output) => _output = output;

    [Theory]
    [InlineData(500)]
    [InlineData(1000)]
    public void LargeDataset_KpiUsesFullPopulationWhileDetailsAreSampled(int datasetSize)
    {
        var previewLimit = 500;
        var model = InstitutionalReportVisualFixtures.CreateBaseModel(
            totalMatched: datasetSize,
            exportedRows: Math.Min(datasetSize, previewLimit),
            truncated: datasetSize > previewLimit);

        Assert.Equal(datasetSize, model.TotalMatchedRows);
        Assert.Equal(Math.Min(datasetSize, previewLimit), model.ExportedDetailRows);
        Assert.Equal(datasetSize > previewLimit, model.DetailRowsTruncated);

        var totalKpi = model.Summary.KpiCards.First(c => c.Key == "total").Value;
        Assert.Equal(datasetSize.ToString("N0"), totalKpi);
        Assert.True(model.Transactions.Count <= previewLimit);
    }

    [Theory]
    [InlineData(500)]
    [InlineData(1000)]
    [InlineData(5000)]
    public void XlsxExporter_HandlesLargeDetailSetsWithoutThrowing(int rowCount)
    {
        var model = InstitutionalReportVisualFixtures.CreateBaseModel(
            totalMatched: rowCount,
            exportedRows: rowCount);
        model.Transactions = InstitutionalReportVisualFixtures.CreateTransactions(rowCount);

        var manifest = InstitutionalReportVisualFixtures.RenderSections(model, ReportSectionId.TransactionDetails);
        var sw = Stopwatch.StartNew();
        var bytes = InstitutionalReportXlsxExporter.Export(model, manifest, new());
        sw.Stop();

        _output.WriteLine($"Dataset={rowCount} DurationMs={sw.ElapsedMilliseconds} SizeBytes={bytes.Length}");
        Assert.NotEmpty(bytes);
        Assert.True(bytes.Length > rowCount);
    }

    [Fact]
    public void ReportingOptions_ResolveDetailLimitPerFormat()
    {
        var options = new ReportingOptions
        {
            MaxPreviewDetailRows = 500,
            MaxPdfDetailRowsPerPart = 5000,
            MaxDocxDetailRows = 20000,
            MaxXlsxDetailRows = 100000,
            MaxHtmlDetailRows = 20000,
        };

        Assert.Equal(5000, options.ResolveDetailLimit(ExportFormat.Pdf));
        Assert.Equal(20000, options.ResolveDetailLimit(ExportFormat.Docx));
        Assert.Equal(100000, options.ResolveDetailLimit(ExportFormat.Xlsx));
        Assert.Equal(20000, options.ResolveDetailLimit(ExportFormat.Html));
    }
}
