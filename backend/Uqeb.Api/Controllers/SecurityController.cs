using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Uqeb.Api.Authorization;
using Uqeb.Api.DTOs.Security;
using Uqeb.Api.Services;

namespace Uqeb.Api.Controllers;

[ApiController]
[Route("api/security")]
[Authorize]
[RequirePermission(PermissionCode.SystemSettingsView)]
public class SecurityController : ControllerBase
{
    private readonly ISecurityAuditService _security;

    public SecurityController(ISecurityAuditService security) => _security = security;

    [HttpGet("login-attempts")]
    public async Task<IActionResult> GetLoginAttempts(
        [FromQuery] LoginAttemptFilterRequest filter,
        CancellationToken cancellationToken) =>
        Ok(await _security.GetRecentLoginAttemptsAsync(filter, cancellationToken));

    [HttpGet("alerts")]
    public async Task<IActionResult> GetAlerts(
        [FromQuery] SecurityAlertFilterRequest filter,
        CancellationToken cancellationToken) =>
        Ok(await _security.GetSecurityAlertsAsync(filter, cancellationToken));

    [HttpPost("alerts/{id}/read")]
    public async Task<IActionResult> MarkAlertAsRead(int id, CancellationToken cancellationToken) =>
        await _security.MarkAlertAsReadAsync(id, cancellationToken) ? Ok() : NotFound();

    [HttpPost("alerts/mark-all-read")]
    public async Task<IActionResult> MarkAllAlertsAsRead(CancellationToken cancellationToken)
    {
        var count = await _security.MarkAllAlertsAsReadAsync(cancellationToken);
        return Ok(new { marked = count });
    }
}
