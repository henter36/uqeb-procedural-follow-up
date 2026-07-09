using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Uqeb.Api.Authorization;
using Uqeb.Api.Reporting.DataQuality;

namespace Uqeb.Api.Controllers;

[ApiController]
[Route("api/data-quality")]
[Authorize]
public sealed class DataQualityController : ControllerBase
{
    private readonly IDataQualityService _service;

    public DataQualityController(IDataQualityService service)
    {
        _service = service;
    }

    [HttpGet("summary")]
    [RequirePermission(PermissionCode.DataQualityView)]
    public async Task<ActionResult<DataQualitySummaryDto>> GetSummary(
        [FromQuery] DataQualityQueryDto query,
        CancellationToken ct)
    {
        var validation = ValidateQuery(query);
        if (validation is not null)
            return validation;

        return Ok(await _service.GetSummaryAsync(query, ct));
    }

    private static BadRequestObjectResult? ValidateQuery(DataQualityQueryDto? query)
    {
        if (query is null)
            return new BadRequestObjectResult(new { message = "Query is required." });

        if (query.Limit is < 1 or > 1000)
            return new BadRequestObjectResult(new { message = "Limit must be between 1 and 1000." });

        if (query.OverdueMoreThanDays is < 0)
            return new BadRequestObjectResult(new { message = "OverdueMoreThanDays must be greater than or equal to 0." });

        if (query.ResponsePeriodLessThanDays is < 0)
            return new BadRequestObjectResult(new { message = "ResponsePeriodLessThanDays must be greater than or equal to 0." });

        return null;
    }
}
