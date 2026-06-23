using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Uqeb.Api.Services.Health;

namespace Uqeb.Api.Controllers;

[ApiController]
[Route("health")]
[AllowAnonymous]
public class HealthController : ControllerBase
{
    private readonly IHealthDatabaseProbe _databaseProbe;
    private readonly ILogger<HealthController> _logger;

    public HealthController(
        IHealthDatabaseProbe databaseProbe,
        ILogger<HealthController> logger)
    {
        _databaseProbe = databaseProbe;
        _logger = logger;
    }

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
    public async Task<IActionResult> Ready(CancellationToken cancellationToken)
    {
        var result = await _databaseProbe.CheckAsync(cancellationToken);
        return MapDatabaseResult(result, readySuccessStatus: "ready", notReadyStatus: "not_ready");
    }

    [HttpGet]
    public async Task<IActionResult> Summary(CancellationToken cancellationToken)
    {
        HealthDatabaseCheckResult result;
        try
        {
            result = await _databaseProbe.CheckAsync(cancellationToken);
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Health summary check failed while verifying database connectivity.");

            result = new HealthDatabaseCheckResult(HealthDatabaseStatus.Error);
        }

        if (result.Status is HealthDatabaseStatus.Error)
        {
            if (result.Exception is not null)
            {
                _logger.LogError(
                    result.Exception,
                    "Health summary check failed while verifying database connectivity.");
            }
            else
            {
                _logger.LogError(
                    "Health summary check reported database connectivity failure.");
            }
        }
        else if (result.Status is HealthDatabaseStatus.Timeout)
        {
            _logger.LogWarning("Health summary check timed out while verifying database connectivity.");
        }

        var databaseReady = result.Status == HealthDatabaseStatus.Ready;
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

        return databaseReady
            ? Ok(payload)
            : StatusCode(StatusCodes.Status503ServiceUnavailable, payload);
    }

    private IActionResult MapDatabaseResult(
        HealthDatabaseCheckResult result,
        string readySuccessStatus,
        string notReadyStatus)
    {
        if (result.Status == HealthDatabaseStatus.Ready)
        {
            return Ok(new
            {
                status = readySuccessStatus,
                timestampUtc = DateTime.UtcNow,
            });
        }

        var reason = result.Status switch
        {
            HealthDatabaseStatus.Unreachable => "database_unreachable",
            HealthDatabaseStatus.Timeout => "database_timeout",
            _ => "database_error",
        };

        if (result.Status == HealthDatabaseStatus.Error)
        {
            if (result.Exception is not null)
            {
                _logger.LogError(
                    result.Exception,
                    "Health readiness check failed while verifying database connectivity.");
            }
            else
            {
                _logger.LogError(
                    "Health readiness check failed while verifying database connectivity.");
            }
        }
        else if (result.Status == HealthDatabaseStatus.Timeout)
        {
            _logger.LogWarning(
                "Health readiness check timed out while verifying database connectivity.");
        }

        return StatusCode(StatusCodes.Status503ServiceUnavailable, new
        {
            status = notReadyStatus,
            reason,
        });
    }
}
