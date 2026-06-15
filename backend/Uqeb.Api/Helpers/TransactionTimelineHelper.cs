using Uqeb.Api.DTOs.Transactions;
using Uqeb.Api.Models.Entities;

namespace Uqeb.Api.Helpers;

public static class TransactionTimelineHelper
{
    public const string StatusNoDueDate = "no_due_date";
    public const string StatusDueToday = "due_today";
    public const string StatusRemaining = "remaining";
    public const string StatusOverdue = "overdue";
    public const string StatusCompleted = "completed";

    public sealed class TimelineMetrics
    {
        public int? ResponseDays { get; init; }
        public DateTime? ResponseDueDate { get; init; }
        public int DaysSinceIncoming { get; init; }
        public DateTime? LastFollowUpDate { get; init; }
        public int? DaysSinceLastFollowUp { get; init; }
        public int? DaysRemainingForResponse { get; init; }
        public string ResponseTimingStatus { get; init; } = StatusNoDueDate;
        public string ResponseTimingLabel { get; init; } = "بدون موعد";
    }

    public static DateTime? ResolveLastFollowUpDate(IEnumerable<FollowUp> followUps)
    {
        DateTime? last = null;
        foreach (var f in followUps)
        {
            var candidate = f.CreatedAt > f.FollowUpDate ? f.CreatedAt : f.FollowUpDate;
            if (!last.HasValue || candidate > last.Value)
                last = candidate;
        }
        return last?.Date;
    }

    public static TimelineMetrics Compute(
        DateTime incomingDate,
        DateTime? responseDueDate,
        int? responseDueDays,
        bool requiresResponse,
        bool responseCompleted,
        DateTime? lastFollowUpDate,
        DateTime? today = null)
    {
        today ??= DateTime.UtcNow.Date;
        var incoming = incomingDate.Date;
        var daysSinceIncoming = Math.Max(0, (today.Value - incoming).Days);

        int? daysSinceLastFollowUp = null;
        if (lastFollowUpDate.HasValue)
            daysSinceLastFollowUp = Math.Max(0, (today.Value - lastFollowUpDate.Value.Date).Days);

        if (responseCompleted)
        {
            return new TimelineMetrics
            {
                ResponseDays = responseDueDays,
                ResponseDueDate = responseDueDate,
                DaysSinceIncoming = daysSinceIncoming,
                LastFollowUpDate = lastFollowUpDate?.Date,
                DaysSinceLastFollowUp = daysSinceLastFollowUp,
                DaysRemainingForResponse = null,
                ResponseTimingStatus = StatusCompleted,
                ResponseTimingLabel = "مكتمل"
            };
        }

        if (!requiresResponse || !responseDueDate.HasValue)
        {
            return new TimelineMetrics
            {
                ResponseDays = responseDueDays,
                ResponseDueDate = responseDueDate,
                DaysSinceIncoming = daysSinceIncoming,
                LastFollowUpDate = lastFollowUpDate?.Date,
                DaysSinceLastFollowUp = daysSinceLastFollowUp,
                DaysRemainingForResponse = null,
                ResponseTimingStatus = StatusNoDueDate,
                ResponseTimingLabel = "بدون موعد"
            };
        }

        var daysRemaining = (responseDueDate.Value.Date - today.Value).Days;

        if (daysRemaining < 0)
        {
            var overdueDays = Math.Abs(daysRemaining);
            return new TimelineMetrics
            {
                ResponseDays = responseDueDays,
                ResponseDueDate = responseDueDate,
                DaysSinceIncoming = daysSinceIncoming,
                LastFollowUpDate = lastFollowUpDate?.Date,
                DaysSinceLastFollowUp = daysSinceLastFollowUp,
                DaysRemainingForResponse = daysRemaining,
                ResponseTimingStatus = StatusOverdue,
                ResponseTimingLabel = $"متأخرة {overdueDays} أيام"
            };
        }

        if (daysRemaining == 0)
        {
            return new TimelineMetrics
            {
                ResponseDays = responseDueDays,
                ResponseDueDate = responseDueDate,
                DaysSinceIncoming = daysSinceIncoming,
                LastFollowUpDate = lastFollowUpDate?.Date,
                DaysSinceLastFollowUp = daysSinceLastFollowUp,
                DaysRemainingForResponse = 0,
                ResponseTimingStatus = StatusDueToday,
                ResponseTimingLabel = "مستحق اليوم"
            };
        }

        return new TimelineMetrics
        {
            ResponseDays = responseDueDays,
            ResponseDueDate = responseDueDate,
            DaysSinceIncoming = daysSinceIncoming,
            LastFollowUpDate = lastFollowUpDate?.Date,
            DaysSinceLastFollowUp = daysSinceLastFollowUp,
            DaysRemainingForResponse = daysRemaining,
            ResponseTimingStatus = StatusRemaining,
            ResponseTimingLabel = $"متبقي {daysRemaining} يوم"
        };
    }

    public static void ApplyTo(TransactionListDto dto, TimelineMetrics metrics)
    {
        dto.ResponseDays = metrics.ResponseDays;
        dto.DaysSinceIncoming = metrics.DaysSinceIncoming;
        dto.DaysSinceLastFollowUp = metrics.DaysSinceLastFollowUp;
        dto.LastFollowUpDate = metrics.LastFollowUpDate;
        dto.DaysRemainingForResponse = metrics.DaysRemainingForResponse;
        dto.ResponseTimingStatus = metrics.ResponseTimingStatus;
        dto.ResponseTimingLabel = metrics.ResponseTimingLabel;
    }

    public static TimelineMetrics ComputeForTransaction(Transaction t, DateTime now, DateTime? lastFollowUpDate = null)
    {
        lastFollowUpDate ??= t.FollowUps.Count > 0 ? ResolveLastFollowUpDate(t.FollowUps) : null;
        return Compute(
            t.IncomingDate,
            t.ResponseDueDate,
            t.ResponseDueDays,
            t.RequiresResponse,
            t.ResponseCompleted,
            lastFollowUpDate,
            now.Date);
    }

    public static void ApplyForTransaction(TransactionListDto dto, Transaction t, DateTime now, DateTime? lastFollowUpDate = null) =>
        ApplyTo(dto, ComputeForTransaction(t, now, lastFollowUpDate));
}
