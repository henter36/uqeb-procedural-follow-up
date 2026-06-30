using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Uqeb.Api.DTOs.System;
using Uqeb.Api.Services;

namespace Uqeb.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/system")]
public sealed class SystemController(IBuildInfoService buildInfoService) : ControllerBase
{
    [HttpGet("version")]
    public ActionResult<SystemVersionDto> GetVersion() => Ok(buildInfoService.GetVersion());
}
