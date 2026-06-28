using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Uqeb.Api.Middleware;
using Xunit;

namespace Uqeb.Api.Tests;

public class HealthEndpointTests : IClassFixture<HealthEndpointWebApplicationFactory>
{
    private readonly HttpClient _client;

    public HealthEndpointTests(HealthEndpointWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Live_ReturnsOkWithoutDatabase()
    {
        var response = await _client.GetAsync("/health/live");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("live", await response.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
        Assert.True(response.Headers.Contains(CorrelationIdMiddleware.HeaderName));
    }

    [Fact]
    public async Task Ready_ReturnsOkWhenDatabaseIsAvailable()
    {
        var response = await _client.GetAsync("/health/ready");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("ready", await response.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Summary_ReturnsHealthyWhenDatabaseIsAvailable()
    {
        var response = await _client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("healthy", body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("followUpPrintSchema", body, StringComparison.Ordinal);
        Assert.Contains("followUpDefaultTemplate", body, StringComparison.Ordinal);
        Assert.Contains("followUpPrintOptions", body, StringComparison.Ordinal);
        Assert.Contains("followUpPrintProcessor", body, StringComparison.Ordinal);
    }
}

public class HealthEndpointProbeThrowTests : IClassFixture<ThrowingHealthWebApplicationFactory>
{
    private readonly HttpClient _client;

    public HealthEndpointProbeThrowTests(ThrowingHealthWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Ready_Returns503WhenProbeThrowsUnexpectedException()
    {
        var response = await _client.GetAsync("/health/ready");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("\"status\":\"not_ready\"", body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("database_error", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("simulated unexpected probe failure", body, StringComparison.Ordinal);
        Assert.DoesNotContain("InvalidOperationException", body, StringComparison.Ordinal);
        Assert.DoesNotContain("stack", body, StringComparison.OrdinalIgnoreCase);
    }
}

public class HealthEndpointProbeThrowTimeoutTests : IClassFixture<ThrowingTimeoutHealthWebApplicationFactory>
{
    private readonly HttpClient _client;

    public HealthEndpointProbeThrowTimeoutTests(ThrowingTimeoutHealthWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Ready_Returns503WhenProbeThrowsInternalCancellation()
    {
        var response = await _client.GetAsync("/health/ready");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("\"status\":\"not_ready\"", body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("database_timeout", body, StringComparison.OrdinalIgnoreCase);
    }
}

public sealed class HealthEndpointWebApplicationFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        HealthTestHostBuilder.Configure(builder);
    }
}
