using Xunit;
using Uqeb.Api.Reporting.Enums;
using Uqeb.Api.Reporting.Models;
using Uqeb.Api.Reporting.Services;
using Uqeb.Api.Models.Enums;

namespace Uqeb.Api.Tests.Reporting;

public class InstitutionalReportMetricsCalculatorTests
{
    private static TransactionReportSnapshot Snap(int id, bool closed = false, bool overdue = false, bool partial = false, bool joint = false) =>
        new()
        {
            TransactionId = id,
            IncomingDate = DateTime.UtcNow.Date.AddDays(-10),
            IsClosed = closed,
            IsOpen = !closed,
            IsOverdue = overdue,
            IsPartialReply = partial,
            IsJointDepartment = joint,
            Status = closed ? TransactionStatus.Closed : TransactionStatus.InProgress
        };

    [Fact]
    public void TotalEqualsOpenPlusClosed()
    {
        var snapshots = new[] { Snap(1), Snap(2, closed: true), Snap(3) };
        var result = InstitutionalReportMetricsCalculator.Calculate(snapshots, DateTime.UtcNow.Date);
        Assert.Equal(3, result.TotalTransactions);
        Assert.Equal(2, result.OpenCount);
        Assert.Equal(1, result.ClosedCount);
        Assert.Equal(result.OpenCount + result.ClosedCount, result.TotalTransactions);
    }

    [Fact]
    public void OverdueIsSubsetOfOpen()
    {
        var snapshots = new[] { Snap(1, overdue: true), Snap(2), Snap(3, closed: true) };
        var result = InstitutionalReportMetricsCalculator.Calculate(snapshots, DateTime.UtcNow.Date);
        Assert.True(result.OverdueCount <= result.OpenCount);
    }

    [Fact]
    public void PartialAndJointDoNotDoubleTotal()
    {
        var snapshots = new[]
        {
            Snap(1, partial: true, joint: true),
            Snap(2, partial: true),
            Snap(3, joint: true)
        };
        var result = InstitutionalReportMetricsCalculator.Calculate(snapshots, DateTime.UtcNow.Date);
        Assert.Equal(3, result.TotalTransactions);
        Assert.Equal(2, result.PartialResponseCount);
        Assert.Equal(2, result.JointDepartmentCount);
    }
}
