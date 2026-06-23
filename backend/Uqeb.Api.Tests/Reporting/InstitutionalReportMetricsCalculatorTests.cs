using Uqeb.Api.Models.Enums;
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
    public void OverdueIsSubsetOfOpen()
    {
        var snapshots = new[]
        {
            Snap(1, TransactionStatus.Overdue, overdue: true),
            Snap(2, TransactionStatus.InProgress),
            Snap(3, TransactionStatus.Closed),
        };
        var result = InstitutionalReportMetricsCalculator.Calculate(snapshots, DateTime.UtcNow.Date);
        Assert.True(result.OverdueCount <= result.OpenCount);
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
}
