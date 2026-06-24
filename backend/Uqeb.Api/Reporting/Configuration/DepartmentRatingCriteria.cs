namespace Uqeb.Api.Reporting.Configuration;

public sealed class DepartmentRatingCriteria
{
    public double GoodOnTimeRateThreshold { get; init; } = 85;
    public double NeedsFollowUpOnTimeRateThreshold { get; init; } = 70;
    public int CriticalOverdueCountThreshold { get; init; } = 5;
    public int CriticalOldestOpenDaysThreshold { get; init; } = 30;
    public int CriticalPartialResponsesThreshold { get; init; } = 3;
    public int CriticalStaleUpdateDaysThreshold { get; init; } = 14;
}
