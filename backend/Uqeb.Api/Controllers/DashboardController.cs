using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Uqeb.Api.Services;

namespace Uqeb.Api.Controllers;

[ApiController]
[Route("api/dashboard")]
[Authorize]
public class DashboardController : ControllerBase
{
    private readonly IReportService _reports;

    public DashboardController(IReportService reports) => _reports = reports;

    [HttpGet("summary")]
    public async Task<IActionResult> Summary() => Ok(await _reports.GetDashboardSummaryAsync());

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
