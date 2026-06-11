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
}
