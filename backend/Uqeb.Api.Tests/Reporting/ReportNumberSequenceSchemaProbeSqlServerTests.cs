using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Uqeb.Api.Data;
using Uqeb.Api.Reporting.Operations;
using Uqeb.Api.Reporting.Services;
using Uqeb.Api.Services.Health;
using Xunit;

namespace Uqeb.Api.Tests.Reporting;

public class ReportNumberSequenceSchemaProbeSqlServerTests
{
    private static string? ConnectionString =>
        Environment.GetEnvironmentVariable("UQEB_REFERENCE_DATA_TEST_CONNECTION");

    private static bool IsSqlServerAvailable()
    {
        if (string.IsNullOrWhiteSpace(ConnectionString))
            return false;

        try
        {
            using var connection = new SqlConnection(ConnectionString);
            connection.Open();
            return true;
        }
        catch
        {
            return false;
        }
    }

    [Fact]
    public void TableExistsSql_ProjectsValueAlias()
    {
        Assert.Contains("AS [Value]", ReportNumberSequenceSchemaProbe.TableExistsSql, StringComparison.Ordinal);
        Assert.Contains("CAST(1 AS bit)", ReportNumberSequenceSchemaProbe.TableExistsSql, StringComparison.Ordinal);
        Assert.Contains("CAST(0 AS bit)", ReportNumberSequenceSchemaProbe.TableExistsSql, StringComparison.Ordinal);
    }

    [Fact]
    public async Task IsTableAvailableAsync_ReturnsTrue_WhenSequenceTableExists()
    {
        if (!IsSqlServerAvailable())
            return;

        var context = await CreateDatabaseAsync(withSequenceTable: true);
        try
        {
            var probe = new ReportNumberSequenceSchemaProbe(context.DbFactory);

            Assert.True(await probe.IsTableAvailableAsync());
        }
        finally
        {
            await SqlServerTestDatabaseHelper.DropDatabaseAsync(ConnectionString!, context.DatabaseName);
        }
    }

    [Fact]
    public async Task IsTableAvailableAsync_ReturnsFalse_WhenSequenceTableIsMissing()
    {
        if (!IsSqlServerAvailable())
            return;

        var context = await CreateDatabaseAsync(withSequenceTable: false);
        try
        {
            var probe = new ReportNumberSequenceSchemaProbe(context.DbFactory);

            Assert.False(await probe.IsTableAvailableAsync());
        }
        finally
        {
            await SqlServerTestDatabaseHelper.DropDatabaseAsync(ConnectionString!, context.DatabaseName);
        }
    }

    [Fact]
    public async Task HealthReady_ReturnsOk_WhenReportNumberSequenceProbeUsesSqlServer()
    {
        if (!IsSqlServerAvailable())
            return;

        var context = await CreateDatabaseAsync(withSequenceTable: true);
        await using var factory = new ReportingHealthSqlServerWebApplicationFactory(context.DbFactory);
        try
        {
            using var client = factory.CreateClient();

            var response = await client.GetAsync("/health/ready");
            var body = await response.Content.ReadAsStringAsync();

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Contains("\"status\":\"ready\"", body, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("reporting_not_ready", body, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Invalid column name 'Value'", body, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            await SqlServerTestDatabaseHelper.DropDatabaseAsync(ConnectionString!, context.DatabaseName);
        }
    }

    private sealed record SqlServerSchemaProbeTestContext(
        string DatabaseName,
        IDbContextFactory<AppDbContext> DbFactory);

    private static async Task<SqlServerSchemaProbeTestContext> CreateDatabaseAsync(bool withSequenceTable)
    {
        var databaseName = $"Uqeb_ReportSeq_Health_{Guid.NewGuid():N}";
        var builder = new SqlConnectionStringBuilder(ConnectionString!) { InitialCatalog = databaseName };
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(builder.ConnectionString)
            .Options;
        IDbContextFactory<AppDbContext> dbFactory = new TestDbContextFactory(options);

        await using (var db = dbFactory.CreateDbContext())
        {
            await db.Database.EnsureCreatedAsync();

            if (!withSequenceTable)
                await db.Database.ExecuteSqlRawAsync("DROP TABLE IF EXISTS ReportNumberSequences");
        }

        return new SqlServerSchemaProbeTestContext(databaseName, dbFactory);
    }

    private sealed class ReportingHealthSqlServerWebApplicationFactory(
        IDbContextFactory<AppDbContext> dbFactory) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            HealthTestHostBuilder.Configure(
                builder,
                services =>
                {
                    services.RemoveAll<DbContextOptions<AppDbContext>>();
                    services.RemoveAll<IDbContextFactory<AppDbContext>>();
                    services.RemoveAll<AppDbContext>();
                    services.RemoveAll<IReportingChromiumProbe>();
                    services.RemoveAll<IHealthDatabaseProbe>();

                    services.AddSingleton(dbFactory);
                    services.AddScoped(sp => sp.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContext());
                    services.AddSingleton<IReportingChromiumProbe, PassingReportingChromiumProbe>();
                    services.AddSingleton<IHealthDatabaseProbe, SuccessfulHealthDatabaseProbe>();
                },
                new Dictionary<string, string?>
                {
                    ["FeatureFlags:InstitutionalReports"] = "true",
                });
        }
    }

    private sealed class PassingReportingChromiumProbe : IReportingChromiumProbe
    {
        public Task<ReportingChromiumProbeResult> ProbeAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(CreateResult());

        public Task<ReportingChromiumProbeResult> ProbeLaunchOnlyAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(CreateResult());

        private static ReportingChromiumProbeResult CreateResult() => new()
        {
            State = ReportingChromiumProbeState.Ready,
            ExecutableAvailable = true,
            LaunchSuccessful = true,
            Summary = "ready",
        };
    }
}
