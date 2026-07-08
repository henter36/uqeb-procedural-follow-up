using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Uqeb.Api.Authorization;
using Uqeb.Api.DTOs.Reports;
using Uqeb.Api.Services;

namespace Uqeb.Api.Controllers;

// Institution-wide operational aggregates. DepartmentUser is excluded — see
// Policies.ViewOperationalDashboard for why — so it never sees cross-department counts here.
[ApiController]
[Route("api/dashboard")]
[Authorize(Policy = Policies.ViewOperationalDashboard)]
[RequirePermission(PermissionCode.DashboardView)]
public class DashboardController : ControllerBase
{
    private readonly IReportService _reports;
    private readonly IMemoryCacheCoordinator _cache;
    private readonly ICacheInvalidationService _cacheInvalidation;

    public DashboardController(
        IReportService reports,
        IMemoryCacheCoordinator cache,
        ICacheInvalidationService cacheInvalidation)
    {
        _reports = reports;
        _cache = cache;
        _cacheInvalidation = cacheInvalidation;
    }

    [HttpGet("summary")]
    public async Task<IActionResult> Summary()
    {
        var result = await _cache.GetOrCreateAsync(
            _cacheInvalidation.BuildDashboardSummaryKey(),
            () => _reports.GetDashboardSummaryAsync(),
            _cacheInvalidation.DashboardCacheDuration);
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
