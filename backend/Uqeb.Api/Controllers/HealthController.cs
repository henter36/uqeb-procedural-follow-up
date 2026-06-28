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
    private readonly IDeploymentReportingHealthContributor _reportingHealth;
    private readonly IDeploymentFollowUpPrintHealthContributor _followUpPrintHealth;
    private readonly ILogger<HealthController> _logger;

    public HealthController(
        IHealthDatabaseProbe databaseProbe,
        IDeploymentReportingHealthContributor reportingHealth,
        IDeploymentFollowUpPrintHealthContributor followUpPrintHealth,
        ILogger<HealthController> logger)
    {
        _databaseProbe = databaseProbe;
        _reportingHealth = reportingHealth;
        _followUpPrintHealth = followUpPrintHealth;
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
        var databaseResult = await CheckDatabaseSafelyAsync(
            cancellationToken,
            "Health readiness check");
        var reportingResult = await CheckReportingSafelyAsync(cancellationToken);
        var followUpPrintResult = await CheckFollowUpPrintSafelyAsync(cancellationToken);

        if (databaseResult.Status != HealthDatabaseStatus.Ready)
        {
            return MapDatabaseResult(
                databaseResult,
                readySuccessStatus: "ready",
                notReadyStatus: "not_ready");
        }

        if (!reportingResult.IsReady || !followUpPrintResult.IsReady)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new
            {
                status = "not_ready",
                reason = !reportingResult.IsReady ? "reporting_not_ready" : "follow_up_print_not_ready",
                checks = ToCheckDictionary([.. reportingResult.Checks, .. followUpPrintResult.Checks]),
                timestampUtc = DateTime.UtcNow,
            });
        }

        return Ok(new
        {
            status = "ready",
            checks = ToCheckDictionary([.. reportingResult.Checks, .. followUpPrintResult.Checks]),
            timestampUtc = DateTime.UtcNow,
        });
    }

    [HttpGet]
    public async Task<IActionResult> Summary(CancellationToken cancellationToken)
    {
        var databaseResult = await CheckDatabaseSafelyAsync(
            cancellationToken,
            "Health summary check");
        var reportingResult = await CheckReportingSafelyAsync(cancellationToken);
        var followUpPrintResult = await CheckFollowUpPrintSafelyAsync(cancellationToken);

        var databaseReady = databaseResult.Status == HealthDatabaseStatus.Ready;
        var reportingReady = reportingResult.IsReady;
        var followUpPrintReady = followUpPrintResult.IsReady;
        var checks = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["live"] = "pass",
            ["database"] = databaseReady ? "pass" : "fail",
        };

        foreach (var check in reportingResult.Checks)
        {
            checks[check.Name] = check.Status;
        }
        foreach (var check in followUpPrintResult.Checks)
        {
            checks[check.Name] = check.Status;
        }

        var healthy = databaseReady && reportingReady && followUpPrintReady;
        var payload = new
        {
            status = healthy ? "healthy" : "degraded",
            checks,
            timestampUtc = DateTime.UtcNow,
        };

        return healthy
            ? Ok(payload)
            : StatusCode(StatusCodes.Status503ServiceUnavailable, payload);
    }

    private async Task<DeploymentReportingHealthResult> CheckFollowUpPrintSafelyAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await _followUpPrintHealth.EvaluateAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(
                exception,
                "FollowUp Print readiness check timed out.");

            return new DeploymentReportingHealthResult(
                FeatureEnabled: true,
                IsReady: false,
                Checks:
                [
                    new DeploymentReportingHealthCheck("followUpPrintSchema", "fail", "follow_up_print_timeout"),
                    new DeploymentReportingHealthCheck("followUpDefaultTemplate", "fail", "follow_up_print_timeout"),
                    new DeploymentReportingHealthCheck("followUpPrintOptions", "fail", "follow_up_print_timeout"),
                    new DeploymentReportingHealthCheck("followUpPrintProcessor", "fail", "follow_up_print_timeout"),
                ]);
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "FollowUp Print readiness check failed.");

            return new DeploymentReportingHealthResult(
                FeatureEnabled: true,
                IsReady: false,
                Checks:
                [
                    new DeploymentReportingHealthCheck("followUpPrintSchema", "fail", "follow_up_print_error"),
                    new DeploymentReportingHealthCheck("followUpDefaultTemplate", "fail", "follow_up_print_error"),
                    new DeploymentReportingHealthCheck("followUpPrintOptions", "fail", "follow_up_print_error"),
                    new DeploymentReportingHealthCheck("followUpPrintProcessor", "fail", "follow_up_print_error"),
                ]);
        }
    }

    private async Task<DeploymentReportingHealthResult> CheckReportingSafelyAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await _reportingHealth.EvaluateAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(
                exception,
                "Reporting readiness check timed out.");

            return new DeploymentReportingHealthResult(
                FeatureEnabled: true,
                IsReady: false,
                Checks:
                [
                    new DeploymentReportingHealthCheck("institutionalReporting", "fail", "reporting_timeout"),
                ]);
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Reporting readiness check failed.");

            return new DeploymentReportingHealthResult(
                FeatureEnabled: true,
                IsReady: false,
                Checks:
                [
                    new DeploymentReportingHealthCheck("institutionalReporting", "fail", "reporting_error"),
                ]);
        }
    }

    private static Dictionary<string, string> ToCheckDictionary(IReadOnlyList<DeploymentReportingHealthCheck> checks) =>
        checks.ToDictionary(check => check.Name, check => check.Status, StringComparer.OrdinalIgnoreCase);

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
