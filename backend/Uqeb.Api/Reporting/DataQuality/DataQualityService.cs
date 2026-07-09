using Microsoft.EntityFrameworkCore;
using System.Globalization;
using System.Text;
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
    public const string PotentialDuplicateOrSimilarTransactionRuleCode = "PotentialDuplicateOrSimilarTransaction";
    private const int MaxTransactionsToInspect = 10_000;
    private const int ReviewKeyBatchSize = 2_000;
    private const double StrongSubjectSimilarityThreshold = 0.65d;
    private const double SupportingSubjectSimilarityThreshold = 0.55d;
    private static readonly HashSet<string> SubjectStopWords = new(StringComparer.Ordinal)
    {
        "بشان",
        "بخصوص",
        "خطاب",
        "معامله",
        "افاده"
    };

    private readonly AppDbContext _db;
    private readonly TimeProvider _clock;

    public DataQualityService(AppDbContext db, TimeProvider clock)
    {
        _db = db;
        _clock = clock;
    }

    public async Task<DataQualitySummaryDto> GetSummaryAsync(DataQualityQueryDto query, CancellationToken ct = default)
    {
        var limit = Math.Clamp(query.Limit ?? 500, 1, 1000);
        var today = ReportingTemporalCalculator.RiyadhBusinessDate(_clock);
        var transactions = await BuildTransactionQuery(query)
            .Take(MaxTransactionsToInspect)
            .ToListAsync(ct);

        var issues = new List<DataQualityIssueDto>();
        foreach (var transaction in transactions)
        {
            if (query.OverdueMoreThanDays.HasValue)
                AddOverdueIssue(issues, transaction, today, query.OverdueMoreThanDays.Value);

            if (query.IncludeReferralDateAfterIncomingDate == true)
                AddReferralDateIssues(issues, transaction);

            if (query.ResponsePeriodLessThanDays.HasValue)
                AddShortResponsePeriodIssue(issues, transaction, query.ResponsePeriodLessThanDays.Value);
        }

        if (query.IncludePotentialDuplicateTransactions == true)
            AddPotentialDuplicateIssues(issues, transactions);

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
            AffectedTransactions = orderedIssues
                .SelectMany(x => new[] { x.TransactionId, x.RelatedTransactionId })
                .Where(x => x.HasValue)
                .Select(x => x!.Value)
                .Distinct()
                .Count(),
            GeneratedAtUtc = _clock.GetUtcNow().UtcDateTime,
            Issues = displayedIssues
        };
    }

    private IQueryable<Transaction> BuildTransactionQuery(DataQualityQueryDto query)
    {
        var transactions = _db.Transactions
            .AsNoTracking()
            .AsSplitQuery()
            .Include(x => x.IncomingFromParty)
            .Include(x => x.IncomingFromDepartment)
            .Include(x => x.CategoryEntity)
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

    private static void AddPotentialDuplicateIssues(List<DataQualityIssueDto> issues, IReadOnlyList<Transaction> transactions)
    {
        var candidatesByParty = transactions
            .Select(CreateDuplicateCandidate)
            .Where(candidate => candidate.IncomingPartyKey.Length > 0)
            .GroupBy(candidate => candidate.IncomingPartyKey, StringComparer.Ordinal);

        foreach (var group in candidatesByParty)
            AddPotentialDuplicateIssuesForParty(issues, group.OrderBy(candidate => candidate.Transaction.Id).ToList());
    }

    private static void AddPotentialDuplicateIssuesForParty(List<DataQualityIssueDto> issues, IReadOnlyList<DuplicateCandidate> candidates)
    {
        for (var i = 0; i < candidates.Count; i++)
        {
            for (var j = i + 1; j < candidates.Count; j++)
            {
                var match = TryClassifyDuplicate(candidates[i], candidates[j]);
                if (match is null)
                    continue;

                issues.Add(CreatePotentialDuplicateIssue(candidates[i], candidates[j], match));
            }
        }
    }

    private static DuplicateCandidate CreateDuplicateCandidate(Transaction transaction)
    {
        var subjectTokens = NormalizeSubjectTokens(transaction.Subject);
        var departmentKeys = transaction.OutgoingDepartments
            .Select(x => x.Department?.Name)
            .Concat(transaction.Assignments.Select(x => x.Department?.Name))
            .Select(NormalizeComparisonText)
            .Where(x => x.Length > 0)
            .ToHashSet(StringComparer.Ordinal);

        return new DuplicateCandidate(
            transaction,
            NormalizeIncomingNumber(transaction.IncomingNumber),
            NormalizeComparisonText(ResolveIncomingPartyName(transaction)),
            NormalizeComparisonText(transaction.CategoryEntity?.Name ?? transaction.Category),
            subjectTokens,
            departmentKeys);
    }

    private static DuplicateMatch? TryClassifyDuplicate(DuplicateCandidate first, DuplicateCandidate second)
    {
        if (first.Transaction.Id == second.Transaction.Id)
            return null;

        var sameParty = first.IncomingPartyKey.Length > 0 &&
            string.Equals(first.IncomingPartyKey, second.IncomingPartyKey, StringComparison.Ordinal);
        if (!sameParty)
            return null;

        var sameIncomingNumber = first.IncomingNumberKey.Length > 0 &&
            string.Equals(first.IncomingNumberKey, second.IncomingNumberKey, StringComparison.Ordinal);
        var daysBetween = Math.Abs((first.Transaction.IncomingDate.Date - second.Transaction.IncomingDate.Date).Days);

        if (sameIncomingNumber && daysBetween == 0)
        {
            return new DuplicateMatch(
                DataQualitySeverity.High,
                1d,
                "نفس رقم الوارد والتاريخ والجهة");
        }

        var subjectSimilarity = CalculateSubjectSimilarity(first.SubjectTokens, second.SubjectTokens);
        if (daysBetween <= 3 && subjectSimilarity >= StrongSubjectSimilarityThreshold)
        {
            return new DuplicateMatch(
                DataQualitySeverity.Medium,
                subjectSimilarity,
                "موضوع متشابه مع نفس الجهة وتاريخ قريب");
        }

        return TryCreateSupportingDuplicateMatch(first, second, subjectSimilarity);
    }

    private static DuplicateMatch? TryCreateSupportingDuplicateMatch(
        DuplicateCandidate first,
        DuplicateCandidate second,
        double subjectSimilarity)
    {
        var sameCategory = first.CategoryKey.Length > 0 &&
            string.Equals(first.CategoryKey, second.CategoryKey, StringComparison.Ordinal);
        var sameRoutedDepartment = first.DepartmentKeys.Overlaps(second.DepartmentKeys);
        var incomingNumbersNear = AreIncomingNumbersNear(first.IncomingNumberKey, second.IncomingNumberKey);

        if (!sameCategory && !sameRoutedDepartment)
            return null;

        if (subjectSimilarity < SupportingSubjectSimilarityThreshold && !incomingNumbersNear)
            return null;

        var reason = sameCategory
            ? "نفس الجهة والتصنيف مع موضوع أو رقم وارد قريب"
            : "نفس الجهة والإدارات المحالة مع موضوع أو رقم وارد قريب";

        return new DuplicateMatch(
            DataQualitySeverity.Medium,
            Math.Max(subjectSimilarity, incomingNumbersNear ? 0.55d : 0d),
            reason);
    }

    private static DataQualityIssueDto CreatePotentialDuplicateIssue(
        DuplicateCandidate first,
        DuplicateCandidate second,
        DuplicateMatch match)
    {
        var firstTransaction = first.Transaction;
        var secondTransaction = second.Transaction;
        var daysBetween = Math.Abs((firstTransaction.IncomingDate.Date - secondTransaction.IncomingDate.Date).Days);

        return new DataQualityIssueDto
        {
            Id = $"tx-pair:{firstTransaction.Id}:{secondTransaction.Id}:duplicate-similar",
            IssueKey = $"tx-pair:{firstTransaction.Id}:{secondTransaction.Id}:duplicate-similar",
            RuleCode = PotentialDuplicateOrSimilarTransactionRuleCode,
            Severity = match.Severity,
            SeverityLabel = SeverityLabel(match.Severity),
            Category = "التكرار والتشابه",
            IssueType = "معاملات مكررة أو متشابهة",
            TransactionId = firstTransaction.Id,
            TrackingNumber = firstTransaction.InternalTrackingNumber,
            IncomingNumber = firstTransaction.IncomingNumber,
            Subject = firstTransaction.Subject,
            RelatedTransactionId = secondTransaction.Id,
            RelatedTrackingNumber = secondTransaction.InternalTrackingNumber,
            RelatedIncomingNumber = secondTransaction.IncomingNumber,
            RelatedIncomingDate = secondTransaction.IncomingDate,
            SimilarityReason = match.Reason,
            SimilarityScore = Math.Round(match.Score, 2),
            DepartmentName = ResolveIncomingPartyName(firstTransaction),
            FieldName = "IncomingNumber/IncomingDate/IncomingParty/Subject",
            CurrentValue = $"{firstTransaction.IncomingNumber} ({firstTransaction.IncomingDate:yyyy-MM-dd}) ↔ {secondTransaction.IncomingNumber} ({secondTransaction.IncomingDate:yyyy-MM-dd})",
            DaysValue = daysBetween,
            PrimaryDate = firstTransaction.IncomingDate,
            ComparedDate = secondTransaction.IncomingDate,
            Impact = $"المعاملة {firstTransaction.InternalTrackingNumber} قد تتشابه مع {secondTransaction.InternalTrackingNumber}: {match.Reason}.",
            SuggestedAction = "فتح المعاملتين ومراجعة ما إذا كانتا تمثلان نفس الطلب قبل اتخاذ أي إجراء من صفحة المعاملة."
        };
    }

    private static string ResolveIncomingPartyName(Transaction transaction) =>
        transaction.IncomingFromParty?.Name
        ?? transaction.IncomingFromDepartment?.Name
        ?? transaction.IncomingFrom
        ?? string.Empty;

    private static double CalculateSubjectSimilarity(IReadOnlySet<string> firstTokens, IReadOnlySet<string> secondTokens)
    {
        if (firstTokens.Count == 0 || secondTokens.Count == 0)
            return 0d;

        var intersection = firstTokens.Intersect(secondTokens, StringComparer.Ordinal).Count();
        if (intersection == 0)
            return 0d;

        var union = firstTokens.Count + secondTokens.Count - intersection;
        return (double)intersection / union;
    }

    private static HashSet<string> NormalizeSubjectTokens(string? value) =>
        NormalizeComparisonText(value)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(token => token.Length > 1 && !SubjectStopWords.Contains(token))
            .ToHashSet(StringComparer.Ordinal);

    private static string NormalizeIncomingNumber(string? value)
    {
        var normalized = NormalizeComparisonText(value);
        return normalized.Replace(" ", string.Empty, StringComparison.Ordinal);
    }

    private static string NormalizeComparisonText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var builder = new StringBuilder(value.Length);
        var previousWasSpace = true;

        foreach (var raw in value.Trim())
        {
            var character = NormalizeCharacter(raw);
            if (character is '/' or '-' or '_' || char.IsPunctuation(character) || char.IsSymbol(character))
            {
                AppendSpace(builder, ref previousWasSpace);
                continue;
            }

            if (char.IsWhiteSpace(character))
            {
                AppendSpace(builder, ref previousWasSpace);
                continue;
            }

            builder.Append(char.ToLower(character, CultureInfo.InvariantCulture));
            previousWasSpace = false;
        }

        return builder.ToString().Trim();
    }

    private static char NormalizeCharacter(char character) =>
        character switch
        {
            >= '٠' and <= '٩' => (char)('0' + character - '٠'),
            >= '۰' and <= '۹' => (char)('0' + character - '۰'),
            'أ' or 'إ' or 'آ' => 'ا',
            'ى' => 'ي',
            'ة' => 'ه',
            _ => character
        };

    private static void AppendSpace(StringBuilder builder, ref bool previousWasSpace)
    {
        if (previousWasSpace)
            return;

        builder.Append(' ');
        previousWasSpace = true;
    }

    private static bool AreIncomingNumbersNear(string first, string second)
    {
        if (first.Length == 0 || second.Length == 0)
            return false;

        if (string.Equals(first, second, StringComparison.Ordinal))
            return true;

        var firstDigits = new string(first.Where(char.IsDigit).ToArray());
        var secondDigits = new string(second.Where(char.IsDigit).ToArray());
        if (firstDigits.Length == 0 || secondDigits.Length == 0)
            return false;

        return long.TryParse(firstDigits, out var firstNumber) &&
            long.TryParse(secondDigits, out var secondNumber) &&
            Math.Abs(firstNumber - secondNumber) <= 2;
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
        var reviews = new Dictionary<string, DataQualityReviewLookup>(StringComparer.Ordinal);

        foreach (var batch in keys.Chunk(ReviewKeyBatchSize))
        {
            var batchReviews = await _db.DataQualityReviews
                .AsNoTracking()
                .Where(x => batch.Contains(x.IssueKey))
                .GroupJoin(
                    _db.Users.AsNoTracking(),
                    review => review.ReviewedByUserId,
                    user => user.Id,
                    (review, users) => new DataQualityReviewLookup(review, users.FirstOrDefault()))
                .ToListAsync(ct);

            foreach (var review in batchReviews)
                reviews[review.Review.IssueKey] = review;
        }

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
        if (query.ReviewedOnly == true)
        {
            issues.RemoveAll(x => !x.IsReviewed);
        }
        else if (query.IncludeReviewed != true)
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

    private sealed record DuplicateCandidate(
        Transaction Transaction,
        string IncomingNumberKey,
        string IncomingPartyKey,
        string CategoryKey,
        HashSet<string> SubjectTokens,
        HashSet<string> DepartmentKeys);

    private sealed record DuplicateMatch(
        DataQualitySeverity Severity,
        double Score,
        string Reason);

    private sealed record DataQualityReviewLookup(DataQualityReview Review, User? User);
}
