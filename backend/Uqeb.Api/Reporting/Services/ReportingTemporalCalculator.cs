using Uqeb.Api.Reporting.DTOs;
using Uqeb.Api.Reporting.Models;

namespace Uqeb.Api.Reporting.Services;

/// <summary>
/// Central reporting reference date and temporal facts derived from report metadata.
/// </summary>
internal static class ReportingTemporalCalculator
{
    public static DateTime ResolveReferenceDate(ReportMetadataDto metadata) =>
        metadata.GeneratedAt == default ? DateTime.UtcNow.Date : metadata.GeneratedAt.Date;

    public static bool IsOpen(TransactionReportSnapshot snapshot) => snapshot.IsOpen;

    public static bool IsOverdue(TransactionReportSnapshot snapshot, DateTime referenceDate) =>
        InstitutionalReportMetricsCalculator.IsOverdue(snapshot, referenceDate);

    public static bool IsResponseOverdue(TransactionReportSnapshot snapshot, DateTime referenceDate) =>
        snapshot.RequiresResponse
        && !snapshot.ResponseCompleted
        && snapshot.ResponseDueDate.HasValue
        && snapshot.ResponseDueDate.Value.Date < referenceDate.Date;

    public static int AgeDays(TransactionReportSnapshot snapshot, DateTime referenceDate) =>
        Math.Max(0, (referenceDate.Date - snapshot.IncomingDate.Date).Days);

    public static int? DaysOverdue(TransactionReportSnapshot snapshot, DateTime referenceDate)
    {
        var dueDates = new[] { snapshot.ResponseDueDate, snapshot.EarliestPendingReplyDueDate }
            .Where(d => d.HasValue)
            .Select(d => d!.Value.Date)
            .ToList();
        if (dueDates.Count == 0)
            return snapshot.IsOverdue ? snapshot.ElapsedDays : null;

        return Math.Max(0, (referenceDate.Date - dueDates.Min()).Days);
    }

    public static bool IsStale(TransactionReportSnapshot snapshot, DateTime referenceDate, int staleDays) =>
        DaysSinceLastAction(snapshot, referenceDate) >= staleDays;

    public static int? CompletionDays(TransactionReportSnapshot snapshot) =>
        snapshot.IsClosed && snapshot.ClosedAt.HasValue
            ? Math.Max(0, (snapshot.ClosedAt!.Value.Date - snapshot.IncomingDate.Date).Days)
            : null;

    public static int DaysSinceLastAction(TransactionReportSnapshot snapshot, DateTime referenceDate)
    {
        var lastAction = new[] { snapshot.UpdatedAt, snapshot.LastFollowUpDate, snapshot.ClosedAt }
            .Where(d => d.HasValue)
            .Select(d => d!.Value.Date)
            .DefaultIfEmpty(snapshot.CreatedAt.Date)
            .Max();
        return Math.Max(0, (referenceDate.Date - lastAction).Days);
    }
}
