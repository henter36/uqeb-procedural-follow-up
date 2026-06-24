using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Uqeb.Api.Authorization;
using Uqeb.Api.Reporting.DTOs;
using Uqeb.Api.Reporting.Services;

namespace Uqeb.Api.Controllers;

[ApiController]
[Route("api/institutional-reports")]
[Authorize(Policy = Policies.SupervisorOrAdmin)]
public class InstitutionalReportConfigurationController : ControllerBase
{
    private readonly IReportingReadinessService _readiness;

    public InstitutionalReportConfigurationController(IReportingReadinessService readiness) =>
        _readiness = readiness;

    [HttpGet("configuration")]
    public ActionResult<ReportingConfigurationDto> GetConfiguration() =>
        Ok(_readiness.GetConfiguration());

    [HttpGet("readiness")]
    public ActionResult<ReportingReadinessDto> GetReadiness() =>
        Ok(_readiness.GetReadiness());
}
