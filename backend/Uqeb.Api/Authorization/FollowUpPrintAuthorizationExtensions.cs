using Microsoft.AspNetCore.Authorization;
using Uqeb.Api.Models.Enums;

namespace Uqeb.Api.Authorization;

public static class FollowUpPrintAuthorizationExtensions
{
    public static IServiceCollection AddUqebAuthorizationPolicies(this IServiceCollection services)
    {
        services.AddAuthorization(options =>
        {
            // Defense-in-depth: every controller today carries an explicit [Authorize] (verified
            // as part of the authorization audit), but this ensures a future controller added
            // without one fails closed (requires authentication) instead of defaulting to
            // anonymous. [AllowAnonymous] endpoints (login, health checks, branding logo) are
            // unaffected — it always overrides both this and any [Authorize] policy.
            options.FallbackPolicy = new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .Build();

            options.AddPolicy(Policies.AdminOnly, p => p.RequireRole(UserRole.Admin.ToString()));
            options.AddPolicy(Policies.SupervisorOrAdmin, p => p.RequireRole(
                UserRole.Admin.ToString(), UserRole.Supervisor.ToString()));
            options.AddPolicy(Policies.CanEditTransactions, p => p.RequireRole(
                UserRole.Admin.ToString(), UserRole.Supervisor.ToString(), UserRole.DataEntry.ToString()));
            options.AddPolicy(Policies.CanCloseTransactions, p => p.RequireRole(
                UserRole.Admin.ToString(), UserRole.Supervisor.ToString()));
            options.AddPolicy(Policies.CanManageUsers, p => p.RequireRole(UserRole.Admin.ToString()));
            // Institution-wide operational aggregates (dashboard). DepartmentUser is the only
            // role scoped to a single department everywhere else in the system (transactions,
            // responses, follow-up print, institutional reports); it must not see cross-department
            // counts here either, so every other role is admitted and DepartmentUser is excluded.
            options.AddPolicy(Policies.ViewOperationalDashboard, p => p.RequireRole(
                UserRole.Admin.ToString(), UserRole.Supervisor.ToString(),
                UserRole.DataEntry.ToString(), UserRole.Reader.ToString()));
            options.AddPolicy(Policies.ManageLetterTemplates, p => p.RequireRole(
                UserRole.Admin.ToString(), UserRole.Supervisor.ToString()));
            options.AddPolicy(Policies.CreateFollowUpPrintJob, p => p.RequireRole(
                UserRole.Admin.ToString(), UserRole.Supervisor.ToString(), UserRole.DataEntry.ToString()));
            options.AddPolicy(Policies.ViewFollowUpPrintJobs, p => p.RequireRole(
                UserRole.Admin.ToString(), UserRole.Supervisor.ToString(), UserRole.DataEntry.ToString()));
            options.AddPolicy(Policies.CancelFollowUpPrintJob, p => p.RequireRole(
                UserRole.Admin.ToString(), UserRole.Supervisor.ToString()));
            options.AddPolicy(Policies.RetryFollowUpPrintJob, p => p.RequireRole(
                UserRole.Admin.ToString(), UserRole.Supervisor.ToString()));
            options.AddPolicy(Policies.PrintFollowUpLetters, p => p.RequireRole(
                UserRole.Admin.ToString(), UserRole.Supervisor.ToString(), UserRole.DataEntry.ToString()));
            options.AddPolicy(Policies.RegisterPrintedFollowUp, p => p.RequireRole(
                UserRole.Admin.ToString(), UserRole.Supervisor.ToString(), UserRole.DataEntry.ToString()));
            options.AddPolicy(Policies.CancelFollowUpPrintRecord, p => p.RequireRole(
                UserRole.Admin.ToString(), UserRole.Supervisor.ToString()));
            options.AddPolicy(Policies.SubmitDepartmentResponse, p => p.RequireRole(
                UserRole.Admin.ToString(), UserRole.Supervisor.ToString(),
                UserRole.DataEntry.ToString(), UserRole.DepartmentUser.ToString()));
            options.AddPolicy(Policies.ReviewDepartmentResponse, p => p.RequireRole(
                UserRole.Admin.ToString(), UserRole.Supervisor.ToString(),
                UserRole.DataEntry.ToString()));
        });

        return services;
    }
}
