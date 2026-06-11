namespace Uqeb.Api.Authorization;

public static class Policies
{
    public const string AdminOnly = "AdminOnly";
    public const string SupervisorOrAdmin = "SupervisorOrAdmin";
    public const string CanEditTransactions = "CanEditTransactions";
    public const string CanCloseTransactions = "CanCloseTransactions";
    public const string CanManageUsers = "CanManageUsers";
}
