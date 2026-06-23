using Microsoft.Extensions.Options;
using Uqeb.Api.Configuration;

namespace Uqeb.Api.Middleware;

public sealed class InstitutionalReportsFeatureGateMiddleware
{
    private readonly RequestDelegate _next;

    public InstitutionalReportsFeatureGateMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context, IOptions<FeatureFlagsSettings> featureFlags)
    {
        if (context.Request.Path.StartsWithSegments("/api/institutional-reports", out _))
        {
            if (!featureFlags.Value.InstitutionalReports)
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }
        }

        await _next(context);
    }
}
