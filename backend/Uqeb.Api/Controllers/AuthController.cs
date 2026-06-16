using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Uqeb.Api.DTOs.Auth;
using Uqeb.Api.Services;

namespace Uqeb.Api.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly IAuthService _auth;
    private readonly ISecurityAuditService _securityAudit;

    public AuthController(IAuthService auth, ISecurityAuditService securityAudit)
    {
        _auth = auth;
        _securityAudit = securityAudit;
    }

    [HttpPost("login")]
    [EnableRateLimiting("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest? request)
    {
        var username = request?.Username?.Trim();
        if (request == null || string.IsNullOrWhiteSpace(username))
        {
            await _securityAudit.RecordLoginAttemptAsync(username, null, false, "invalid_credentials", HttpContext);
            return Unauthorized(new { message = "بيانات الدخول غير صحيحة" });
        }

        var result = await _auth.LoginAsync(request);
        if (result == null)
        {
            await _securityAudit.RecordLoginAttemptAsync(username, null, false, "invalid_credentials", HttpContext);
            return Unauthorized(new { message = "بيانات الدخول غير صحيحة" });
        }

        await _securityAudit.RecordLoginAttemptAsync(result.Username, null, true, null, HttpContext);
        return Ok(result);
    }
}
