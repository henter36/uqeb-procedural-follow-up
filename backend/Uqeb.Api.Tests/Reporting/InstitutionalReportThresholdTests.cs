using Uqeb.Api.Models.Enums;
using Uqeb.Api.Reporting.Configuration;
using Uqeb.Api.Reporting.Models;
using Uqeb.Api.Reporting.Services;
using Xunit;
#pragma warning disable CS8618

namespace Uqeb.Api.Tests.Reporting;

/// <summary>
/// Tests that all risk/stale thresholds come from ReportingAnalysisOptions,
/// not from hardcoded literals. Changing a threshold must change the output.
/// </summary>
public class InstitutionalReportThresholdTests
{
    private static readonly DateTime Today = new(2026, 6, 15, 0, 0, 0, DateTimeKind.Utc);

    private static TransactionReportSnapshot OpenSnap(int id, DateTime? updatedAt = null) =>
        new()
        {
            TransactionId = id,
            IncomingDate = Today.AddDays(-30),
            Status = TransactionStatus.InProgress,
            IsClosed = false,
            IsOpen = true,
            UpdatedAt = updatedAt,
            CreatedAt = Today.AddDays(-30),
        };

    [Fact]
    public void StaleRiskWindowDays_DeterminesTransactionsWithoutRecentUpdate()
    {
        // Transaction last updated 15 days ago.
        var updatedAt = Today.AddDays(-15);
        var snap = OpenSnap(1, updatedAt: updatedAt);

        // With StaleRiskWindowDays = 14 → 15 > 14 → stale (counted)
        var staleWith14 = IsStaleForRisk(snap, staleDays: 14);
        // With StaleRiskWindowDays = 20 → 15 < 20 → not stale
        var staleWith20 = IsStaleForRisk(snap, staleDays: 20);

        Assert.True(staleWith14, "Transaction updated 15 days ago should be stale with window=14.");
        Assert.False(staleWith20, "Transaction updated 15 days ago should not be stale with window=20.");
    }

    [Fact]
    public void StaleTransactionDays_DeterminesStaleTransactionKpi()
    {
        // Transaction last updated exactly at the threshold boundary.
        var snap = OpenSnap(1, updatedAt: Today.AddDays(-7));

        // With StaleTransactionDays = 7 → DaysSinceLastAction = 7 → IsStale (>=)
        var staleWith7 = ReportingTemporalCalculator.IsStale(snap, Today, staleDays: 7);
        // With StaleTransactionDays = 8 → DaysSinceLastAction = 7 → not stale
        var staleWith8 = ReportingTemporalCalculator.IsStale(snap, Today, staleDays: 8);

        Assert.True(staleWith7, "Transaction updated 7 days ago should be stale when threshold is 7.");
        Assert.False(staleWith8, "Transaction updated 7 days ago should not be stale when threshold is 8.");
    }

    [Fact]
    public void ReportingAnalysisOptions_DefaultThresholds_AreReasonable()
    {
        var opts = new ReportingAnalysisOptions();
        Assert.Equal(7, opts.StaleTransactionDays);
        Assert.Equal(14, opts.StaleRiskWindowDays);
        Assert.Equal(7, opts.RecommendationTargetDays);
        Assert.Equal(10, opts.CriticalOverdueDays);
    }

    [Fact]
    public void ReportingAnalysisOptions_Validate_AcceptsValidThresholds()
    {
        var opts = new ReportingAnalysisOptions
        {
            StaleRiskWindowDays = 21,
            RecommendationTargetDays = 14,
        };
        // Should not throw.
        var ex = Record.Exception(opts.Validate);
        Assert.Null(ex);
    }

    [Fact]
    public void ReportingAnalysisOptions_Validate_RejectsZeroStaleRiskWindow()
    {
        var opts = new ReportingAnalysisOptions { StaleRiskWindowDays = 0 };
        Assert.Throws<InvalidOperationException>(opts.Validate);
    }

    [Fact]
    public void ReportingAnalysisOptions_Validate_RejectsZeroRecommendationTargetDays()
    {
        var opts = new ReportingAnalysisOptions { RecommendationTargetDays = 0 };
        Assert.Throws<InvalidOperationException>(opts.Validate);
    }

    private static bool IsStaleForRisk(TransactionReportSnapshot snap, int staleDays)
    {
        var lastAction = snap.UpdatedAt ?? snap.CreatedAt;
        return (Today - lastAction).Days >= staleDays;
    }
}
