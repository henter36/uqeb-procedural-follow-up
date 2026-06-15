using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Uqeb.Api.DTOs.Reports;
using Uqeb.Api.Services;

namespace Uqeb.Api.Controllers;

[ApiController]
[Route("api/dashboard")]
[Authorize]
public class DashboardController : ControllerBase
{
    private readonly IReportService _reports;
    private readonly IMemoryCache _cache;
    private readonly ICacheInvalidationService _cacheInvalidation;

    public DashboardController(
        IReportService reports,
        IMemoryCache cache,
        ICacheInvalidationService cacheInvalidation)
    {
        _reports = reports;
        _cache = cache;
        _cacheInvalidation = cacheInvalidation;
    }

    [HttpGet("summary")]
    public async Task<IActionResult> Summary()
    {
        var cacheKey = _cacheInvalidation.DashboardSummaryKey;
        if (_cache.TryGetValue(cacheKey, out DashboardSummaryDto? cached) && cached != null)
            return Ok(cached);

        var result = await _reports.GetDashboardSummaryAsync();
        _cache.Set(cacheKey, result, _cacheInvalidation.DashboardCacheDuration);
        return Ok(result);
    }

    [HttpGet("action-required")]
    public async Task<IActionResult> ActionRequired() => Ok(await _reports.GetDashboardActionRequiredAsync());

    [HttpGet("top-overdue-departments")]
    public async Task<IActionResult> TopOverdueDepartments() =>
        Ok(await _reports.GetDashboardTopOverdueDepartmentsAsync());

    [HttpGet("top-incoming-parties")]
    public async Task<IActionResult> TopIncomingParties() =>
        Ok(await _reports.GetDashboardTopIncomingPartiesAsync());

    [HttpGet("category-distribution")]
    public async Task<IActionResult> CategoryDistribution() =>
        Ok(await _reports.GetDashboardCategoryDistributionAsync());

    [HttpGet("status-distribution")]
    public async Task<IActionResult> StatusDistribution() =>
        Ok(await _reports.GetDashboardStatusDistributionAsync());
}
