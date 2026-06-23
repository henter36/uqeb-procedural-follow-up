using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Uqeb.Api.Data;
using Uqeb.Api.Reporting.Services;
using Xunit;

namespace Uqeb.Api.Tests.Reporting;

public class InstitutionalReportNumberAllocatorSqlServerIntegrationTests
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
    public async Task AllocateAsync_ProducesUniqueSequentialNumbers_FromDifferentAllocatorInstances()
    {
        if (!IsSqlServerAvailable())
            return;

        var databaseName = $"Uqeb_ReportNumbers_{Guid.NewGuid():N}";
        var builder = new SqlConnectionStringBuilder(ConnectionString!) { InitialCatalog = databaseName };
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(builder.ConnectionString)
            .Options;
        IDbContextFactory<AppDbContext> dbFactory = new TestDbContextFactory(options);

        await using (var db = dbFactory.CreateDbContext())
            await db.Database.EnsureCreatedAsync();

        var allocators = Enumerable.Range(0, 20)
            .Select(_ => new InstitutionalReportNumberAllocator(dbFactory))
            .ToList();

        var numbers = await Task.WhenAll(allocators.Select(a => a.AllocateAsync()));
        var year = DateTime.UtcNow.Year;

        Assert.Equal(20, numbers.Distinct().Count());
        Assert.All(numbers, n => Assert.Matches($@"^REP-{year}-\d{{6}}$", n));

        var numeric = numbers.Select(n => int.Parse(n.Split('-')[2])).OrderBy(n => n).ToList();
        Assert.Equal(Enumerable.Range(numeric[0], 20), numeric);
    }

    [Fact]
    public async Task AllocateAsync_ConcurrentYearRowCreation_ProducesUniqueNumbers()
    {
        if (!IsSqlServerAvailable())
            return;

        var databaseName = $"Uqeb_ReportNumbers_Create_{Guid.NewGuid():N}";
        var builder = new SqlConnectionStringBuilder(ConnectionString!) { InitialCatalog = databaseName };
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(builder.ConnectionString)
            .Options;
        IDbContextFactory<AppDbContext> dbFactory = new TestDbContextFactory(options);

        await using (var db = dbFactory.CreateDbContext())
            await db.Database.EnsureCreatedAsync();

        var numbers = await Task.WhenAll(Enumerable.Range(0, 20)
            .Select(_ => new InstitutionalReportNumberAllocator(dbFactory).AllocateAsync()));

        Assert.Equal(20, numbers.Distinct().Count());
    }
}
