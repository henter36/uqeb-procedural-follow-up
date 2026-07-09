using Microsoft.EntityFrameworkCore;
using Uqeb.Api.Data;
using Uqeb.Api.Models.Entities;
using Uqeb.Api.Models.Enums;
using Uqeb.Api.Reporting.Services;

namespace Uqeb.Api.Reporting.DataQuality;

public sealed class DataQualityService : IDataQualityService
{
    public const string OverdueDurationRuleCode = "OverdueDurationExceedsThreshold";
    public const string ReferralDateAfterIncomingDateRuleCode = "ReferralDateAfterIncomingDate";
    public const string ResponsePeriodLessThanThresholdRuleCode = "ResponsePeriodLessThanThreshold";

    private readonly AppDbContext _db;
    private readonly TimeProvider _clock;

    public DataQualityService(AppDbContext db, TimeProvider clock)
    {
        _db = db;
        _clock = clock;
    }

    public async Task<DataQualitySummaryDto> GetSummaryAsync(DataQualityQueryDto query, CancellationToken ct = default)
    {
        var limit = Math.Clamp(query.Limit, 1, 1000);
        var today = ReportingTemporalCalculator.RiyadhBusinessDate(_clock);
        var transactions = await BuildTransactionQuery(query).ToListAsync(ct);

        var issues = new List<DataQualityIssueDto>();
        foreach (var transaction in transactions)
        {
            if (query.OverdueMoreThanDays.HasValue)
                AddOverdueIssue(issues, transaction, today, query.OverdueMoreThanDays.Value);

            if (query.IncludeReferralDateAfterIncomingDate)
                AddReferralDateIssues(issues, transaction);

            if (query.ResponsePeriodLessThanDays.HasValue)
                AddShortResponsePeriodIssue(issues, transaction, query.ResponsePeriodLessThanDays.Value);
        }

        ApplySimpleFilters(issues, query);
        await AttachReviewStateAsync(issues, ct);
        ApplyReviewFilters(issues, query);

        var orderedIssues = issues
            .OrderByDescending(x => x.Severity)
            .ThenByDescending(x => x.DaysValue ?? 0)
            .ThenByDescending(x => x.PrimaryDate)
            .ThenByDescending(x => x.TransactionId)
            .ToList();

        var displayedIssues = orderedIssues
            .Take(limit)
            .ToList();

        return new DataQualitySummaryDto
        {
            TotalIssues = orderedIssues.Count,
            CriticalCount = orderedIssues.Count(x => x.Severity == DataQualitySeverity.Critical),
            HighCount = orderedIssues.Count(x => x.Severity == DataQualitySeverity.High),
            MediumCount = orderedIssues.Count(x => x.Severity == DataQualitySeverity.Medium),
            LowCount = orderedIssues.Count(x => x.Severity == DataQualitySeverity.Low),
            AffectedTransactions = orderedIssues.Where(x => x.TransactionId.HasValue).Select(x => x.TransactionId!.Value).Distinct().Count(),
            GeneratedAtUtc = _clock.GetUtcNow().UtcDateTime,
            Issues = displayedIssues
        };
    }

    private IQueryable<Transaction> BuildTransactionQuery(DataQualityQueryDto query)
    {
        var transactions = _db.Transactions
            .AsNoTracking()
            .AsSplitQuery()
            .Include(x => x.OutgoingDepartments).ThenInclude(x => x.Department)
            .Include(x => x.Assignments).ThenInclude(x => x.Department)
            .Where(x => !x.IsArchived);

        if (query.From.HasValue)
        {
            var fromDate = query.From.Value.Date;
            transactions = transactions.Where(x => x.IncomingDate >= fromDate);
        }

        if (query.To.HasValue)
        {
            var toDateExclusive = query.To.Value.Date.AddDays(1);
            transactions = transactions.Where(x => x.IncomingDate < toDateExclusive);
        }

        if (query.DepartmentId.HasValue)
        {
            var departmentId = query.DepartmentId.Value;
            transactions = transactions.Where(x =>
                x.OutgoingDepartments.Any(d => d.DepartmentId == departmentId) ||
                x.Assignments.Any(a => a.DepartmentId == departmentId));
        }

        return transactions.OrderByDescending(x => x.IncomingDate).ThenByDescending(x => x.Id);
    }

