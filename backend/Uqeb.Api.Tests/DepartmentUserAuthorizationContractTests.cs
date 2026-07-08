using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
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
    public void GeneralReportsController_RequiresReportsViewPermission()
    {
        var permission = typeof(ReportsController)
            .GetCustomAttributes(typeof(RequirePermissionAttribute), inherit: true)
            .Cast<RequirePermissionAttribute>()
            .Single();

        Assert.Equal(PermissionCode.ReportsView, GetPermission(permission));
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
    public void DepartmentResponseAdminEdit_RequiresSupervisorOrAdminPolicy_NotTheBroaderReviewPolicy()
    {
        // AdminEdit lets a reviewer directly overwrite an already-recorded response's text/date.
        // Unlike Approve/Reject/ReturnForCorrection, DataEntry must not have this power, so it
        // must use the narrower SupervisorOrAdmin policy rather than ReviewDepartmentResponse
        // (which includes DataEntry).
        var method = GetControllerMethod<DepartmentResponsesController>(nameof(DepartmentResponsesController.AdminEdit));

        Assert.Equal(Policies.SupervisorOrAdmin, GetMethodPolicy(method));
    }

    [Fact]
    public void SupervisorOrAdmin_DoesNotAllowDataEntry()
    {
        Assert.DoesNotContain(PermissionCode.TransactionsCancel, RolePermissionDefaults.GetPermissions(UserRole.DataEntry));
    }

    [Fact]
    public void FollowUpPrintCreateJob_RequiresCreatePolicy()
    {
        var method = GetControllerMethod<FollowUpPrintController>(nameof(FollowUpPrintController.CreateJob));

        Assert.Equal(Policies.CreateFollowUpPrintJob, GetMethodPolicy(method));
    }

    [Fact]
    public void DepartmentUser_DefaultPermissions_RemainDepartmentScoped()
    {
        var permissions = RolePermissionDefaults.GetPermissions(UserRole.DepartmentUser);

        Assert.DoesNotContain(PermissionCode.DashboardView, permissions);
        Assert.DoesNotContain(PermissionCode.TransactionsEdit, permissions);
        Assert.DoesNotContain(PermissionCode.FollowUpPrintCreate, permissions);
    }

    [Fact]
    public void SubmitDepartmentResponse_AllowsDepartmentUser()
    {
        Assert.Contains(PermissionCode.TransactionResponsesEdit, RolePermissionDefaults.GetPermissions(UserRole.DepartmentUser));
    }

    [Fact]
    public void ViewOperationalDashboard_AllowsReader()
    {
        // Reader is a global read-only role (unlike DepartmentUser it carries no department
        // scope anywhere else in the system), so it must still see institution-wide dashboard
        // aggregates even though DepartmentUser is excluded.
        Assert.Contains(PermissionCode.DashboardView, RolePermissionDefaults.GetPermissions(UserRole.Reader));
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

    private static PermissionCode GetPermission(RequirePermissionAttribute attribute) =>
        (PermissionCode)(typeof(RequirePermissionAttribute)
            .GetField("_permission", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
            ?.GetValue(attribute)
            ?? throw new InvalidOperationException("RequirePermissionAttribute permission field was not found."));
}
