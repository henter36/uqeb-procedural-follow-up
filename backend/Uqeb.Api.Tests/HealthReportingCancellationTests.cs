using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Uqeb.Api.Services.Health;
using Xunit;

namespace Uqeb.Api.Tests;

public class HealthReportingCancellationTests : IClassFixture<CancelledReportingHealthWebApplicationFactory>
{
    private readonly HttpClient _client;

    public HealthReportingCancellationTests(CancelledReportingHealthWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Ready_PropagatesClientCancellation()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await _client.GetAsync("/health/ready", cts.Token));
    }
}

public class HealthReportingTimeoutTests : IClassFixture<TimeoutReportingHealthWebApplicationFactory>
{
    private readonly HttpClient _client;

    public HealthReportingTimeoutTests(TimeoutReportingHealthWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Ready_Returns503_WhenReportingProbeTimesOut()
    {
        var response = await _client.GetAsync("/health/ready");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("not_ready", body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("reporting_not_ready", body, StringComparison.OrdinalIgnoreCase);
    }
}

public class HealthReportingFailureTests : IClassFixture<ThrowingReportingHealthWebApplicationFactory>
{
    private readonly HttpClient _client;

    public HealthReportingFailureTests(ThrowingReportingHealthWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Ready_Returns503_WhenReportingProbeThrows()
    {
        var response = await _client.GetAsync("/health/ready");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("reporting_not_ready", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("simulated reporting failure", body, StringComparison.OrdinalIgnoreCase);
    }
}

public sealed class CancelledReportingHealthWebApplicationFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        HealthTestHostBuilder.Configure(builder, services =>
        {
            services.RemoveAll<IDeploymentReportingHealthContributor>();
            services.AddSingleton<IDeploymentReportingHealthContributor, CancellingReportingHealthContributor>();
        });
    }
}

public sealed class TimeoutReportingHealthWebApplicationFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        HealthTestHostBuilder.Configure(builder, services =>
        {
            services.RemoveAll<IDeploymentReportingHealthContributor>();
            services.AddSingleton<IDeploymentReportingHealthContributor, TimeoutReportingHealthContributor>();
        });
    }
}

public sealed class ThrowingReportingHealthWebApplicationFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        HealthTestHostBuilder.Configure(builder, services =>
        {
            services.RemoveAll<IDeploymentReportingHealthContributor>();
            services.AddSingleton<IDeploymentReportingHealthContributor, ThrowingReportingHealthContributor>();
        });
    }
}

internal sealed class CancellingReportingHealthContributor : IDeploymentReportingHealthContributor
{
    public Task<DeploymentReportingHealthResult> EvaluateAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        throw new InvalidOperationException("should not reach");
    }
}

internal sealed class TimeoutReportingHealthContributor : IDeploymentReportingHealthContributor
{
    public Task<DeploymentReportingHealthResult> EvaluateAsync(CancellationToken cancellationToken = default) =>
        throw new OperationCanceledException();
}

internal sealed class ThrowingReportingHealthContributor : IDeploymentReportingHealthContributor
{
    public Task<DeploymentReportingHealthResult> EvaluateAsync(CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException("simulated reporting failure");
}
