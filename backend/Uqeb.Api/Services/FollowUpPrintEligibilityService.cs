using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Uqeb.Api.Configuration;
using Uqeb.Api.Data;
using Uqeb.Api.DTOs.FollowUpPrint;
using Uqeb.Api.Helpers;
using Uqeb.Api.Models.Entities;
using Uqeb.Api.Models.Enums;

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
        var candidates = await LoadCandidatesAsync(snapshot, currentUser, cancellationToken);
        var exclusionCutoff = today.AddDays(-filter.PrintedLetterExclusionDays);

        var eligible = candidates
            .Select(row => MapEligible(row, today, filter.DaysSinceLastFollowUp, snapshot.ExcludeRecentlyPrinted, exclusionCutoff, _timeZone))
            .Where(x => x != null)
            .Cast<EligibleTransactionDto>()
            .OrderByDescending(x => x.DaysSinceReference)
            .ThenBy(x => x.IncomingNumber)
            .ToList();

        var page = Math.Max(filter.Page, 1);
        var pageSize = Math.Clamp(filter.PageSize, 1, _options.AbsoluteMaxBatchPrintSize);
        var items = eligible.Skip((page - 1) * pageSize).Take(pageSize).ToList();

        return new PagedEligibleTransactionsDto
        {
            TotalCount = eligible.Count,
            Page = page,
            PageSize = pageSize,
            Items = items,
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
        var candidates = await LoadCandidatesAsync(snapshot, currentUser, cancellationToken);
        var exclusionCutoff = today.AddDays(-filter.PrintedLetterExclusionDays);
        var effectiveBatchSize = Math.Clamp(
            batchSize <= 0 ? _options.DefaultBatchPrintSize : batchSize,
            1,
            _options.AbsoluteMaxBatchPrintSize);

        var eligibleCandidates = candidates
            .Where(c => IsEligibleCandidate(c, today, filter.DaysSinceLastFollowUp, snapshot.ExcludeRecentlyPrinted, exclusionCutoff, _timeZone))
            .ToList();

        var payloads = await BuildPayloadsFromCandidatesAsync(eligibleCandidates, cancellationToken);

        return new FollowUpPrintEligibilityPreviewDto
        {
            MatchedCount = candidates.Count,
            EligibleCount = eligibleCandidates.Count,
            RecentlyPrintedExcludedCount = candidates.Count - eligibleCandidates.Count,
            EstimatedLetterCount = payloads.Count,
            EstimatedPartCount = payloads.Count == 0
                ? 0
                : (int)Math.Ceiling(payloads.Count / (double)effectiveBatchSize),
        };
    }

    public async Task<IReadOnlyList<FollowUpPrintJobLetterPayload>> BuildLetterPayloadsAsync(
        FollowUpPrintFilterSnapshot filter,
        int? maxCount = null,
        CancellationToken cancellationToken = default)
    {
        var today = _timeZone.TodayDisplayDate;
        var candidates = await LoadCandidatesAsync(filter, null, cancellationToken);
        var exclusionCutoff = today.AddDays(-filter.PrintedLetterExclusionDays);
        var eligibleCandidates = candidates
            .Where(c => IsEligibleCandidate(c, today, filter.DaysSinceLastFollowUp, filter.ExcludeRecentlyPrinted, exclusionCutoff, _timeZone))
            .ToList();

        if (filter.TransactionIds is { Count: > 0 })
        {
            var allowed = filter.TransactionIds.ToHashSet();
            eligibleCandidates = eligibleCandidates.Where(c => allowed.Contains(c.TransactionId)).ToList();
        }

        return await BuildPayloadsFromCandidatesAsync(
            maxCount.HasValue ? eligibleCandidates.Take(maxCount.Value).ToList() : eligibleCandidates,
            cancellationToken);
    }

    private async Task<List<CandidateRow>> LoadCandidatesAsync(
        FollowUpPrintFilterSnapshot filter,
        ICurrentUserService? currentUser,
        CancellationToken cancellationToken)
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

        var transactions = await query
            .Select(t => new
            {
                t.Id,
                t.IncomingNumber,
                t.Subject,
                t.IncomingDate,
                FollowUpCount = t.FollowUps.Count,
                LastFollowUpDate = t.FollowUps
                    .OrderByDescending(f => f.FollowUpDate)
                    .Select(f => (DateTime?)f.FollowUpDate)
                    .FirstOrDefault(),
                LastOpenAssignmentDate = t.Assignments
                    .Where(a => a.Status == AssignmentStatus.Active)
                    .OrderByDescending(a => a.AssignedDate)
                    .Select(a => (DateTime?)a.AssignedDate)
                    .FirstOrDefault(),
            })
            .ToListAsync(cancellationToken);

        var transactionIds = transactions.Select(t => t.Id).ToList();
        var recentPrints = await _db.FollowUpLetterPrintRecords.AsNoTracking()
            .Where(r => transactionIds.Contains(r.TransactionId) && !r.IsCancelled && r.RegisteredFollowUpId == null)
            .GroupBy(r => r.TransactionId)
            .Select(g => new
            {
                TransactionId = g.Key,
                LastPrintRequestedAt = g.Max(x => x.PrintRequestedAt),
            })
            .ToDictionaryAsync(x => x.TransactionId, cancellationToken);

        return transactions.Select(t =>
        {
            recentPrints.TryGetValue(t.Id, out var printInfo);
            return new CandidateRow
            {
                TransactionId = t.Id,
                IncomingNumber = t.IncomingNumber,
                Subject = t.Subject,
                IncomingDate = t.IncomingDate,
                FollowUpCount = t.FollowUpCount,
                LastFollowUpDate = t.LastFollowUpDate,
                LastOpenAssignmentDate = t.LastOpenAssignmentDate,
                LastPrintRequestedAt = printInfo?.LastPrintRequestedAt,
            };
        }).ToList();
    }

    private async Task<IReadOnlyList<FollowUpPrintJobLetterPayload>> BuildPayloadsFromCandidatesAsync(
        IReadOnlyList<CandidateRow> candidates,
        CancellationToken cancellationToken)
    {
        var payloads = new List<FollowUpPrintJobLetterPayload>();
        foreach (var candidate in candidates)
        {
            var targets = await _renderService.ResolveTargetEntitiesAsync(candidate.TransactionId, cancellationToken: cancellationToken);
            if (targets.Count == 0)
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

    private static EligibleTransactionDto? MapEligible(
        CandidateRow row,
        DateTime today,
        int daysSinceLastFollowUp,
        bool excludeRecentlyPrinted,
        DateTime exclusionCutoff,
        IFollowUpLetterTimeZone timeZone)
    {
        if (!IsEligibleCandidate(row, today, daysSinceLastFollowUp, excludeRecentlyPrinted, exclusionCutoff, timeZone))
            return null;

        var referenceDate = GetReferenceDate(row);
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

    private static bool IsEligibleCandidate(
        CandidateRow row,
        DateTime today,
        int daysSinceLastFollowUp,
        bool excludeRecentlyPrinted,
        DateTime exclusionCutoff,
        IFollowUpLetterTimeZone timeZone)
    {
        if (GetDaysSinceReference(row, today) < daysSinceLastFollowUp)
            return false;

        if (excludeRecentlyPrinted &&
            row.LastPrintRequestedAt.HasValue &&
            timeZone.ToDisplayTime(row.LastPrintRequestedAt.Value).Date >= exclusionCutoff.Date)
            return false;

        return true;
    }

    private static int GetDaysSinceReference(CandidateRow row, DateTime today) =>
        (today - GetReferenceDate(row).Date).Days;

    private static DateTime GetReferenceDate(CandidateRow row)
    {
        if (row.LastFollowUpDate.HasValue)
            return row.LastFollowUpDate.Value;

        if (row.LastOpenAssignmentDate.HasValue)
            return row.LastOpenAssignmentDate.Value;

        return row.IncomingDate;
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
        public DateTime? LastFollowUpDate { get; init; }
        public DateTime? LastOpenAssignmentDate { get; init; }
        public DateTime? LastPrintRequestedAt { get; init; }

    }
}
