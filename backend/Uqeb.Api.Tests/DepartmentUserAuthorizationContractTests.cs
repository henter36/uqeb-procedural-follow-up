using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Uqeb.Api.Authorization;
using Uqeb.Api.Controllers;
using Uqeb.Api.Models.Enums;
using Xunit;

namespace Uqeb.Api.Tests;

public class DepartmentUserAuthorizationContractTests
{
    [Theory]
    [InlineData(nameof(TransactionsController.Update))]
    [InlineData(nameof(TransactionsController.AddAssignment))]
    [InlineData(nameof(TransactionsController.AddFollowUp))]
    [InlineData(nameof(TransactionsController.ReplyAssignment))]
    [InlineData(nameof(TransactionsController.ReplyFollowUp))]
    [InlineData(nameof(TransactionsController.PreviewFollowUpLetter))]
    [InlineData(nameof(TransactionsController.DownloadFollowUpLetterPdf))]
    public void TransactionMutationActions_RequireCanEditTransactions(string actionName)
    {
        var method = GetControllerMethod<TransactionsController>(actionName);

        Assert.Equal(Policies.CanEditTransactions, GetMethodPolicy(method));
    }

    [Fact]
    public void GeneralReportsController_RequiresCanEditTransactions()
    {
        var policy = typeof(ReportsController)
            .GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true)
            .Cast<AuthorizeAttribute>()
            .Select(a => a.Policy)
            .Single(p => p == Policies.CanEditTransactions);

        Assert.Equal(Policies.CanEditTransactions, policy);
    }

    [Theory]
    [InlineData(nameof(DepartmentResponsesController.GetPendingReview))]
    [InlineData(nameof(DepartmentResponsesController.Approve))]
    [InlineData(nameof(DepartmentResponsesController.ReturnForCorrection))]
    [InlineData(nameof(DepartmentResponsesController.Reject))]
    public void DepartmentResponseReviewActions_RequireReviewPolicy(string actionName)
    {
        var method = GetControllerMethod<DepartmentResponsesController>(actionName);

        Assert.Equal(Policies.ReviewDepartmentResponse, GetMethodPolicy(method));
    }

    [Fact]
    public void FollowUpPrintCreateJob_RequiresCreatePolicy()
    {
        var method = GetControllerMethod<FollowUpPrintController>(nameof(FollowUpPrintController.CreateJob));

        Assert.Equal(Policies.CreateFollowUpPrintJob, GetMethodPolicy(method));
    }

    [Theory]
    [InlineData(Policies.CanEditTransactions)]
    [InlineData(Policies.CreateFollowUpPrintJob)]
    [InlineData(Policies.ReviewDepartmentResponse)]
    public void PrivilegedPolicies_DoNotAllowDepartmentUser(string policyName)
    {
        var roles = GetPolicyRoles(policyName);

        Assert.DoesNotContain(UserRole.DepartmentUser.ToString(), roles);
    }

    [Fact]
    public void SubmitDepartmentResponse_AllowsDepartmentUser()
    {
        var roles = GetPolicyRoles(Policies.SubmitDepartmentResponse);

        Assert.Contains(UserRole.DepartmentUser.ToString(), roles);
    }

    private static string GetMethodPolicy(System.Reflection.MethodInfo method) =>
        method
            .GetCustomAttributes(typeof(AuthorizeAttribute), inherit: false)
            .Cast<AuthorizeAttribute>()
            .Select(a => a.Policy)
            .Single(p => !string.IsNullOrWhiteSpace(p))!;

    private static System.Reflection.MethodInfo GetControllerMethod<TController>(string actionName) =>
        typeof(TController)
            .GetMethods()
            .Single(m => m.Name == actionName && m.GetCustomAttributes(typeof(NonActionAttribute), inherit: false).Length == 0);

    private static IReadOnlyCollection<string> GetPolicyRoles(string policyName)
    {
        var services = new ServiceCollection();
        services.AddUqebAuthorizationPolicies();
        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<AuthorizationOptions>>().Value;
        var policy = options.GetPolicy(policyName)
            ?? throw new InvalidOperationException($"Policy {policyName} was not registered.");

        return policy.Requirements
            .OfType<RolesAuthorizationRequirement>()
            .SelectMany(r => r.AllowedRoles)
            .ToArray();
    }
}
