using Uqeb.Api.Services;

namespace Uqeb.Api.Middleware;

public class UnauthorizedAccessLoggingMiddleware
{
    private readonly RequestDelegate _next;

    public UnauthorizedAccessLoggingMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context, ISecurityAuditService securityAudit)
    {
        await _next(context);

        if (context.Response.StatusCode is not (401 or 403))
            return;

        var reason = context.Response.StatusCode == 401 ? "unauthorized" : "forbidden";
        await securityAudit.RecordUnauthorizedAccessAsync(context, context.Response.StatusCode, reason);
    }
}
