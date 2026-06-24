using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Uqeb.Api.Data;
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
        var allocator = new InstitutionalReportNumberAllocator(
            dbFactory,
            NullLogger<InstitutionalReportNumberAllocator>.Instance);

        var first = await allocator.AllocateAsync();
        var second = await allocator.AllocateAsync();

        var year = DateTime.UtcNow.Year;
        Assert.Equal($"REP-{year}-000001", first);
        Assert.Equal($"REP-{year}-000002", second);
    }
}
