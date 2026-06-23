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
        var result = await CheckDatabaseSafelyAsync(
            cancellationToken,
            "Health readiness check");

        return MapDatabaseResult(
            result,
            readySuccessStatus: "ready",
            notReadyStatus: "not_ready");
    }

    [HttpGet]
    public async Task<IActionResult> Summary(CancellationToken cancellationToken)
    {
        var result = await CheckDatabaseSafelyAsync(
            cancellationToken,
            "Health summary check");

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

    private async Task<HealthDatabaseCheckResult> CheckDatabaseSafelyAsync(
        CancellationToken cancellationToken,
        string logContext)
    {
        try
        {
            return await _databaseProbe.CheckAsync(cancellationToken);
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(
                exception,
                "{LogContext} timed out while verifying database connectivity.",
                logContext);

            return new HealthDatabaseCheckResult(HealthDatabaseStatus.Timeout, exception);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "{LogContext} failed while verifying database connectivity.",
                logContext);

            return new HealthDatabaseCheckResult(HealthDatabaseStatus.Error, exception);
        }
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
