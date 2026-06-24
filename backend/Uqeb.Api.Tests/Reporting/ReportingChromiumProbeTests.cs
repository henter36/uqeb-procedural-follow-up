using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Playwright;
using Uqeb.Api.Reporting.Configuration;
using Uqeb.Api.Reporting.Operations;
using Xunit;

namespace Uqeb.Api.Tests.Reporting;

[Collection(PlaywrightTestCollection.Name)]
public class ReportingChromiumProbeTests
{
    private static bool RequirePlaywrightInCi =>
        string.Equals(Environment.GetEnvironmentVariable("REQUIRE_PLAYWRIGHT_TESTS"), "1", StringComparison.Ordinal);

    private static async Task<bool> IsPlaywrightAvailableAsync()
    {
        try
        {
            using var playwright = await Playwright.CreateAsync();
            await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });
            await browser.CloseAsync();
            return true;
        }
        catch (PlaywrightException)
        {
            return false;
        }
    }

    [Fact]
    public async Task ProbeLaunchOnly_WhenExecutableMissing_ReturnsExecutableMissing()
    {
        var originalPath = Environment.GetEnvironmentVariable("PLAYWRIGHT_BROWSERS_PATH");
        var probe = CreateProbe();

        try
        {
            Environment.SetEnvironmentVariable(
                "PLAYWRIGHT_BROWSERS_PATH",
                Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")));

            var result = await probe.ProbeLaunchOnlyAsync();

            Assert.Equal(ReportingChromiumProbeState.ExecutableMissing, result.State);
            Assert.False(result.ExecutableAvailable);
            Assert.False(result.LaunchSuccessful);
            Assert.Equal("Chromium executable is not installed.", result.Summary);
        }
        finally
        {
            Environment.SetEnvironmentVariable("PLAYWRIGHT_BROWSERS_PATH", originalPath);
        }
    }

    [Fact]
    public async Task ProbeLaunchOnly_WhenExecutablePathMissing_ReturnsExecutableMissing()
    {
        var originalPath = Environment.GetEnvironmentVariable("PLAYWRIGHT_BROWSERS_PATH");
        var probe = CreateProbe();

        try
        {
            Environment.SetEnvironmentVariable(
                "PLAYWRIGHT_BROWSERS_PATH",
                Path.Combine(Path.GetTempPath(), "uqeb-nonexistent-chromium-" + Guid.NewGuid().ToString("N")));

            var result = await probe.ProbeLaunchOnlyAsync();

            Assert.Equal(ReportingChromiumProbeState.ExecutableMissing, result.State);
            Assert.False(result.LaunchSuccessful);
        }
        finally
        {
            Environment.SetEnvironmentVariable("PLAYWRIGHT_BROWSERS_PATH", originalPath);
        }
    }

    [Fact]
    public async Task ProbeLaunchOnly_WhenChromiumAvailable_ReturnsReady()
    {
        if (!await IsPlaywrightAvailableAsync())
        {
            if (RequirePlaywrightInCi)
            {
                Assert.Fail("Playwright Chromium is required in CI but is not available.");
            }

            return;
        }

        var probe = CreateProbe();
        var result = await probe.ProbeLaunchOnlyAsync();

        Assert.Equal(ReportingChromiumProbeState.Ready, result.State);
        Assert.True(result.ExecutableAvailable);
        Assert.True(result.LaunchSuccessful);
        Assert.Equal("Chromium launch probe succeeded.", result.Summary);
    }

    private static ReportingChromiumProbe CreateProbe(ReportingOptions? options = null)
    {
        return new ReportingChromiumProbe(
            Options.Create(options ?? new ReportingOptions
            {
                ChromiumProbeTimeoutSeconds = 15,
                ReadinessCacheSeconds = 1,
            }),
            new MemoryCache(new MemoryCacheOptions()),
            new ReportingMetrics(),
            NullLogger<ReportingChromiumProbe>.Instance);
    }
}
