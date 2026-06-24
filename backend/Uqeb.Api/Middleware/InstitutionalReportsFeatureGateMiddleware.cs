using Microsoft.Extensions.Options;
using Uqeb.Api.Configuration;
using Uqeb.Api.Reporting.Services;
using Uqeb.Api.Services;

namespace Uqeb.Api.Middleware;

public sealed class InstitutionalReportsFeatureGateMiddleware
{
    private readonly RequestDelegate _next;

    public InstitutionalReportsFeatureGateMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(
        HttpContext context,
        IOptions<FeatureFlagsSettings> featureFlags,
        IReportingRolloutService rollout,
        ICurrentUserService currentUser)
    {
        if (!IsInstitutionalReportsPath(context.Request.Path))
        {
            await _next(context);
            return;
        }

        if (!featureFlags.Value.InstitutionalReports)
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        if (context.User.Identity?.IsAuthenticated == true && !rollout.IsEnabledForUser(currentUser))
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        await _next(context);
    }

    private static bool IsInstitutionalReportsPath(PathString path) =>
        path.StartsWithSegments("/api/institutional-reports", out _);
}
