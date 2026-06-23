using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Uqeb.Api.Data;
using Uqeb.Api.Services;
using Uqeb.Api.Services.Health;

namespace Uqeb.Api.Tests;

internal static class HealthTestHostBuilder
{
    internal static void Configure(
        IWebHostBuilder builder,
        Action<IServiceCollection>? configureServices = null,
        Dictionary<string, string?>? extraConfig = null)
    {
        builder.UseEnvironment("Development");

        var values = new Dictionary<string, string?>
        {
            ["Jwt:Key"] = "integration-test-jwt-key-32-chars-min",
            ["Jwt:Issuer"] = "UqebApiTests",
            ["Jwt:Audience"] = "UqebClientTests",
            ["ConnectionStrings:DefaultConnection"] = "Server=(localdb)\\mssqllocaldb;Database=unused;Trusted_Connection=True;",
            ["HealthChecks:DatabaseTimeoutSeconds"] = "5",
        };

        if (extraConfig is not null)
        {
            foreach (var entry in extraConfig)
                values[entry.Key] = entry.Value;
        }

        foreach (var entry in values)
            builder.UseSetting(entry.Key, entry.Value);

        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(values));

        builder.ConfigureServices(services =>
        {
            services.RemoveAll(typeof(DbContextOptions<AppDbContext>));
            services.RemoveAll(typeof(IDbContextFactory<AppDbContext>));
            services.RemoveAll(typeof(AppDbContext));
            services.RemoveAll(typeof(IHealthDatabaseProbe));
            services.RemoveAll(typeof(ISecurityAuditService));

            services.AddDbContextFactory<AppDbContext>(options =>
                options.UseInMemoryDatabase($"health-tests-{Guid.NewGuid():N}"));
            services.AddScoped(sp =>
                sp.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContext());
            services.AddScoped<IHealthDatabaseProbe, SuccessfulHealthDatabaseProbe>();
            services.AddSingleton<ISecurityAuditService, NoOpSecurityAuditService>();

            configureServices?.Invoke(services);
        });
    }
}

internal sealed class SuccessfulHealthDatabaseProbe : IHealthDatabaseProbe
{
    public Task<HealthDatabaseCheckResult> CheckAsync(CancellationToken cancellationToken) =>
        Task.FromResult(new HealthDatabaseCheckResult(HealthDatabaseStatus.Ready));
}
