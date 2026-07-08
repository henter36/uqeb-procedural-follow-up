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
    public async Task<IActionResult> GetLoginAttempts([FromQuery] LoginAttemptFilterRequest filter) =>
        Ok(await _security.GetRecentLoginAttemptsAsync(filter));

    [HttpGet("alerts")]
    public async Task<IActionResult> GetAlerts([FromQuery] SecurityAlertFilterRequest filter) =>
        Ok(await _security.GetSecurityAlertsAsync(filter));

    [HttpPost("alerts/{id}/read")]
    public async Task<IActionResult> MarkAlertAsRead(int id) =>
        await _security.MarkAlertAsReadAsync(id) ? Ok() : NotFound();

    [HttpPost("alerts/mark-all-read")]
    public async Task<IActionResult> MarkAllAlertsAsRead()
    {
        var count = await _security.MarkAllAlertsAsReadAsync();
        return Ok(new { marked = count });
    }
}
