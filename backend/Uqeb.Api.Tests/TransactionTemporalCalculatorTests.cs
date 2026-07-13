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
    public void DaysOverdue_counts_open_transaction_from_due_date_to_reference_date()
    {
        var referenceDate = new DateTime(2026, 6, 25, 18, 30, 0, DateTimeKind.Utc);
        var transaction = new Transaction
        {
            IncomingDate = new DateTime(2026, 6, 1),
            RequiresResponse = true,
            ResponseCompleted = false,
            ResponseDueDate = new DateTime(2026, 6, 20, 23, 59, 0, DateTimeKind.Utc),
            Status = TransactionStatus.WaitingForReply
        };

        Assert.Equal(5, TransactionTemporalCalculator.DaysOverdue(transaction, referenceDate));
    }

    [Fact]
    public void IsResponseOverdue_does_not_count_due_today_as_overdue()
    {
        var referenceDate = new DateTime(2026, 6, 25, 18, 30, 0, DateTimeKind.Utc);
        var transaction = new Transaction
        {
            IncomingDate = referenceDate.AddDays(-5),
            RequiresResponse = true,
            ResponseCompleted = false,
            ResponseDueDate = referenceDate.Date,
            Status = TransactionStatus.WaitingForReply
        };

        Assert.False(TransactionTemporalCalculator.IsResponseOverdue(transaction, referenceDate));
        Assert.Null(TransactionTemporalCalculator.DaysOverdue(transaction, referenceDate));
    }

    [Fact]
    public void IsAssignmentOverdue_does_not_count_due_today_as_overdue()
    {
        var referenceDate = new DateTime(2026, 6, 25, 18, 30, 0, DateTimeKind.Utc);

        Assert.False(TransactionTemporalCalculator.IsAssignmentOverdue(
            ReplyStatus.Pending,
            requiresReply: true,
            AssignmentStatus.Active,
            new DateTime(2026, 6, 25, 0, 0, 0, DateTimeKind.Utc),
            referenceDate));
    }

    [Fact]
    public void IsOverdue_and_DaysOverdue_use_same_date_only_assignment_boundary()
    {
        var referenceDate = new DateTime(2026, 6, 25, 18, 30, 0, DateTimeKind.Utc);
        var transaction = new Transaction
        {
            IncomingDate = new DateTime(2026, 6, 1),
            RequiresResponse = false,
            Status = TransactionStatus.WaitingForReply
        };
        var facts = new[]
        {
            new TransactionTemporalCalculator.AssignmentSummaryFacts(
                ReplyStatus.Pending,
                RequiresReply: true,
                AssignmentStatus.Active,
                new DateTime(2026, 6, 25, 0, 0, 0, DateTimeKind.Utc))
        };

        Assert.False(TransactionTemporalCalculator.IsOverdue(transaction, facts, referenceDate));
        Assert.Null(TransactionTemporalCalculator.DaysOverdue(transaction, referenceDate, facts[0].DueDate));
    }

    [Fact]
    public void IsResponseOverdue_counts_completed_transaction_after_due_date()
    {
        var transaction = new Transaction
        {
            IncomingDate = new DateTime(2026, 1, 1),
            RequiresResponse = true,
            ResponseCompleted = true,
            ResponseCompletedDate = new DateTime(2026, 1, 10),
            ResponseDueDate = new DateTime(2026, 1, 5),
            Status = TransactionStatus.ResponseCompleted
        };

        Assert.True(TransactionTemporalCalculator.IsResponseOverdue(transaction, new DateTime(2026, 1, 20)));
        Assert.Equal(5, TransactionTemporalCalculator.DaysOverdue(transaction, new DateTime(2026, 1, 20)));
    }

    [Fact]
    public void DaysOverdue_returns_null_for_on_time_completed_transaction()
    {
        var transaction = new Transaction
        {
            IncomingDate = new DateTime(2026, 1, 1),
            RequiresResponse = true,
            ResponseCompleted = true,
            ResponseCompletedDate = new DateTime(2026, 1, 5, 18, 0, 0, DateTimeKind.Utc),
            ResponseDueDate = new DateTime(2026, 1, 5, 0, 0, 0, DateTimeKind.Utc),
            Status = TransactionStatus.ResponseCompleted
        };

        Assert.False(TransactionTemporalCalculator.IsResponseOverdue(transaction, new DateTime(2026, 1, 20)));
        Assert.Null(TransactionTemporalCalculator.DaysOverdue(transaction, new DateTime(2026, 1, 20)));
    }

    [Fact]
    public void IsResponseOverdue_uses_response_completion_before_later_closed_date()
    {
        var transaction = new Transaction
        {
            IncomingDate = new DateTime(2026, 1, 1),
            RequiresResponse = true,
            ResponseCompleted = true,
            ResponseCompletedDate = new DateTime(2026, 1, 5),
            ClosedAt = new DateTime(2026, 1, 12),
            ResponseDueDate = new DateTime(2026, 1, 5),
            Status = TransactionStatus.Closed
        };

        Assert.False(TransactionTemporalCalculator.IsResponseOverdue(transaction, new DateTime(2026, 1, 20)));
        Assert.Null(TransactionTemporalCalculator.DaysOverdue(transaction, new DateTime(2026, 1, 20)));
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
