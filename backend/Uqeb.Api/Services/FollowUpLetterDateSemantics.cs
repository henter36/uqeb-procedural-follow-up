namespace Uqeb.Api.Services;

/// <summary>
/// Converts stored dates to display values using Riyadh-local semantics.
/// UTC timestamps are shifted to the display zone; business dates keep their calendar day.
/// </summary>
public static class FollowUpLetterDateSemantics
{
    public static DateTime ToBusinessDisplayDate(DateTime value, IFollowUpLetterTimeZone timeZone)
    {
        if (value.Kind == DateTimeKind.Utc)
            return timeZone.ToDisplayTime(value).Date;

        return value.Date;
    }

    public static DateTime? ToBusinessDisplayDate(DateTime? value, IFollowUpLetterTimeZone timeZone) =>
        value.HasValue ? ToBusinessDisplayDate(value.Value, timeZone) : null;

    public static DateTime ToDisplayTimestamp(DateTime value, IFollowUpLetterTimeZone timeZone)
    {
        if (value.Kind == DateTimeKind.Utc)
            return timeZone.ToDisplayTime(value);

        return value;
    }

    public static DateTime? ToDisplayTimestamp(DateTime? value, IFollowUpLetterTimeZone timeZone) =>
        value.HasValue ? ToDisplayTimestamp(value.Value, timeZone) : null;
}
