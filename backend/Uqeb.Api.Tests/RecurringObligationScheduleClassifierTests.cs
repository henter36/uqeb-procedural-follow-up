using Uqeb.Api.Helpers;
using Xunit;

namespace Uqeb.Api.Tests;

public class RecurringObligationScheduleClassifierTests
{
    [Fact]
    public void Classify_returns_Overdue_when_due_date_is_before_today()
    {
        var now = new DateTime(2026, 7, 10, 0, 0, 0, DateTimeKind.Utc);
        var dueDate = new DateTime(2026, 7, 9, 0, 0, 0, DateTimeKind.Utc);

        var (status, daysRemaining) = RecurringObligationScheduleClassifier.Classify(dueDate, now, dueSoonWithinDays: 7);

        Assert.Equal(RecurringObligationScheduleStatus.Overdue, status);
        Assert.Equal(-1, daysRemaining);
    }

    [Fact]
    public void Classify_returns_DueSoon_when_due_date_is_today()
    {
        var now = new DateTime(2026, 7, 10, 0, 0, 0, DateTimeKind.Utc);
        var dueDate = new DateTime(2026, 7, 10, 0, 0, 0, DateTimeKind.Utc);

        var (status, daysRemaining) = RecurringObligationScheduleClassifier.Classify(dueDate, now, dueSoonWithinDays: 7);

        Assert.Equal(RecurringObligationScheduleStatus.DueSoon, status);
        Assert.Equal(0, daysRemaining);
    }

    [Fact]
    public void Classify_returns_DueSoon_exactly_at_the_threshold_boundary()
    {
        var now = new DateTime(2026, 7, 10, 0, 0, 0, DateTimeKind.Utc);
        var dueDate = new DateTime(2026, 7, 17, 0, 0, 0, DateTimeKind.Utc); // exactly 7 days out

        var (status, daysRemaining) = RecurringObligationScheduleClassifier.Classify(dueDate, now, dueSoonWithinDays: 7);

        Assert.Equal(RecurringObligationScheduleStatus.DueSoon, status);
        Assert.Equal(7, daysRemaining);
    }

    [Fact]
    public void Classify_returns_Upcoming_one_day_past_the_threshold_boundary()
    {
        var now = new DateTime(2026, 7, 10, 0, 0, 0, DateTimeKind.Utc);
        var dueDate = new DateTime(2026, 7, 18, 0, 0, 0, DateTimeKind.Utc); // 8 days out

        var (status, daysRemaining) = RecurringObligationScheduleClassifier.Classify(dueDate, now, dueSoonWithinDays: 7);

        Assert.Equal(RecurringObligationScheduleStatus.Upcoming, status);
        Assert.Equal(8, daysRemaining);
    }

    [Fact]
    public void Classify_ignores_time_of_day_and_only_compares_calendar_dates()
    {
        // "now" is 23:59:59 on the due date itself. A naive (dueDate - now).TotalDays
        // would be a small negative fraction that truncates to -1, misclassifying a
        // same-day obligation as overdue by one day. Classify must use .Date-only
        // comparison so this still resolves to "due today" (DaysRemaining = 0).
        var dueDate = new DateTime(2026, 7, 10, 0, 0, 0, DateTimeKind.Utc);
        var now = new DateTime(2026, 7, 10, 23, 59, 59, DateTimeKind.Utc);

        var (status, daysRemaining) = RecurringObligationScheduleClassifier.Classify(dueDate, now, dueSoonWithinDays: 7);

        Assert.Equal(RecurringObligationScheduleStatus.DueSoon, status);
        Assert.Equal(0, daysRemaining);
    }

    [Fact]
    public void Classify_ignores_time_of_day_on_the_due_date_itself()
    {
        // dueDate carries a non-midnight time component; only its Date should count.
        var dueDate = new DateTime(2026, 7, 20, 18, 30, 0, DateTimeKind.Utc);
        var now = new DateTime(2026, 7, 10, 6, 0, 0, DateTimeKind.Utc);

        var (status, daysRemaining) = RecurringObligationScheduleClassifier.Classify(dueDate, now, dueSoonWithinDays: 7);

        Assert.Equal(RecurringObligationScheduleStatus.Upcoming, status);
        Assert.Equal(10, daysRemaining);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    [InlineData(14)]
    public void Classify_respects_a_custom_due_soon_threshold(int dueSoonWithinDays)
    {
        var now = new DateTime(2026, 7, 10, 0, 0, 0, DateTimeKind.Utc);
        var dueDateAtThreshold = now.AddDays(dueSoonWithinDays);
        var dueDatePastThreshold = now.AddDays(dueSoonWithinDays + 1);

        var (atThresholdStatus, _) = RecurringObligationScheduleClassifier.Classify(dueDateAtThreshold, now, dueSoonWithinDays);
        var (pastThresholdStatus, _) = RecurringObligationScheduleClassifier.Classify(dueDatePastThreshold, now, dueSoonWithinDays);

        Assert.Equal(RecurringObligationScheduleStatus.DueSoon, atThresholdStatus);
        Assert.Equal(RecurringObligationScheduleStatus.Upcoming, pastThresholdStatus);
    }
}
