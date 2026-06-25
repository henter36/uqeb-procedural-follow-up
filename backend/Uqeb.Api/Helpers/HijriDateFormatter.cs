using System.Globalization;

namespace Uqeb.Api.Helpers;

public static class HijriDateFormatter
{
    public static string? Format(DateTime date)
    {
        try
        {
            var hijri = new UmAlQuraCalendar();
            return $"{hijri.GetDayOfMonth(date):00}/{hijri.GetMonth(date):00}/{hijri.GetYear(date)} هـ";
        }
        catch
        {
            return null;
        }
    }

    public static string FormatGregorian(DateTime date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    public static string FormatGregorianArabic(DateTime date) =>
        date.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
}
