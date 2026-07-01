using Uqeb.Api.Helpers;
using Uqeb.Api.Models.Enums;
using Xunit;

namespace Uqeb.Api.Tests;

public class TransactionTimelineHelperTests
{
    [Fact]
    public void CompletionMetrics_ClosedTransaction_UsesClosedAt()
    {
        var metrics = TransactionTimelineHelper.Compute(
            new DateTime(2026, 7, 1),
            responseDueDate: null,
            responseDueDays: null,
            requiresResponse: false,
            responseCompleted: false,
            responseCompletedDate: null,
            status: TransactionStatus.Closed,
            closedAt: new DateTime(2026, 7, 4, 18, 30, 0),
            lastFollowUpDate: null,
            today: new DateTime(2026, 7, 5));

        Assert.Equal(new DateTime(2026, 7, 4), metrics.CompletionDate);
        Assert.Equal(3, metrics.CompletionDays);
    }

    [Fact]
    public void CompletionMetrics_ResponseCompletedTransaction_UsesResponseCompletedDate()
    {
        var metrics = TransactionTimelineHelper.Compute(
            new DateTime(2026, 7, 1),
            responseDueDate: null,
            responseDueDays: null,
            requiresResponse: true,
            responseCompleted: true,
            responseCompletedDate: new DateTime(2026, 7, 3, 9, 0, 0),
            status: TransactionStatus.ResponseCompleted,
            closedAt: null,
            lastFollowUpDate: null,
            today: new DateTime(2026, 7, 5));

        Assert.Equal(new DateTime(2026, 7, 3), metrics.CompletionDate);
        Assert.Equal(2, metrics.CompletionDays);
    }

    [Fact]
    public void CompletionMetrics_OpenTransaction_HasNoCompletion()
    {
        var metrics = TransactionTimelineHelper.Compute(
            new DateTime(2026, 7, 1),
            responseDueDate: null,
            responseDueDays: null,
            requiresResponse: false,
            responseCompleted: false,
            responseCompletedDate: null,
            status: TransactionStatus.InProgress,
            closedAt: null,
            lastFollowUpDate: null,
            today: new DateTime(2026, 7, 5));

        Assert.Null(metrics.CompletionDate);
        Assert.Null(metrics.CompletionDays);
    }

    [Fact]
    public void CompletionMetrics_CompletionBeforeIncoming_ClampsToZero()
    {
        var metrics = TransactionTimelineHelper.Compute(
            new DateTime(2026, 7, 4),
            responseDueDate: null,
            responseDueDays: null,
            requiresResponse: false,
            responseCompleted: false,
            responseCompletedDate: null,
            status: TransactionStatus.Closed,
            closedAt: new DateTime(2026, 7, 1),
            lastFollowUpDate: null,
            today: new DateTime(2026, 7, 5));

        Assert.Equal(new DateTime(2026, 7, 1), metrics.CompletionDate);
        Assert.Equal(0, metrics.CompletionDays);
    }

    [Theory]
    [InlineData(TransactionStatus.Archived)]
    [InlineData(TransactionStatus.Cancelled)]
    public void CompletionMetrics_ArchivedOrCancelledWithoutClosedAt_HasNoCompletion(TransactionStatus status)
    {
        var metrics = TransactionTimelineHelper.Compute(
            new DateTime(2026, 7, 1),
            responseDueDate: null,
            responseDueDays: null,
            requiresResponse: false,
            responseCompleted: false,
            responseCompletedDate: null,
            status,
            closedAt: null,
            lastFollowUpDate: null,
            today: new DateTime(2026, 7, 5));

        Assert.Null(metrics.CompletionDate);
        Assert.Null(metrics.CompletionDays);
    }
}
