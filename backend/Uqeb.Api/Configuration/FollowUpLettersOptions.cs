namespace Uqeb.Api.Configuration;

public class FollowUpLettersOptions
{
    public const string SectionName = "FollowUpLetters";

    public string DisplayTimeZoneId { get; set; } = "Arab Standard Time";

    public int DefaultDaysSinceLastFollowUp { get; set; } = 10;
    public int MinDaysSinceLastFollowUp { get; set; } = 1;
    public int MaxDaysSinceLastFollowUp { get; set; } = 365;

    public bool ExcludeRecentlyPrintedByDefault { get; set; } = true;
    public int PrintedLetterExclusionDays { get; set; } = 7;
    public int MinPrintedLetterExclusionDays { get; set; } = 1;
    public int MaxPrintedLetterExclusionDays { get; set; } = 90;

    public int DefaultBatchPrintSize { get; set; } = 25;
    public int MaxBatchPrintSize { get; set; } = 50;
    public int AbsoluteMaxBatchPrintSize { get; set; } = 100;
    public int MaxEstimatedPagesPerPart { get; set; } = 100;
    public int MaxConcurrentPrintJobs { get; set; } = 2;
    public int MaxConcurrentJobsPerUser { get; set; } = 1;
    public int JobLeaseSeconds { get; set; } = 120;
    public int JobRetryCount { get; set; } = 3;
    public int JobExpirationHours { get; set; } = 24;
    public int NotificationPollIntervalSeconds { get; set; } = 45;

    public void Validate()
    {
        if (DefaultDaysSinceLastFollowUp < MinDaysSinceLastFollowUp || DefaultDaysSinceLastFollowUp > MaxDaysSinceLastFollowUp)
            throw new InvalidOperationException("DefaultDaysSinceLastFollowUp is out of range.");

        if (PrintedLetterExclusionDays < MinPrintedLetterExclusionDays || PrintedLetterExclusionDays > MaxPrintedLetterExclusionDays)
            throw new InvalidOperationException("PrintedLetterExclusionDays is out of range.");

        if (DefaultBatchPrintSize <= 0 || MaxBatchPrintSize <= 0 || AbsoluteMaxBatchPrintSize <= 0)
            throw new InvalidOperationException("Batch print sizes must be positive.");

        if (DefaultBatchPrintSize > MaxBatchPrintSize || MaxBatchPrintSize > AbsoluteMaxBatchPrintSize)
            throw new InvalidOperationException("Batch print size hierarchy is invalid.");
    }
}
