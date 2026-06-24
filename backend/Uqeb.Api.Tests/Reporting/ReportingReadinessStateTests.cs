using Uqeb.Api.Reporting.DTOs;
using Uqeb.Api.Reporting.Services;
using Xunit;

namespace Uqeb.Api.Tests.Reporting;

public class ReportingReadinessStateTests
{
    [Fact]
    public void ResolveReadinessState_ReturnsNotApplicable_WhenFeatureDisabledViaDto()
    {
        var dto = new ReportingReadinessDto
        {
            State = ReportingReadinessState.NotApplicable,
            FeatureEnabled = false,
        };

        Assert.Equal(ReportingReadinessState.NotApplicable, dto.State);
        Assert.False(dto.FeatureEnabled);
    }

    [Theory]
    [InlineData(false, false, false, false, false, ReportingReadinessState.Unavailable)]
    [InlineData(true, false, false, false, false, ReportingReadinessState.Degraded)]
    [InlineData(true, true, true, false, true, ReportingReadinessState.Degraded)]
    [InlineData(true, true, true, true, true, ReportingReadinessState.Ready)]
    public void ResolveReadinessState_MapsDependencySignals(
        bool configurationValid,
        bool stylesheetAvailable,
        bool tempWritable,
        bool chromiumLaunchSuccessful,
        bool databaseReachable,
        ReportingReadinessState expected)
    {
        var state = ReportingReadinessService.ResolveReadinessState(
            configurationValid,
            stylesheetAvailable,
            tempWritable,
            chromiumLaunchSuccessful,
            databaseReachable,
            exportConcurrencyAvailable: true);

        Assert.Equal(expected, state);
    }

    [Fact]
    public void ResolveReadinessState_ReturnsUnavailable_WhenConfigurationInvalid()
    {
        var state = ReportingReadinessService.ResolveReadinessState(
            configurationValid: false,
            stylesheetAvailable: true,
            tempWritable: true,
            chromiumLaunchSuccessful: true,
            databaseReachable: true,
            exportConcurrencyAvailable: true);

        Assert.Equal(ReportingReadinessState.Unavailable, state);
    }
}
