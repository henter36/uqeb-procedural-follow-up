using Uqeb.Api.Models.Enums;

namespace Uqeb.Api.Authorization;

public static class RolePermissionDefaults
{
    public static IReadOnlySet<PermissionCode> GetPermissions(UserRole role) =>
        role switch
        {
            UserRole.Admin => Enum.GetValues<PermissionCode>().ToHashSet(),
            UserRole.Supervisor => new HashSet<PermissionCode>
            {
                PermissionCode.DashboardView,
                PermissionCode.TransactionsView,
                PermissionCode.TransactionsCreate,
                PermissionCode.TransactionsEdit,
                PermissionCode.TransactionsCancel,
                PermissionCode.TransactionsExport,
                PermissionCode.TransactionDetailsView,
                PermissionCode.TransactionAssignmentsCreate,
                PermissionCode.TransactionResponsesEdit,
                PermissionCode.TransactionAttachmentsManage,
                PermissionCode.ReportsView,
                PermissionCode.ReportsExportPdf,
                PermissionCode.ReportsExportExcel,
                PermissionCode.FollowUpPrintView,
                PermissionCode.FollowUpPrintCreate,
                PermissionCode.FollowUpPrintExport,
                PermissionCode.LookupsView,
            },
            UserRole.DataEntry => new HashSet<PermissionCode>
            {
                PermissionCode.DashboardView,
                PermissionCode.TransactionsView,
                PermissionCode.TransactionsCreate,
                PermissionCode.TransactionsEdit,
                PermissionCode.TransactionsExport,
                PermissionCode.TransactionDetailsView,
                PermissionCode.TransactionAssignmentsCreate,
                PermissionCode.TransactionResponsesEdit,
                PermissionCode.TransactionAttachmentsManage,
                PermissionCode.ReportsView,
                PermissionCode.ReportsExportExcel,
                PermissionCode.FollowUpPrintView,
                PermissionCode.FollowUpPrintCreate,
                PermissionCode.FollowUpPrintExport,
                PermissionCode.LookupsView,
            },
            UserRole.DepartmentUser => new HashSet<PermissionCode>
            {
                PermissionCode.TransactionDetailsView,
                PermissionCode.TransactionResponsesEdit,
                PermissionCode.TransactionAttachmentsManage,
            },
            UserRole.Reader => new HashSet<PermissionCode>
            {
                PermissionCode.DashboardView,
                PermissionCode.TransactionsView,
                PermissionCode.TransactionDetailsView,
                PermissionCode.LookupsView,
            },
            _ => new HashSet<PermissionCode>(),
        };
}
