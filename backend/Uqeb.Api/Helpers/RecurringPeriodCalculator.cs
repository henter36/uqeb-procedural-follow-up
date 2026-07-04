using Uqeb.Api.Models.Enums;

namespace Uqeb.Api.Helpers;

public static class RecurringPeriodCalculator
{
    private static readonly string[] ArabicMonthNames =
    {
        "يناير", "فبراير", "مارس", "أبريل", "مايو", "يونيو",
        "يوليو", "أغسطس", "سبتمبر", "أكتوبر", "نوفمبر", "ديسمبر"
    };

    private static readonly string[] ArabicQuarterOrdinals = { "الأول", "الثاني", "الثالث", "الرابع" };
    private static readonly string[] ArabicHalfOrdinals = { "الأول", "الثاني" };

    public readonly record struct PeriodInfo(
        string PeriodKey,
        string PeriodLabel,
        DateTime PeriodStart,
        DateTime PeriodEnd,
        DateTime DueDate);

    public static PeriodInfo Compute(RecurrenceType recurrenceType, string periodKey, DateTime anchorStartDate)
    {
        if (string.IsNullOrWhiteSpace(periodKey))
            throw new InvalidOperationException("الفترة مطلوبة.");

        return recurrenceType switch
        {
            RecurrenceType.Monthly => ComputeMonthly(periodKey, anchorStartDate),
            RecurrenceType.Quarterly => ComputeQuarterly(periodKey, anchorStartDate),
            RecurrenceType.SemiAnnual => ComputeSemiAnnual(periodKey, anchorStartDate),
            RecurrenceType.Annual => ComputeAnnual(periodKey, anchorStartDate),
            _ => throw new InvalidOperationException("نوع التكرار غير مدعوم.")
        };
    }

    private static PeriodInfo ComputeFromYearAndStartMonth(
        DateTime anchorStartDate, int targetYear, int targetStartMonth, int spanMonths, string periodKey, string label)
    {
        var anchorPeriodStartMonth = ((anchorStartDate.Month - 1) / spanMonths * spanMonths) + 1;
        var monthOffset = ((targetYear - anchorStartDate.Year) * 12) + targetStartMonth - anchorPeriodStartMonth;
        var periodIndex = monthOffset / spanMonths;
        var periodStart = anchorStartDate.Date.AddMonths(periodIndex * spanMonths);
        var periodEnd = anchorStartDate.Date.AddMonths((periodIndex + 1) * spanMonths);
        return new PeriodInfo(periodKey, label, periodStart, periodEnd, periodEnd);
    }

    private static PeriodInfo ComputeMonthly(string periodKey, DateTime anchorStartDate)
    {
        if (!TryParseYearMonth(periodKey, out var year, out var month))
            throw new InvalidOperationException("صيغة الفترة الشهرية غير صحيحة. الصيغة الصحيحة: YYYY-MM.");

        var label = $"{ArabicMonthNames[month - 1]} {year}";
        return ComputeFromYearAndStartMonth(anchorStartDate, year, month, 1, NormalizeMonthlyKey(year, month), label);
    }

    private static PeriodInfo ComputeQuarterly(string periodKey, DateTime anchorStartDate)
    {
        if (!TryParseYearQuarter(periodKey, out var year, out var quarter))
            throw new InvalidOperationException("صيغة الفترة الربع سنوية غير صحيحة. الصيغة الصحيحة: YYYY-Q1 إلى YYYY-Q4.");

        var startMonth = ((quarter - 1) * 3) + 1;
        var label = $"الربع {ArabicQuarterOrdinals[quarter - 1]} {year}";
        return ComputeFromYearAndStartMonth(anchorStartDate, year, startMonth, 3, NormalizeQuarterlyKey(year, quarter), label);
    }

    private static PeriodInfo ComputeSemiAnnual(string periodKey, DateTime anchorStartDate)
    {
        if (!TryParseYearHalf(periodKey, out var year, out var half))
            throw new InvalidOperationException("صيغة الفترة النصف سنوية غير صحيحة. الصيغة الصحيحة: YYYY-H1 أو YYYY-H2.");

        var startMonth = ((half - 1) * 6) + 1;
        var label = $"النصف {ArabicHalfOrdinals[half - 1]} {year}";
        return ComputeFromYearAndStartMonth(anchorStartDate, year, startMonth, 6, NormalizeHalfKey(year, half), label);
    }

