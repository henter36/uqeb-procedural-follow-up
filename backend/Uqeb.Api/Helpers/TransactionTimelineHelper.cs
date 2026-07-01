using Uqeb.Api.DTOs.Transactions;
using Uqeb.Api.Models.Entities;
using Uqeb.Api.Models.Enums;

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
        public DateTime? CompletionDate { get; init; }
        public int? CompletionDays { get; init; }
        public int DaysSinceIncoming { get; init; }
        public DateTime? LastFollowUpDate { get; init; }
        public int? DaysSinceLastFollowUp { get; init; }
        public int? DaysRemainingForResponse { get; init; }
        public string ResponseTimingStatus { get; init; } = StatusNoDueDate;
        public string ResponseTimingLabel { get; init; } = "بدون موعد";
    }

    public sealed class TimelineComputationInput
    {
        public DateTime IncomingDate { get; init; }
        public DateTime? ResponseDueDate { get; init; }
        public int? ResponseDueDays { get; init; }
        public bool RequiresResponse { get; init; }
        public bool ResponseCompleted { get; init; }
        public DateTime? ResponseCompletedDate { get; init; }
        public TransactionStatus Status { get; init; } = TransactionStatus.New;
        public DateTime? ClosedAt { get; init; }
        public DateTime? LastFollowUpDate { get; init; }
        public DateTime? Today { get; init; }
    }

    private sealed record TimelineMetricsBuildInput
    {
        public TimelineComputationInput Source { get; init; } = new();
        public int DaysSinceIncoming { get; init; }
        public DateTime? LastFollowUpDate { get; init; }
        public int? DaysSinceLastFollowUp { get; init; }
        public DateTime? CompletionDate { get; init; }
        public int? CompletionDays { get; init; }
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

    public static TimelineMetrics Compute(TimelineComputationInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        var today = input.Today?.Date ?? DateTime.UtcNow.Date;
        var incoming = input.IncomingDate.Date;
        var daysSinceIncoming = Math.Max(0, (today - incoming).Days);
        var (completionDate, completionDays) = ComputeCompletion(
            incoming,
            input.ResponseCompleted,
            input.ResponseCompletedDate,
            input.Status,
            input.ClosedAt);

        var lastFollowUpDate = input.LastFollowUpDate?.Date;
        int? daysSinceLastFollowUp = null;
        if (lastFollowUpDate.HasValue)
            daysSinceLastFollowUp = Math.Max(0, (today - lastFollowUpDate.Value).Days);

        var buildInput = new TimelineMetricsBuildInput
        {
            Source = input,
            DaysSinceIncoming = daysSinceIncoming,
            LastFollowUpDate = lastFollowUpDate,
            DaysSinceLastFollowUp = daysSinceLastFollowUp,
            CompletionDate = completionDate,
            CompletionDays = completionDays
        };

        if (input.ResponseCompleted)
        {
            return BuildMetrics(buildInput with
            {
                DaysRemainingForResponse = null,
                ResponseTimingStatus = StatusCompleted,
                ResponseTimingLabel = "مكتمل"
            });
        }

        if (!input.RequiresResponse || !input.ResponseDueDate.HasValue)
        {
            return BuildMetrics(buildInput with
            {
                DaysRemainingForResponse = null,
                ResponseTimingStatus = StatusNoDueDate,
                ResponseTimingLabel = "بدون موعد"
            });
        }

        var daysRemaining = (input.ResponseDueDate.Value.Date - today).Days;
        if (daysRemaining < 0)
        {
            var overdueDays = Math.Abs(daysRemaining);
            return BuildMetrics(buildInput with
            {
                DaysRemainingForResponse = daysRemaining,
                ResponseTimingStatus = StatusOverdue,
                ResponseTimingLabel = $"متأخرة {overdueDays} أيام"
            });
        }

        if (daysRemaining == 0)
        {
            return BuildMetrics(buildInput with
            {
                DaysRemainingForResponse = 0,
                ResponseTimingStatus = StatusDueToday,
                ResponseTimingLabel = "مستحق اليوم"
            });
        }

        return BuildMetrics(buildInput with
        {
            DaysRemainingForResponse = daysRemaining,
            ResponseTimingStatus = StatusRemaining,
            ResponseTimingLabel = $"متبقي {daysRemaining} يوم"
        });
    }

    public static TimelineMetrics Compute(
        DateTime incomingDate,
        DateTime? responseDueDate,
        int? responseDueDays,
        bool requiresResponse,
        bool responseCompleted,
        DateTime? lastFollowUpDate,
        DateTime? today = null) =>
        Compute(new TimelineComputationInput
        {
            IncomingDate = incomingDate,
            ResponseDueDate = responseDueDate,
            ResponseDueDays = responseDueDays,
            RequiresResponse = requiresResponse,
            ResponseCompleted = responseCompleted,
            LastFollowUpDate = lastFollowUpDate,
            Today = today
        });

    private static TimelineMetrics BuildMetrics(TimelineMetricsBuildInput input)
    {
        return new TimelineMetrics
        {
            ResponseDays = input.Source.ResponseDueDays,
            ResponseDueDate = input.Source.ResponseDueDate,
            CompletionDate = input.CompletionDate,
            CompletionDays = input.CompletionDays,
            DaysSinceIncoming = input.DaysSinceIncoming,
            LastFollowUpDate = input.LastFollowUpDate,
            DaysSinceLastFollowUp = input.DaysSinceLastFollowUp,
            DaysRemainingForResponse = input.DaysRemainingForResponse,
            ResponseTimingStatus = input.ResponseTimingStatus,
            ResponseTimingLabel = input.ResponseTimingLabel
        };
    }

    private static (DateTime? CompletionDate, int? CompletionDays) ComputeCompletion(
        DateTime incomingDate,
        bool responseCompleted,
        DateTime? responseCompletedDate,
        TransactionStatus status,
        DateTime? closedAt)
    {
        DateTime? completionDate = null;

        if (closedAt.HasValue)
        {
            completionDate = closedAt.Value.Date;
        }
        else if ((responseCompleted || status is TransactionStatus.ResponseCompleted or TransactionStatus.Closed)
                 && responseCompletedDate.HasValue)
        {
            completionDate = responseCompletedDate.Value.Date;
        }

        var completionDays = completionDate.HasValue
            ? Math.Max(0, (completionDate.Value - incomingDate.Date).Days)
            : (int?)null;

        return (completionDate, completionDays);
    }

    public static void ApplyTo(TransactionListDto dto, TimelineMetrics metrics)
    {
        dto.ResponseDays = metrics.ResponseDays;
        dto.CompletionDate = metrics.CompletionDate;
        dto.CompletionDays = metrics.CompletionDays;
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

        return Compute(new TimelineComputationInput
        {
            IncomingDate = t.IncomingDate,
            ResponseDueDate = t.ResponseDueDate,
            ResponseDueDays = t.ResponseDueDays,
            RequiresResponse = t.RequiresResponse,
            ResponseCompleted = t.ResponseCompleted,
            ResponseCompletedDate = t.ResponseCompletedDate,
            Status = t.Status,
            ClosedAt = t.ClosedAt,
            LastFollowUpDate = lastFollowUpDate,
            Today = now.Date
        });
    }

    public static void ApplyForTransaction(TransactionListDto dto, Transaction t, DateTime now, DateTime? lastFollowUpDate = null) =>
        ApplyTo(dto, ComputeForTransaction(t, now, lastFollowUpDate));
}
