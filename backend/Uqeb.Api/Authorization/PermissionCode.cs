namespace Uqeb.Api.Authorization;

public enum PermissionCode
{
    DashboardView = 100,

    TransactionsView = 200,
    TransactionsCreate = 201,
    TransactionsEdit = 202,
    TransactionsCancel = 203,
    TransactionsExport = 204,

    TransactionDetailsView = 300,
    TransactionAssignmentsCreate = 301,
    TransactionResponsesEdit = 302,
    TransactionAttachmentsManage = 303,

    ReportsView = 400,
    ReportsBuild = 401,
    ReportsExportPdf = 402,
    ReportsExportExcel = 403,
    ReportsTemplatesManage = 404,

    FollowUpPrintView = 500,
    FollowUpPrintCreate = 501,
    FollowUpPrintExport = 502,

    LookupsView = 600,
    LookupsManage = 601,

    UsersView = 700,
    UsersManage = 701,
    UserPermissionsManage = 702,

    SystemSettingsView = 800,
    SystemSettingsManage = 801
}
