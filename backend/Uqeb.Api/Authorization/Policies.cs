namespace Uqeb.Api.Authorization;

public static class Policies
{
    public const string AdminOnly = "AdminOnly";
    public const string SupervisorOrAdmin = "SupervisorOrAdmin";
    public const string CanEditTransactions = "CanEditTransactions";
    public const string CanCloseTransactions = "CanCloseTransactions";
    public const string CanManageUsers = "CanManageUsers";
    public const string ViewOperationalDashboard = "ViewOperationalDashboard";

    public const string ManageLetterTemplates = "ManageLetterTemplates";
    public const string CreateFollowUpPrintJob = "CreateFollowUpPrintJob";
    public const string ViewFollowUpPrintJobs = "ViewFollowUpPrintJobs";
    public const string CancelFollowUpPrintJob = "CancelFollowUpPrintJob";
    public const string RetryFollowUpPrintJob = "RetryFollowUpPrintJob";
    public const string PrintFollowUpLetters = "PrintFollowUpLetters";
    public const string RegisterPrintedFollowUp = "RegisterPrintedFollowUp";
    public const string CancelFollowUpPrintRecord = "CancelFollowUpPrintRecord";

    public const string SubmitDepartmentResponse = "SubmitDepartmentResponse";
    public const string ReviewDepartmentResponse = "ReviewDepartmentResponse";
}
