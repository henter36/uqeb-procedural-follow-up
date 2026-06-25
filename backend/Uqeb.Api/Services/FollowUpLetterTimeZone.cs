using Microsoft.Extensions.Options;
using Uqeb.Api.Configuration;

namespace Uqeb.Api.Services;

public interface IFollowUpLetterTimeZone
{
    TimeZoneInfo TimeZone { get; }
    DateTime ToDisplayTime(DateTime utc);
    DateTime? ToDisplayTime(DateTime? utc);
    DateTime TodayDisplayDate { get; }
}

public sealed class FollowUpLetterTimeZone : IFollowUpLetterTimeZone
{
    public FollowUpLetterTimeZone(IOptions<FollowUpLettersOptions> options)
    {
        TimeZone = ResolveTimeZone(options.Value.DisplayTimeZoneId);
    }

    public TimeZoneInfo TimeZone { get; }

    public DateTime ToDisplayTime(DateTime utc)
    {
        var normalized = utc.Kind switch
        {
            DateTimeKind.Utc => utc,
            DateTimeKind.Local => utc.ToUniversalTime(),
            _ => DateTime.SpecifyKind(utc, DateTimeKind.Utc)
        };
        return TimeZoneInfo.ConvertTimeFromUtc(normalized, TimeZone);
    }

    public DateTime? ToDisplayTime(DateTime? utc) => utc.HasValue ? ToDisplayTime(utc.Value) : null;

    public DateTime TodayDisplayDate => ToDisplayTime(DateTime.UtcNow).Date;

    internal static TimeZoneInfo ResolveTimeZone(string? timeZoneId)
    {
        if (!string.IsNullOrWhiteSpace(timeZoneId))
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(timeZoneId.Trim());
            }
            catch (TimeZoneNotFoundException)
            {
            }
            catch (InvalidTimeZoneException)
            {
            }
        }

        return TimeZoneInfo.FindSystemTimeZoneById("Arab Standard Time");
    }
}
