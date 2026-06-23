using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Uqeb.Api.Data;

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
        Assert.True(response.Headers.Contains("X-Correlation-ID"));
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
    }
}

public sealed class HealthEndpointWebApplicationFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");

        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Key"] = "integration-test-jwt-key-32-chars-min",
                ["Jwt:Issuer"] = "UqebApiTests",
                ["Jwt:Audience"] = "UqebClientTests",
                ["ConnectionStrings:DefaultConnection"] = "Server=(localdb)\\mssqllocaldb;Database=unused;Trusted_Connection=True;",
            });
        });

        builder.ConfigureServices(services =>
        {
            services.RemoveAll(typeof(DbContextOptions<AppDbContext>));
            services.RemoveAll(typeof(IDbContextFactory<AppDbContext>));
            services.RemoveAll(typeof(AppDbContext));

            services.AddDbContextFactory<AppDbContext>(options =>
                options.UseInMemoryDatabase($"health-tests-{Guid.NewGuid():N}"));
            services.AddScoped(sp =>
                sp.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContext());
        });
    }
}
