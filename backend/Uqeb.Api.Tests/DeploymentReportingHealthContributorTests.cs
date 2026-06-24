using Uqeb.Api.Reporting.Operations;
using Uqeb.Api.Reporting.Services;
using Uqeb.Api.Services.Health;
using Xunit;

namespace Uqeb.Api.Tests;

public class DeploymentReportingHealthContributorTests
{
    [Fact]
    public async Task EvaluateAsync_WhenFeatureDisabled_ReturnsNotApplicableChecks()
    {
        var contributor = new DeploymentReportingHealthContributor(
            Microsoft.Extensions.Options.Options.Create(new Uqeb.Api.Configuration.FeatureFlagsSettings
            {
                InstitutionalReports = false,
            }),
            new StubChromiumProbe(true),
            new StubSequenceProbe(true));

        var result = await contributor.EvaluateAsync();

        Assert.False(result.FeatureEnabled);
        Assert.True(result.IsReady);
        Assert.Contains(result.Checks, check => check.Name == "playwrightChromium" && check.Status == "not_applicable");
    }

    [Fact]
    public async Task EvaluateAsync_WhenChromiumMissing_ReturnsNotReady()
    {
        var contributor = new DeploymentReportingHealthContributor(
            Microsoft.Extensions.Options.Options.Create(new Uqeb.Api.Configuration.FeatureFlagsSettings
            {
                InstitutionalReports = true,
            }),
            new StubChromiumProbe(false),
            new StubSequenceProbe(true));

        var result = await contributor.EvaluateAsync();

        Assert.True(result.FeatureEnabled);
        Assert.False(result.IsReady);
        Assert.Contains(result.Checks, check => check.Name == "playwrightChromium" && check.Status == "fail");
    }

    private sealed class StubChromiumProbe(bool ready) : IReportingChromiumProbe
    {
        public Task<ReportingChromiumProbeResult> ProbeAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(CreateResult());

        public Task<ReportingChromiumProbeResult> ProbeLaunchOnlyAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(CreateResult());

        private ReportingChromiumProbeResult CreateResult() => new()
        {
            State = ready ? ReportingChromiumProbeState.Ready : ReportingChromiumProbeState.ExecutableMissing,
            ExecutableAvailable = ready,
            LaunchSuccessful = ready,
            Summary = ready ? "ready" : "missing",
        };
    }

    private sealed class StubSequenceProbe(bool available) : IReportNumberSequenceSchemaProbe
    {
        public Task<bool> IsTableAvailableAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(available);
    }
}
