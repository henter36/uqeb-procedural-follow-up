using Microsoft.EntityFrameworkCore;
using Uqeb.Api.Data;
using Uqeb.Api.Reporting.Services;
using Xunit;

namespace Uqeb.Api.Tests.Reporting;

public class InstitutionalReportNumberAllocatorTests
{
    [Fact]
    public async Task AllocateAsync_RequiresSqlServer_ForAtomicIncrement()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"report-number-inmemory-{Guid.NewGuid():N}")
            .Options;
        IDbContextFactory<AppDbContext> dbFactory = new TestDbContextFactory(options);
        var allocator = new InstitutionalReportNumberAllocator(dbFactory);

        await Assert.ThrowsAsync<InvalidOperationException>(() => allocator.AllocateAsync());
    }
}
