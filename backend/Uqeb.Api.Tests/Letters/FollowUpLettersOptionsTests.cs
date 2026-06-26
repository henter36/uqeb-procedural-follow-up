using Uqeb.Api.Configuration;
using Xunit;

namespace Uqeb.Api.Tests.Letters;

public class FollowUpLettersOptionsTests
{
    [Fact]
    public void Validate_AcceptsDefaultOptions()
    {
        var options = new FollowUpLettersOptions();

        options.Validate();
    }

    [Theory]
    [InlineData(nameof(FollowUpLettersOptions.MaxEstimatedPagesPerPart))]
    [InlineData(nameof(FollowUpLettersOptions.MaxConcurrentPrintJobs))]
    [InlineData(nameof(FollowUpLettersOptions.MaxConcurrentJobsPerUser))]
    [InlineData(nameof(FollowUpLettersOptions.JobLeaseSeconds))]
    [InlineData(nameof(FollowUpLettersOptions.JobExpirationHours))]
    [InlineData(nameof(FollowUpLettersOptions.NotificationPollIntervalSeconds))]
    [InlineData(nameof(FollowUpLettersOptions.MaxOutstandingUnprintedJobs))]
    [InlineData(nameof(FollowUpLettersOptions.MaxTemplateContentLength))]
    public void Validate_RejectsNonPositiveRequiredValues(string propertyName)
    {
        var options = new FollowUpLettersOptions();
        typeof(FollowUpLettersOptions).GetProperty(propertyName)!.SetValue(options, 0);

        Assert.Throws<InvalidOperationException>(options.Validate);
    }

    [Fact]
    public void Validate_RejectsNegativeRetryCount()
    {
        var options = new FollowUpLettersOptions { JobRetryCount = -1 };

        Assert.Throws<InvalidOperationException>(options.Validate);
    }

    [Fact]
    public void Validate_RejectsAbsoluteMaxBatchBelowDefaultBatch()
    {
        var options = new FollowUpLettersOptions
        {
            DefaultBatchPrintSize = 50,
            MaxBatchPrintSize = 50,
            AbsoluteMaxBatchPrintSize = 25,
        };

        Assert.Throws<InvalidOperationException>(options.Validate);
    }

    [Fact]
    public void Validate_RejectsInvalidConfiguredTimezone()
    {
        var options = new FollowUpLettersOptions { DisplayTimeZoneId = "Not/A/Real/Zone" };

        Assert.Throws<InvalidOperationException>(options.Validate);
    }
}
