using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Uqeb.Api.Reporting.Configuration;
using Uqeb.Api.Reporting.Services;
using Xunit;

namespace Uqeb.Api.Tests.Reporting;

public class ReportingOptionsValidationTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void ValidateDetailLimit_RejectsNonPositiveValues(int detailLimit)
    {
        var ex = Assert.Throws<ArgumentOutOfRangeException>(() => ReportingOptions.ValidateDetailLimit(detailLimit));
        Assert.Equal("detailLimit", ex.ParamName);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(10_000)]
    public void ValidateDetailLimit_AcceptsPositiveValues(int detailLimit)
    {
        ReportingOptions.ValidateDetailLimit(detailLimit);
    }

    [Fact]
    public void ForPreview_RejectsZeroDetailLimit()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ReportAssemblyOptions.ForPreview(5, 0));
    }

    [Fact]
    public void Validate_ThrowsWhenAnalysisSectionMissing()
    {
        var options = new ReportingOptions { Analysis = null! };
        var ex = Assert.Throws<InvalidOperationException>(() => options.Validate());
        Assert.Contains("Reporting:Analysis", ex.Message);
    }

    [Fact]
    public void HostFailsFast_WhenMaxPdfDetailRowsIsInvalid()
    {
        Assert.ThrowsAny<Exception>(() =>
        {
            using var factory = new InvalidReportingOptionsWebApplicationFactory();
            _ = factory.CreateClient();
        });
    }

    private sealed class InvalidReportingOptionsWebApplicationFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            HealthTestHostBuilder.Configure(
                builder,
                extraConfig: new Dictionary<string, string?>
                {
                    ["Reporting:MaxPdfDetailRows"] = "0",
                    ["FeatureFlags:InstitutionalReports"] = "false",
                });
        }
    }
}
