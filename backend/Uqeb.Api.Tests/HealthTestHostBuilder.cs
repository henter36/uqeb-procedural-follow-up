using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
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
        Dictionary<string, string?>? extraConfig = null,
        string? inMemoryDatabaseName = null)
    {
        builder.UseEnvironment("Development");

        var values = new Dictionary<string, string?>
        {
            ["Testing:UseInMemoryDatabase"] = "true",
            ["Jwt:Key"] = "integration-test-jwt-key-32-chars-min",
            ["Jwt:Issuer"] = "UqebApiTests",
            ["Jwt:Audience"] = "UqebClientTests",
            ["ConnectionStrings:DefaultConnection"] = "Server=(localdb)\\mssqllocaldb;Database=unused;Trusted_Connection=True;",
            ["HealthChecks:DatabaseTimeoutSeconds"] = "5",
            ["FeatureFlags:InstitutionalReports"] = "false",
            ["DatabaseStartup:RunMigrationsOnStartup"] = "false",
            ["DatabaseStartup:RunReferenceSeedOnStartup"] = "false",
            ["DatabaseStartup:RunDefaultUsersSeedOnStartup"] = "false",
            ["DatabaseStartup:RunDemoSeedOnStartup"] = "false",
        };

        if (extraConfig is not null)
        {
            foreach (var entry in extraConfig)
                values[entry.Key] = entry.Value;
        }

        foreach (var entry in values)
            builder.UseSetting(entry.Key, entry.Value);

        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(values));

        var databaseName = inMemoryDatabaseName ?? $"health-tests-{Guid.NewGuid():N}";

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<DbContextOptions<AppDbContext>>();
            services.RemoveAll<IDbContextFactory<AppDbContext>>();
            services.RemoveAll<AppDbContext>();
            services.RemoveAll<IHealthDatabaseProbe>();
            services.RemoveAll<IDeploymentFollowUpPrintHealthContributor>();
            services.RemoveAll<ISecurityAuditService>();

            services.AddDbContextFactory<AppDbContext>(options =>
                options.UseInMemoryDatabase(databaseName));
            services.AddScoped(sp =>
                sp.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContext());
            services.AddScoped<IHealthDatabaseProbe, SuccessfulHealthDatabaseProbe>();
            services.AddSingleton<IDeploymentFollowUpPrintHealthContributor, PassingFollowUpPrintHealthContributor>();
            services.AddSingleton<ISecurityAuditService, NoOpSecurityAuditService>();

            configureServices?.Invoke(services);
        });
    }
}

internal sealed class PassingFollowUpPrintHealthContributor : IDeploymentFollowUpPrintHealthContributor
{
    public Task<DeploymentReportingHealthResult> EvaluateAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(new DeploymentReportingHealthResult(
            FeatureEnabled: true,
            IsReady: true,
            Checks:
            [
                new DeploymentReportingHealthCheck("followUpPrintSchema", "pass"),
                new DeploymentReportingHealthCheck("followUpDefaultTemplate", "pass"),
                new DeploymentReportingHealthCheck("followUpPrintOptions", "pass"),
                new DeploymentReportingHealthCheck("followUpPrintProcessor", "pass"),
            ]));
}

internal sealed class SuccessfulHealthDatabaseProbe : IHealthDatabaseProbe
{
    public Task<HealthDatabaseCheckResult> CheckAsync(CancellationToken cancellationToken) =>
        Task.FromResult(new HealthDatabaseCheckResult(HealthDatabaseStatus.Ready));
}

internal sealed class ThrowingHealthDatabaseProbe : IHealthDatabaseProbe
{
    public Task<HealthDatabaseCheckResult> CheckAsync(CancellationToken cancellationToken) =>
        throw new InvalidOperationException("simulated unexpected probe failure");
}

internal sealed class ThrowingTimeoutHealthDatabaseProbe : IHealthDatabaseProbe
{
    public Task<HealthDatabaseCheckResult> CheckAsync(CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
            throw new OperationCanceledException(cancellationToken);

        throw new OperationCanceledException();
    }
}

public sealed class ThrowingHealthWebApplicationFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        HealthTestHostBuilder.Configure(builder, services =>
        {
            services.RemoveAll(typeof(IHealthDatabaseProbe));
            services.AddSingleton<IHealthDatabaseProbe, ThrowingHealthDatabaseProbe>();
        });
    }
}

public sealed class ThrowingTimeoutHealthWebApplicationFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        HealthTestHostBuilder.Configure(builder, services =>
        {
            services.RemoveAll(typeof(IHealthDatabaseProbe));
            services.AddSingleton<IHealthDatabaseProbe, ThrowingTimeoutHealthDatabaseProbe>();
        });
    }
}
