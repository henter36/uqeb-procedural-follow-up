using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Uqeb.Api.Data;

namespace Uqeb.Api.Controllers;

[ApiController]
[Route("health")]
[AllowAnonymous]
public class HealthController : ControllerBase
{
    [HttpGet("live")]
    public IActionResult Live()
    {
        return Ok(new
        {
            status = "live",
            timestampUtc = DateTime.UtcNow,
        });
    }

    [HttpGet("ready")]
    public async Task<IActionResult> Ready(
        [FromServices] AppDbContext db,
        CancellationToken cancellationToken)
    {
        try
        {
            if (!await db.Database.CanConnectAsync(cancellationToken))
            {
                return StatusCode(StatusCodes.Status503ServiceUnavailable, new
                {
                    status = "not_ready",
                    reason = "database_unreachable",
                });
            }

            return Ok(new
            {
                status = "ready",
                timestampUtc = DateTime.UtcNow,
            });
        }
        catch (Exception)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new
            {
                status = "not_ready",
                reason = "database_error",
            });
        }
    }

    [HttpGet]
    public async Task<IActionResult> Summary(
        [FromServices] AppDbContext db,
        CancellationToken cancellationToken)
    {
        var databaseReady = false;
        try
        {
            databaseReady = await db.Database.CanConnectAsync(cancellationToken);
        }
        catch
        {
            databaseReady = false;
        }

        var payload = new
        {
            status = databaseReady ? "healthy" : "degraded",
            checks = new
            {
                live = "pass",
                database = databaseReady ? "pass" : "fail",
            },
            timestampUtc = DateTime.UtcNow,
        };

        return databaseReady ? Ok(payload) : StatusCode(StatusCodes.Status503ServiceUnavailable, payload);
    }
}
