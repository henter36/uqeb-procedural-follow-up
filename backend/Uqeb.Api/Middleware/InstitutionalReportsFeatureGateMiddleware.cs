using Microsoft.Extensions.Options;
using Uqeb.Api.Configuration;
using Uqeb.Api.Reporting.Configuration;
using Uqeb.Api.Reporting.Operations;
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
        IOptions<ReportingRolloutOptions> rolloutOptions,
        IReportingRolloutService rollout,
        IReportingRolloutObserver rolloutObserver,
        IReportingCorrelationIdProvider correlationIdProvider,
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

        if (rolloutOptions.Value.EmergencyDisable)
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        if (context.User.Identity?.IsAuthenticated == true)
        {
            var decision = rollout.Evaluate(currentUser);
            rolloutObserver.RecordDecision(
                rolloutOptions.Value.EnforcementMode,
                decision,
                correlationIdProvider.GetCorrelationId());

            if (rolloutOptions.Value.EnforcementMode == ReportingRolloutEnforcementMode.Enforced
                && !decision.Allowed)
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }
        }

        await _next(context);
    }

    private static bool IsInstitutionalReportsPath(PathString path) =>
        path.StartsWithSegments("/api/institutional-reports", out _);
}
