using Uqeb.Api.Tests.Reporting.Visual;
using Xunit;
using Xunit.Abstractions;

namespace Uqeb.Api.Tests.Reporting.Performance;

public class ReportingSyntheticSeedGeneratorTests
{
    private readonly ITestOutputHelper _output;

    public ReportingSyntheticSeedGeneratorTests(ITestOutputHelper output) => _output = output;

    [Theory]
    [InlineData(1_000)]
    [InlineData(5_000)]
    [InlineData(10_000)]
    [InlineData(20_000)]
    [InlineData(50_000)]
    public void GenerateSyntheticDataset_HasExpectedShape(int size)
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("RUN_REPORTING_ACCEPTANCE"), "1", StringComparison.Ordinal))
        {
            _output.WriteLine("Skipping seed generation; set RUN_REPORTING_ACCEPTANCE=1.");
            return;
        }

        var model = InstitutionalReportVisualFixtures.CreateBaseModel(totalMatched: size, exportedRows: size);
        model.Transactions = InstitutionalReportVisualFixtures.CreateTransactions(size);

        Assert.Equal(size, model.TotalMatchedRows);
        Assert.Equal(size, model.Transactions.Count);
        Assert.Contains(model.Transactions, t => t.ElapsedDays > 30);
        Assert.Contains(model.Transactions, t => t.ElapsedDays <= 30);
    }
}
