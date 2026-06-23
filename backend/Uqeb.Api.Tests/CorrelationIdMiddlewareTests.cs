using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Uqeb.Api.Middleware;
using Uqeb.Api.Services.Health;
using Xunit;

namespace Uqeb.Api.Tests;

public class CorrelationIdMiddlewareTests : IClassFixture<HealthEndpointWebApplicationFactory>
{
    private static readonly Regex HexCorrelationId = new("^[0-9a-f]{32}$", RegexOptions.CultureInvariant);

    private readonly HttpClient _client;

    public CorrelationIdMiddlewareTests(HealthEndpointWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Missing_header_generates_new_32_character_hex_id()
    {
        var response = await _client.GetAsync("/health/live");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        AssertCorrelationHeaderIsGenerated(response);
    }

    [Fact]
    public async Task Valid_header_is_echoed()
    {
        const string correlationId = "abc-123_X.y";
        var request = new HttpRequestMessage(HttpMethod.Get, "/health/live");
        request.Headers.Add(CorrelationIdMiddleware.HeaderName, correlationId);

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(correlationId, response.Headers.GetValues(CorrelationIdMiddleware.HeaderName).Single());
    }

    [Theory]
    [InlineData("too-long-value-aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    [InlineData("has space")]
    [InlineData("has\rcrlf")]
    [InlineData("has\nlf")]
    [InlineData("unicode-مرحبا")]
    public async Task Invalid_header_is_replaced(string invalidValue)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/health/live");
        request.Headers.TryAddWithoutValidation(CorrelationIdMiddleware.HeaderName, invalidValue);

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        AssertCorrelationHeaderIsGenerated(response);
        Assert.DoesNotContain(invalidValue, response.Headers.GetValues(CorrelationIdMiddleware.HeaderName).Single());
    }

    [Fact]
    public async Task Multiple_header_values_are_replaced()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/health/live");
        request.Headers.TryAddWithoutValidation(CorrelationIdMiddleware.HeaderName, new[] { "first", "second" });

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        AssertCorrelationHeaderIsGenerated(response);
    }

    [Fact]
    public async Task Unauthorized_response_includes_correlation_header()
    {
        var response = await _client.GetAsync("/api/dashboard/summary");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.True(response.Headers.Contains(CorrelationIdMiddleware.HeaderName));
    }

    [Fact]
    public async Task Forbidden_response_includes_correlation_header()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/security/login-attempts");
        request.Headers.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", TestJwtHelper.CreateToken("DataEntry"));

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.True(response.Headers.Contains(CorrelationIdMiddleware.HeaderName));
    }

    [Fact]
    public async Task Responses_include_security_headers()
    {
        var response = await _client.GetAsync("/health/live");

        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("DENY", response.Headers.GetValues("X-Frame-Options").Single());
    }

    private static void AssertCorrelationHeaderIsGenerated(HttpResponseMessage response)
    {
        Assert.True(response.Headers.Contains(CorrelationIdMiddleware.HeaderName));
        var value = response.Headers.GetValues(CorrelationIdMiddleware.HeaderName).Single();
        Assert.Matches(HexCorrelationId, value);
    }
}

public class HealthEndpointFailureTests : IClassFixture<FailingHealthWebApplicationFactory>
{
    private readonly HttpClient _client;

    public HealthEndpointFailureTests(FailingHealthWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Ready_Returns503WithDatabaseErrorWithoutExceptionDetails()
    {
        var response = await _client.GetAsync("/health/ready");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("database_error", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("simulated", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("stack", body, StringComparison.OrdinalIgnoreCase);
        Assert.True(response.Headers.Contains(CorrelationIdMiddleware.HeaderName));
    }

    [Fact]
    public async Task Summary_Returns503DegradedWhenDatabaseFails()
    {
        var response = await _client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("degraded", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("simulated", body, StringComparison.OrdinalIgnoreCase);
    }
}

public class HealthEndpointTimeoutTests : IClassFixture<TimeoutHealthWebApplicationFactory>
{
    private readonly HttpClient _client;

    public HealthEndpointTimeoutTests(TimeoutHealthWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Ready_Returns503DatabaseTimeout()
    {
        var response = await _client.GetAsync("/health/ready");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("database_timeout", body, StringComparison.OrdinalIgnoreCase);
    }
}

internal sealed class FailingHealthDatabaseProbe : IHealthDatabaseProbe
{
    public Task<HealthDatabaseCheckResult> CheckAsync(CancellationToken cancellationToken)
    {
        return Task.FromResult(
            new HealthDatabaseCheckResult(
                HealthDatabaseStatus.Error,
                new InvalidOperationException("simulated database failure for tests")));
    }
}

internal sealed class TimeoutHealthDatabaseProbe : IHealthDatabaseProbe
{
    public Task<HealthDatabaseCheckResult> CheckAsync(CancellationToken cancellationToken) =>
        Task.FromResult(new HealthDatabaseCheckResult(HealthDatabaseStatus.Timeout));
}

public sealed class FailingHealthWebApplicationFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        HealthTestHostBuilder.Configure(builder, services =>
        {
            services.RemoveAll(typeof(IHealthDatabaseProbe));
            services.AddSingleton<IHealthDatabaseProbe, FailingHealthDatabaseProbe>();
        });
    }
}

public sealed class TimeoutHealthWebApplicationFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        HealthTestHostBuilder.Configure(builder, services =>
        {
            services.RemoveAll(typeof(IHealthDatabaseProbe));
            services.AddSingleton<IHealthDatabaseProbe, TimeoutHealthDatabaseProbe>();
        }, new Dictionary<string, string?>
        {
            ["HealthChecks:DatabaseTimeoutSeconds"] = "1",
        });
    }
}
