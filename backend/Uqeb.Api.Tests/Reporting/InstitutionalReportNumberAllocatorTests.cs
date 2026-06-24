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
    public async Task AllocateAsync_ConcurrentEfFallback_ProducesUniqueNumbers_OnInMemoryProvider()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"report-number-concurrent-{Guid.NewGuid():N}")
            .Options;
        IDbContextFactory<AppDbContext> dbFactory = new TestDbContextFactory(options);
        var year = DateTime.UtcNow.Year;

        var numbers = await Task.WhenAll(Enumerable.Range(0, 10)
            .Select(_ => new InstitutionalReportNumberAllocator(dbFactory).AllocateAsync()));

        Assert.Equal(10, numbers.Distinct().Count());
        Assert.All(numbers, n => Assert.Matches($@"^REP-{year}-\d{{6}}$", n));
    }
}
