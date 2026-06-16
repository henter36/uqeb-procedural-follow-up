using System.Collections.Concurrent;
using Uqeb.Api.Helpers;

namespace Uqeb.Api.Middleware;

public class LoginRateLimitMiddleware
{
    private const int MaxRequestsPerWindow = 5;
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(1);

    private static readonly ConcurrentDictionary<string, RateLimitEntry> Counters = new();

    private readonly RequestDelegate _next;

    public LoginRateLimitMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context)
    {
        if (HttpMethods.IsPost(context.Request.Method)
            && context.Request.Path.StartsWithSegments("/api/auth/login", StringComparison.OrdinalIgnoreCase))
        {
            var ip = HttpContextSecurityHelper.GetClientIpAddress(context) ?? "unknown";
            var now = DateTime.UtcNow;
            var entry = Counters.AddOrUpdate(
                ip,
                _ => new RateLimitEntry(1, now.Add(Window)),
                (_, existing) =>
                {
                    if (now >= existing.ResetAt)
                        return new RateLimitEntry(1, now.Add(Window));
                    return new RateLimitEntry(existing.Count + 1, existing.ResetAt);
                });

            if (entry.Count > MaxRequestsPerWindow && now < entry.ResetAt)
            {
                context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                context.Response.ContentType = "application/json";
                await context.Response.WriteAsJsonAsync(new
                {
                    message = "تم تجاوز عدد محاولات الدخول المسموح بها. حاول لاحقًا."
                });
                return;
            }
        }

        await _next(context);
    }

    private sealed record RateLimitEntry(int Count, DateTime ResetAt);
}
