using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Uqeb.Api.Authorization;
using Uqeb.Api.Reporting.Services;
using Uqeb.Api.Services;

namespace Uqeb.Api.Controllers;

[ApiController]
[Route("api/institutional-reports")]
[Authorize(Policy = Policies.AdminOnly)]
public class InstitutionalReportRolloutController : ControllerBase
{
    private readonly IReportingRolloutService _rollout;
    private readonly ICurrentUserService _currentUser;

    public InstitutionalReportRolloutController(
        IReportingRolloutService rollout,
        ICurrentUserService currentUser)
    {
        _rollout = rollout;
        _currentUser = currentUser;
    }

    [HttpGet("rollout-status")]
    public ActionResult<ReportingRolloutAccessStatusDto> GetRolloutStatus() =>
        Ok(_rollout.GetAccessStatus(_currentUser));
}
