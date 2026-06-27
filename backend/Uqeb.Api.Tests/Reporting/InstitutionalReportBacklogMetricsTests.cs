using Uqeb.Api.Models.Enums;
using Uqeb.Api.Reporting.Configuration;
using Uqeb.Api.Reporting.DTOs;
using Uqeb.Api.Reporting.Enums;
using Uqeb.Api.Reporting.Models;
using Uqeb.Api.Reporting.Services;
using Xunit;

namespace Uqeb.Api.Tests.Reporting;

/// <summary>
/// Tests for BacklogGrowthRate and AverageResponseDays correctness:
/// - BacklogGrowthRate must be unavailable without a comparison period.
/// - BacklogGrowthRate must reflect the correct delta when comparison exists.
/// - AverageResponseDays must be unavailable when no ResponseCompleted transactions exist.
/// - AverageResponseDays must be estimated (not zero) when data exists.
/// </summary>
public class InstitutionalReportBacklogMetricsTests
{
    private static readonly DateTime ReferenceDate = new(2026, 6, 15, 0, 0, 0, DateTimeKind.Utc);

    private static readonly ReportingAnalysisOptions DefaultOptions = new()
    {
        SignificantChangeThresholdPercent = 10,
        StableChangeThresholdPercent = 3,
        MinimumComparisonSampleSize = 1,
        MinimumRankingSampleSize = 1,
        MaxExecutiveFindings = 10,
        MaxExecutiveCriticalCases = 10,
        MaxRecommendations = 10,
        CriticalOverdueDays = 10,
        StaleTransactionDays = 7,
    };

    private static TransactionReportSnapshot OpenSnap(int id, bool responseCompleted = false, int? closedDaysAgo = null) =>
        new()
        {
            TransactionId = id,
            IncomingDate = ReferenceDate.AddDays(-20),
            Status = closedDaysAgo.HasValue ? TransactionStatus.Closed : TransactionStatus.InProgress,
            IsClosed = closedDaysAgo.HasValue,
            IsOpen = !closedDaysAgo.HasValue,
            ClosedAt = closedDaysAgo.HasValue ? ReferenceDate.AddDays(-closedDaysAgo.Value) : null,
            RequiresResponse = true,
            ResponseCompleted = responseCompleted,
        };

    private static InstitutionalReportAnalysisService.InstitutionalReportAnalysisInput BuildInput(
        IReadOnlyList<TransactionReportSnapshot> current,
        IReadOnlyList<TransactionReportSnapshot>? previous = null)
    {
        var currentMetrics = InstitutionalReportMetricsCalculator.Calculate(current, ReferenceDate);
        var previousMetrics = previous is not null
            ? InstitutionalReportMetricsCalculator.Calculate(previous, ReferenceDate.AddMonths(-1))
            : null;

        return new InstitutionalReportAnalysisService.InstitutionalReportAnalysisInput
        {
            Request = new ReportBuildRequestDto
            {
                ReportType = InstitutionalReportType.ExecutiveComprehensive,
                SectionIds = [ReportSectionId.KeyPerformanceIndicators],
                Filters = new ReportFiltersDto
                {
                    DateFrom = ReferenceDate.AddDays(-30),
                    DateTo = ReferenceDate
                }
            },
            Metadata = new ReportMetadataDto
            {
                GeneratedAt = ReferenceDate,
                PeriodFrom = ReferenceDate.AddDays(-30),
                PeriodTo = ReferenceDate,
                Title = "اختبار",
                ReportNumber = "T-001"
            },
            Filters = new ReportFiltersDto(),
            CurrentMetrics = currentMetrics,
            CurrentSnapshots = current,
            PreviousMetrics = previousMetrics,
            PreviousSnapshots = previous ?? [],
            Options = DefaultOptions,
            DetailLimit = 500,
            DetailRowsTruncated = false
        };
    }

    // ── BacklogGrowthRate ─────────────────────────────────────────────────────

    [Fact]
    public void BacklogGrowthRate_WithoutComparisonPeriod_IsUnavailable()
    {
        var snapshots = new[] { OpenSnap(1), OpenSnap(2), OpenSnap(3) };
        var result = InstitutionalReportAnalysisService.Build(BuildInput(snapshots, previous: null));

        var kpi = result.Kpis.Single(k => k.Key == "BacklogGrowthRate");
        Assert.False(kpi.IsAvailable, "BacklogGrowthRate must be unavailable when no comparison period exists.");
        Assert.NotEmpty(kpi.UnavailableReason ?? string.Empty);
    }

    [Fact]
    public void BacklogGrowthRate_WithComparisonPeriod_ReflectsCorrectDelta()
    {
        // Current: 5 open. Previous: 3 open. Expected delta: +2.
        var current = Enumerable.Range(1, 5).Select(i => OpenSnap(i)).ToArray();
        var previous = Enumerable.Range(10, 3).Select(i => OpenSnap(i)).ToArray();

        var result = InstitutionalReportAnalysisService.Build(BuildInput(current, previous));

        var kpi = result.Kpis.Single(k => k.Key == "BacklogGrowthRate");
        Assert.True(kpi.IsAvailable, "BacklogGrowthRate must be available when comparison period exists.");
        Assert.Equal(2m, kpi.NumericValue);
    }

