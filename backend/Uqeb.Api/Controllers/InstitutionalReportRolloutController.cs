using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Uqeb.Api.Authorization;
using Uqeb.Api.Reporting.Services;

namespace Uqeb.Api.Controllers;

[ApiController]
[Route("api/institutional-reports")]
[Authorize(Policy = Policies.AdminOnly)]
public class InstitutionalReportRolloutController : ControllerBase
{
    private readonly IReportingRolloutService _rollout;

    public InstitutionalReportRolloutController(IReportingRolloutService rollout) => _rollout = rollout;

    [HttpGet("rollout-status")]
    public ActionResult<ReportingRolloutStatusDto> GetRolloutStatus() => Ok(_rollout.GetStatus());
}
