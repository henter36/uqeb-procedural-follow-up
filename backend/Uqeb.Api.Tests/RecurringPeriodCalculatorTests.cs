using Uqeb.Api.Helpers;
using Uqeb.Api.Models.Enums;
using Xunit;

namespace Uqeb.Api.Tests;

public class RecurringPeriodCalculatorTests
{
    [Fact]
    public void Compute_Monthly_calculates_period_start_and_end_correctly()
    {
        var period = RecurringPeriodCalculator.Compute(RecurrenceType.Monthly, "2026-07", 10);

        Assert.Equal(new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc), period.PeriodStart);
        Assert.Equal(new DateTime(2026, 7, 31, 0, 0, 0, DateTimeKind.Utc), period.PeriodEnd);
        Assert.Equal(new DateTime(2026, 8, 10, 0, 0, 0, DateTimeKind.Utc), period.DueDate);
        Assert.Equal("يوليو 2026", period.PeriodLabel);
    }

    [Fact]
    public void Compute_does_not_overflow_for_the_maximum_accepted_DueDaysAfterPeriodEnd_at_max_year()
    {
        // RecurringTemplateRequestValidator caps DueDaysAfterPeriodEnd at 365 and periodKey years at 3000;
        // this is the most extreme input the validator allows through to the calculator.
        var period = RecurringPeriodCalculator.Compute(RecurrenceType.Monthly, "3000-12", 365);

        Assert.True(period.DueDate > period.PeriodEnd);
    }

    [Fact]
    public void Compute_Monthly_handles_February_end_of_month_in_leap_year()
    {
        var period = RecurringPeriodCalculator.Compute(RecurrenceType.Monthly, "2028-02", 0);

        Assert.Equal(new DateTime(2028, 2, 29, 0, 0, 0, DateTimeKind.Utc), period.PeriodEnd);
    }

    [Fact]
    public void Compute_Quarterly_calculates_period_start_and_end_correctly()
    {
        var period = RecurringPeriodCalculator.Compute(RecurrenceType.Quarterly, "2026-Q3", 10);

        Assert.Equal(new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc), period.PeriodStart);
        Assert.Equal(new DateTime(2026, 9, 30, 0, 0, 0, DateTimeKind.Utc), period.PeriodEnd);
        Assert.Equal(new DateTime(2026, 10, 10, 0, 0, 0, DateTimeKind.Utc), period.DueDate);
        Assert.Equal("الربع الثالث 2026", period.PeriodLabel);
    }

    [Fact]
    public void Compute_Quarterly_calculates_first_quarter_correctly()
    {
        var period = RecurringPeriodCalculator.Compute(RecurrenceType.Quarterly, "2026-Q1", 0);

        Assert.Equal(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), period.PeriodStart);
        Assert.Equal(new DateTime(2026, 3, 31, 0, 0, 0, DateTimeKind.Utc), period.PeriodEnd);
    }

    [Theory]
    [InlineData(RecurrenceType.Monthly, "2026-01")]
    [InlineData(RecurrenceType.Quarterly, "2026-Q1")]
    public void Compute_returns_Utc_DateTimeKind_for_all_period_dates(RecurrenceType recurrenceType, string periodKey)
    {
        var period = RecurringPeriodCalculator.Compute(recurrenceType, periodKey, 5);

        Assert.Equal(DateTimeKind.Utc, period.PeriodStart.Kind);
        Assert.Equal(DateTimeKind.Utc, period.PeriodEnd.Kind);
        Assert.Equal(DateTimeKind.Utc, period.DueDate.Kind);
    }

    [Fact]
    public void GetNextPeriodKey_Monthly_returns_first_period_from_StartDate_when_none_generated_yet()
    {
        var next = RecurringPeriodCalculator.GetNextPeriodKey(
            RecurrenceType.Monthly,
            new DateTime(2026, 3, 15, 0, 0, 0, DateTimeKind.Utc),
            lastGeneratedPeriodKey: null);

        Assert.Equal("2026-03", next);
    }

    [Fact]
    public void GetNextPeriodKey_Monthly_returns_period_after_last_generated()
    {
        var next = RecurringPeriodCalculator.GetNextPeriodKey(
            RecurrenceType.Monthly,
            new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            lastGeneratedPeriodKey: "2026-12");

        Assert.Equal("2027-01", next);
    }

    [Fact]
    public void GetNextPeriodKey_Quarterly_returns_quarter_after_last_generated()
    {
        var next = RecurringPeriodCalculator.GetNextPeriodKey(
            RecurrenceType.Quarterly,
            new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            lastGeneratedPeriodKey: "2026-Q4");

        Assert.Equal("2027-Q1", next);
    }
}
