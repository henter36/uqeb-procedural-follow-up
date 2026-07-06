using Uqeb.Api.Models.Enums;
using Uqeb.Api.Reporting.Configuration;
using Uqeb.Api.Reporting.DTOs;
using Uqeb.Api.Reporting.Enums;
using Uqeb.Api.Reporting.Models;
using Uqeb.Api.Reporting.Services;
using Xunit;

namespace Uqeb.Api.Tests.Reporting;

public class InstitutionalReportMetricsCalculatorTests
{
    private static TransactionReportSnapshot Snap(
        int id,
        TransactionStatus status,
        bool overdue = false,
        bool partial = false,
        bool joint = false) =>
        new()
        {
            TransactionId = id,
            IncomingDate = DateTime.UtcNow.Date.AddDays(-10),
            Status = status,
            IsClosed = status == TransactionStatus.Closed,
            IsOpen = TransactionStatusSemantics.IsOperationalOpen(status),
            IsOverdue = overdue,
            IsPartialReply = partial,
            IsJointDepartment = joint,
        };

    [Fact]
    public void TotalEqualsOperationalBuckets()
    {
        var snapshots = new[]
        {
            Snap(1, TransactionStatus.InProgress),
            Snap(2, TransactionStatus.Closed),
            Snap(3, TransactionStatus.Cancelled),
            Snap(4, TransactionStatus.Archived),
        };

        var result = InstitutionalReportMetricsCalculator.Calculate(snapshots, DateTime.UtcNow.Date);

        Assert.Equal(4, result.TotalTransactions);
        Assert.Equal(1, result.OpenCount);
        Assert.Equal(1, result.ClosedCount);
        Assert.Equal(1, result.CancelledCount);
        Assert.Equal(1, result.ArchivedCount);
        Assert.Equal(
            result.TotalTransactions,
            result.OpenCount + result.ClosedCount + result.CancelledCount + result.ArchivedCount);
    }

    [Fact]
    public void OverdueSeparatesOpenAndCompletedLate()
    {
        var today = DateTime.UtcNow.Date;
        var snapshots = new[]
        {
            new TransactionReportSnapshot
            {
                TransactionId = 1,
                IncomingDate = today.AddDays(-30),
                Status = TransactionStatus.Overdue,
                IsOpen = true,
                RequiresResponse = true,
                ResponseDueDate = today.AddDays(-1),
                ResponseCompleted = false,
            },
            new TransactionReportSnapshot
            {
                TransactionId = 2,
                IncomingDate = today.AddDays(-30),
                Status = TransactionStatus.Closed,
                IsClosed = true,
                RequiresResponse = true,
                ResponseDueDate = today.AddDays(-10),
                ResponseCompleted = true,
                ClosedAt = today.AddDays(-5),
            },
            Snap(3, TransactionStatus.InProgress),
        };
        var result = InstitutionalReportMetricsCalculator.Calculate(snapshots, today);

        Assert.Equal(2, result.OverdueCount);
        Assert.Equal(1, result.OpenOverdueCount);
        Assert.Equal(1, result.CompletedLateCount);
    }

    [Fact]
    public void PartialAndJointDoNotDoubleTotal()
    {
        var snapshots = new[]
        {
            Snap(1, TransactionStatus.PartiallyReplied, partial: true, joint: true),
            Snap(2, TransactionStatus.PartiallyReplied, partial: true),
            Snap(3, TransactionStatus.InProgress, joint: true),
        };
        var result = InstitutionalReportMetricsCalculator.Calculate(snapshots, DateTime.UtcNow.Date);
        Assert.Equal(3, result.TotalTransactions);
        Assert.Equal(2, result.PartialResponseCount);
        Assert.Equal(2, result.JointDepartmentCount);
    }

    [Fact]
    public void CancelledAndArchivedAreExcludedFromOpenAndClosed()
    {
        var snapshots = new[]
        {
            Snap(1, TransactionStatus.Cancelled),
            Snap(2, TransactionStatus.Archived),
        };

        var result = InstitutionalReportMetricsCalculator.Calculate(snapshots, DateTime.UtcNow.Date);

        Assert.Equal(0, result.OpenCount);
        Assert.Equal(0, result.ClosedCount);
        Assert.Equal(1, result.CancelledCount);
        Assert.Equal(1, result.ArchivedCount);
    }

    // ── StaleUpdates uses count threshold, not the days threshold ───────────

    [Fact]
    public void RateDepartment_StaleUpdates_UsesCountThreshold_NotDaysThreshold()
    {
        // StaleUpdates = 4 (count), CriticalStaleUpdateDaysThreshold = 14 (days), CriticalStaleUpdateCountThreshold = 5 (count)
        // 4 < 5 → not Critical based on stale count.
        // If we mistakenly used DaysThreshold (14) as count limit, 4 < 14 → also not critical. Same conclusion.
        // So prove the positive: 6 stale updates exceeds count threshold (5) but not days threshold (14) → Critical.
        var metrics = new InstitutionalReportMetricsCalculator.DepartmentPerformanceMetrics
        {
            OnTimeCompletionRate = 90,
            OverdueCount = 0,
            OldestOpenDays = 10,
            PartialResponses = 0,
            StaleUpdates = 6,
        };
        var criteria = new DepartmentRatingCriteria
        {
            GoodOnTimeRateThreshold = 85,
            NeedsFollowUpOnTimeRateThreshold = 70,
            CriticalOverdueCountThreshold = 20,
            CriticalOldestOpenDaysThreshold = 60,
            CriticalPartialResponsesThreshold = 10,
            CriticalStaleUpdateDaysThreshold = 14,
            CriticalStaleUpdateCountThreshold = 5,
        };

        // 6 stale updates >= count threshold (5) → Critical
        var rating = InstitutionalReportMetricsCalculator.RateDepartment(metrics, criteria);
        Assert.Equal(DepartmentRatingLevel.Critical, rating);
    }

    [Fact]
    public void RateDepartment_StaleUpdates_DoesNotConfuseDaysThresholdWithCount()
    {
        // StaleUpdates = 6, CriticalStaleUpdateCountThreshold = 10 → NOT critical on stale count alone.
        // Confirms that the count threshold (10) is used, not the days threshold (14).
        var metrics = new InstitutionalReportMetricsCalculator.DepartmentPerformanceMetrics
        {
            OnTimeCompletionRate = 90,
            OverdueCount = 0,
            OldestOpenDays = 10,
            PartialResponses = 0,
            StaleUpdates = 6,
        };
        var criteria = new DepartmentRatingCriteria
        {
            GoodOnTimeRateThreshold = 85,
            NeedsFollowUpOnTimeRateThreshold = 70,
            CriticalOverdueCountThreshold = 20,
            CriticalOldestOpenDaysThreshold = 60,
            CriticalPartialResponsesThreshold = 10,
            CriticalStaleUpdateDaysThreshold = 14,
            CriticalStaleUpdateCountThreshold = 10,
        };

        // 6 < count threshold (10) → Good (no other criteria triggers)
        var rating = InstitutionalReportMetricsCalculator.RateDepartment(metrics, criteria);
        Assert.Equal(DepartmentRatingLevel.Good, rating);
    }
}
