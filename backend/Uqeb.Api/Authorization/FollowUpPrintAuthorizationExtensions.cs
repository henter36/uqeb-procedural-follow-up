using Uqeb.Api.Models.Enums;

namespace Uqeb.Api.Authorization;

public static class FollowUpPrintAuthorizationExtensions
{
    public static IServiceCollection AddUqebAuthorizationPolicies(this IServiceCollection services)
    {
        services.AddAuthorization(options =>
        {
            options.AddPolicy(Policies.AdminOnly, p => p.RequireRole(UserRole.Admin.ToString()));
            options.AddPolicy(Policies.SupervisorOrAdmin, p => p.RequireRole(
                UserRole.Admin.ToString(), UserRole.Supervisor.ToString()));
            options.AddPolicy(Policies.CanEditTransactions, p => p.RequireRole(
                UserRole.Admin.ToString(), UserRole.Supervisor.ToString(), UserRole.DataEntry.ToString()));
            options.AddPolicy(Policies.CanCloseTransactions, p => p.RequireRole(
                UserRole.Admin.ToString(), UserRole.Supervisor.ToString()));
            options.AddPolicy(Policies.CanManageUsers, p => p.RequireRole(UserRole.Admin.ToString()));
            options.AddPolicy(Policies.ManageLetterTemplates, p => p.RequireRole(
                UserRole.Admin.ToString(), UserRole.Supervisor.ToString()));
            options.AddPolicy(Policies.CreateFollowUpPrintJob, p => p.RequireRole(
                UserRole.Admin.ToString(), UserRole.Supervisor.ToString(), UserRole.DataEntry.ToString()));
            options.AddPolicy(Policies.ViewFollowUpPrintJobs, p => p.RequireRole(
                UserRole.Admin.ToString(), UserRole.Supervisor.ToString(),
                UserRole.DataEntry.ToString(), UserRole.DepartmentUser.ToString()));
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
