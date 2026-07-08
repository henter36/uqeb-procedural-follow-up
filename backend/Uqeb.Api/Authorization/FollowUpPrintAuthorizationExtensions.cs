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

            options.AddPolicy(Policies.AdminOnly, p => p.RequireAssertion(context =>
                HasAnyRole(context, UserRole.Admin) || HasPermission(context, PermissionCode.SystemSettingsManage)));
            options.AddPolicy(Policies.SupervisorOrAdmin, p => p.RequireAssertion(context =>
                HasAnyRole(context, UserRole.Admin, UserRole.Supervisor) || HasPermission(context, PermissionCode.TransactionDetailsView)));
            options.AddPolicy(Policies.CanEditTransactions, p => p.RequireAssertion(context =>
                HasAnyRole(context, UserRole.Admin, UserRole.Supervisor, UserRole.DataEntry) || HasPermission(context, PermissionCode.TransactionsEdit)));
            options.AddPolicy(Policies.CanCloseTransactions, p => p.RequireAssertion(context =>
                HasAnyRole(context, UserRole.Admin, UserRole.Supervisor) || HasPermission(context, PermissionCode.TransactionsCancel)));
            options.AddPolicy(Policies.CanManageUsers, p => p.RequireAssertion(context =>
                HasAnyRole(context, UserRole.Admin) ||
                HasPermission(context, PermissionCode.UsersManage) ||
                HasPermission(context, PermissionCode.UserPermissionsManage)));
            // Institution-wide operational aggregates (dashboard). DepartmentUser is the only
            // role scoped to a single department everywhere else in the system (transactions,
            // responses, follow-up print, institutional reports); it must not see cross-department
            // counts here either, so every other role is admitted and DepartmentUser is excluded.
            options.AddPolicy(Policies.ViewOperationalDashboard, p => p.RequireAssertion(context =>
                HasAnyRole(context, UserRole.Admin, UserRole.Supervisor, UserRole.DataEntry, UserRole.Reader) ||
                HasPermission(context, PermissionCode.DashboardView)));
            options.AddPolicy(Policies.ManageLetterTemplates, p => p.RequireAssertion(context =>
                HasAnyRole(context, UserRole.Admin, UserRole.Supervisor) || HasPermission(context, PermissionCode.ReportsTemplatesManage)));
            options.AddPolicy(Policies.CreateFollowUpPrintJob, p => p.RequireAssertion(context =>
                HasAnyRole(context, UserRole.Admin, UserRole.Supervisor, UserRole.DataEntry) || HasPermission(context, PermissionCode.FollowUpPrintCreate)));
            options.AddPolicy(Policies.ViewFollowUpPrintJobs, p => p.RequireAssertion(context =>
                HasAnyRole(context, UserRole.Admin, UserRole.Supervisor, UserRole.DataEntry) || HasPermission(context, PermissionCode.FollowUpPrintView)));
            options.AddPolicy(Policies.CancelFollowUpPrintJob, p => p.RequireAssertion(context =>
                HasAnyRole(context, UserRole.Admin, UserRole.Supervisor) || HasPermission(context, PermissionCode.FollowUpPrintCreate)));
            options.AddPolicy(Policies.RetryFollowUpPrintJob, p => p.RequireAssertion(context =>
                HasAnyRole(context, UserRole.Admin, UserRole.Supervisor) || HasPermission(context, PermissionCode.FollowUpPrintCreate)));
            options.AddPolicy(Policies.PrintFollowUpLetters, p => p.RequireAssertion(context =>
                HasAnyRole(context, UserRole.Admin, UserRole.Supervisor, UserRole.DataEntry) || HasPermission(context, PermissionCode.FollowUpPrintExport)));
            options.AddPolicy(Policies.RegisterPrintedFollowUp, p => p.RequireAssertion(context =>
                HasAnyRole(context, UserRole.Admin, UserRole.Supervisor, UserRole.DataEntry) || HasPermission(context, PermissionCode.FollowUpPrintCreate)));
            options.AddPolicy(Policies.CancelFollowUpPrintRecord, p => p.RequireAssertion(context =>
                HasAnyRole(context, UserRole.Admin, UserRole.Supervisor) || HasPermission(context, PermissionCode.FollowUpPrintCreate)));
            options.AddPolicy(Policies.SubmitDepartmentResponse, p => p.RequireAssertion(context =>
                HasAnyRole(context, UserRole.Admin, UserRole.Supervisor, UserRole.DataEntry, UserRole.DepartmentUser) ||
                HasPermission(context, PermissionCode.TransactionResponsesEdit)));
            options.AddPolicy(Policies.ReviewDepartmentResponse, p => p.RequireAssertion(context =>
                HasAnyRole(context, UserRole.Admin, UserRole.Supervisor, UserRole.DataEntry) ||
                HasPermission(context, PermissionCode.TransactionResponsesEdit)));
        });

        return services;
    }

    private static bool HasAnyRole(AuthorizationHandlerContext context, params UserRole[] roles)
    {
        var roleNames = roles.Select(role => role.ToString()).ToHashSet(StringComparer.Ordinal);
        return context.User.Claims.Any(claim =>
            claim.Type == System.Security.Claims.ClaimTypes.Role && roleNames.Contains(claim.Value));
    }

    private static bool HasPermission(AuthorizationHandlerContext context, PermissionCode permission) =>
        context.User.HasClaim(PermissionClaims.PermissionClaimType, permission.ToString());
}
