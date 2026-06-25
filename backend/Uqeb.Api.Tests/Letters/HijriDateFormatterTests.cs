using System.Globalization;
using Uqeb.Api.Helpers;
using Xunit;

namespace Uqeb.Api.Tests.Letters;

public class HijriDateFormatterTests
{
    [Theory]
    [InlineData(2025, 6, 1)]
    [InlineData(2025, 6, 25)]
    public void Format_ReturnsStableUmAlQuraRepresentation(int year, int month, int day)
    {
        var date = new DateTime(year, month, day, 0, 0, 0, DateTimeKind.Unspecified);
        var hijri = new UmAlQuraCalendar();
        var expected = $"{hijri.GetDayOfMonth(date):00}/{hijri.GetMonth(date):00}/{hijri.GetYear(date)} هـ";

        var formatted = HijriDateFormatter.Format(date);

        Assert.Equal(expected, formatted);
    }

    [Fact]
    public void Format_ReturnsNull_WhenCalendarConversionFails()
    {
        var result = HijriDateFormatter.Format(DateTime.MinValue);

        Assert.Null(result);
    }

    [Fact]
    public void FormatGregorian_UsesInvariantIsoDate()
    {
        var date = new DateTime(2025, 6, 25);

        Assert.Equal("2025-06-25", HijriDateFormatter.FormatGregorian(date));
    }

    [Fact]
    public void FormatGregorianArabic_UsesDayFirstInvariantFormat()
    {
        var date = new DateTime(2025, 6, 25);

        Assert.Equal("25/06/2025", HijriDateFormatter.FormatGregorianArabic(date));
    }

    [Fact]
    public void Format_MatchesDirectUmAlQuraCalendarFormatting()
    {
        var date = new DateTime(2025, 6, 1);
        var hijri = new UmAlQuraCalendar();
        var expected = $"{hijri.GetDayOfMonth(date):00}/{hijri.GetMonth(date):00}/{hijri.GetYear(date)} هـ";

        Assert.Equal(expected, HijriDateFormatter.Format(date));
    }
}
