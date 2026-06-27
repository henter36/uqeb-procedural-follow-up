using Uqeb.Api.Reporting.DTOs;
using Uqeb.Api.Reporting.Models;

namespace Uqeb.Api.Reporting.Services;

/// <summary>
/// Central reporting reference date and temporal facts derived from report metadata.
/// All "business date" resolutions use Riyadh local time (Asia/Riyadh / Arab Standard Time).
/// UTC timestamps are used only for storage and audit; never passed directly as business dates.
/// </summary>
internal static class ReportingTemporalCalculator
{
    // Windows uses "Arab Standard Time"; IANA on Linux/macOS uses "Asia/Riyadh".
    private static readonly TimeZoneInfo RiyadhTz = FindRiyadhTimeZone();

    private static TimeZoneInfo FindRiyadhTimeZone()
    {
        if (TimeZoneInfo.TryFindSystemTimeZoneById("Asia/Riyadh", out var iana))
            return iana;
        if (TimeZoneInfo.TryFindSystemTimeZoneById("Arab Standard Time", out var win))
            return win;
        // Last resort: UTC+3 fixed offset (no DST — Saudi Arabia does not observe DST).
        return TimeZoneInfo.CreateCustomTimeZone("AST+3", TimeSpan.FromHours(3), "Riyadh (fallback)", "Riyadh");
    }

    /// <summary>
    /// Returns today's business date in Riyadh local time (UTC+3, no DST).
    /// Use this instead of DateTime.UtcNow.Date to avoid the ~21:00 UTC cut-over issue.
    /// </summary>
    public static DateTime RiyadhBusinessDate(TimeProvider? clock = null)
    {
        var utcNow = clock is not null
            ? clock.GetUtcNow().UtcDateTime
            : DateTime.UtcNow;
        var riyadhLocal = TimeZoneInfo.ConvertTimeFromUtc(utcNow, RiyadhTz);
        return riyadhLocal.Date;
    }

    /// <summary>
    /// Converts a Riyadh business date to a UTC DateTime representing midnight Riyadh time.
    /// Use this for inclusive period boundaries in database queries.
    /// </summary>
    public static DateTime ToUtcPeriodStart(DateTime riyadhDate) =>
        TimeZoneInfo.ConvertTimeToUtc(
            DateTime.SpecifyKind(riyadhDate.Date, DateTimeKind.Unspecified), RiyadhTz);

    /// <summary>
    /// Converts a Riyadh business date to a UTC DateTime representing end-of-day (23:59:59.999) Riyadh time.
    /// Use this for inclusive period end boundaries in database queries.
    /// </summary>
    public static DateTime ToUtcPeriodEnd(DateTime riyadhDate) =>
        TimeZoneInfo.ConvertTimeToUtc(
            DateTime.SpecifyKind(riyadhDate.Date.AddDays(1).AddTicks(-1), DateTimeKind.Unspecified), RiyadhTz);

    /// <summary>
    /// Resolves the report reference date. GeneratedAt is stored in UTC; we derive the
    /// Riyadh calendar date so comparisons against IncomingDate (also stored as UTC) are consistent.
    /// </summary>
    public static DateTime ResolveReferenceDate(ReportMetadataDto metadata)
    {
        if (metadata.GeneratedAt == default)
            return RiyadhBusinessDate();
        // GeneratedAt is UTC — convert to Riyadh to get the correct calendar date.
        var generatedUtc = DateTime.SpecifyKind(metadata.GeneratedAt, DateTimeKind.Utc);
        return TimeZoneInfo.ConvertTimeFromUtc(generatedUtc, RiyadhTz).Date;
    }

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
