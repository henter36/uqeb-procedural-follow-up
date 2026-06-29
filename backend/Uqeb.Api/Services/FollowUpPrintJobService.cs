using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
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
    Task<PagedFollowUpPrintJobsDto> ListJobsAsync(int page, int pageSize, FollowUpPrintJobListStatusFilter status, ICurrentUserService currentUser, CancellationToken cancellationToken = default);
    Task<List<FollowUpPrintJobPartDto>> GetJobPartsAsync(int jobId, ICurrentUserService currentUser, CancellationToken cancellationToken = default);
    Task<List<FollowUpLetterPrintRecordDto>> MarkPartPrintRequestedAsync(int jobId, int partNumber, ICurrentUserService currentUser, CancellationToken cancellationToken = default);
    Task<string?> GetPartPrintViewHtmlAsync(int jobId, int partNumber, ICurrentUserService currentUser, CancellationToken cancellationToken = default);
}

internal sealed record SignatoryOptions(
    string? Position,
    string? Rank,
    string? NameOverride)
{
    public static SignatoryOptions FromRequest(CreateFollowUpPrintJobRequest r) =>
        new(r.SignatoryPosition, r.SignatoryRank, r.SignatoryNameOverride);
}

public sealed class FollowUpPrintJobService : IFollowUpPrintJobService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private readonly record struct FollowUpPrintJobCounts(int Transactions, int Letters);

    private readonly AppDbContext _db;
    private readonly IFollowUpPrintEligibilityService _eligibility;
    private readonly IFollowUpLetterRenderService _renderService;
    private readonly IFollowUpPrintAccessService _access;
    private readonly IAuditService _audit;
    private readonly FollowUpLettersOptions _options;
    private readonly ILogger<FollowUpPrintJobService> _logger;

    public FollowUpPrintJobService(
        AppDbContext db,
        IFollowUpPrintEligibilityService eligibility,
        IFollowUpLetterRenderService renderService,
        IFollowUpPrintAccessService access,
        IAuditService audit,
        IOptions<FollowUpLettersOptions> options,
        ILogger<FollowUpPrintJobService> logger)
    {
        _db = db;
        _eligibility = eligibility;
        _renderService = renderService;
        _access = access;
        _audit = audit;
        _options = options.Value;
        _logger = logger;
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
        request.IdempotencyKey = NormalizeIdempotencyKey(request.IdempotencyKey);

        // DepartmentUser may only filter by their own department; prevent cross-department access.
        if (currentUser.Role == UserRole.DepartmentUser &&
            request.Filter.DepartmentId.HasValue &&
            request.Filter.DepartmentId != currentUser.DepartmentId)
        {
            throw new FollowUpPrintValidationException("لا يمكن تحديد إدارة مختلفة عن إدارتك.");
        }

        var requestHash = FollowUpPrintRequestHash.Compute(request, batchSize);
        var existingJob = await TryResolveExistingIdempotentJobAsync(
            request,
            currentUser,
            requestHash,
            cancellationToken);
        if (existingJob != null)
            return existingJob;

        try
        {
            await EnsureConcurrentJobLimitsAsync(currentUser, cancellationToken);
        }
        catch (InvalidOperationException) when (!string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            var racedJob = await TryResolveExistingIdempotentJobAsync(
                request,
                currentUser,
                requestHash,
                cancellationToken);

            if (racedJob != null)
                return racedJob;

            throw;
        }

        var rawCandidates = await _eligibility.BuildEligibleCandidatesWithTargetsAsync(
            request.Filter,
            currentUser,
            cancellationToken);

        // Filter to targets with exactly one positive ID before creating anything in the DB.
        // This prevents CK_FollowUpPrintJobPayloads_TargetShape violations and ensures
        // TotalLetters/TotalTransactions reflect only storable payloads.
        var validCandidates = BuildValidCandidates(rawCandidates);

        if (validCandidates.Count == 0)
            throw new FollowUpPrintValidationException("لا توجد معاملات أو جهات صالحة للطباعة وفق الفلاتر الحالية.");

        var counts = new FollowUpPrintJobCounts(
            validCandidates.Count,
            validCandidates.Sum(candidate => candidate.Targets.Count));

        var template = await ResolveActiveTemplateAsync(request.TemplateId, cancellationToken);
        var snapshot = BuildSnapshot(request.Filter);

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
                counts,
                cancellationToken);

            ordinal = await AddPayloadsForCandidatesAsync(
                job,
                validCandidates,
                template,
                currentUser,
                request.ResponseDeadlineDays,
                SignatoryOptions.FromRequest(request),
                cancellationToken);

            await PersistIdempotencyKeyAsync(request, currentUser, requestHash, job.Id, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (SqlExceptionHelper.IsCheckConstraintViolation(ex, "CK_FollowUpPrintJobPayloads_TargetShape"))
        {
            await transaction.RollbackAsync(cancellationToken);
            _db.ChangeTracker.Clear();
            throw new FollowUpPrintValidationException(
                "بعض الجهات المستهدفة ليست مرتبطة بإدارة أو طرف خارجي بمعرّف صالح. تحقق من بيانات المعاملات المختارة.",
                ex);
        }
        catch (DbUpdateException ex) when (SqlExceptionHelper.IsDuplicateKey(ex))
        {
            await transaction.RollbackAsync(cancellationToken);
            _db.ChangeTracker.Clear();

            var racedJob = await TryResolveExistingIdempotentJobAsync(
                request,
                currentUser,
                requestHash,
                cancellationToken);
            if (racedJob != null)
                return racedJob;

            throw;
        }

        // Audit is best-effort: the job is already committed; do not fail the response over an audit write.
        try
        {
            await FollowUpPrintAuditWriter.LogJobQueuedAsync(_audit, currentUser.UserId, job.Id, $"letters={ordinal};batchSize={batchSize}");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Audit log failed for committed print job {JobId}. Job was created successfully.", job.Id);
        }

        // Use CancellationToken.None: the original token may be cancelled if the client disconnected
        // after commit, but the job is committed — we must return a valid response regardless.
        try
        {
            var dto = await GetJobAsync(job.Id, currentUser, CancellationToken.None);
            if (dto != null)
                return dto;
            _logger.LogWarning("GetJobAsync returned null for committed print job {JobId}. Returning fallback DTO.", job.Id);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "GetJobAsync failed for committed print job {JobId}. Returning fallback DTO.", job.Id);
        }

        return BuildFallbackJobDto(job);
    }

    private static FollowUpPrintJobDto BuildFallbackJobDto(FollowUpPrintJob job) => new()
    {
        Id = job.Id,
        Status = job.Status,
        TemplateId = job.TemplateId,
        TotalTransactions = job.TotalTransactions,
        TotalLetters = job.TotalLetters,
        ProcessedLetters = job.ProcessedLetters,
        ReadyLetters = job.ReadyLetters,
        FailedLetters = job.FailedLetters,
        SkippedLetters = job.SkippedLetters,
        TotalParts = job.TotalParts,
        ReadyParts = job.ReadyParts,
        PrintedParts = job.PrintedParts,
        CurrentPart = job.CurrentPart,
        CreatedAt = job.CreatedAt,
        StartedAt = job.StartedAt,
        ReadyAt = job.ReadyAt,
        CompletedAt = job.CompletedAt,
        FailedAt = job.FailedAt,
        CancelledAt = job.CancelledAt,
        FailureReason = job.FailureReason,
        Parts = [],
    };

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
        FollowUpPrintJobListStatusFilter status,
        ICurrentUserService currentUser,
        CancellationToken cancellationToken = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var query = ApplyStatusFilter(
            _access.ApplyJobListScope(_db.FollowUpPrintJobs.AsNoTracking(), currentUser),
            status);

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(j => j.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(MapJobExpr)
            .ToListAsync(cancellationToken);

        foreach (var item in items)
            item.Parts = await GetJobPartsInternalAsync(item.Id, cancellationToken);

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

    private static IQueryable<FollowUpPrintJob> ApplyStatusFilter(
        IQueryable<FollowUpPrintJob> query,
        FollowUpPrintJobListStatusFilter status) =>
        status switch
        {
            FollowUpPrintJobListStatusFilter.All => query,
            FollowUpPrintJobListStatusFilter.Cancelled => query.Where(j => j.Status == FollowUpPrintJobStatus.Cancelled),
            FollowUpPrintJobListStatusFilter.ReadyToPrint => query.Where(j =>
                j.Status == FollowUpPrintJobStatus.ReadyToPrint ||
                j.Status == FollowUpPrintJobStatus.PartiallyPrinted),
            FollowUpPrintJobListStatusFilter.Completed => query.Where(j => j.Status == FollowUpPrintJobStatus.Completed),
            FollowUpPrintJobListStatusFilter.Failed => query.Where(j =>
                j.Status == FollowUpPrintJobStatus.Failed ||
                j.Status == FollowUpPrintJobStatus.Expired),
            _ => query.Where(j => j.Status != FollowUpPrintJobStatus.Cancelled &&
                                  j.Status != FollowUpPrintJobStatus.Completed &&
                                  j.Status != FollowUpPrintJobStatus.Failed &&
                                  j.Status != FollowUpPrintJobStatus.Expired),
        };

    public async Task<List<FollowUpLetterPrintRecordDto>> MarkPartPrintRequestedAsync(
        int jobId,
        int partNumber,
        ICurrentUserService currentUser,
        CancellationToken cancellationToken = default)
    {
        await _access.EnsureCanPrintPartAsync(jobId, partNumber, currentUser, cancellationToken);

        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);

        var part = await LoadPrintablePartAsync(jobId, partNumber, cancellationToken);

        if (part.Status == FollowUpPrintJobPartStatus.Printed)
            return await LoadExistingPrintRecordDtosAsync(part.Id, cancellationToken);

        if (part.Status is not (FollowUpPrintJobPartStatus.ReadyToPrint or FollowUpPrintJobPartStatus.PartiallyReady))
            throw new InvalidOperationException("جزء مهمة الطباعة غير جاهز للطباعة.");

        var payloads = await LoadAssignedPayloadsAsync(part, cancellationToken);

        var now = DateTime.UtcNow;
        var records = CreatePrintRecords(payloads, part, jobId, currentUser.UserId, now);

        _db.FollowUpLetterPrintRecords.AddRange(records);
        ApplyPrintedPartState(part, partNumber, now);

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (IsPrintRecordDuplicate(ex))
        {
            await transaction.RollbackAsync(cancellationToken);
            var existing = await LoadExistingPrintRecordDtosAsync(part.Id, cancellationToken);
            if (existing.Count > 0)
                return existing;

            throw;
        }

        await transaction.CommitAsync(cancellationToken);
        await WritePrintRequestAuditAsync(part, jobId, currentUser.UserId, records.Count);

        return records.Select(MapRecord).ToList();
    }

    private async Task<FollowUpPrintJobPart> LoadPrintablePartAsync(
        int jobId,
        int partNumber,
        CancellationToken cancellationToken) =>
        await _db.FollowUpPrintJobParts
            .Include(p => p.Job)
            .FirstOrDefaultAsync(p => p.JobId == jobId && p.PartNumber == partNumber, cancellationToken)
        ?? throw new InvalidOperationException("جزء مهمة الطباعة غير موجود.");

    private async Task<List<FollowUpPrintJobPayload>> LoadAssignedPayloadsAsync(
        FollowUpPrintJobPart part,
        CancellationToken cancellationToken)
    {
        var payloads = await _db.FollowUpPrintJobPayloads.AsNoTracking()
            .Where(p => p.PartId == part.Id && p.Status == FollowUpPrintJobPayloadStatus.Assigned)
            .OrderBy(p => p.PayloadOrdinal)
            .ToListAsync(cancellationToken);

        return payloads.Count > 0 ? payloads : BuildFallbackPayloads(part.PayloadJson);
    }

    private static List<FollowUpPrintJobPayload> BuildFallbackPayloads(string payloadJson) =>
        DeserializePayload(payloadJson)
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

    private static List<FollowUpLetterPrintRecord> CreatePrintRecords(
        IReadOnlyCollection<FollowUpPrintJobPayload> payloads,
        FollowUpPrintJobPart part,
        int jobId,
        int userId,
        DateTime now) =>
        payloads.Select(payload => new FollowUpLetterPrintRecord
        {
            TransactionId = payload.TransactionId,
            TargetDepartmentId = payload.TargetDepartmentId,
            TargetEntityId = payload.TargetEntityId,
            TargetEntityNameSnapshot = payload.TargetEntityName,
            TemplateId = part.Job.TemplateId,
            FollowUpSequence = payload.FollowUpSequence,
            ResponseDeadlineDays = payload.ResponseDeadlineDays ?? part.Job.ResponseDeadlineDays,
            DocumentSnapshotJson = payload.SnapshotJson == "{}" ? null : payload.SnapshotJson,
            PrintRequestedAt = now,
            PrintRequestedById = userId,
            BatchJobId = jobId,
            BatchJobPartId = part.Id,
            CreatedAt = now,
        }).ToList();

    private static void ApplyPrintedPartState(FollowUpPrintJobPart part, int partNumber, DateTime now)
    {
        part.Status = FollowUpPrintJobPartStatus.Printed;
        part.PrintedAt = now;
        part.Job.PrintedParts += 1;
        part.Job.CurrentPart = partNumber;

        if (part.Job.ReadyParts > 0 && part.Job.PrintedParts >= part.Job.ReadyParts)
            part.Job.Status = FollowUpPrintJobStatus.Completed;
        else if (part.Job.PrintedParts > 0)
            part.Job.Status = FollowUpPrintJobStatus.PartiallyPrinted;

        if (part.Job.Status == FollowUpPrintJobStatus.Completed)
            part.Job.CompletedAt = now;
    }

    private async Task<List<FollowUpLetterPrintRecordDto>> LoadExistingPrintRecordDtosAsync(
        int partId,
        CancellationToken cancellationToken)
    {
        var existing = await _db.FollowUpLetterPrintRecords.AsNoTracking()
            .Where(r => r.BatchJobPartId == partId)
            .ToListAsync(cancellationToken);

        return existing.Select(MapRecord).ToList();
    }

    private async Task WritePrintRequestAuditAsync(
        FollowUpPrintJobPart part,
        int jobId,
        int userId,
        int recordsCount)
    {
        await FollowUpPrintAuditWriter.LogPartPrintRequestedAsync(
            _audit,
            userId,
            part.Id,
            jobId,
            $"records={recordsCount}");

        if (part.Job.Status == FollowUpPrintJobStatus.Completed)
            await FollowUpPrintAuditWriter.LogJobCompletedAsync(
                _audit,
                userId,
                jobId,
                $"printedParts={part.Job.PrintedParts}");
    }

    private static bool IsPrintRecordDuplicate(DbUpdateException ex) =>
        SqlExceptionHelper.IsDuplicateKey(ex, "IX_FollowUpLetterPrintRecords_Part_Tx_Dept_Seq") ||
        SqlExceptionHelper.IsDuplicateKey(ex, "IX_FollowUpLetterPrintRecords_Part_Tx_Entity_Seq");

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

        return documents.Count == 0 ? null : FollowUpLetterPrintViewRenderer.Render(documents, _options, $"مهمة طباعة #{jobId} — الجزء {partNumber}");
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
        var idempotencyKey = NormalizeIdempotencyKey(request.IdempotencyKey, required: false);
        if (string.IsNullOrWhiteSpace(idempotencyKey))
            return null;

        var existingKey = await _db.FollowUpPrintIdempotencyKeys.AsNoTracking()
            .FirstOrDefaultAsync(
                k => k.UserId == currentUser.UserId &&
                     k.Key == idempotencyKey &&
                     k.Operation == FollowUpPrintOperations.CreateJob,
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
            ? await _db.LetterTemplates.AsNoTracking().FirstOrDefaultAsync(
                t => t.Id == templateId.Value &&
                     t.TemplateType == LetterTemplateType.FollowUp &&
                     t.IsActive,
                cancellationToken)
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
        FollowUpPrintJobCounts counts,
        CancellationToken cancellationToken)
    {
        var job = new FollowUpPrintJob
        {
            RequestedById = currentUser.UserId,
            ScopeDepartmentId = request.Filter.DepartmentId
                ?? (currentUser.Role == UserRole.DepartmentUser ? currentUser.DepartmentId : null),
            Status = FollowUpPrintJobStatus.Queued,
            FilterSnapshotJson = JsonSerializer.Serialize(snapshot, JsonOptions),
            TemplateId = template.Id,
            ResponseDeadlineDays = request.ResponseDeadlineDays,
            ExcludeRecentlyPrinted = request.Filter.ExcludeRecentlyPrinted,
            PrintedLetterExclusionDays = request.Filter.PrintedLetterExclusionDays,
            DaysSinceLastFollowUp = request.Filter.DaysSinceLastFollowUp,
            BatchSize = batchSize,
            NextPayloadOrdinal = 0,
            TotalTransactions = counts.Transactions,
            TotalLetters = counts.Letters,
            CreatedAt = DateTime.UtcNow,
        };

        _db.FollowUpPrintJobs.Add(job);
        await _db.SaveChangesAsync(cancellationToken);
        return job;
    }

    private async Task<int> AddPayloadsForCandidatesAsync(
        FollowUpPrintJob job,
        IReadOnlyList<EligibleCandidateWithTargets> validCandidates,
        LetterTemplate template,
        ICurrentUserService currentUser,
        int? responseDeadlineDays,
        SignatoryOptions signatoryOptions,
        CancellationToken cancellationToken)
    {
        var ordinal = 0;
        foreach (var candidate in validCandidates)
        {
            var sequence = FollowUpSequenceCalculator.CalculateExpectedSequence(candidate.FollowUpCount);
            foreach (var target in candidate.Targets)
            {
                // All candidates here are pre-validated by BuildValidCandidates.
                // Normalize: treat 0 as null so the stored value satisfies CK_FollowUpPrintJobPayloads_TargetShape.
                var deptId = target.DepartmentId is > 0 ? target.DepartmentId : null;
                var entityId = target.ExternalPartyId is > 0 ? target.ExternalPartyId : null;

                ordinal++;
                var document = await _renderService.BuildDocumentAsync(new FollowUpLetterBuildRequest
                {
                    TransactionId = candidate.TransactionId,
                    Target = target,
                    CurrentUser = currentUser,
                    TemplateId = template.Id,
                    FollowUpSequenceOverride = sequence,
                    ResponseDeadlineDays = responseDeadlineDays,
                    SignatoryPosition = signatoryOptions.Position,
                    SignatoryRank = signatoryOptions.Rank,
                    SignatoryNameOverride = signatoryOptions.NameOverride,
                    CancellationToken = cancellationToken,
                });

                var payload = new FollowUpPrintJobPayload
                {
                    JobId = job.Id,
                    PayloadOrdinal = ordinal,
                    TransactionId = candidate.TransactionId,
                    TargetDepartmentId = deptId,
                    TargetEntityId = entityId,
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

        // TotalLetters and TotalTransactions are already set correctly from valid counts
        // in CreateQueuedJobEntityAsync; only NextPayloadOrdinal needs to be updated here.
        job.NextPayloadOrdinal = ordinal;
        await _db.SaveChangesAsync(cancellationToken);
        return ordinal;
    }

    /// <summary>
    /// Returns candidates filtered to only valid, unique targets.
    /// A target is valid when exactly one of DepartmentId / ExternalPartyId is a positive integer.
    /// Duplicate targets (same normalized IDs within a transaction) are also dropped.
    /// Candidates with no valid targets after filtering are excluded entirely.
    /// </summary>
    private IReadOnlyList<EligibleCandidateWithTargets> BuildValidCandidates(
        IReadOnlyList<EligibleCandidateWithTargets> candidates)
    {
        var result = new List<EligibleCandidateWithTargets>();
        foreach (var candidate in candidates)
        {
            var validTargets = BuildValidTargets(candidate);

            if (validTargets.Count > 0)
                result.Add(new EligibleCandidateWithTargets
                {
                    TransactionId = candidate.TransactionId,
                    FollowUpCount = candidate.FollowUpCount,
                    Targets = validTargets,
                });
        }
        return result;
    }

    private List<FollowUpLetterTargetEntity> BuildValidTargets(EligibleCandidateWithTargets candidate)
    {
        var seen = new HashSet<(int?, int?)>();
        var validTargets = new List<FollowUpLetterTargetEntity>();
        foreach (var target in candidate.Targets)
            TryAddValidTarget(candidate.TransactionId, target, seen, validTargets);
        return validTargets;
    }

    private void TryAddValidTarget(
        int transactionId,
        FollowUpLetterTargetEntity target,
        HashSet<(int?, int?)> seen,
        List<FollowUpLetterTargetEntity> validTargets)
    {
        if (!HasValidTargetShape(target))
        {
            LogSkippedInvalidTarget(transactionId, target);
            return;
        }

        var key = BuildDeduplicationKey(target);
        if (!seen.Add(key))
        {
            LogSkippedDuplicateTarget(transactionId, target, key);
            return;
        }

        validTargets.Add(target);
    }

    /// <summary>
    /// A target has a valid shape when exactly one of DepartmentId / ExternalPartyId is a positive integer.
    /// Zero is treated as absent — it would not satisfy a FK on the departments/parties tables.
    /// </summary>
    private static bool HasValidTargetShape(FollowUpLetterTargetEntity target)
    {
        var hasDept = target.DepartmentId is > 0;
        var hasEntity = target.ExternalPartyId is > 0;
        return hasDept ^ hasEntity;
    }

    private static (int? DepartmentId, int? ExternalPartyId) NormalizeTargetIds(FollowUpLetterTargetEntity target) => (
        target.DepartmentId is > 0 ? target.DepartmentId : null,
        target.ExternalPartyId is > 0 ? target.ExternalPartyId : null);

    private static (int? DepartmentId, int? ExternalPartyId) BuildDeduplicationKey(FollowUpLetterTargetEntity target)
        => NormalizeTargetIds(target);

    private void LogSkippedInvalidTarget(int transactionId, FollowUpLetterTargetEntity target)
    {
        _logger.LogWarning(
            "Skipping target with invalid shape. " +
            "TransactionId={TransactionId}, DepartmentId={DepartmentId}, " +
            "ExternalPartyId={ExternalPartyId}, TargetName={TargetName}. " +
            "Reason=InvalidTargetShape",
            transactionId,
            target.DepartmentId,
            target.ExternalPartyId,
            target.Name);
    }

    private void LogSkippedDuplicateTarget(
        int transactionId,
        FollowUpLetterTargetEntity target,
        (int? DepartmentId, int? ExternalPartyId) key)
    {
        _logger.LogWarning(
            "Skipping duplicate target. " +
            "TransactionId={TransactionId}, DepartmentId={DepartmentId}, " +
            "ExternalPartyId={ExternalPartyId}, TargetName={TargetName}. " +
            "Reason=DuplicateTarget",
            transactionId,
            key.DepartmentId,
            key.ExternalPartyId,
            target.Name);
    }

    private async Task PersistIdempotencyKeyAsync(
        CreateFollowUpPrintJobRequest request,
        ICurrentUserService currentUser,
        string requestHash,
        int jobId,
        CancellationToken cancellationToken)
    {
        var idempotencyKey = NormalizeIdempotencyKey(request.IdempotencyKey, required: false);
        if (string.IsNullOrWhiteSpace(idempotencyKey))
            return;

        _db.FollowUpPrintIdempotencyKeys.Add(new FollowUpPrintIdempotencyKey
        {
            UserId = currentUser.UserId,
            Key = idempotencyKey,
            Operation = FollowUpPrintOperations.CreateJob,
            RequestHash = requestHash,
            ResultId = jobId,
            CreatedAt = DateTime.UtcNow,
        });
        await _db.SaveChangesAsync(cancellationToken);
    }

    private static string? NormalizeIdempotencyKey(string? value, bool required = false)
    {
        var key = value?.Trim();
        if (string.IsNullOrWhiteSpace(key))
        {
            if (required)
                throw new InvalidOperationException("مفتاح idempotency مطلوب.");
            return null;
        }

        if (key.Length > 128)
            throw new InvalidOperationException("مفتاح idempotency يجب ألا يتجاوز 128 حرفًا.");

        return key;
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
        HasDocumentSnapshot = record.DocumentSnapshotJson != null && record.DocumentSnapshotJson != "",
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
