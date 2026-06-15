using Microsoft.Extensions.Caching.Memory;
using Uqeb.Api.DTOs.Reports;
using Uqeb.Api.Helpers;

namespace Uqeb.Api.Services;

public interface ICacheInvalidationService
{
    string DashboardSummaryKey { get; }
    TimeSpan DashboardCacheDuration { get; }
    TimeSpan ReportsPageSummaryCacheDuration { get; }
    TimeSpan ReferenceDataCacheDuration { get; }

    string BuildReportsPageSummaryKey(ReportFilterRequest? filter);
    string BuildDepartmentsKey(bool activeOnly);
    string BuildCategoriesKey(bool activeOnly);
    string BuildExternalPartiesKey(bool activeOnly);

    void InvalidateOnTransactionChange();
    void InvalidateReferenceData();
}

public class CacheInvalidationService : ICacheInvalidationService
{
    public const string DashboardKey = "dashboard:summary";

    private readonly IMemoryCache _cache;
    private int _reportsVersion;
    private int _referenceVersion;

    public CacheInvalidationService(IMemoryCache cache) => _cache = cache;

    public string DashboardSummaryKey => DashboardKey;

    public TimeSpan DashboardCacheDuration => TimeSpan.FromSeconds(60);

    public TimeSpan ReportsPageSummaryCacheDuration => TimeSpan.FromSeconds(45);

    public TimeSpan ReferenceDataCacheDuration => TimeSpan.FromMinutes(10);

    public string BuildReportsPageSummaryKey(ReportFilterRequest? filter) =>
        $"reports:page-summary:v{Volatile.Read(ref _reportsVersion)}:{ReportFilterCacheKey.Build(filter)}";

    public string BuildDepartmentsKey(bool activeOnly) =>
        $"ref:departments:v{Volatile.Read(ref _referenceVersion)}:active={activeOnly}";

    public string BuildCategoriesKey(bool activeOnly) =>
        $"ref:categories:v{Volatile.Read(ref _referenceVersion)}:active={activeOnly}";

    public string BuildExternalPartiesKey(bool activeOnly) =>
        $"ref:external-parties:v{Volatile.Read(ref _referenceVersion)}:active={activeOnly}";

    public void InvalidateOnTransactionChange()
    {
        _cache.Remove(DashboardKey);
        Interlocked.Increment(ref _reportsVersion);
        Interlocked.Increment(ref _referenceVersion);
    }

    public void InvalidateReferenceData()
    {
        Interlocked.Increment(ref _referenceVersion);
    }
}
