using Uqeb.Api.Models.Enums;

namespace Uqeb.Api.Helpers;

/// <summary>
/// Computed (never stored) classification of a recurring obligation's next due
/// date relative to "now". Only meaningful while the owning template's
/// RecurringTemplateStatus is Active; Paused/Terminated templates are always
/// NotApplicable regardless of their (informational-only, Paused-only) next due date.
/// </summary>
public static class RecurringObligationScheduleStatus
{
    public const string Upcoming = "Upcoming";
    public const string DueSoon = "DueSoon";
    public const string Overdue = "Overdue";
    public const string NotApplicable = "NotApplicable";
}

/// <summary>
/// Pure, side-effect-free classification of a due date relative to "now". Extracted
/// out of ReportService so it can be unit-tested with fully explicit inputs instead of
/// depending on the wall clock, and so date-only vs. date+time bugs are caught directly:
/// only the Date component of each input is ever compared, never TimeOfDay or DateTimeKind.
/// </summary>
public static class RecurringObligationScheduleClassifier
{
    public static (string ScheduleStatus, int DaysRemaining) Classify(DateTime dueDate, DateTime now, int dueSoonWithinDays)
    {
        var remainingDays = (dueDate.Date - now.Date).Days;
        var status = remainingDays < 0
            ? RecurringObligationScheduleStatus.Overdue
            : remainingDays <= dueSoonWithinDays
                ? RecurringObligationScheduleStatus.DueSoon
                : RecurringObligationScheduleStatus.Upcoming;
        return (status, remainingDays);
    }
}

public static class RecurringObligationLabels
{
    public static string RecurrenceType(RecurrenceType type) => type switch
    {
        Models.Enums.RecurrenceType.Monthly => "شهري",
        Models.Enums.RecurrenceType.Quarterly => "ربع سنوي",
        Models.Enums.RecurrenceType.SemiAnnual => "نصف سنوي",
        Models.Enums.RecurrenceType.Annual => "سنوي",
        _ => type.ToString()
    };

    public static string Status(string status) => status switch
    {
        "Active" => "نشط",
        "Paused" => "موقوف",
        "Terminated" => "منتهٍ",
        _ => status
    };

    public static string ScheduleStatus(string scheduleStatus) => scheduleStatus switch
    {
        RecurringObligationScheduleStatus.Upcoming => "قادم",
        RecurringObligationScheduleStatus.DueSoon => "قريب الاستحقاق",
        RecurringObligationScheduleStatus.Overdue => "متأخر",
        RecurringObligationScheduleStatus.NotApplicable => "غير منطبق",
        _ => scheduleStatus
    };

    public static string Priority(Priority priority) => priority switch
    {
        Models.Enums.Priority.Normal => "عادي",
        Models.Enums.Priority.Urgent => "عاجل",
        Models.Enums.Priority.VeryUrgent => "عاجل جداً",
        _ => priority.ToString()
    };
}
