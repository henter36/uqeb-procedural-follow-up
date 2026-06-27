using Uqeb.Api.Reporting.DTOs;
using Uqeb.Api.Reporting.Services;
using Xunit;

namespace Uqeb.Api.Tests.Reporting;

/// <summary>
/// Tests for the central temporal engine: Riyadh business date resolution,
/// UTC↔Riyadh conversion, and midnight-boundary correctness.
/// </summary>
public class ReportingTemporalCalculatorTests
{
    // ── RiyadhBusinessDate ────────────────────────────────────────────────────

    [Fact]
    public void RiyadhBusinessDate_ReturnsRiyadhDate_NotUtcDate()
    {
        // 20:30 UTC on 2026-06-15 is 23:30 Riyadh (UTC+3) → still June 15.
        // 22:00 UTC on 2026-06-15 is 01:00 Riyadh on June 16 → different calendar date.
        // This test uses a fixed clock to assert the Riyadh calendar date is used.
        var utcAt2100 = new DateTime(2026, 6, 15, 21, 0, 0, DateTimeKind.Utc);
        var clock = new FixedClock(utcAt2100);

        var result = ReportingTemporalCalculator.RiyadhBusinessDate(clock);

        // 21:00 UTC = midnight Riyadh (21+3=24 → 00:00 next day)
        // So 21:00 UTC on June 15 = 00:00 June 16 Riyadh → business date is June 16.
        Assert.Equal(new DateTime(2026, 6, 16), result);
    }

    [Fact]
    public void RiyadhBusinessDate_LateEveningUtc_IsNextDayRiyadh()
    {
        // 22:00 UTC = 01:00 Riyadh next day.
        var clock = new FixedClock(new DateTime(2026, 6, 15, 22, 0, 0, DateTimeKind.Utc));
        var result = ReportingTemporalCalculator.RiyadhBusinessDate(clock);
        Assert.Equal(new DateTime(2026, 6, 16), result);
    }

    [Fact]
    public void RiyadhBusinessDate_EarlyMorningUtc_IsSameDayRiyadh()
    {
        // 06:00 UTC = 09:00 Riyadh → same calendar date.
        var clock = new FixedClock(new DateTime(2026, 6, 15, 6, 0, 0, DateTimeKind.Utc));
        var result = ReportingTemporalCalculator.RiyadhBusinessDate(clock);
        Assert.Equal(new DateTime(2026, 6, 15), result);
    }

    [Fact]
    public void RiyadhBusinessDate_JustBeforeMidnightRiyadh_IsSameDay()
    {
        // 20:59 UTC = 23:59 Riyadh — still June 15.
        var clock = new FixedClock(new DateTime(2026, 6, 15, 20, 59, 0, DateTimeKind.Utc));
        var result = ReportingTemporalCalculator.RiyadhBusinessDate(clock);
        Assert.Equal(new DateTime(2026, 6, 15), result);
    }

    [Fact]
    public void RiyadhBusinessDate_AtMidnightRiyadh_IsNextDay()
    {
        // 21:00 UTC = 00:00 Riyadh next day → June 16.
        var clock = new FixedClock(new DateTime(2026, 6, 15, 21, 0, 0, DateTimeKind.Utc));
        var result = ReportingTemporalCalculator.RiyadhBusinessDate(clock);
        Assert.Equal(new DateTime(2026, 6, 16), result);
    }

    // ── ResolveReferenceDate ──────────────────────────────────────────────────

    [Fact]
    public void ResolveReferenceDate_UsesRiyadhCalendarDate_NotUtcDate()
    {
        // GeneratedAt is stored as UTC. 22:00 UTC June 15 = 01:00 June 16 Riyadh.
        // The reference date should be June 16 (Riyadh), not June 15 (UTC).
        var metadata = new ReportMetadataDto
        {
            GeneratedAt = new DateTime(2026, 6, 15, 22, 0, 0, DateTimeKind.Utc)
        };

        var result = ReportingTemporalCalculator.ResolveReferenceDate(metadata);

        Assert.Equal(new DateTime(2026, 6, 16), result);
    }

    [Fact]
    public void ResolveReferenceDate_WithDefaultGeneratedAt_ReturnsNonDefault()
    {
        // When GeneratedAt is default, fall back to RiyadhBusinessDate() (today).
        var metadata = new ReportMetadataDto { GeneratedAt = default };
        var result = ReportingTemporalCalculator.ResolveReferenceDate(metadata);
        // Should be a non-default date (roughly today).
        Assert.NotEqual(default(DateTime), result);
        Assert.True(result.Year >= 2026);
    }

    // ── ToUtcPeriodStart / ToUtcPeriodEnd ─────────────────────────────────────

    [Fact]
    public void ToUtcPeriodStart_ConvertsRiyadhMidnightToUtc()
    {
        // Riyadh midnight June 15 = UTC 21:00 June 14.
        var riyadhDate = new DateTime(2026, 6, 15);
        var utcStart = ReportingTemporalCalculator.ToUtcPeriodStart(riyadhDate);
        Assert.Equal(new DateTime(2026, 6, 14, 21, 0, 0, DateTimeKind.Utc), utcStart);
    }

    [Fact]
    public void ToUtcPeriodEnd_ConvertsRiyadhEndOfDayToUtc()
    {
        // Riyadh end-of-day June 15 = last tick before midnight = 23:59:59.9999999 local.
        // UTC = Riyadh - 3h → 20:59:59.9999999 UTC on June 15.
        var riyadhDate = new DateTime(2026, 6, 15);
        var utcEnd = ReportingTemporalCalculator.ToUtcPeriodEnd(riyadhDate);

        // Verify date and time components independently (avoids tick-level equality traps).
        Assert.Equal(DateTimeKind.Utc, utcEnd.Kind);
        Assert.Equal(2026, utcEnd.Year);
        Assert.Equal(6, utcEnd.Month);
        Assert.Equal(15, utcEnd.Day);
        Assert.Equal(20, utcEnd.Hour);
        Assert.Equal(59, utcEnd.Minute);
        Assert.Equal(59, utcEnd.Second);
        // Must be strictly before the next Riyadh-midnight boundary (UTC 21:00 June 15).
        Assert.True(utcEnd < new DateTime(2026, 6, 15, 21, 0, 0, DateTimeKind.Utc));
    }

    // ── Fixed clock helper ────────────────────────────────────────────────────

    private sealed class FixedClock(DateTime utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(utcNow, TimeSpan.Zero);
    }
}
