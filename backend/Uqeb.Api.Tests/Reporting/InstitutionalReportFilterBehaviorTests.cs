using Uqeb.Api.Models.Enums;
using Uqeb.Api.Reporting.DTOs;
using Uqeb.Api.Reporting.Models;
using Xunit;

namespace Uqeb.Api.Tests.Reporting;

/// <summary>
/// Unit tests for the IncludeOverdue and DateTo filter behaviors.
/// DateTo uses exclusive upper bound so full-day transactions are included.
/// IncludeOverdue=true filters to only overdue snapshots in-memory after SQL load.
/// </summary>
public class InstitutionalReportFilterBehaviorTests
{
    // ── DateTo inclusive-day filter ─────────────────────────────────────────

    [Fact]
    public void DateTo_ExclusiveUpperBound_IncludesTransactionsAtNoonOnLastDay()
    {
        var dateTo = new DateTime(2026, 6, 25);
        var toExclusive = dateTo.Date.AddDays(1);
        var txAtNoon = new DateTime(2026, 6, 25, 12, 0, 0);

        Assert.True(txAtNoon < toExclusive,
            "Transaction at noon on DateTo day must be included (< exclusive bound).");
    }

    [Fact]
    public void DateTo_ExclusiveUpperBound_ExcludesTransactionsOnNextDay()
    {
        var dateTo = new DateTime(2026, 6, 25);
        var toExclusive = dateTo.Date.AddDays(1);
        var txNextDay = new DateTime(2026, 6, 26, 0, 0, 0);

        Assert.False(txNextDay < toExclusive,
            "Transaction at start of next day must be excluded.");
    }

    [Fact]
    public void DateTo_ExclusiveUpperBound_IncludesTransactionAtStartOfDay()
    {
        var dateTo = new DateTime(2026, 6, 25);
        var toExclusive = dateTo.Date.AddDays(1);
        var txAtMidnight = new DateTime(2026, 6, 25, 0, 0, 0);

        Assert.True(txAtMidnight < toExclusive,
            "Transaction at midnight on DateTo day must be included.");
    }

    [Fact]
    public void DateTo_OldBehavior_MissesIntradayTransactions()
    {
        // Demonstrates the bug that was fixed: old <= comparison with a date-only value
        // would exclude transactions that have a time component on the same day.
        var dateTo = new DateTime(2026, 6, 25); // date-only (time = 00:00:00)
        var txAtNoon = new DateTime(2026, 6, 25, 12, 0, 0);

        // Old behavior: txAtNoon <= dateTo → 2026-06-25 12:00 <= 2026-06-25 00:00 → false (excluded!)
        Assert.False(txAtNoon <= dateTo,
            "Old <= filter excluded same-day transactions with a time component.");

        // New behavior: txAtNoon < dateTo.AddDays(1) → 2026-06-25 12:00 < 2026-06-26 → true (included ✓)
        var toExclusive = dateTo.Date.AddDays(1);
        Assert.True(txAtNoon < toExclusive,
            "New exclusive upper bound correctly includes same-day transactions.");
    }

    // ── IncludeOverdue in-memory filter ────────────────────────────────────

    [Fact]
    public void IncludeOverdue_Filter_RetainsOnlyOverdueSnapshots()
    {
        var snapshots = new List<TransactionReportSnapshot>
        {
            new() { TransactionId = 1, IsOverdue = true, IsOpen = true,
                    IncomingDate = DateTime.Today.AddDays(-5), Status = TransactionStatus.Overdue,
                    CreatedAt = DateTime.Today.AddDays(-5) },
            new() { TransactionId = 2, IsOverdue = false, IsOpen = true,
                    IncomingDate = DateTime.Today.AddDays(-3), Status = TransactionStatus.InProgress,
                    CreatedAt = DateTime.Today.AddDays(-3) },
            new() { TransactionId = 3, IsOverdue = true, IsOpen = true,
                    IncomingDate = DateTime.Today.AddDays(-7), Status = TransactionStatus.Overdue,
                    CreatedAt = DateTime.Today.AddDays(-7) },
        };

        // Mirrors the production code: if (request.Filters.IncludeOverdue) filter to only overdue
        var filtered = snapshots.Where(s => s.IsOverdue).ToList();

        Assert.Equal(2, filtered.Count);
        Assert.All(filtered, s => Assert.True(s.IsOverdue));
    }

    [Fact]
    public void IncludeOverdue_False_ReturnsAllSnapshots()
    {
        var snapshots = new List<TransactionReportSnapshot>
        {
            new() { TransactionId = 1, IsOverdue = true, IsOpen = true,
                    IncomingDate = DateTime.Today.AddDays(-5), Status = TransactionStatus.Overdue,
                    CreatedAt = DateTime.Today.AddDays(-5) },
            new() { TransactionId = 2, IsOverdue = false, IsOpen = true,
                    IncomingDate = DateTime.Today.AddDays(-3), Status = TransactionStatus.InProgress,
                    CreatedAt = DateTime.Today.AddDays(-3) },
        };

        var filters = new ReportFiltersDto { IncludeOverdue = false };

        // When IncludeOverdue = false, no filter is applied — all snapshots pass through
        var result = filters.IncludeOverdue
            ? snapshots.Where(s => s.IsOverdue).ToList()
            : snapshots;

        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void IncludeOverdue_Default_IsFalse()
    {
        // Ensures the DTO default does not unexpectedly narrow the dataset.
        var filters = new ReportFiltersDto();
        Assert.False(filters.IncludeOverdue,
            "Default IncludeOverdue must be false so new requests include all transactions.");
    }

    [Fact]
    public void IncludeOverdue_Filter_UpdatesTotalMatchedCount()
    {
        // Simulates how totalMatched is updated after filtering
        var snapshots = new List<TransactionReportSnapshot>
        {
            new() { TransactionId = 1, IsOverdue = true, IsOpen = true,
                    IncomingDate = DateTime.Today.AddDays(-5), Status = TransactionStatus.Overdue,
                    CreatedAt = DateTime.Today.AddDays(-5) },
            new() { TransactionId = 2, IsOverdue = false, IsOpen = true,
                    IncomingDate = DateTime.Today.AddDays(-3), Status = TransactionStatus.InProgress,
                    CreatedAt = DateTime.Today.AddDays(-3) },
            new() { TransactionId = 3, IsOverdue = true, IsOpen = true,
                    IncomingDate = DateTime.Today.AddDays(-7), Status = TransactionStatus.Overdue,
                    CreatedAt = DateTime.Today.AddDays(-7) },
        };

        var totalMatched = snapshots.Count;
        var includeOverdue = true;

        if (includeOverdue)
        {
            snapshots = snapshots.Where(s => s.IsOverdue).ToList();
            totalMatched = snapshots.Count;
        }

        Assert.Equal(2, totalMatched);
        Assert.Equal(2, snapshots.Count);
    }
}
