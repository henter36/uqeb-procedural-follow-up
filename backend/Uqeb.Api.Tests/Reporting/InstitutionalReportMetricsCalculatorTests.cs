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
    public void OpenTransactionWithLateCompletedResponseCountsAsCompletedLateNotOpenOverdue()
    {
        var today = DateTime.UtcNow.Date;
        var snapshots = new[]
        {
            new TransactionReportSnapshot
            {
                TransactionId = 1,
                IncomingDate = today.AddDays(-30),
                Status = TransactionStatus.ReadyForResponse,
                IsOpen = true,
                RequiresResponse = true,
                ResponseDueDate = today.AddDays(-10),
                ResponseCompleted = true,
                ResponseCompletedDate = today.AddDays(-5),
            },
        };
        var result = InstitutionalReportMetricsCalculator.Calculate(snapshots, today);

        Assert.Equal(1, result.OverdueCount);
        Assert.Equal(0, result.OpenOverdueCount);
        Assert.Equal(1, result.CompletedLateCount);
        Assert.Equal(result.OverdueCount, result.OpenOverdueCount + result.CompletedLateCount);
    }

    // ── Procedural completion via department referrals ─────────────────────

    [Fact]
    public void ProceduralCompletionBeforeDueDate_IsNotOverdue_DespiteLateAdministrativeApproval()
    {
        // Due 2026-07-10, last department reply 2026-07-09 (on time), supervisor doesn't
        // approve/close until 2026-07-15. The department finished on time — the transaction
        // must not be counted as overdue just because the final sign-off is still pending.
        var today = new DateTime(2026, 7, 20, 0, 0, 0, DateTimeKind.Utc);
        var snapshot = new TransactionReportSnapshot
        {
            TransactionId = 1,
            IncomingDate = new DateTime(2026, 6, 20, 0, 0, 0, DateTimeKind.Utc),
            Status = TransactionStatus.ReadyForResponse,
            IsOpen = true,
            RequiresResponse = true,
            ResponseDueDate = new DateTime(2026, 7, 10, 0, 0, 0, DateTimeKind.Utc),
            ResponseCompleted = false,
            IsProcedurallyCompleteForReporting = true,
            ProceduralCompletionDateForReporting = new DateTime(2026, 7, 9, 0, 0, 0, DateTimeKind.Utc),
        };

        Assert.False(InstitutionalReportMetricsCalculator.IsOpenOverdue(snapshot, today));
        Assert.False(InstitutionalReportMetricsCalculator.IsCompletedLate(snapshot));
        Assert.False(InstitutionalReportMetricsCalculator.IsOverdue(snapshot, today));

        var result = InstitutionalReportMetricsCalculator.Calculate([snapshot], today);
        Assert.Equal(0, result.OverdueCount);
        Assert.Equal(0, result.OpenOverdueCount);
        Assert.Equal(0, result.CompletedLateCount);
        Assert.Equal(1, result.PendingAdministrativeReviewCount);
    }

    [Fact]
    public void ProceduralCompletionAfterDueDate_IsCompletedLate_ButNotOpenOverdueForever()
    {
        // Due 2026-07-10, last department reply 2026-07-13 — the departments themselves were
        // 3 days late. That must be flagged as completed-late, but must stop being counted as
        // a continuously-open-overdue item once departments finished (regardless of when the
        // transaction is eventually formally closed).
        var today = new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc);
        var snapshot = new TransactionReportSnapshot
        {
            TransactionId = 1,
            IncomingDate = new DateTime(2026, 6, 20, 0, 0, 0, DateTimeKind.Utc),
            Status = TransactionStatus.ReadyForResponse,
            IsOpen = true,
            RequiresResponse = true,
            ResponseDueDate = new DateTime(2026, 7, 10, 0, 0, 0, DateTimeKind.Utc),
            ResponseCompleted = false,
            IsProcedurallyCompleteForReporting = true,
            ProceduralCompletionDateForReporting = new DateTime(2026, 7, 13, 0, 0, 0, DateTimeKind.Utc),
        };

        Assert.False(InstitutionalReportMetricsCalculator.IsOpenOverdue(snapshot, today));
        Assert.True(InstitutionalReportMetricsCalculator.IsCompletedLate(snapshot));
        Assert.True(InstitutionalReportMetricsCalculator.IsOverdue(snapshot, today));

        var result = InstitutionalReportMetricsCalculator.Calculate([snapshot], today);
        Assert.Equal(1, result.OverdueCount);
        Assert.Equal(0, result.OpenOverdueCount);
        Assert.Equal(1, result.CompletedLateCount);
    }

    [Fact]
    public void IncompleteReferrals_StillCountsAsOpenOverdue_NotProcedurallyComplete()
    {
        var today = new DateTime(2026, 7, 20, 0, 0, 0, DateTimeKind.Utc);
        var snapshot = new TransactionReportSnapshot
        {
            TransactionId = 1,
            IncomingDate = new DateTime(2026, 6, 20, 0, 0, 0, DateTimeKind.Utc),
            Status = TransactionStatus.WaitingForReply,
            IsOpen = true,
            RequiresResponse = true,
            ResponseDueDate = new DateTime(2026, 7, 10, 0, 0, 0, DateTimeKind.Utc),
            ResponseCompleted = false,
            IsProcedurallyCompleteForReporting = false,
            ProceduralCompletionDateForReporting = null,
        };

        Assert.True(InstitutionalReportMetricsCalculator.IsOpenOverdue(snapshot, today));
        Assert.True(InstitutionalReportMetricsCalculator.IsOverdue(snapshot, today));
    }

    [Fact]
    public void AverageCompletionDaysAndOnTimeRate_PreferProceduralDateOverLateClosure()
    {
        // Closed on 2026-07-20 (supervisor delay), but departments finished on 2026-07-09,
        // 3 days before the 2026-07-10 due date. Department-facing metrics must reflect the
        // 2026-07-09 completion, not the late formal closure.
        var today = new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc);
        var snapshot = new TransactionReportSnapshot
        {
            TransactionId = 1,
            IncomingDate = new DateTime(2026, 6, 20, 0, 0, 0, DateTimeKind.Utc),
            Status = TransactionStatus.Closed,
            IsClosed = true,
            RequiresResponse = true,
            ResponseDueDate = new DateTime(2026, 7, 10, 0, 0, 0, DateTimeKind.Utc),
            ResponseCompleted = true,
            ClosedAt = new DateTime(2026, 7, 20, 0, 0, 0, DateTimeKind.Utc),
            IsProcedurallyCompleteForReporting = true,
            ProceduralCompletionDateForReporting = new DateTime(2026, 7, 9, 0, 0, 0, DateTimeKind.Utc),
        };

        var result = InstitutionalReportMetricsCalculator.Calculate([snapshot], today);

        // (2026-07-09 - 2026-06-20) = 19 days, not (2026-07-20 - 2026-06-20) = 30 days.
        Assert.Equal(19, result.AverageCompletionDays);
        Assert.Equal(100, result.OnTimeCompletionRate);
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
