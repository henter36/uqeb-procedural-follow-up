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
    public int MaxOutstandingUnprintedJobs { get; set; } = 10;
    public int MaxTemplateContentLength { get; set; } = 50_000;
    public int JobLeaseSeconds { get; set; } = 120;
    public int JobRetryCount { get; set; } = 3;
    public int JobExpirationHours { get; set; } = 24;
    public int NotificationPollIntervalSeconds { get; set; } = 45;

    // Render limits: defend against unbounded documents and body text.
    public int MaxLettersPerPrintView { get; set; } = 100;
    public int MaxBodyLinesPerLetter { get; set; } = 500;
    public int MaxBodyCharactersPerLetter { get; set; } = 20_000;

    public void Validate()
    {
        ValidateFollowUpDaysRange();
        ValidateExclusionDaysRange();
        ValidateBatchSizes();
        ValidateJobSettings();
        ValidateRenderLimits();

        if (!Uqeb.Api.Services.FollowUpLetterTimeZone.CanResolveConfiguredTimeZone(DisplayTimeZoneId))
            throw new InvalidOperationException("DisplayTimeZoneId is invalid.");
    }

    private void ValidateFollowUpDaysRange()
    {
        if (DefaultDaysSinceLastFollowUp < MinDaysSinceLastFollowUp || DefaultDaysSinceLastFollowUp > MaxDaysSinceLastFollowUp)
            throw new InvalidOperationException("DefaultDaysSinceLastFollowUp is out of range.");

        if (PrintedLetterExclusionDays < MinPrintedLetterExclusionDays || PrintedLetterExclusionDays > MaxPrintedLetterExclusionDays)
            throw new InvalidOperationException("PrintedLetterExclusionDays is out of range.");
    }

    private void ValidateExclusionDaysRange()
    {
        if (MaxTemplateContentLength <= 0)
            throw new InvalidOperationException("MaxTemplateContentLength must be positive.");

        if (MaxOutstandingUnprintedJobs <= 0)
            throw new InvalidOperationException("MaxOutstandingUnprintedJobs must be positive.");

        if (MaxEstimatedPagesPerPart <= 0)
            throw new InvalidOperationException("MaxEstimatedPagesPerPart must be positive.");
    }

    private void ValidateBatchSizes()
    {
        if (DefaultBatchPrintSize <= 0 || MaxBatchPrintSize <= 0 || AbsoluteMaxBatchPrintSize <= 0)
            throw new InvalidOperationException("Batch print sizes must be positive.");

        if (DefaultBatchPrintSize > MaxBatchPrintSize || MaxBatchPrintSize > AbsoluteMaxBatchPrintSize)
            throw new InvalidOperationException("Batch print size hierarchy is invalid.");
    }

    private void ValidateJobSettings()
    {
        if (MaxConcurrentPrintJobs <= 0)
            throw new InvalidOperationException("MaxConcurrentPrintJobs must be positive.");
        if (MaxConcurrentJobsPerUser <= 0)
            throw new InvalidOperationException("MaxConcurrentJobsPerUser must be positive.");
        if (JobLeaseSeconds <= 0)
            throw new InvalidOperationException("JobLeaseSeconds must be positive.");
        if (JobRetryCount < 0)
            throw new InvalidOperationException("JobRetryCount must be non-negative.");
        if (JobExpirationHours <= 0)
            throw new InvalidOperationException("JobExpirationHours must be positive.");
        if (NotificationPollIntervalSeconds <= 0)
            throw new InvalidOperationException("NotificationPollIntervalSeconds must be positive.");
    }

    private void ValidateRenderLimits()
    {
        if (MaxLettersPerPrintView <= 0)
            throw new InvalidOperationException("MaxLettersPerPrintView must be positive.");
        if (MaxBodyLinesPerLetter <= 0)
            throw new InvalidOperationException("MaxBodyLinesPerLetter must be positive.");
        if (MaxBodyCharactersPerLetter <= 0)
            throw new InvalidOperationException("MaxBodyCharactersPerLetter must be positive.");
    }
}
