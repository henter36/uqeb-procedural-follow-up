using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Uqeb.Api.Configuration;
using Uqeb.Api.Data;
using Uqeb.Api.DTOs.FollowUpPrint;
using Uqeb.Api.Helpers;
using Uqeb.Api.Models.Entities;
using Uqeb.Api.Models.Enums;
using Uqeb.Api.Models.Letters;

namespace Uqeb.Api.Services;

public interface IFollowUpPrintEligibilityService
{
    Task<PagedEligibleTransactionsDto> GetEligibleAsync(
        FollowUpPrintFilterRequest filter,
        ICurrentUserService currentUser,
        CancellationToken cancellationToken = default);

    Task<FollowUpPrintEligibilityPreviewDto> PreviewAsync(
        FollowUpPrintFilterRequest filter,
        int batchSize,
        ICurrentUserService currentUser,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<FollowUpPrintJobLetterPayload>> BuildLetterPayloadsAsync(
        FollowUpPrintFilterSnapshot filter,
        int? maxCount = null,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<EligibleCandidateWithTargets>> BuildEligibleCandidatesWithTargetsAsync(
        FollowUpPrintFilterRequest filter,
        ICurrentUserService currentUser,
        CancellationToken cancellationToken = default);
}

public sealed class EligibleCandidateWithTargets
{
    public int TransactionId { get; init; }
    public int FollowUpCount { get; init; }
    public IReadOnlyList<FollowUpLetterTargetEntity> Targets { get; init; } = [];
}

public sealed class FollowUpPrintEligibilityService : IFollowUpPrintEligibilityService
{
    private static readonly TransactionStatus[] ExcludedStatuses =
    [
        TransactionStatus.Closed,
        TransactionStatus.Cancelled,
        TransactionStatus.Archived,
    ];

    private readonly AppDbContext _db;
    private readonly IFollowUpLetterRenderService _renderService;
    private readonly IFollowUpLetterTimeZone _timeZone;
    private readonly FollowUpLettersOptions _options;

    public FollowUpPrintEligibilityService(
        AppDbContext db,
        IFollowUpLetterRenderService renderService,
        IFollowUpLetterTimeZone timeZone,
        IOptions<FollowUpLettersOptions> options)
    {
        _db = db;
        _renderService = renderService;
        _timeZone = timeZone;
        _options = options.Value;
    }

    public async Task<PagedEligibleTransactionsDto> GetEligibleAsync(
        FollowUpPrintFilterRequest filter,
        ICurrentUserService currentUser,
        CancellationToken cancellationToken = default)
    {
        ValidateFilter(filter);
        var snapshot = ToSnapshot(filter);
        var today = _timeZone.TodayDisplayDate;
        var dueCutoffDate = today.AddDays(-filter.DaysSinceLastFollowUp);
        var exclusionCutoff = today.AddDays(-filter.PrintedLetterExclusionDays);
        var exclusionThresholdUtc = ComputeExclusionThresholdUtc(exclusionCutoff);

        var projected = ProjectCandidates(BuildFilteredTransactionsQuery(snapshot, currentUser));
        var eligibleQuery = ApplyDueAndPrintFilters(
            projected,
            dueCutoffDate,
            snapshot.ExcludeRecentlyPrinted,
            exclusionThresholdUtc);

        var page = Math.Max(filter.Page, 1);
        var pageSize = Math.Clamp(filter.PageSize, 1, _options.AbsoluteMaxBatchPrintSize);

        var totalCount = await eligibleQuery.CountAsync(cancellationToken);
        var rows = await eligibleQuery
            .OrderBy(x => x.ReferenceDate)
            .ThenBy(x => x.IncomingNumber)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return new PagedEligibleTransactionsDto
        {
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize,
            Items = rows
                .Select(row => MapEligible(row, today, snapshot.ExcludeRecentlyPrinted, exclusionCutoff, _timeZone))
                .ToList(),
        };
    }

    public async Task<FollowUpPrintEligibilityPreviewDto> PreviewAsync(
        FollowUpPrintFilterRequest filter,
        int batchSize,
        ICurrentUserService currentUser,
        CancellationToken cancellationToken = default)
    {
        ValidateFilter(filter);
        var snapshot = ToSnapshot(filter);
        var today = _timeZone.TodayDisplayDate;
        var dueCutoffDate = today.AddDays(-filter.DaysSinceLastFollowUp);
        var exclusionCutoff = today.AddDays(-filter.PrintedLetterExclusionDays);
        var exclusionThresholdUtc = ComputeExclusionThresholdUtc(exclusionCutoff);
        var effectiveBatchSize = Math.Clamp(
            batchSize <= 0 ? _options.DefaultBatchPrintSize : batchSize,
            1,
            _options.AbsoluteMaxBatchPrintSize);

        var projected = ProjectCandidates(BuildFilteredTransactionsQuery(snapshot, currentUser));

        var matchedCount = await projected.CountAsync(cancellationToken);
        var notDueYetCount = await projected
            .Where(x => x.ReferenceDate.Date > dueCutoffDate)
            .CountAsync(cancellationToken);

        var dueQuery = projected.Where(x => x.ReferenceDate.Date <= dueCutoffDate);

        var recentlyPrintedExcludedCount = snapshot.ExcludeRecentlyPrinted
            ? await dueQuery
                .Where(x => x.LastPrintRequestedAt.HasValue && x.LastPrintRequestedAt >= exclusionThresholdUtc)
                .CountAsync(cancellationToken)
            : 0;

        var dueNotExcludedIds = await ApplyDueAndPrintFilters(
                dueQuery,
                dueCutoffDate,
                snapshot.ExcludeRecentlyPrinted,
                exclusionThresholdUtc)
            .Select(x => x.TransactionId)
            .ToListAsync(cancellationToken);

        var targetsByTransaction = await _renderService.ResolveTargetEntitiesBulkAsync(
            dueNotExcludedIds,
            cancellationToken);

        var noTargetCount = 0;
        var eligibleTransactionCount = 0;
        var estimatedLetterCount = 0;

        foreach (var transactionId in dueNotExcludedIds)
        {
            if (!targetsByTransaction.TryGetValue(transactionId, out var targets) || targets.Count == 0)
            {
                noTargetCount++;
                continue;
            }

            eligibleTransactionCount++;
            estimatedLetterCount += targets.Count;
        }

        return new FollowUpPrintEligibilityPreviewDto
        {
            MatchedCount = matchedCount,
            EligibleTransactionCount = eligibleTransactionCount,
            RecentlyPrintedExcludedCount = recentlyPrintedExcludedCount,
            NotDueYetCount = notDueYetCount,
            NoTargetCount = noTargetCount,
            EstimatedLetterCount = estimatedLetterCount,
            EstimatedPartCount = estimatedLetterCount == 0
                ? 0
                : (int)Math.Ceiling(estimatedLetterCount / (double)effectiveBatchSize),
        };
    }

    public async Task<IReadOnlyList<EligibleCandidateWithTargets>> BuildEligibleCandidatesWithTargetsAsync(
        FollowUpPrintFilterRequest filter,
        ICurrentUserService currentUser,
        CancellationToken cancellationToken = default)
    {
        ValidateFilter(filter);
        var snapshot = ToSnapshot(filter);
        var today = _timeZone.TodayDisplayDate;
        var dueCutoffDate = today.AddDays(-filter.DaysSinceLastFollowUp);
        var exclusionCutoff = today.AddDays(-filter.PrintedLetterExclusionDays);
        var exclusionThresholdUtc = ComputeExclusionThresholdUtc(exclusionCutoff);

        var eligibleRows = await ApplyDueAndPrintFilters(
                ProjectCandidates(BuildFilteredTransactionsQuery(snapshot, currentUser)),
                dueCutoffDate,
                snapshot.ExcludeRecentlyPrinted,
                exclusionThresholdUtc)
            .OrderBy(x => x.ReferenceDate)
            .ThenBy(x => x.IncomingNumber)
            .ToListAsync(cancellationToken);

        return await BuildCandidatesWithTargetsAsync(eligibleRows, cancellationToken);
    }

    public async Task<IReadOnlyList<FollowUpPrintJobLetterPayload>> BuildLetterPayloadsAsync(
        FollowUpPrintFilterSnapshot filter,
        int? maxCount = null,
        CancellationToken cancellationToken = default)
    {
        var today = _timeZone.TodayDisplayDate;
        var dueCutoffDate = today.AddDays(-filter.DaysSinceLastFollowUp);
        var exclusionCutoff = today.AddDays(-filter.PrintedLetterExclusionDays);
        var exclusionThresholdUtc = ComputeExclusionThresholdUtc(exclusionCutoff);

        var eligibleQuery = ApplyDueAndPrintFilters(
            ProjectCandidates(BuildFilteredTransactionsQuery(filter, null)),
            dueCutoffDate,
            filter.ExcludeRecentlyPrinted,
            exclusionThresholdUtc);

        if (filter.TransactionIds is { Count: > 0 })
        {
            var allowed = filter.TransactionIds.ToHashSet();
            eligibleQuery = eligibleQuery.Where(x => allowed.Contains(x.TransactionId));
        }

        eligibleQuery = eligibleQuery
            .OrderBy(x => x.ReferenceDate)
            .ThenBy(x => x.IncomingNumber);

        if (maxCount.HasValue)
            eligibleQuery = eligibleQuery.Take(maxCount.Value);

        var eligibleRows = await eligibleQuery.ToListAsync(cancellationToken);
        return await BuildPayloadsFromCandidatesAsync(eligibleRows, cancellationToken);
    }

    private IQueryable<Transaction> BuildFilteredTransactionsQuery(
        FollowUpPrintFilterSnapshot filter,
        ICurrentUserService? currentUser)
    {
        var query = _db.Transactions.AsNoTracking()
            .Where(t => !ExcludedStatuses.Contains(t.Status) && !t.IsArchived);

        if (filter.DepartmentId.HasValue)
        {
            var departmentId = filter.DepartmentId.Value;
            query = query.Where(t =>
                t.Assignments.Any(a => a.DepartmentId == departmentId && a.Status == AssignmentStatus.Active) ||
                t.OutgoingDepartments.Any(d => d.DepartmentId == departmentId));
        }

        if (filter.CategoryId.HasValue)
            query = query.Where(t => t.CategoryId == filter.CategoryId.Value);

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var term = filter.Search.Trim();
            query = query.Where(t =>
                t.IncomingNumber.Contains(term) ||
                t.Subject.Contains(term) ||
                (t.IncomingFrom != null && t.IncomingFrom.Contains(term)));
        }

        if (currentUser?.Role == UserRole.DepartmentUser && currentUser.DepartmentId.HasValue)
        {
            var departmentId = currentUser.DepartmentId.Value;
            query = query.Where(t => t.Assignments.Any(a => a.DepartmentId == departmentId));
        }

        return query;
    }

    private IQueryable<CandidateRow> ProjectCandidates(IQueryable<Transaction> query) =>
        query.Select(t => new CandidateRow
        {
            TransactionId = t.Id,
            IncomingNumber = t.IncomingNumber,
            Subject = t.Subject,
            IncomingDate = t.IncomingDate,
            FollowUpCount = t.FollowUps.Count,
            ReferenceDate =
                t.FollowUps
                    .OrderByDescending(f => f.FollowUpDate)
                    .Select(f => (DateTime?)f.FollowUpDate)
                    .FirstOrDefault()
                ?? t.Assignments
                    .Where(a => a.Status == AssignmentStatus.Active)
                    .OrderByDescending(a => a.AssignedDate)
                    .Select(a => (DateTime?)a.AssignedDate)
                    .FirstOrDefault()
                ?? t.IncomingDate,
            LastPrintRequestedAt = _db.FollowUpLetterPrintRecords
                .Where(r => r.TransactionId == t.Id && !r.IsCancelled && r.RegisteredFollowUpId == null)
                .Select(r => (DateTime?)r.PrintRequestedAt)
                .Max(),
        });

    private static IQueryable<CandidateRow> ApplyDueAndPrintFilters(
        IQueryable<CandidateRow> query,
        DateTime dueCutoffDate,
        bool excludeRecentlyPrinted,
        DateTime exclusionThresholdUtc) =>
        query
            .Where(x => x.ReferenceDate.Date <= dueCutoffDate)
            .Where(x =>
                !excludeRecentlyPrinted ||
                !x.LastPrintRequestedAt.HasValue ||
                x.LastPrintRequestedAt < exclusionThresholdUtc);

    private async Task<IReadOnlyList<EligibleCandidateWithTargets>> BuildCandidatesWithTargetsAsync(
        IReadOnlyList<CandidateRow> candidates,
        CancellationToken cancellationToken)
    {
        if (candidates.Count == 0)
            return [];

        var targetsByTransaction = await _renderService.ResolveTargetEntitiesBulkAsync(
            candidates.Select(c => c.TransactionId).ToList(),
            cancellationToken);

        var result = new List<EligibleCandidateWithTargets>();
        foreach (var candidate in candidates)
        {
            if (!targetsByTransaction.TryGetValue(candidate.TransactionId, out var targets) || targets.Count == 0)
                continue;

            result.Add(new EligibleCandidateWithTargets
            {
                TransactionId = candidate.TransactionId,
                FollowUpCount = candidate.FollowUpCount,
                Targets = targets,
            });
        }

        return result;
    }

    private async Task<IReadOnlyList<FollowUpPrintJobLetterPayload>> BuildPayloadsFromCandidatesAsync(
        IReadOnlyList<CandidateRow> candidates,
        CancellationToken cancellationToken)
    {
        if (candidates.Count == 0)
            return [];

        var targetsByTransaction = await _renderService.ResolveTargetEntitiesBulkAsync(
            candidates.Select(c => c.TransactionId).ToList(),
            cancellationToken);

        var payloads = new List<FollowUpPrintJobLetterPayload>();
        foreach (var candidate in candidates)
        {
            if (!targetsByTransaction.TryGetValue(candidate.TransactionId, out var targets) || targets.Count == 0)
                continue;

            var sequence = FollowUpSequenceCalculator.CalculateExpectedSequence(candidate.FollowUpCount);
            foreach (var target in targets)
            {
                payloads.Add(new FollowUpPrintJobLetterPayload
                {
                    TransactionId = candidate.TransactionId,
                    TargetDepartmentId = target.DepartmentId,
                    TargetEntityId = target.ExternalPartyId,
                    TargetEntityName = target.Name,
                    FollowUpSequence = sequence,
                });
            }
        }

        return payloads;
    }

    private static EligibleTransactionDto MapEligible(
        CandidateRow row,
        DateTime today,
        bool excludeRecentlyPrinted,
        DateTime exclusionCutoff,
        IFollowUpLetterTimeZone timeZone)
    {
        var referenceDate = row.ReferenceDate;
        return new EligibleTransactionDto
        {
            TransactionId = row.TransactionId,
            IncomingNumber = row.IncomingNumber,
            Subject = row.Subject,
            IncomingDate = row.IncomingDate,
            ReferenceDate = referenceDate,
            DaysSinceReference = (today - referenceDate.Date).Days,
            ExpectedFollowUpSequence = FollowUpSequenceCalculator.CalculateExpectedSequence(row.FollowUpCount),
            RecentlyPrintedExcluded = excludeRecentlyPrinted &&
                row.LastPrintRequestedAt.HasValue &&
                timeZone.ToDisplayTime(row.LastPrintRequestedAt.Value).Date >= exclusionCutoff.Date,
            LastPrintRequestedAt = row.LastPrintRequestedAt,
        };
    }

    private DateTime ComputeExclusionThresholdUtc(DateTime exclusionCutoffDisplayDate)
    {
        var localStart = DateTime.SpecifyKind(exclusionCutoffDisplayDate.Date, DateTimeKind.Unspecified);
        return TimeZoneInfo.ConvertTimeToUtc(localStart, _timeZone.TimeZone);
    }

    private static FollowUpPrintFilterSnapshot ToSnapshot(FollowUpPrintFilterRequest filter) => new()
    {
        DaysSinceLastFollowUp = filter.DaysSinceLastFollowUp,
        ExcludeRecentlyPrinted = filter.ExcludeRecentlyPrinted,
        PrintedLetterExclusionDays = filter.PrintedLetterExclusionDays,
        DepartmentId = filter.DepartmentId,
        CategoryId = filter.CategoryId,
        Search = filter.Search,
    };

    private void ValidateFilter(FollowUpPrintFilterRequest filter)
    {
        if (filter.DaysSinceLastFollowUp < _options.MinDaysSinceLastFollowUp ||
            filter.DaysSinceLastFollowUp > _options.MaxDaysSinceLastFollowUp)
            throw new InvalidOperationException("عدد أيام التعقيب خارج النطاق المسموح.");

        if (filter.PrintedLetterExclusionDays < _options.MinPrintedLetterExclusionDays ||
            filter.PrintedLetterExclusionDays > _options.MaxPrintedLetterExclusionDays)
            throw new InvalidOperationException("عدد أيام استثناء الطباعة خارج النطاق المسموح.");
    }

    private sealed class CandidateRow
    {
        public int TransactionId { get; init; }
        public string IncomingNumber { get; init; } = string.Empty;
        public string Subject { get; init; } = string.Empty;
        public DateTime IncomingDate { get; init; }
        public int FollowUpCount { get; init; }
        public DateTime ReferenceDate { get; init; }
        public DateTime? LastPrintRequestedAt { get; init; }
    }
}
