using Uqeb.Api.Models.Enums;

namespace Uqeb.Api.Reporting.Services;

/// <summary>
/// Defines how transaction statuses are bucketed in institutional report KPIs.
/// <list type="bullet">
/// <item><description><see cref="IsOperationalOpen"/> — active operational work (excludes Closed, Cancelled, Archived).</description></item>
/// <item><description><see cref="IsOperationalClosed"/> — successfully closed transactions only.</description></item>
/// <item><description>Cancelled and Archived — terminal states counted in TotalTransactions but excluded from Open/Closed operational KPIs.</description></item>
/// </list>
/// </summary>
public static class TransactionStatusSemantics
{
    public static bool IsOperationalOpen(TransactionStatus status) =>
        status is not TransactionStatus.Closed
            and not TransactionStatus.Cancelled
            and not TransactionStatus.Archived;

    public static bool IsOperationalClosed(TransactionStatus status) =>
        status == TransactionStatus.Closed;

    public static bool IsCancelled(TransactionStatus status) =>
        status == TransactionStatus.Cancelled;

    public static bool IsArchived(TransactionStatus status) =>
        status == TransactionStatus.Archived;
}