    private static PeriodInfo ComputeAnnual(string periodKey, DateTime anchorStartDate)
    {
        if (!TryParseYearOnly(periodKey, out var year))
            throw new InvalidOperationException("صيغة الفترة السنوية غير صحيحة. الصيغة الصحيحة: YYYY.");

        var label = $"سنة {year}";
        return ComputeFromYearAndStartMonth(anchorStartDate, year, 1, 12, NormalizeYearKey(year), label);
    }

    public static bool TryParseYearMonth(string periodKey, out int year, out int month)
    {
        year = 0;
        month = 0;
        if (string.IsNullOrWhiteSpace(periodKey))
            return false;

        var parts = periodKey.Split('-');
        if (parts.Length != 2)
            return false;

        if (!int.TryParse(parts[0], out year) || year < 1900 || year > 3000)
            return false;

        if (!int.TryParse(parts[1], out month) || month < 1 || month > 12)
            return false;

        return true;
    }

    public static bool TryParseYearQuarter(string periodKey, out int year, out int quarter) =>
        TryParseYearAndPrefixedIndex(periodKey, 'Q', 4, out year, out quarter);

    public static bool TryParseYearHalf(string periodKey, out int year, out int half) =>
        TryParseYearAndPrefixedIndex(periodKey, 'H', 2, out year, out half);

    public static bool TryParseYearOnly(string periodKey, out int year)
    {
        year = 0;
        if (string.IsNullOrWhiteSpace(periodKey) || periodKey.Length != 4)
            return false;

        if (!int.TryParse(periodKey, out year) || year < 1900 || year > 3000)
        {
            year = 0;
            return false;
        }

        return true;
    }

    private static bool TryParseYearAndPrefixedIndex(string periodKey, char prefix, int maxIndex, out int year, out int index)
    {
        year = 0;
        index = 0;
        if (string.IsNullOrWhiteSpace(periodKey))
            return false;

        var parts = periodKey.Split('-');
        if (parts.Length != 2)
            return false;

        if (!int.TryParse(parts[0], out year) || year < 1900 || year > 3000)
            return false;

        var indexPart = parts[1];
        if (indexPart.Length != 2 || char.ToUpperInvariant(indexPart[0]) != prefix)
            return false;

        if (!int.TryParse(indexPart[1].ToString(), out index) || index < 1 || index > maxIndex)
            return false;

        return true;
    }

    private static string NormalizeMonthlyKey(int year, int month) => $"{year:D4}-{month:D2}";

    private static string NormalizeQuarterlyKey(int year, int quarter) => $"{year:D4}-Q{quarter}";

    private static string NormalizeHalfKey(int year, int half) => $"{year:D4}-H{half}";

    private static string NormalizeYearKey(int year) => $"{year:D4}";

    public static string GetNextPeriodKey(RecurrenceType recurrenceType, DateTime startDate, string? lastGeneratedPeriodKey) =>
        recurrenceType switch
        {
            RecurrenceType.Monthly => GetNextMonthlyPeriodKey(startDate, lastGeneratedPeriodKey),
            RecurrenceType.Quarterly => GetNextQuarterlyPeriodKey(startDate, lastGeneratedPeriodKey),
            RecurrenceType.SemiAnnual => GetNextSemiAnnualPeriodKey(startDate, lastGeneratedPeriodKey),
            RecurrenceType.Annual => GetNextAnnualPeriodKey(startDate, lastGeneratedPeriodKey),
            _ => throw new InvalidOperationException("نوع التكرار غير مدعوم.")
        };

