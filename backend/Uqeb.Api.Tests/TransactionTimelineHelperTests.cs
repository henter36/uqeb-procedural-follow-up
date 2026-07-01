using Uqeb.Api.Helpers;
using Uqeb.Api.Models.Enums;
using Xunit;

namespace Uqeb.Api.Tests;

public class TransactionTimelineHelperTests
{
    [Fact]
    public void CompletionMetrics_ClosedTransaction_UsesClosedAt()
    {
        var metrics = TransactionTimelineHelper.Compute(new TransactionTimelineHelper.TimelineComputationInput
        {
            IncomingDate = new DateTime(2026, 7, 1),
            RequiresResponse = false,
            ResponseCompleted = false,
            Status = TransactionStatus.Closed,
            ClosedAt = new DateTime(2026, 7, 4, 18, 30, 0),
            Today = new DateTime(2026, 7, 5)
        });

        Assert.Equal(new DateTime(2026, 7, 4), metrics.CompletionDate);
        Assert.Equal(3, metrics.CompletionDays);
    }

    [Fact]
    public void CompletionMetrics_ResponseCompletedTransaction_UsesResponseCompletedDate()
    {
        var metrics = TransactionTimelineHelper.Compute(new TransactionTimelineHelper.TimelineComputationInput
        {
            IncomingDate = new DateTime(2026, 7, 1),
            RequiresResponse = true,
            ResponseCompleted = true,
            ResponseCompletedDate = new DateTime(2026, 7, 3, 9, 0, 0),
            Status = TransactionStatus.ResponseCompleted,
            Today = new DateTime(2026, 7, 5)
        });

        Assert.Equal(new DateTime(2026, 7, 3), metrics.CompletionDate);
        Assert.Equal(2, metrics.CompletionDays);
    }

    [Fact]
    public void CompletionMetrics_OpenTransaction_HasNoCompletion()
    {
        var metrics = TransactionTimelineHelper.Compute(new TransactionTimelineHelper.TimelineComputationInput
        {
            IncomingDate = new DateTime(2026, 7, 1),
            RequiresResponse = false,
            ResponseCompleted = false,
            Status = TransactionStatus.InProgress,
            Today = new DateTime(2026, 7, 5)
        });

        Assert.Null(metrics.CompletionDate);
        Assert.Null(metrics.CompletionDays);
    }

    [Fact]
    public void CompletionMetrics_CompletionBeforeIncoming_ClampsToZero()
    {
        var metrics = TransactionTimelineHelper.Compute(new TransactionTimelineHelper.TimelineComputationInput
        {
            IncomingDate = new DateTime(2026, 7, 4),
            RequiresResponse = false,
            ResponseCompleted = false,
            Status = TransactionStatus.Closed,
            ClosedAt = new DateTime(2026, 7, 1),
            Today = new DateTime(2026, 7, 5)
        });

        Assert.Equal(new DateTime(2026, 7, 1), metrics.CompletionDate);
        Assert.Equal(0, metrics.CompletionDays);
    }

    [Theory]
    [InlineData(TransactionStatus.Archived)]
    [InlineData(TransactionStatus.Cancelled)]
    public void CompletionMetrics_ArchivedOrCancelledWithoutClosedAt_HasNoCompletion(TransactionStatus status)
    {
        var metrics = TransactionTimelineHelper.Compute(new TransactionTimelineHelper.TimelineComputationInput
        {
            IncomingDate = new DateTime(2026, 7, 1),
            RequiresResponse = false,
            ResponseCompleted = false,
            Status = status,
            Today = new DateTime(2026, 7, 5)
        });

        Assert.Null(metrics.CompletionDate);
        Assert.Null(metrics.CompletionDays);
    }

    [Theory]
    [InlineData(TransactionStatus.Archived)]
    [InlineData(TransactionStatus.Cancelled)]
    public void CompletionMetrics_ArchivedOrCancelledWithClosedAt_UsesClosedAt(TransactionStatus status)
    {
        var metrics = TransactionTimelineHelper.Compute(new TransactionTimelineHelper.TimelineComputationInput
        {
            IncomingDate = new DateTime(2026, 7, 1),
            RequiresResponse = false,
            ResponseCompleted = false,
            Status = status,
            ClosedAt = new DateTime(2026, 7, 6, 8, 0, 0),
            Today = new DateTime(2026, 7, 7)
        });

        Assert.Equal(new DateTime(2026, 7, 6), metrics.CompletionDate);
        Assert.Equal(5, metrics.CompletionDays);
    }
}
