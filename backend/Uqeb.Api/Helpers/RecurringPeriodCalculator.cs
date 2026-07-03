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

    public readonly record struct PeriodInfo(
        string PeriodKey,
        string PeriodLabel,
        DateTime PeriodStart,
        DateTime PeriodEnd,
        DateTime DueDate);

    public static PeriodInfo Compute(RecurrenceType recurrenceType, string periodKey, int dueDaysAfterPeriodEnd)
    {
        if (string.IsNullOrWhiteSpace(periodKey))
            throw new InvalidOperationException("الفترة مطلوبة.");

        return recurrenceType switch
        {
            RecurrenceType.Monthly => ComputeMonthly(periodKey, dueDaysAfterPeriodEnd),
            RecurrenceType.Quarterly => ComputeQuarterly(periodKey, dueDaysAfterPeriodEnd),
            _ => throw new InvalidOperationException("نوع التكرار غير مدعوم.")
        };
    }

    private static PeriodInfo ComputeMonthly(string periodKey, int dueDaysAfterPeriodEnd)
    {
        if (!TryParseYearMonth(periodKey, out var year, out var month))
            throw new InvalidOperationException("صيغة الفترة الشهرية غير صحيحة. الصيغة الصحيحة: YYYY-MM.");

        var periodStart = new DateTime(year, month, 1, 0, 0, 0, DateTimeKind.Utc);
        var periodEnd = periodStart.AddMonths(1).AddDays(-1);
        var label = $"{ArabicMonthNames[month - 1]} {year}";
        var dueDate = periodEnd.AddDays(dueDaysAfterPeriodEnd);

        return new PeriodInfo(NormalizeMonthlyKey(year, month), label, periodStart, periodEnd, dueDate);
    }

    private static PeriodInfo ComputeQuarterly(string periodKey, int dueDaysAfterPeriodEnd)
    {
        if (!TryParseYearQuarter(periodKey, out var year, out var quarter))
            throw new InvalidOperationException("صيغة الفترة الربع سنوية غير صحيحة. الصيغة الصحيحة: YYYY-Q1 إلى YYYY-Q4.");

        var startMonth = ((quarter - 1) * 3) + 1;
        var periodStart = new DateTime(year, startMonth, 1, 0, 0, 0, DateTimeKind.Utc);
        var periodEnd = periodStart.AddMonths(3).AddDays(-1);
        var label = $"الربع {ArabicQuarterOrdinals[quarter - 1]} {year}";
        var dueDate = periodEnd.AddDays(dueDaysAfterPeriodEnd);

        return new PeriodInfo(NormalizeQuarterlyKey(year, quarter), label, periodStart, periodEnd, dueDate);
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

    public static bool TryParseYearQuarter(string periodKey, out int year, out int quarter)
    {
        year = 0;
        quarter = 0;
        if (string.IsNullOrWhiteSpace(periodKey))
            return false;

        var parts = periodKey.Split('-');
        if (parts.Length != 2)
            return false;

        if (!int.TryParse(parts[0], out year) || year < 1900 || year > 3000)
            return false;

        var quarterPart = parts[1];
        if (quarterPart.Length != 2 || char.ToUpperInvariant(quarterPart[0]) != 'Q')
            return false;

        if (!int.TryParse(quarterPart[1].ToString(), out quarter) || quarter < 1 || quarter > 4)
            return false;

        return true;
    }

    private static string NormalizeMonthlyKey(int year, int month) => $"{year:D4}-{month:D2}";

    private static string NormalizeQuarterlyKey(int year, int quarter) => $"{year:D4}-Q{quarter}";

    public static string GetNextPeriodKey(RecurrenceType recurrenceType, DateTime startDate, string? lastGeneratedPeriodKey)
    {
        if (recurrenceType == RecurrenceType.Monthly)
        {
            DateTime next;
            if (!string.IsNullOrWhiteSpace(lastGeneratedPeriodKey) && TryParseYearMonth(lastGeneratedPeriodKey, out var y, out var m))
                next = new DateTime(y, m, 1, 0, 0, 0, DateTimeKind.Utc).AddMonths(1);
            else
                next = new DateTime(startDate.Year, startDate.Month, 1, 0, 0, 0, DateTimeKind.Utc);

            return NormalizeMonthlyKey(next.Year, next.Month);
        }
        else
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
    }

    public static string GetPeriodLabel(RecurrenceType recurrenceType, string periodKey)
    {
        if (recurrenceType == RecurrenceType.Monthly && TryParseYearMonth(periodKey, out var y, out var m))
            return $"{ArabicMonthNames[m - 1]} {y}";
        if (recurrenceType == RecurrenceType.Quarterly && TryParseYearQuarter(periodKey, out var yy, out var q))
            return $"الربع {ArabicQuarterOrdinals[q - 1]} {yy}";
        return periodKey;
    }
}
