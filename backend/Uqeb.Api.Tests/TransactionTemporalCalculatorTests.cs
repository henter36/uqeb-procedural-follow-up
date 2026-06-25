using Uqeb.Api.Helpers;
using Uqeb.Api.Models.Entities;
using Uqeb.Api.Models.Enums;
using Xunit;

namespace Uqeb.Api.Tests;

public class TransactionTemporalCalculatorTests
{
    [Fact]
    public void IsOpen_excludes_terminal_statuses()
    {
        Assert.True(TransactionTemporalCalculator.IsOpen(TransactionStatus.New));
        Assert.True(TransactionTemporalCalculator.IsOpen(TransactionStatus.WaitingForReply));
        Assert.False(TransactionTemporalCalculator.IsOpen(TransactionStatus.Closed));
        Assert.False(TransactionTemporalCalculator.IsOpen(TransactionStatus.Cancelled));
        Assert.False(TransactionTemporalCalculator.IsOpen(TransactionStatus.Archived));
    }

    [Fact]
    public void IsOverdue_uses_dynamic_assignment_due_dates_without_persisted_overdue_status()
    {
        var referenceDate = new DateTime(2026, 6, 25, 12, 0, 0, DateTimeKind.Utc);
        var transaction = new Transaction
        {
            IncomingDate = referenceDate.AddDays(-20),
            RequiresResponse = false,
            Status = TransactionStatus.WaitingForReply
        };
        var facts = new[]
        {
            new TransactionTemporalCalculator.AssignmentSummaryFacts(
                ReplyStatus.Pending,
                RequiresReply: true,
                AssignmentStatus.Active,
                referenceDate.AddDays(-3))
        };

        Assert.True(TransactionTemporalCalculator.IsOverdue(transaction, facts, referenceDate));
    }

    [Fact]
    public void DaysOverdue_returns_null_when_not_past_due()
    {
        var referenceDate = new DateTime(2026, 6, 25);
        var transaction = new Transaction
        {
            IncomingDate = referenceDate.AddDays(-5),
            RequiresResponse = true,
            ResponseCompleted = false,
            ResponseDueDate = referenceDate.AddDays(2)
        };

        Assert.Null(TransactionTemporalCalculator.DaysOverdue(transaction, referenceDate));
    }

    [Fact]
    public void CompletionDays_counts_closed_transactions_only()
    {
        var incoming = new DateTime(2026, 6, 1);
        var closed = new Transaction
        {
            IncomingDate = incoming,
            Status = TransactionStatus.Closed,
            ClosedAt = incoming.AddDays(10)
        };

        Assert.Equal(10, TransactionTemporalCalculator.CompletionDays(closed));
        Assert.Null(TransactionTemporalCalculator.CompletionDays(new Transaction { Status = TransactionStatus.New, IncomingDate = incoming }));
    }
}
