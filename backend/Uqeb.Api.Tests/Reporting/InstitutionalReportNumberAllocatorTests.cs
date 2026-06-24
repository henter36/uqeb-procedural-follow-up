using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Uqeb.Api.Data;
using Uqeb.Api.Models.Entities;
using Uqeb.Api.Reporting.Services;
using Xunit;

namespace Uqeb.Api.Tests.Reporting;

public class InstitutionalReportNumberAllocatorTests
{
    [Fact]
    public async Task AllocateAsync_UsesEfCoreFallback_OnInMemoryProvider()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"report-number-inmemory-{Guid.NewGuid():N}")
            .Options;
        IDbContextFactory<AppDbContext> dbFactory = new TestDbContextFactory(options);
        var allocator = new InstitutionalReportNumberAllocator(dbFactory);
        var year = DateTime.UtcNow.Year;

        var first = await allocator.AllocateAsync();
        var second = await allocator.AllocateAsync();

        Assert.Equal($"REP-{year}-000001", first);
        Assert.Equal($"REP-{year}-000002", second);
    }

    [Fact]
    public async Task AllocateAsync_IncrementsSequentially_OnInMemoryProvider()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"report-number-sequential-{Guid.NewGuid():N}")
            .Options;
        IDbContextFactory<AppDbContext> dbFactory = new TestDbContextFactory(options);
        var allocator = new InstitutionalReportNumberAllocator(dbFactory);
        var year = DateTime.UtcNow.Year;

        var numbers = new List<string>();
        for (var i = 0; i < 10; i++)
            numbers.Add(await allocator.AllocateAsync());

        Assert.Equal(10, numbers.Distinct().Count());
        Assert.All(numbers, n => Assert.Matches($@"^REP-{year}-\d{{6}}$", n));

        var numeric = numbers.Select(n => int.Parse(n.Split('-')[2], System.Globalization.CultureInfo.InvariantCulture)).OrderBy(n => n).ToList();
        Assert.Equal(Enumerable.Range(1, 10), numeric);
    }
}