    private static void AddOverdueIssue(
        List<DataQualityIssueDto> issues,
        Transaction transaction,
        DateTime today,
        int threshold)
    {
        var dueDate = ResolveOverdueDueDate(transaction, today);
        if (!dueDate.HasValue)
            return;

        var overdueDays = (today.Date - dueDate.Value.Date).Days;
        if (overdueDays <= threshold)
            return;

        var severity = overdueDays >= 30 ? DataQualitySeverity.Critical : DataQualitySeverity.High;
        issues.Add(new DataQualityIssueDto
        {
            Id = $"tx:{transaction.Id}:overdue-duration",
            IssueKey = $"tx:{transaction.Id}:overdue-duration",
            RuleCode = OverdueDurationRuleCode,
            Severity = severity,
            SeverityLabel = SeverityLabel(severity),
            Category = "التأخر",
            IssueType = "مدة التأخر تتجاوز الحد المحدد",
            TransactionId = transaction.Id,
            TrackingNumber = transaction.InternalTrackingNumber,
            IncomingNumber = transaction.IncomingNumber,
            Subject = transaction.Subject,
            DepartmentName = ResolveResponsibleDepartmentName(transaction),
            FieldName = "ResponseDueDate",
            CurrentValue = $"{overdueDays} يوم تأخر",
            DaysValue = overdueDays,
            PrimaryDate = dueDate,
            Impact = "تعرض المعاملات التي تجاوز تأخرها الحد الذي حدده المستخدم.",
            SuggestedAction = "فتح المعاملة ومراجعة آخر إجراء أو تعديل البيانات من صفحة المعاملة إذا لزم."
        });
    }

    private static DateTime? ResolveOverdueDueDate(Transaction transaction, DateTime today)
    {
        if (transaction.Status is TransactionStatus.Cancelled or TransactionStatus.Archived)
            return null;

        var dueDates = new List<DateTime>();

        if (transaction.ResponseDueDate.HasValue &&
            !transaction.ResponseCompleted &&
            transaction.Status != TransactionStatus.Closed &&
            transaction.ResponseDueDate.Value.Date < today.Date)
        {
            dueDates.Add(transaction.ResponseDueDate.Value.Date);
        }

        dueDates.AddRange(transaction.Assignments
            .Where(a =>
                a.Status == AssignmentStatus.Active &&
                a.RequiresReply &&
                a.ReplyStatus != ReplyStatus.Replied &&
                a.DueDate.HasValue &&
                a.DueDate.Value.Date < today.Date)
            .Select(a => a.DueDate!.Value.Date));

        return dueDates.Count == 0 ? null : dueDates.Min();
    }

    private static void AddReferralDateIssues(List<DataQualityIssueDto> issues, Transaction transaction)
    {
        foreach (var assignment in transaction.Assignments)
        {
            var assignmentDate = assignment.AssignedDate;
            if (assignmentDate.Date <= transaction.IncomingDate.Date)
                continue;

            var daysAfterIncoming = (assignmentDate.Date - transaction.IncomingDate.Date).Days;
            var severity = daysAfterIncoming >= 7 ? DataQualitySeverity.High : DataQualitySeverity.Medium;

            issues.Add(new DataQualityIssueDto
            {
                Id = $"tx:{transaction.Id}:assignment:{assignment.Id}:referral-after-incoming",
                IssueKey = $"tx:{transaction.Id}:assignment:{assignment.Id}:referral-after-incoming",
                RuleCode = ReferralDateAfterIncomingDateRuleCode,
                Severity = severity,
                SeverityLabel = SeverityLabel(severity),
                Category = "الإحالات",
                IssueType = "تاريخ الإحالة أكبر من تاريخ الوارد",
                TransactionId = transaction.Id,
                TrackingNumber = transaction.InternalTrackingNumber,
                IncomingNumber = transaction.IncomingNumber,
                Subject = transaction.Subject,
                DepartmentName = assignment.Department?.Name,
                FieldName = "AssignedDate",
                CurrentValue = $"تاريخ الإحالة بعد تاريخ الوارد بـ {daysAfterIncoming} يوم",
                DaysValue = daysAfterIncoming,
                PrimaryDate = assignmentDate,
                ComparedDate = transaction.IncomingDate,
                Impact = "تساعد على حصر الحالات التي لم تتم فيها الإحالة في نفس تاريخ الوارد أو تم إدخالها بتاريخ لاحق.",
                SuggestedAction = "فتح المعاملة ومراجعة تاريخ الإحالة وتعديله من صفحة المعاملة إذا كان غير صحيح."
            });
        }
    }