    private static string GetNextMonthlyPeriodKey(DateTime startDate, string? lastGeneratedPeriodKey)
    {
        DateTime next;
        if (!string.IsNullOrWhiteSpace(lastGeneratedPeriodKey) && TryParseYearMonth(lastGeneratedPeriodKey, out var y, out var m))
            next = new DateTime(y, m, 1, 0, 0, 0, DateTimeKind.Utc).AddMonths(1);
        else
            next = new DateTime(startDate.Year, startDate.Month, 1, 0, 0, 0, DateTimeKind.Utc);

        return NormalizeMonthlyKey(next.Year, next.Month);
    }

    private static string GetNextQuarterlyPeriodKey(DateTime startDate, string? lastGeneratedPeriodKey)
    {
        DateTime next;
        if (!string.IsNullOrWhiteSpace(lastGeneratedPeriodKey) && TryParseYearQuarter(lastGeneratedPeriodKey, out var y, out var q))
        {
            var startMonth = ((q - 1) * 3) + 1;
            next = new DateTime(y, startMonth, 1, 0, 0, 0, DateTimeKind.Utc).AddMonths(3);
        }
        else
        {
            var currentQuarter = ((startDate.Month - 1) / 3) + 1;
            var startMonth = ((currentQuarter - 1) * 3) + 1;
            next = new DateTime(startDate.Year, startMonth, 1, 0, 0, 0, DateTimeKind.Utc);
        }

        var quarter = ((next.Month - 1) / 3) + 1;
        return NormalizeQuarterlyKey(next.Year, quarter);
    }

    private static string GetNextSemiAnnualPeriodKey(DateTime startDate, string? lastGeneratedPeriodKey)
    {
        DateTime next;
        if (!string.IsNullOrWhiteSpace(lastGeneratedPeriodKey) && TryParseYearHalf(lastGeneratedPeriodKey, out var y, out var h))
        {
            var startMonth = ((h - 1) * 6) + 1;
            next = new DateTime(y, startMonth, 1, 0, 0, 0, DateTimeKind.Utc).AddMonths(6);
        }
        else
        {
            var currentHalf = ((startDate.Month - 1) / 6) + 1;
            var startMonth = ((currentHalf - 1) * 6) + 1;
            next = new DateTime(startDate.Year, startMonth, 1, 0, 0, 0, DateTimeKind.Utc);
        }

        var half = ((next.Month - 1) / 6) + 1;
        return NormalizeHalfKey(next.Year, half);
    }

    private static string GetNextAnnualPeriodKey(DateTime startDate, string? lastGeneratedPeriodKey)
    {
        if (!string.IsNullOrWhiteSpace(lastGeneratedPeriodKey) && TryParseYearOnly(lastGeneratedPeriodKey, out var y))
            return NormalizeYearKey(y + 1);

        return NormalizeYearKey(startDate.Year);
    }

    public static string GetPeriodLabel(RecurrenceType recurrenceType, string periodKey)
    {
        if (recurrenceType == RecurrenceType.Monthly && TryParseYearMonth(periodKey, out var y, out var m))
            return $"{ArabicMonthNames[m - 1]} {y}";
        if (recurrenceType == RecurrenceType.Quarterly && TryParseYearQuarter(periodKey, out var yq, out var q))
            return $"الربع {ArabicQuarterOrdinals[q - 1]} {yq}";
        if (recurrenceType == RecurrenceType.SemiAnnual && TryParseYearHalf(periodKey, out var yh, out var h))
            return $"النصف {ArabicHalfOrdinals[h - 1]} {yh}";
        if (recurrenceType == RecurrenceType.Annual && TryParseYearOnly(periodKey, out var ya))
            return $"سنة {ya}";
        return periodKey;
    }

    public static string GetPeriodKeyForDate(RecurrenceType recurrenceType, DateTime date) =>
        recurrenceType switch
        {
            RecurrenceType.Monthly => NormalizeMonthlyKey(date.Year, date.Month),
            RecurrenceType.Quarterly => NormalizeQuarterlyKey(date.Year, ((date.Month - 1) / 3) + 1),
            RecurrenceType.SemiAnnual => NormalizeHalfKey(date.Year, ((date.Month - 1) / 6) + 1),
            RecurrenceType.Annual => NormalizeYearKey(date.Year),
            _ => throw new InvalidOperationException("نوع التكرار غير مدعوم.")
        };
}
