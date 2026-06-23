using Microsoft.EntityFrameworkCore;
using Uqeb.Api.Data;
using Uqeb.Api.Reporting.Services;
using Xunit;

namespace Uqeb.Api.Tests.Reporting;

internal sealed class TestDbContextFactory(DbContextOptions<AppDbContext> options) : IDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext() => new(options);

    public Task<AppDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(CreateDbContext());
}

public class InstitutionalReportNumberAllocatorTests
{
    [Fact]
    public async Task AllocateAsync_GeneratesUniqueSequentialNumbersPerYear()
    {
        var dbName = $"report-number-tests-{Guid.NewGuid():N}";
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;
        IDbContextFactory<AppDbContext> dbFactory = new TestDbContextFactory(options);

        var allocator = new InstitutionalReportNumberAllocator(dbFactory);
        var year = DateTime.UtcNow.Year;

        var first = await allocator.AllocateAsync();
        var second = await allocator.AllocateAsync();

        Assert.NotEqual(first, second);
        Assert.StartsWith($"REP-{year}-", first);
        Assert.StartsWith($"REP-{year}-", second);

        await using var db = dbFactory.CreateDbContext();
        var sequence = await db.ReportNumberSequences.SingleAsync(s => s.Year == year);
        Assert.Equal(2, sequence.LastNumber);
    }

    [Fact]
    public async Task AllocateAsync_DoesNotProduceDuplicateNumbersUnderConcurrency()
    {
        var dbName = $"report-number-concurrency-{Guid.NewGuid():N}";
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;
        IDbContextFactory<AppDbContext> dbFactory = new TestDbContextFactory(options);

        var allocator = new InstitutionalReportNumberAllocator(dbFactory);
        var numbers = await Task.WhenAll(Enumerable.Range(0, 8)
            .Select(_ => allocator.AllocateAsync()));

        Assert.Equal(8, numbers.Distinct().Count());
    }
}
