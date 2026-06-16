using System.Security.Claims;

namespace Uqeb.Api.Helpers;

public static class HttpContextSecurityHelper
{
    public static string? GetClientIpAddress(HttpContext context)
    {
        var forwarded = context.Request.Headers["X-Forwarded-For"].FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(forwarded))
            return forwarded.Split(',')[0].Trim();

        return context.Connection.RemoteIpAddress?.ToString();
    }

    public static string? GetUserAgent(HttpContext context) =>
        context.Request.Headers.UserAgent.ToString();

    public static int? GetUserId(HttpContext context)
    {
        var id = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
        return int.TryParse(id, out var userId) ? userId : null;
    }

    public static bool IsAdminProbePath(PathString path)
    {
        var value = path.Value ?? string.Empty;
        return value.StartsWith("/api/users", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("/api/departments", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("/api/external-parties", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("/api/security", StringComparison.OrdinalIgnoreCase);
    }
}
