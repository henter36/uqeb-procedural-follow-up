using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Uqeb.Api.Configuration;
using Uqeb.Api.Data;
using Uqeb.Api.DTOs.FollowUpPrint;
using Uqeb.Api.Exceptions;
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
    Task<FollowUpPrintJobDto?> GetJobAsync(int jobId, ICurrentUserService currentUser, CancellationToken cancellationToken = default);
    Task<PagedFollowUpPrintJobsDto> ListJobsAsync(int page, int pageSize, ICurrentUserService currentUser, CancellationToken cancellationToken = default);
    Task<List<FollowUpPrintJobPartDto>> GetJobPartsAsync(int jobId, ICurrentUserService currentUser, CancellationToken cancellationToken = default);
    Task<List<FollowUpLetterPrintRecordDto>> MarkPartPrintRequestedAsync(int jobId, int partNumber, ICurrentUserService currentUser, CancellationToken cancellationToken = default);
    Task<string?> GetPartPrintViewHtmlAsync(int jobId, int partNumber, ICurrentUserService currentUser, CancellationToken cancellationToken = default);
}

public sealed class FollowUpPrintJobService : IFollowUpPrintJobService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private readonly AppDbContext _db;
    private readonly IFollowUpPrintEligibilityService _eligibility;
    private readonly IFollowUpLetterRenderService _renderService;
    private readonly IFollowUpPrintAccessService _access;
    private readonly IAuditService _audit;
    private readonly FollowUpLettersOptions _options;

    public FollowUpPrintJobService(
        AppDbContext db,
        IFollowUpPrintEligibilityService eligibility,
        IFollowUpLetterRenderService renderService,
        IFollowUpPrintAccessService access,
        IAuditService audit,
        IOptions<FollowUpLettersOptions> options)
    {
        _db = db;
        _eligibility = eligibility;
        _renderService = renderService;
        _access = access;
        _audit = audit;
        _options = options.Value;
    }

    public async Task<FollowUpPrintJobDto> CreateJobAsync(
        CreateFollowUpPrintJobRequest request,
        ICurrentUserService currentUser,
        CancellationToken cancellationToken = default)
    {
        var batchSize = Math.Clamp(
            request.BatchSize ?? _options.DefaultBatchPrintSize,
            1,
            _options.AbsoluteMaxBatchPrintSize);

        var requestHash = FollowUpPrintRequestHash.Compute(request, batchSize);
        var existingJob = await TryResolveExistingIdempotentJobAsync(
            request,
            currentUser,
            requestHash,
            cancellationToken);
        if (existingJob != null)
            return existingJob;

        await EnsureConcurrentJobLimitsAsync(currentUser, cancellationToken);

        var preview = await PreviewJobAsync(request, currentUser, cancellationToken);
        if (preview.EligibleTransactionCount == 0 || preview.EstimatedLetterCount == 0)
            throw new InvalidOperationException("لا توجد معاملات مستحقة للتعقيب ضمن الفلاتر المحددة.");

        var template = await ResolveActiveTemplateAsync(request.TemplateId, cancellationToken);
        var snapshot = BuildSnapshot(request.Filter);
        var eligibleCandidates = await _eligibility.BuildEligibleCandidatesWithTargetsAsync(request.Filter, currentUser, cancellationToken);

        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);

        FollowUpPrintJob job;
        int ordinal;
        try
        {
            job = await CreateQueuedJobEntityAsync(
                request,
                currentUser,
                template,
                snapshot,
                batchSize,
                preview,
                cancellationToken);

            ordinal = await AddPayloadsForCandidatesAsync(
                job,
                eligibleCandidates,
                template,
                currentUser,
                request.ResponseDeadlineDays,
                cancellationToken);

            await PersistIdempotencyKeyAsync(request, currentUser, requestHash, job.Id, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (SqlExceptionHelper.IsDuplicateKey(ex))
        {
            await transaction.RollbackAsync(cancellationToken);

            var racedJob = await TryResolveExistingIdempotentJobAsync(
                request,
                currentUser,
                requestHash,
                cancellationToken);
            if (racedJob != null)
                return racedJob;

            throw;
        }

        await FollowUpPrintAuditWriter.LogJobQueuedAsync(_audit, currentUser.UserId, job.Id, $"letters={ordinal};batchSize={batchSize}");

        return (await GetJobAsync(job.Id, currentUser, cancellationToken))!;
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
        await _access.EnsureCanMutateJobAsync(jobId, currentUser, cancellationToken);

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
        foreach (var part in parts.Where(p => p.Status is FollowUpPrintJobPartStatus.Pending or FollowUpPrintJobPartStatus.Processing or FollowUpPrintJobPartStatus.ReadyToPrint or FollowUpPrintJobPartStatus.PartiallyReady))
            part.Status = FollowUpPrintJobPartStatus.Cancelled;

        var pendingPayloads = await _db.FollowUpPrintJobPayloads
            .Where(p => p.JobId == jobId && p.Status == FollowUpPrintJobPayloadStatus.Pending)
            .ToListAsync(cancellationToken);

        foreach (var payload in pendingPayloads)
            payload.Status = FollowUpPrintJobPayloadStatus.Skipped;

        await _db.SaveChangesAsync(cancellationToken);
        await FollowUpPrintAuditWriter.LogJobCancelledAsync(_audit, currentUser.UserId, jobId);
        return await GetJobAsync(jobId, currentUser, cancellationToken);
    }

    public async Task<FollowUpPrintJobDto?> RetryJobAsync(
        int jobId,
        ICurrentUserService currentUser,
        CancellationToken cancellationToken = default)
    {
        await _access.EnsureCanMutateJobAsync(jobId, currentUser, cancellationToken);

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

        var payloads = await _db.FollowUpPrintJobPayloads.Where(p => p.JobId == jobId).ToListAsync(cancellationToken);
        foreach (var payload in payloads)
        {
            payload.PartId = null;
            payload.FailureReason = null;
            payload.Status = payload.SnapshotJson == "{}" || string.IsNullOrWhiteSpace(payload.SnapshotJson)
                ? FollowUpPrintJobPayloadStatus.Failed
                : FollowUpPrintJobPayloadStatus.Pending;
            if (payload.Status == FollowUpPrintJobPayloadStatus.Failed)
                payload.FailureReason = "تعذر بناء مستند الخطاب.";
        }

        await _db.SaveChangesAsync(cancellationToken);
        await FollowUpPrintAuditWriter.LogJobRetryRequestedAsync(_audit, currentUser.UserId, jobId, $"retryCount={job.RetryCount}");
        return await GetJobAsync(jobId, currentUser, cancellationToken);
    }

    public async Task<FollowUpPrintJobDto?> GetJobAsync(int jobId, ICurrentUserService currentUser, CancellationToken cancellationToken = default)
    {
        await _access.EnsureCanViewJobAsync(jobId, currentUser, cancellationToken);

        var job = await _db.FollowUpPrintJobs.AsNoTracking()
            .Where(j => j.Id == jobId)
            .Select(MapJobExpr)
            .FirstOrDefaultAsync(cancellationToken);
        if (job == null) return null;
        job.Parts = await GetJobPartsInternalAsync(jobId, cancellationToken);
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

        var query = _access.ApplyJobListScope(_db.FollowUpPrintJobs.AsNoTracking(), currentUser);

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

    public async Task<List<FollowUpPrintJobPartDto>> GetJobPartsAsync(
        int jobId,
        ICurrentUserService currentUser,
        CancellationToken cancellationToken = default)
    {
        await _access.EnsureCanViewJobAsync(jobId, currentUser, cancellationToken);
        return await GetJobPartsInternalAsync(jobId, cancellationToken);
    }

    public async Task<List<FollowUpLetterPrintRecordDto>> MarkPartPrintRequestedAsync(
        int jobId,
        int partNumber,
        ICurrentUserService currentUser,
        CancellationToken cancellationToken = default)
    {
        await _access.EnsureCanPrintPartAsync(jobId, partNumber, currentUser, cancellationToken);

        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);

        var part = await _db.FollowUpPrintJobParts
            .Include(p => p.Job)
            .FirstOrDefaultAsync(p => p.JobId == jobId && p.PartNumber == partNumber, cancellationToken)
            ?? throw new InvalidOperationException("جزء مهمة الطباعة غير موجود.");

        if (part.Status == FollowUpPrintJobPartStatus.Printed)
        {
            var existing = await _db.FollowUpLetterPrintRecords.AsNoTracking()
                .Where(r => r.BatchJobPartId == part.Id)
                .ToListAsync(cancellationToken);
            return existing.Select(MapRecord).ToList();
        }

        if (part.Status is not (FollowUpPrintJobPartStatus.ReadyToPrint or FollowUpPrintJobPartStatus.PartiallyReady))
            throw new InvalidOperationException("جزء مهمة الطباعة غير جاهز للطباعة.");

        var payloads = await _db.FollowUpPrintJobPayloads.AsNoTracking()
            .Where(p => p.PartId == part.Id && p.Status == FollowUpPrintJobPayloadStatus.Assigned)
            .OrderBy(p => p.PayloadOrdinal)
            .ToListAsync(cancellationToken);

        if (payloads.Count == 0)
            payloads = DeserializePayload(part.PayloadJson)
                .Select(p => new FollowUpPrintJobPayload
                {
                    TransactionId = p.TransactionId,
                    TargetDepartmentId = p.TargetDepartmentId,
                    TargetEntityId = p.TargetEntityId,
                    TargetEntityName = p.TargetEntityName,
                    FollowUpSequence = p.FollowUpSequence,
                    ResponseDeadlineDays = p.ResponseDeadlineDays,
                    Status = FollowUpPrintJobPayloadStatus.Assigned,
                })
                .ToList();

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

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            await transaction.RollbackAsync(cancellationToken);
            var existing = await _db.FollowUpLetterPrintRecords.AsNoTracking()
                .Where(r => r.BatchJobPartId == part.Id)
                .ToListAsync(cancellationToken);
            if (existing.Count > 0)
                return existing.Select(MapRecord).ToList();
            throw;
        }

        await transaction.CommitAsync(cancellationToken);
        await FollowUpPrintAuditWriter.LogPartPrintRequestedAsync(_audit, currentUser.UserId, part.Id, jobId, $"records={records.Count}");

        if (part.Job.Status == FollowUpPrintJobStatus.Completed)
            await FollowUpPrintAuditWriter.LogJobCompletedAsync(_audit, currentUser.UserId, jobId, $"printedParts={part.Job.PrintedParts}");

        return records.Select(MapRecord).ToList();
    }

    public async Task<string?> GetPartPrintViewHtmlAsync(
        int jobId,
        int partNumber,
        ICurrentUserService currentUser,
        CancellationToken cancellationToken = default)
    {
        await _access.EnsureCanViewPartAsync(jobId, partNumber, currentUser, cancellationToken);

        var part = await _db.FollowUpPrintJobParts.AsNoTracking()
            .FirstOrDefaultAsync(p => p.JobId == jobId && p.PartNumber == partNumber, cancellationToken);

        if (part == null || part.Status == FollowUpPrintJobPartStatus.Cancelled)
            return null;

        var payloadSnapshots = await _db.FollowUpPrintJobPayloads.AsNoTracking()
            .Where(p => p.PartId == part.Id && p.Status == FollowUpPrintJobPayloadStatus.Assigned)
            .OrderBy(p => p.PayloadOrdinal)
            .Select(p => p.SnapshotJson)
            .ToListAsync(cancellationToken);

        var documents = new List<FollowUpLetterDocumentModel>();
        foreach (var snapshotJson in payloadSnapshots)
        {
            if (string.IsNullOrWhiteSpace(snapshotJson) || snapshotJson == "{}")
                continue;

            var document = JsonSerializer.Deserialize<FollowUpLetterDocumentModel>(snapshotJson, JsonOptions);
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

    private async Task<List<FollowUpPrintJobPartDto>> GetJobPartsInternalAsync(int jobId, CancellationToken cancellationToken) =>
        await _db.FollowUpPrintJobParts.AsNoTracking()
            .Where(p => p.JobId == jobId)
            .OrderBy(p => p.PartNumber)
            .Select(MapPartExpr)
            .ToListAsync(cancellationToken);

    private async Task EnsureConcurrentJobLimitsAsync(ICurrentUserService currentUser, CancellationToken cancellationToken)
    {
        var processingStatuses = new[]
        {
            FollowUpPrintJobStatus.Queued,
            FollowUpPrintJobStatus.Claimed,
            FollowUpPrintJobStatus.Processing,
        };

        var globalProcessing = await _db.FollowUpPrintJobs.CountAsync(
            j => processingStatuses.Contains(j.Status),
            cancellationToken);
        if (globalProcessing >= _options.MaxConcurrentPrintJobs)
            throw new InvalidOperationException("تم بلوغ الحد الأقصى لمهام الطباعة المتزامنة. حاول لاحقًا.");

        var userProcessing = await _db.FollowUpPrintJobs.CountAsync(
            j => j.RequestedById == currentUser.UserId && processingStatuses.Contains(j.Status),
            cancellationToken);

        if (userProcessing >= _options.MaxConcurrentJobsPerUser)
            throw new InvalidOperationException("لديك مهمة طباعة نشطة بالفعل.");

        var unprintedStatuses = new[]
        {
            FollowUpPrintJobStatus.ReadyToPrint,
            FollowUpPrintJobStatus.PartiallyPrinted,
        };

        var outstandingUnprinted = await _db.FollowUpPrintJobs.CountAsync(
            j => j.RequestedById == currentUser.UserId && unprintedStatuses.Contains(j.Status),
            cancellationToken);

        if (outstandingUnprinted >= _options.MaxOutstandingUnprintedJobs)
            throw new InvalidOperationException("لديك مهام طباعة جاهزة غير مطبوعة تتجاوز الحد المسموح.");
    }

    private async Task<FollowUpPrintJobDto?> TryResolveExistingIdempotentJobAsync(
        CreateFollowUpPrintJobRequest request,
        ICurrentUserService currentUser,
        string requestHash,
        CancellationToken cancellationToken)
    {
        var idempotencyKey = request.IdempotencyKey?.Trim();
        if (string.IsNullOrWhiteSpace(idempotencyKey))
            return null;

        var existingKey = await _db.FollowUpPrintIdempotencyKeys.AsNoTracking()
            .FirstOrDefaultAsync(
                k => k.UserId == currentUser.UserId &&
                     k.Key == idempotencyKey &&
                     k.Operation == "create-job",
                cancellationToken);

        if (existingKey == null)
            return null;

        if (!string.Equals(existingKey.RequestHash, requestHash, StringComparison.OrdinalIgnoreCase))
            throw new FollowUpPrintConflictException("مفتاح idempotency مستخدم لطلب مختلف.");

        if (existingKey.ResultId is not int existingJobId)
            return null;

        var jobExists = await _db.FollowUpPrintJobs.AsNoTracking()
            .AnyAsync(j => j.Id == existingJobId, cancellationToken);
        if (!jobExists)
        {
            var orphanKey = await _db.FollowUpPrintIdempotencyKeys
                .FirstAsync(k => k.Id == existingKey.Id, cancellationToken);
            _db.FollowUpPrintIdempotencyKeys.Remove(orphanKey);
            await _db.SaveChangesAsync(cancellationToken);
            return null;
        }

        return await GetJobAsync(existingJobId, currentUser, cancellationToken);
    }

    private async Task<LetterTemplate> ResolveActiveTemplateAsync(int? templateId, CancellationToken cancellationToken)
    {
        var template = templateId.HasValue
            ? await _db.LetterTemplates.AsNoTracking().FirstOrDefaultAsync(t => t.Id == templateId.Value && t.IsActive, cancellationToken)
            : await _renderService.GetOrCreateDefaultTemplateAsync(cancellationToken);

        if (template == null)
            throw new InvalidOperationException("قالب الخطاب غير موجود أو غير نشط.");

        return template;
    }

    private async Task<FollowUpPrintJob> CreateQueuedJobEntityAsync(
        CreateFollowUpPrintJobRequest request,
        ICurrentUserService currentUser,
        LetterTemplate template,
        FollowUpPrintFilterSnapshot snapshot,
        int batchSize,
        FollowUpPrintEligibilityPreviewDto preview,
        CancellationToken cancellationToken)
    {
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
            BatchSize = batchSize,
            NextPayloadOrdinal = 0,
            TotalTransactions = preview.EligibleTransactionCount,
            TotalLetters = preview.EstimatedLetterCount,
            CreatedAt = DateTime.UtcNow,
        };

        _db.FollowUpPrintJobs.Add(job);
        await _db.SaveChangesAsync(cancellationToken);
        return job;
    }

    private async Task<int> AddPayloadsForCandidatesAsync(
        FollowUpPrintJob job,
        IReadOnlyList<EligibleCandidateWithTargets> eligibleCandidates,
        LetterTemplate template,
        ICurrentUserService currentUser,
        int? responseDeadlineDays,
        CancellationToken cancellationToken)
    {
        var ordinal = 0;
        foreach (var candidate in eligibleCandidates)
        {
            var sequence = FollowUpSequenceCalculator.CalculateExpectedSequence(candidate.FollowUpCount);
            foreach (var target in candidate.Targets)
            {
                ordinal++;
                var document = await _renderService.BuildDocumentAsync(new FollowUpLetterBuildRequest
                {
                    TransactionId = candidate.TransactionId,
                    Target = target,
                    CurrentUser = currentUser,
                    TemplateId = template.Id,
                    FollowUpSequenceOverride = sequence,
                    ResponseDeadlineDays = responseDeadlineDays,
                    CancellationToken = cancellationToken,
                });

                var payload = new FollowUpPrintJobPayload
                {
                    JobId = job.Id,
                    PayloadOrdinal = ordinal,
                    TransactionId = candidate.TransactionId,
                    TargetDepartmentId = target.DepartmentId,
                    TargetEntityId = target.ExternalPartyId,
                    TargetEntityName = target.Name,
                    FollowUpSequence = sequence,
                    ResponseDeadlineDays = responseDeadlineDays,
                };

                if (document == null)
                {
                    payload.Status = FollowUpPrintJobPayloadStatus.Failed;
                    payload.FailureReason = "تعذر بناء مستند الخطاب.";
                    payload.SnapshotJson = "{}";
                }
                else
                {
                    document.LogoPath = null;
                    payload.Status = FollowUpPrintJobPayloadStatus.Pending;
                    payload.SnapshotJson = JsonSerializer.Serialize(document, JsonOptions);
                }

                _db.FollowUpPrintJobPayloads.Add(payload);
            }
        }

        job.NextPayloadOrdinal = ordinal;
        await _db.SaveChangesAsync(cancellationToken);
        return ordinal;
    }

    private async Task PersistIdempotencyKeyAsync(
        CreateFollowUpPrintJobRequest request,
        ICurrentUserService currentUser,
        string requestHash,
        int jobId,
        CancellationToken cancellationToken)
    {
        var idempotencyKey = request.IdempotencyKey?.Trim();
        if (string.IsNullOrWhiteSpace(idempotencyKey))
            return;

        _db.FollowUpPrintIdempotencyKeys.Add(new FollowUpPrintIdempotencyKey
        {
            UserId = currentUser.UserId,
            Key = idempotencyKey,
            Operation = "create-job",
            RequestHash = requestHash,
            ResultId = jobId,
            CreatedAt = DateTime.UtcNow,
        });
        await _db.SaveChangesAsync(cancellationToken);
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
