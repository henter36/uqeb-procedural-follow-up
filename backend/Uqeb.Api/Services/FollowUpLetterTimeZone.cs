using Microsoft.Extensions.Logging;
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
    public FollowUpLetterTimeZone(IOptions<FollowUpLettersOptions> options, ILogger<FollowUpLetterTimeZone> logger)
    {
        TimeZone = ResolveTimeZone(options.Value.DisplayTimeZoneId, logger);
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

    internal static TimeZoneInfo ResolveTimeZone(string? timeZoneId, ILogger logger)
    {
        if (!string.IsNullOrWhiteSpace(timeZoneId))
        {
            var configuredId = timeZoneId.Trim();
            if (TryFindTimeZone(configuredId, logger, "Configured display timezone {TimeZoneId} was not found.", configuredId, out var configured))
                return configured;

            throw new InvalidOperationException($"Invalid follow-up letter display timezone: {configuredId}");
        }

        if (TryFindTimeZone("Asia/Riyadh", logger, "Asia/Riyadh timezone alias was not found.", "Asia/Riyadh", out var riyadh))
            return riyadh;

        logger.LogWarning("Falling back to Arab Standard Time for empty follow-up letter display timezone.");
        return TimeZoneInfo.FindSystemTimeZoneById("Arab Standard Time");
    }

    public static bool CanResolveConfiguredTimeZone(string? timeZoneId)
    {
        if (string.IsNullOrWhiteSpace(timeZoneId))
            return CanFindTimeZone("Asia/Riyadh") || CanFindTimeZone("Arab Standard Time");

        return CanFindTimeZone(timeZoneId.Trim());
    }

    private static bool CanFindTimeZone(string id)
    {
        try
        {
            _ = TimeZoneInfo.FindSystemTimeZoneById(id);
            return true;
        }
        catch (TimeZoneNotFoundException)
        {
            return false;
        }
        catch (InvalidTimeZoneException)
        {
            return false;
        }
    }

    private static bool TryFindTimeZone(
        string id,
        ILogger logger,
        string notFoundMessage,
        object? logArg,
        out TimeZoneInfo timeZone)
    {
        try
        {
            timeZone = TimeZoneInfo.FindSystemTimeZoneById(id);
            return true;
        }
        catch (TimeZoneNotFoundException ex)
        {
            logger.LogWarning(ex, notFoundMessage, logArg ?? id);
        }
        catch (InvalidTimeZoneException ex)
        {
            logger.LogWarning(ex, "Invalid timezone id {TimeZoneId}.", id);
        }

        timeZone = null!;
        return false;
    }
}
