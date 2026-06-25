using System.Text.Json;
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

public interface IFollowUpPrintJobService
{
    Task<FollowUpPrintJobDto> CreateJobAsync(CreateFollowUpPrintJobRequest request, ICurrentUserService currentUser, CancellationToken cancellationToken = default);
    Task<FollowUpPrintEligibilityPreviewDto> PreviewJobAsync(CreateFollowUpPrintJobRequest request, ICurrentUserService currentUser, CancellationToken cancellationToken = default);
    Task<FollowUpPrintJobDto?> CancelJobAsync(int jobId, ICurrentUserService currentUser, CancellationToken cancellationToken = default);
    Task<FollowUpPrintJobDto?> RetryJobAsync(int jobId, ICurrentUserService currentUser, CancellationToken cancellationToken = default);
    Task<FollowUpPrintJobDto?> GetJobAsync(int jobId, CancellationToken cancellationToken = default);
    Task<PagedFollowUpPrintJobsDto> ListJobsAsync(int page, int pageSize, ICurrentUserService currentUser, CancellationToken cancellationToken = default);
    Task<List<FollowUpPrintJobPartDto>> GetJobPartsAsync(int jobId, CancellationToken cancellationToken = default);
    Task<List<FollowUpLetterPrintRecordDto>> MarkPartPrintRequestedAsync(int jobId, int partNumber, ICurrentUserService currentUser, CancellationToken cancellationToken = default);
    Task<string?> GetPartPrintViewHtmlAsync(int jobId, int partNumber, ICurrentUserService currentUser, CancellationToken cancellationToken = default);
}

