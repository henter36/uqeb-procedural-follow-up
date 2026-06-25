using Microsoft.Extensions.Caching.Memory;
using Uqeb.Api.Services;
using Xunit;

namespace Uqeb.Api.Tests;

public class CacheCoordinatorTests
{
    [Fact]
    public async Task GetOrCreateAsync_executes_factory_once_under_parallel_load()
    {
        var cache = new MemoryCache(new MemoryCacheOptions());
        var coordinator = new MemoryCacheCoordinator(cache);
        var executions = 0;

        async Task<DashboardAggregateProbe> Factory()
        {
            Interlocked.Increment(ref executions);
            await Task.Delay(50);
            return new DashboardAggregateProbe { Value = 42 };
        }

        var tasks = Enumerable.Range(0, 8)
            .Select(_ => coordinator.GetOrCreateAsync("probe-key", Factory, TimeSpan.FromMinutes(1)))
            .ToArray();

        var results = await Task.WhenAll(tasks);

        Assert.All(results, result => Assert.Equal(42, result.Value));
        Assert.Equal(1, executions);
    }

    [Fact]
    public void BuildDashboardSummaryKey_changes_when_reports_version_increments()
    {
        var cache = new MemoryCache(new MemoryCacheOptions());
        var invalidation = new CacheInvalidationService(cache);
        var first = invalidation.BuildDashboardSummaryKey();

        invalidation.InvalidateOnTransactionChange();

        Assert.NotEqual(first, invalidation.BuildDashboardSummaryKey());
        Assert.NotEqual(first, invalidation.BuildDashboardFullKey());
    }

    [Fact]
    public void BuildReportsPageSummaryKey_is_independent_from_reference_version()
    {
        var cache = new MemoryCache(new MemoryCacheOptions());
        var invalidation = new CacheInvalidationService(cache);
        var reportsKey = invalidation.BuildReportsPageSummaryKey(null);
        var departmentsKey = invalidation.BuildDepartmentsKey(activeOnly: true);

        invalidation.InvalidateReferenceData();

        Assert.Equal(reportsKey, invalidation.BuildReportsPageSummaryKey(null));
        Assert.NotEqual(departmentsKey, invalidation.BuildDepartmentsKey(activeOnly: true));
    }

    private sealed class DashboardAggregateProbe
    {
        public int Value { get; init; }
    }
}