    private static void AddShortResponsePeriodIssue(List<DataQualityIssueDto> issues, Transaction transaction, int threshold)
    {
        if (!transaction.RequiresResponse)
            return;

        var responsePeriodDays = transaction.ResponseDueDays
            ?? (transaction.ResponseDueDate.HasValue
                ? (transaction.ResponseDueDate.Value.Date - transaction.IncomingDate.Date).Days
                : (int?)null);
        if (!responsePeriodDays.HasValue || responsePeriodDays.Value >= threshold)
            return;

        var severity = responsePeriodDays.Value <= 0 ? DataQualitySeverity.Critical : DataQualitySeverity.Medium;
        issues.Add(new DataQualityIssueDto
        {
            Id = $"tx:{transaction.Id}:short-response-period",
            IssueKey = $"tx:{transaction.Id}:short-response-period",
            RuleCode = ResponsePeriodLessThanThresholdRuleCode,
            Severity = severity,
            SeverityLabel = SeverityLabel(severity),
            Category = "فترة الرد",
            IssueType = "فترة الرد أقل من الحد المحدد",
            TransactionId = transaction.Id,
            TrackingNumber = transaction.InternalTrackingNumber,
            IncomingNumber = transaction.IncomingNumber,
            Subject = transaction.Subject,
            DepartmentName = ResolveResponsibleDepartmentName(transaction),
            FieldName = transaction.ResponseDueDays.HasValue ? "ResponseDueDays" : "ResponseDueDate",
            CurrentValue = $"فترة الرد المحددة {responsePeriodDays.Value} يوم",
            DaysValue = responsePeriodDays,
            PrimaryDate = transaction.ResponseDueDate,
            ComparedDate = transaction.IncomingDate,
            Impact = "تعرض المعاملات التي حددت لها فترة رد قصيرة مقارنة بالحد الذي اختاره المستخدم.",
            SuggestedAction = "فتح المعاملة ومراجعة تاريخ الاستحقاق أو فترة الرد من صفحة المعاملة."
        });
    }

    private static void ApplySimpleFilters(List<DataQualityIssueDto> issues, DataQualityQueryDto query)
    {
        if (!string.IsNullOrWhiteSpace(query.Severity))
        {
            issues.RemoveAll(x =>
                !string.Equals(x.Severity.ToString(), query.Severity, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(x.SeverityLabel, query.Severity, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(query.Category))
            issues.RemoveAll(x => !string.Equals(x.Category, query.Category, StringComparison.OrdinalIgnoreCase));
    }

    private async Task AttachReviewStateAsync(List<DataQualityIssueDto> issues, CancellationToken ct)
    {
        if (issues.Count == 0)
            return;

        var keys = issues.Select(x => x.IssueKey).Distinct().ToList();
        var reviews = await _db.DataQualityReviews
            .AsNoTracking()
            .Where(x => keys.Contains(x.IssueKey))
            .GroupJoin(
                _db.Users.AsNoTracking(),
                review => review.ReviewedByUserId,
                user => user.Id,
                (review, users) => new { Review = review, User = users.FirstOrDefault() })
            .ToDictionaryAsync(x => x.Review.IssueKey, x => x, ct);

        foreach (var issue in issues)
        {
            if (!reviews.TryGetValue(issue.IssueKey, out var review))
                continue;

            issue.IsReviewed = review.Review.IsReviewed;
            issue.ReviewedAtUtc = review.Review.ReviewedAtUtc;
            issue.ReviewedByName = review.User?.FullName ?? review.User?.Username;
            issue.ReviewNote = review.Review.ReviewNote;
        }
    }

    private static void ApplyReviewFilters(List<DataQualityIssueDto> issues, DataQualityQueryDto query)
    {
        if (query.ReviewedOnly)
        {
            issues.RemoveAll(x => !x.IsReviewed);
        }
        else if (!query.IncludeReviewed)
        {
            issues.RemoveAll(x => x.IsReviewed);
        }
    }

    private static string? ResolveResponsibleDepartmentName(Transaction transaction) =>
        transaction.OutgoingDepartments
            .OrderBy(x => x.Id)
            .Select(x => x.Department.Name)
            .FirstOrDefault()
        ?? transaction.Assignments
            .OrderBy(x => x.Id)
            .Select(x => x.Department?.Name)
            .FirstOrDefault(x => !string.IsNullOrWhiteSpace(x));

    private static string SeverityLabel(DataQualitySeverity severity) =>
        severity switch
        {
            DataQualitySeverity.Critical => "حرجة",
            DataQualitySeverity.High => "عالية",
            DataQualitySeverity.Medium => "متوسطة",
            _ => "منخفضة"
        };
}