public sealed class FollowUpPrintJobService : IFollowUpPrintJobService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private readonly AppDbContext _db;
    private readonly IFollowUpPrintEligibilityService _eligibility;
    private readonly IFollowUpLetterRenderService _renderService;
    private readonly FollowUpLettersOptions _options;

    public FollowUpPrintJobService(
        AppDbContext db,
        IFollowUpPrintEligibilityService eligibility,
        IFollowUpLetterRenderService renderService,
        IOptions<FollowUpLettersOptions> options)
    {
        _db = db;
        _eligibility = eligibility;
        _renderService = renderService;
        _options = options.Value;
    }

    public async Task<FollowUpPrintJobDto> CreateJobAsync(
        CreateFollowUpPrintJobRequest request,
        ICurrentUserService currentUser,
        CancellationToken cancellationToken = default)
    {
        await EnsureConcurrentJobLimitsAsync(currentUser, cancellationToken);

        if (!string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            var existing = await _db.FollowUpPrintIdempotencyKeys.AsNoTracking()
                .FirstOrDefaultAsync(
                    k => k.UserId == currentUser.UserId &&
                         k.Key == request.IdempotencyKey.Trim() &&
                         k.Operation == "create-job" &&
                         k.ResultId.HasValue,
                    cancellationToken);

            if (existing?.ResultId is int existingJobId)
            {
                var existingJob = await GetJobAsync(existingJobId, cancellationToken);
                if (existingJob != null)
                    return existingJob;
            }
        }

        var preview = await PreviewJobAsync(request, currentUser, cancellationToken);
        if (preview.EligibleCount == 0)
            throw new InvalidOperationException("لا توجد معاملات مستحقة للتعقيب ضمن الفلاتر المحددة.");

        var template = request.TemplateId.HasValue
            ? await _db.LetterTemplates.AsNoTracking().FirstOrDefaultAsync(t => t.Id == request.TemplateId.Value && t.IsActive, cancellationToken)
            : await _renderService.GetOrCreateDefaultTemplateAsync(cancellationToken);

        if (template == null)
            throw new InvalidOperationException("قالب الخطاب غير موجود أو غير نشط.");

        var snapshot = BuildSnapshot(request.Filter);
        var job = new FollowUpPrintJob
        {
            RequestedById = currentUser.UserId,
            Status = FollowUpPrintJobStatus.Queued,
            FilterSnapshotJson = JsonSerializer.Serialize(snapshot, JsonOptions),
            TemplateId = template.Id,
            ResponseDeadlineDays = request.ResponseDeadlineDays,
            ExcludeRecentlyPrinted = request.Filter.ExcludeRecentlyPrinted,
            PrintedLetterExclusionDays = request.Filter.PrintedLetterExclusionDays,
            DaysSinceLastFollowUp = request.Filter.DaysSinceLastFollowUp,
            TotalTransactions = preview.EligibleCount,
            TotalLetters = preview.EstimatedLetterCount,
            CreatedAt = DateTime.UtcNow,
        };

        _db.FollowUpPrintJobs.Add(job);

        if (!string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            _db.FollowUpPrintIdempotencyKeys.Add(new FollowUpPrintIdempotencyKey
            {
                UserId = currentUser.UserId,
                Key = request.IdempotencyKey.Trim(),
                Operation = "create-job",
                CreatedAt = DateTime.UtcNow,
            });
        }

        await _db.SaveChangesAsync(cancellationToken);

        if (!string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            var key = await _db.FollowUpPrintIdempotencyKeys
                .FirstAsync(
                    k => k.UserId == currentUser.UserId &&
                         k.Key == request.IdempotencyKey.Trim() &&
                         k.Operation == "create-job",
                    cancellationToken);
            key.ResultId = job.Id;
            await _db.SaveChangesAsync(cancellationToken);
        }

        return (await GetJobAsync(job.Id, cancellationToken))!;
    }

    public Task<FollowUpPrintEligibilityPreviewDto> PreviewJobAsync(
        CreateFollowUpPrintJobRequest request,
        ICurrentUserService currentUser,
        CancellationToken cancellationToken = default)
    {
        var batchSize = request.BatchSize ?? _options.DefaultBatchPrintSize;
        return _eligibility.PreviewAsync(request.Filter, batchSize, currentUser, cancellationToken);
    }

    public async Task<FollowUpPrintJobDto?> CancelJobAsync(
        int jobId,
        ICurrentUserService currentUser,
        CancellationToken cancellationToken = default)
    {
        var job = await _db.FollowUpPrintJobs.FirstOrDefaultAsync(j => j.Id == jobId, cancellationToken);
        if (job == null)
            return null;

        if (job.Status is FollowUpPrintJobStatus.Completed or FollowUpPrintJobStatus.Cancelled)
            throw new InvalidOperationException("لا يمكن إلغاء مهمة الطباعة في حالتها الحالية.");

        job.Status = FollowUpPrintJobStatus.Cancelled;
        job.CancelledAt = DateTime.UtcNow;
        job.LeaseOwner = null;
        job.LeaseExpiresAt = null;

        var parts = await _db.FollowUpPrintJobParts.Where(p => p.JobId == jobId).ToListAsync(cancellationToken);
        foreach (var part in parts.Where(p => p.Status is FollowUpPrintJobPartStatus.Pending or FollowUpPrintJobPartStatus.Processing or FollowUpPrintJobPartStatus.ReadyToPrint))
            part.Status = FollowUpPrintJobPartStatus.Cancelled;

        await _db.SaveChangesAsync(cancellationToken);
        return await GetJobAsync(jobId, cancellationToken);
    }

    public async Task<FollowUpPrintJobDto?> RetryJobAsync(
        int jobId,
        ICurrentUserService currentUser,
        CancellationToken cancellationToken = default)
    {
        var job = await _db.FollowUpPrintJobs.FirstOrDefaultAsync(j => j.Id == jobId, cancellationToken);
        if (job == null)
            return null;

        if (job.Status != FollowUpPrintJobStatus.Failed && job.Status != FollowUpPrintJobStatus.Expired)
            throw new InvalidOperationException("يمكن إعادة المحاولة للمهام الفاشلة أو المنتهية فقط.");

        if (job.RetryCount >= _options.JobRetryCount)
            throw new InvalidOperationException("تم تجاوز الحد الأقصى لمحاولات إعادة المعالجة.");

        job.Status = FollowUpPrintJobStatus.Queued;
        job.FailureReason = null;
        job.FailedAt = null;
        job.StartedAt = null;
        job.ReadyAt = null;
        job.CompletedAt = null;
        job.LeaseOwner = null;
        job.LeaseExpiresAt = null;
        job.RetryCount += 1;
        job.ProcessedLetters = 0;
        job.ReadyLetters = 0;
        job.FailedLetters = 0;
        job.SkippedLetters = 0;
        job.TotalParts = 0;
        job.ReadyParts = 0;
        job.PrintedParts = 0;
        job.CurrentPart = 0;

        var oldParts = await _db.FollowUpPrintJobParts.Where(p => p.JobId == jobId).ToListAsync(cancellationToken);
        _db.FollowUpPrintJobParts.RemoveRange(oldParts);
        await _db.SaveChangesAsync(cancellationToken);
        return await GetJobAsync(jobId, cancellationToken);
    }

    public async Task<FollowUpPrintJobDto?> GetJobAsync(int jobId, CancellationToken cancellationToken = default)
    {
        var job = await _db.FollowUpPrintJobs.AsNoTracking()
            .Where(j => j.Id == jobId)
            .Select(MapJobExpr)
            .FirstOrDefaultAsync(cancellationToken);
        if (job == null) return null;
        job.Parts = await GetJobPartsAsync(jobId, cancellationToken);
        return job;
    }

    public async Task<PagedFollowUpPrintJobsDto> ListJobsAsync(
        int page,
        int pageSize,
        ICurrentUserService currentUser,
        CancellationToken cancellationToken = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var query = _db.FollowUpPrintJobs.AsNoTracking().AsQueryable();
        if (currentUser.Role == UserRole.DepartmentUser)
            query = query.Where(j => j.RequestedById == currentUser.UserId);

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(j => j.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(MapJobExpr)
            .ToListAsync(cancellationToken);

        return new PagedFollowUpPrintJobsDto
        {
            TotalCount = total,
            Page = page,
            PageSize = pageSize,
            Items = items,
        };
    }

    public async Task<List<FollowUpPrintJobPartDto>> GetJobPartsAsync(int jobId, CancellationToken cancellationToken = default) =>
        await _db.FollowUpPrintJobParts.AsNoTracking()
            .Where(p => p.JobId == jobId)
            .OrderBy(p => p.PartNumber)
            .Select(MapPartExpr)
            .ToListAsync(cancellationToken);

    public async Task<List<FollowUpLetterPrintRecordDto>> MarkPartPrintRequestedAsync(
        int jobId,
        int partNumber,
        ICurrentUserService currentUser,
        CancellationToken cancellationToken = default)
    {
        var part = await _db.FollowUpPrintJobParts
            .Include(p => p.Job)
            .FirstOrDefaultAsync(p => p.JobId == jobId && p.PartNumber == partNumber, cancellationToken)
            ?? throw new InvalidOperationException("جزء مهمة الطباعة غير موجود.");

        if (part.Status != FollowUpPrintJobPartStatus.ReadyToPrint)
            throw new InvalidOperationException("جزء مهمة الطباعة غير جاهز للطباعة.");

        var payloads = DeserializePayload(part.PayloadJson);
        var now = DateTime.UtcNow;
        var records = new List<FollowUpLetterPrintRecord>();

        foreach (var payload in payloads)
        {
            records.Add(new FollowUpLetterPrintRecord
            {
                TransactionId = payload.TransactionId,
                TargetDepartmentId = payload.TargetDepartmentId,
                TargetEntityId = payload.TargetEntityId,
                TargetEntityNameSnapshot = payload.TargetEntityName,
                TemplateId = part.Job.TemplateId,
                FollowUpSequence = payload.FollowUpSequence,
                ResponseDeadlineDays = payload.ResponseDeadlineDays ?? part.Job.ResponseDeadlineDays,
                PrintRequestedAt = now,
                PrintRequestedById = currentUser.UserId,
                BatchJobId = jobId,
                BatchJobPartId = part.Id,
                CreatedAt = now,
            });
        }

        _db.FollowUpLetterPrintRecords.AddRange(records);
        part.Status = FollowUpPrintJobPartStatus.Printed;
        part.PrintedAt = now;
        part.Job.PrintedParts += 1;
        part.Job.CurrentPart = partNumber;

        if (part.Job.ReadyParts > 0 && part.Job.PrintedParts >= part.Job.ReadyParts)
            part.Job.Status = FollowUpPrintJobStatus.Completed;
        else if (part.Job.PrintedParts > 0)
            part.Job.Status = FollowUpPrintJobStatus.PartiallyPrinted;

        part.Job.CompletedAt = part.Job.Status == FollowUpPrintJobStatus.Completed ? now : part.Job.CompletedAt;

        await _db.SaveChangesAsync(cancellationToken);
        return records.Select(MapRecord).ToList();
    }

    public async Task<string?> GetPartPrintViewHtmlAsync(
        int jobId,
        int partNumber,
        ICurrentUserService currentUser,
        CancellationToken cancellationToken = default)
    {
        var part = await _db.FollowUpPrintJobParts.AsNoTracking()
            .Include(p => p.Job)
            .FirstOrDefaultAsync(p => p.JobId == jobId && p.PartNumber == partNumber, cancellationToken);

        if (part == null || part.Status == FollowUpPrintJobPartStatus.Cancelled)
            return null;

        var payloads = DeserializePayload(part.PayloadJson);
        var documents = new List<FollowUpLetterDocumentModel>();

        foreach (var payload in payloads)
        {
            var target = new FollowUpLetterTargetEntity(
                payload.TargetEntityName,
                payload.TargetDepartmentId,
                payload.TargetEntityId);

            var document = await _renderService.BuildDocumentAsync(
                payload.TransactionId,
                target,
                currentUser,
                part.Job.TemplateId,
                followUpSequenceOverride: payload.FollowUpSequence,
                responseDeadlineDays: payload.ResponseDeadlineDays ?? part.Job.ResponseDeadlineDays,
                cancellationToken: cancellationToken);

            if (document != null)
                documents.Add(document);
        }

        return documents.Count == 0 ? null : FollowUpLetterPrintViewRenderer.Render(documents, $"مهمة طباعة #{jobId} — الجزء {partNumber}");
    }

    internal static List<FollowUpPrintJobLetterPayload> DeserializePayload(string payloadJson)
    {
        if (string.IsNullOrWhiteSpace(payloadJson))
            return [];

        return JsonSerializer.Deserialize<List<FollowUpPrintJobLetterPayload>>(payloadJson, JsonOptions) ?? [];
    }

    private async Task EnsureConcurrentJobLimitsAsync(ICurrentUserService currentUser, CancellationToken cancellationToken)
    {
        var activeStatuses = new[]
        {
            FollowUpPrintJobStatus.Queued,
            FollowUpPrintJobStatus.Claimed,
            FollowUpPrintJobStatus.Processing,
            FollowUpPrintJobStatus.ReadyToPrint,
            FollowUpPrintJobStatus.PartiallyPrinted,
        };

        var globalActive = await _db.FollowUpPrintJobs.CountAsync(j => activeStatuses.Contains(j.Status), cancellationToken);
        if (globalActive >= _options.MaxConcurrentPrintJobs)
            throw new InvalidOperationException("تم بلوغ الحد الأقصى لمهام الطباعة المتزامنة. حاول لاحقًا.");

        var userActive = await _db.FollowUpPrintJobs.CountAsync(
            j => j.RequestedById == currentUser.UserId && activeStatuses.Contains(j.Status),
            cancellationToken);

        if (userActive >= _options.MaxConcurrentJobsPerUser)
            throw new InvalidOperationException("لديك مهمة طباعة نشطة بالفعل.");
    }

    private static FollowUpPrintFilterSnapshot BuildSnapshot(FollowUpPrintFilterRequest filter) => new()
    {
        DaysSinceLastFollowUp = filter.DaysSinceLastFollowUp,
        ExcludeRecentlyPrinted = filter.ExcludeRecentlyPrinted,
        PrintedLetterExclusionDays = filter.PrintedLetterExclusionDays,
        DepartmentId = filter.DepartmentId,
        CategoryId = filter.CategoryId,
        Search = filter.Search,
    };

    private static FollowUpLetterPrintRecordDto MapRecord(FollowUpLetterPrintRecord record) => new()
    {
        Id = record.Id,
        TransactionId = record.TransactionId,
        TargetDepartmentId = record.TargetDepartmentId,
        TargetEntityId = record.TargetEntityId,
        TargetEntityNameSnapshot = record.TargetEntityNameSnapshot,
        TemplateId = record.TemplateId,
        FollowUpSequence = record.FollowUpSequence,
        ResponseDeadlineDays = record.ResponseDeadlineDays,
        PrintRequestedAt = record.PrintRequestedAt,
        PrintConfirmedAt = record.PrintConfirmedAt,
        RegisteredFollowUpId = record.RegisteredFollowUpId,
        IsCancelled = record.IsCancelled,
        ReprintOfId = record.ReprintOfId,
    };

    private static readonly System.Linq.Expressions.Expression<Func<FollowUpPrintJob, FollowUpPrintJobDto>> MapJobExpr = j => new FollowUpPrintJobDto
    {
        Id = j.Id,
        Status = j.Status,
        TemplateId = j.TemplateId,
        TotalTransactions = j.TotalTransactions,
        TotalLetters = j.TotalLetters,
        ProcessedLetters = j.ProcessedLetters,
        ReadyLetters = j.ReadyLetters,
        FailedLetters = j.FailedLetters,
        SkippedLetters = j.SkippedLetters,
        TotalParts = j.TotalParts,
        ReadyParts = j.ReadyParts,
        PrintedParts = j.PrintedParts,
        CurrentPart = j.CurrentPart,
        CreatedAt = j.CreatedAt,
        StartedAt = j.StartedAt,
        ReadyAt = j.ReadyAt,
        CompletedAt = j.CompletedAt,
        FailedAt = j.FailedAt,
        CancelledAt = j.CancelledAt,
        FailureReason = j.FailureReason,
    };

    private static readonly System.Linq.Expressions.Expression<Func<FollowUpPrintJobPart, FollowUpPrintJobPartDto>> MapPartExpr = p => new FollowUpPrintJobPartDto
    {
        Id = p.Id,
        JobId = p.JobId,
        PartNumber = p.PartNumber,
        Status = p.Status,
        LetterCount = p.LetterCount,
        EstimatedPages = p.EstimatedPages,
        CreatedAt = p.CreatedAt,
        ReadyAt = p.ReadyAt,
        PrintedAt = p.PrintedAt,
        FailureReason = p.FailureReason,
    };
}