    [Fact]
    public void BacklogGrowthRate_WithZeroPreviousOpen_ShowsAbsoluteDeltaNotMisleadingRate()
    {
        // Current: 4 open. Previous: 0 open (all closed). Delta = 4.
        var current = Enumerable.Range(1, 4).Select(i => OpenSnap(i)).ToArray();
        // Previous period: all closed, no open transactions.
        var previous = new[]
        {
            OpenSnap(10, closedDaysAgo: 5),
            OpenSnap(11, closedDaysAgo: 3),
        };

        var result = InstitutionalReportAnalysisService.Build(BuildInput(current, previous));

        var kpi = result.Kpis.Single(k => k.Key == "BacklogGrowthRate");
        Assert.True(kpi.IsAvailable);
        // Delta = 4 - 0 = 4. Should be a numeric delta, not a percentage.
        Assert.Equal(4m, kpi.NumericValue);
        Assert.Equal("number", kpi.Format);
    }

    [Fact]
    public void BacklogGrowthRate_IsLowerIsBetter()
    {
        // Confirms the KPI direction is set correctly regardless of comparison mode.
        var current = new[] { OpenSnap(1), OpenSnap(2) };
        var previous = new[] { OpenSnap(10) };
        var result = InstitutionalReportAnalysisService.Build(BuildInput(current, previous));
        var kpi = result.Kpis.Single(k => k.Key == "BacklogGrowthRate");
        Assert.Equal(KpiDirection.LowerIsBetter, kpi.Direction);
    }

    // ── AverageResponseDays ───────────────────────────────────────────────────

    [Fact]
    public void AverageResponseDays_WithNoResponseCompletedTransactions_IsUnavailable()
    {
        // All open, none with ResponseCompleted → metric unavailable.
        var snapshots = new[] { OpenSnap(1), OpenSnap(2) };
        var result = InstitutionalReportAnalysisService.Build(BuildInput(snapshots));

        var kpi = result.Kpis.Single(k => k.Key == "AverageResponseDays");
        Assert.False(kpi.IsAvailable, "AverageResponseDays must be unavailable when no ResponseCompleted transactions exist.");
        Assert.NotEmpty(kpi.UnavailableReason ?? string.Empty);
    }

    [Fact]
    public void AverageResponseDays_WithResponseCompletedTransactions_IsAvailableAndPositive()
    {
        // Closed with ResponseCompleted=true: incoming 20 days ago, closed 5-15 days ago.
        var snapshots = new[]
        {
            OpenSnap(1, responseCompleted: true, closedDaysAgo: 15),  // incoming-to-close = 20-15 = 5 days
            OpenSnap(2, responseCompleted: true, closedDaysAgo: 10),  // incoming-to-close = 20-10 = 10 days
        };

        var result = InstitutionalReportAnalysisService.Build(BuildInput(snapshots));

        var kpi = result.Kpis.Single(k => k.Key == "AverageResponseDays");
        Assert.True(kpi.IsAvailable, "AverageResponseDays must be available when ResponseCompleted transactions exist.");
        Assert.True(kpi.NumericValue > 0, "AverageResponseDays value must be positive.");
    }

    [Fact]
    public void AverageResponseDays_IsDistinctFrom_AverageCompletionDays()
    {
        // ResponseDays uses only ResponseCompleted=true transactions.
        // CompletionDays uses all closed transactions.
        var snapshots = new[]
        {
            OpenSnap(1, responseCompleted: true, closedDaysAgo: 15),   // response path: 5 days
            OpenSnap(2, responseCompleted: false, closedDaysAgo: 0),   // completion path: 20 days
        };

        var result = InstitutionalReportAnalysisService.Build(BuildInput(snapshots));

        var completionKpi = result.Kpis.Single(k => k.Key == "AverageCompletionDays");
        var responseKpi = result.Kpis.Single(k => k.Key == "AverageResponseDays");

        Assert.True(completionKpi.IsAvailable);
        Assert.True(responseKpi.IsAvailable);
        // They should differ because they use different transaction subsets.
        Assert.NotEqual(completionKpi.NumericValue, responseKpi.NumericValue);
    }

    [Fact]
    public void AverageResponseDays_OpenWithoutResponse_DoesNotContribute()
    {
        // Open transaction requires response but it's not completed → must NOT contribute to avg.
        var snapshots = new[]
        {
            OpenSnap(1, responseCompleted: false),                    // open, no response
            OpenSnap(2, responseCompleted: true, closedDaysAgo: 10), // closed with response: 10 days
        };

        var result = InstitutionalReportAnalysisService.Build(BuildInput(snapshots));

        var kpi = result.Kpis.Single(k => k.Key == "AverageResponseDays");
        // Only tx#2 contributes: 20 - 10 = 10 days from incoming to close.
        Assert.True(kpi.IsAvailable);
        Assert.Equal(10m, kpi.NumericValue);
    }
}
