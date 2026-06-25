using System.Globalization;
using Microsoft.Extensions.Logging.Abstractions;
using Uqeb.Api.Services;
using Xunit;

namespace Uqeb.Api.Tests.Letters;

public class FollowUpLetterTimeZoneTests
{
    [Theory]
    [InlineData("Arab Standard Time")]
    [InlineData("Asia/Riyadh")]
    public void ResolveTimeZone_AcceptsKnownIds(string timeZoneId)
    {
        var timeZone = FollowUpLetterTimeZone.ResolveTimeZone(timeZoneId, NullLogger.Instance);

        Assert.NotNull(timeZone);
        Assert.False(string.IsNullOrWhiteSpace(timeZone.Id));
    }

    [Fact]
    public void ResolveTimeZone_FallsBackWhenConfiguredIdMissing()
    {
        var timeZone = FollowUpLetterTimeZone.ResolveTimeZone("Not/A/Real/Zone", NullLogger.Instance);

        Assert.Contains(timeZone.Id, new[] { "Asia/Riyadh", "Arab Standard Time" });
    }

    [Fact]
    public void ResolveTimeZone_TriesAsiaRiyadh_BeforeArabStandardTimeFallback()
    {
        var configured = FollowUpLetterTimeZone.ResolveTimeZone("Asia/Riyadh", NullLogger.Instance);
        var fallback = FollowUpLetterTimeZone.ResolveTimeZone(null, NullLogger.Instance);

        Assert.Contains(configured.Id, new[] { "Asia/Riyadh", "Arab Standard Time" });
        Assert.Contains(fallback.Id, new[] { "Asia/Riyadh", "Arab Standard Time" });
    }

    [Theory]
    [InlineData("2025-06-25T20:59:59Z", "2025-06-25")]
    [InlineData("2025-06-25T21:00:00Z", "2025-06-26")]
    public void ToDisplayTime_HandlesUtcBoundary(string utcText, string expectedDate)
    {
        var utc = DateTime.Parse(utcText, null, System.Globalization.DateTimeStyles.AdjustToUniversal);
        var id = OperatingSystem.IsWindows() ? "Arab Standard Time" : "Asia/Riyadh";
        var service = new FollowUpLetterTimeZone(
            Microsoft.Extensions.Options.Options.Create(new Uqeb.Api.Configuration.FollowUpLettersOptions
            {
                DisplayTimeZoneId = id,
            }),
            NullLogger<FollowUpLetterTimeZone>.Instance);

        var local = service.ToDisplayTime(utc);

        Assert.Equal(DateTime.ParseExact(expectedDate, "yyyy-MM-dd", CultureInfo.InvariantCulture).Date, local.Date);
    }

    [Fact]
    public void TodayDisplayDate_UsesResolvedTimeZone()
    {
        var service = new FollowUpLetterTimeZone(
            Microsoft.Extensions.Options.Options.Create(new Uqeb.Api.Configuration.FollowUpLettersOptions
            {
                DisplayTimeZoneId = OperatingSystem.IsWindows() ? "Arab Standard Time" : "Asia/Riyadh",
            }),
            NullLogger<FollowUpLetterTimeZone>.Instance);

        var expected = service.ToDisplayTime(DateTime.UtcNow).Date;

        Assert.Equal(expected, service.TodayDisplayDate);
    }
}
