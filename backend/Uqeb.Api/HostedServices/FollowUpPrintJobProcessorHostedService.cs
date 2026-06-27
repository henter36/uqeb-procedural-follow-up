using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Uqeb.Api.Configuration;
using Uqeb.Api.Data;
using Uqeb.Api.DTOs.FollowUpPrint;
using Uqeb.Api.Exceptions;
using Uqeb.Api.Models.Entities;
using Uqeb.Api.Models.Enums;
using Uqeb.Api.Models.Letters;
using Uqeb.Api.Services;

namespace Uqeb.Api.HostedServices;

public sealed class FollowUpPrintJobProcessorHostedService : BackgroundService
{
    public const string NotificationTypeJobReady = "follow_up_print_job_ready";
    public const string NotificationTypeJobFailed = "follow_up_print_job_failed";

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private static readonly FollowUpPrintJobStatus[] ClaimableStatuses =
    [
        FollowUpPrintJobStatus.Queued,
        FollowUpPrintJobStatus.Claimed,
        FollowUpPrintJobStatus.Processing,
    ];

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<FollowUpPrintJobProcessorHostedService> _logger;
    private readonly FollowUpLettersOptions _options;

    public FollowUpPrintJobProcessorHostedService(
        IServiceScopeFactory scopeFactory,
        IOptions<FollowUpLettersOptions> options,
        ILogger<FollowUpPrintJobProcessorHostedService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _options = options.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("بدأ عامل معالجة مهام طباعة التعقيب (دورة كل 5 ثوانٍ، انتهاء صلاحية المهمة بعد {Hours} ساعة).",
            _options.JobExpirationHours);
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await ProcessOnceAsync(stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "فشلت دورة معالجة مهام طباعة التعقيب — سيُعاد المحاولة بعد 5 ثوانٍ.");
                }

                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }
        finally
        {
            _logger.LogInformation("توقف عامل معالجة مهام طباعة التعقيب.");
        }
    }

    internal async Task ProcessOnceAsync(CancellationToken cancellationToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var notifications = scope.ServiceProvider.GetRequiredService<IUserNotificationService>();
        var audit = scope.ServiceProvider.GetRequiredService<IAuditService>();

        await ExpireStaleJobsAsync(db, cancellationToken);

        var claim = await TryClaimNextJobAsync(db, cancellationToken);
        if (claim == null)
            return;

        var (job, leaseOwner, isLeaseRecovery) = claim.Value;

        try
        {
            await ProcessJobAsync(db, notifications, audit, job, leaseOwner, isLeaseRecovery, cancellationToken);
        }
        catch (FollowUpPrintLeaseExpiredException ex)
        {
            _logger.LogWarning(ex, "توقف عامل الطباعة عن مهمة {JobId} بعد انتهاء الـlease — ستُعاد المعالجة.", ex.JobId);
        }
        catch (Exception ex)
        {
            var reason = $"تعذر إكمال مهمة الطباعة: {ex.GetType().Name} — {ex.Message}";
            _logger.LogError(ex, "فشلت معالجة مهمة الطباعة {JobId}: {Message}", job.Id, ex.Message);

            // Use a fresh scope to save failure status — avoids stale EF context after exception.
            try
            {
                await using var failScope = _scopeFactory.CreateAsyncScope();
                var failDb = failScope.ServiceProvider.GetRequiredService<AppDbContext>();
                await failDb.FollowUpPrintJobs
                    .Where(j => j.Id == job.Id)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(j => j.Status, FollowUpPrintJobStatus.Failed)
                        .SetProperty(j => j.FailureReason, reason)
                        .SetProperty(j => j.FailedAt, DateTime.UtcNow)
                        .SetProperty(j => j.LeaseOwner, (string?)null)
                        .SetProperty(j => j.LeaseExpiresAt, (DateTime?)null),
                        cancellationToken);
            }
            catch (Exception saveEx)
            {
                _logger.LogError(saveEx, "فشل حفظ حالة الفشل للمهمة {JobId}.", job.Id);
                return;
            }

            try
            {
                await FollowUpPrintAuditWriter.LogJobFailedAsync(audit, job.RequestedById, job.Id, reason);
                await notifications.CreateAsync(
                    job.RequestedById,
                    NotificationTypeJobFailed,
                    "فشلت مهمة طباعة التعقيب",
                    $"تعذر إكمال مهمة الطباعة رقم {job.Id}.",
                    $"/follow-up-print/jobs/{job.Id}",
                    cancellationToken);
            }
            catch (Exception notifyEx)
            {
                _logger.LogWarning(notifyEx, "فشل إرسال إشعار فشل المهمة {JobId}.", job.Id);
            }
        }
    }

    private async Task ExpireStaleJobsAsync(AppDbContext db, CancellationToken cancellationToken)
    {
        var threshold = DateTime.UtcNow.AddHours(-_options.JobExpirationHours);
        var staleJobs = await db.FollowUpPrintJobs
            .Where(j =>
                ClaimableStatuses.Contains(j.Status) &&
                j.CreatedAt < threshold)
            .ToListAsync(cancellationToken);

        if (staleJobs.Count == 0) return;

        foreach (var stale in staleJobs)
        {
            _logger.LogWarning(
                "المهمة {JobId} عالقة في الحالة {Status} منذ {Created} — تم وضع علامة انتهاء الصلاحية.",
                stale.Id, stale.Status, stale.CreatedAt);
            stale.Status = FollowUpPrintJobStatus.Expired;
            stale.FailureReason = $"انتهت مدة الصلاحية ({_options.JobExpirationHours} ساعة) دون إكمال التجهيز.";
            stale.FailedAt = DateTime.UtcNow;
            stale.LeaseOwner = null;
            stale.LeaseExpiresAt = null;
        }

        await db.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("تم وضع علامة انتهاء الصلاحية على {Count} مهمة/مهام طباعة عالقة.", staleJobs.Count);
    }

    internal async Task<(FollowUpPrintJob Job, string LeaseOwner, bool IsLeaseRecovery)?> TryClaimNextJobAsync(
        AppDbContext db,
        CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var leaseOwner = Environment.MachineName + ":" + Guid.NewGuid().ToString("N");
        var leaseUntil = now.AddSeconds(_options.JobLeaseSeconds);

        var candidate = await db.FollowUpPrintJobs.AsNoTracking()
            .Where(j =>
                ClaimableStatuses.Contains(j.Status) &&
                (j.LeaseExpiresAt == null || j.LeaseExpiresAt < now))
            .OrderBy(j => j.CreatedAt)
            .Select(j => new { j.Id, j.StartedAt, j.Status })
            .FirstOrDefaultAsync(cancellationToken);

        if (candidate == null)
        {
            _logger.LogDebug("لا توجد مهام طباعة معلقة.");
            return null;
        }

        var isLeaseRecovery = candidate.StartedAt.HasValue &&
                              candidate.Status == FollowUpPrintJobStatus.Processing;

        _logger.LogInformation(
            "التقاط مهمة الطباعة {JobId} (الحالة: {Status}، استرداد lease: {IsRecovery}).",
            candidate.Id, candidate.Status, isLeaseRecovery);

        var claimed = await db.FollowUpPrintJobs
            .Where(j =>
                j.Id == candidate.Id &&
                ClaimableStatuses.Contains(j.Status) &&
                (j.LeaseExpiresAt == null || j.LeaseExpiresAt < now))
            .ExecuteUpdateAsync(
                s => s
                    .SetProperty(j => j.Status, FollowUpPrintJobStatus.Processing)
                    .SetProperty(j => j.LeaseOwner, leaseOwner)
                    .SetProperty(j => j.LeaseExpiresAt, leaseUntil)
                    .SetProperty(j => j.StartedAt, j => j.StartedAt ?? now),
                cancellationToken);

        if (claimed == 0)
        {
            _logger.LogDebug("فشل التقاط المهمة {JobId} — تم التقاطها بواسطة عامل آخر.", candidate.Id);
            return null;
        }

        var job = await db.FollowUpPrintJobs.FirstAsync(j => j.Id == candidate.Id, cancellationToken);
        return (job, leaseOwner, isLeaseRecovery);
    }

    private async Task ProcessJobAsync(
        AppDbContext db,
        IUserNotificationService notifications,
        IAuditService audit,
        FollowUpPrintJob job,
        string leaseOwner,
        bool isLeaseRecovery,
        CancellationToken cancellationToken)
    {
        await RenewLeaseIfOwnerAsync(db, job.Id, leaseOwner, cancellationToken);
        await db.Entry(job).ReloadAsync(cancellationToken);

        if (isLeaseRecovery)
        {
            _logger.LogInformation("استرداد lease للمهمة {JobId} — متابعة المعالجة.", job.Id);
            await FollowUpPrintAuditWriter.LogJobLeaseRecoveredAsync(audit, job.RequestedById, job.Id);
        }
        else
        {
            _logger.LogInformation("بدء معالجة مهمة الطباعة {JobId}.", job.Id);
            await FollowUpPrintAuditWriter.LogJobStartedAsync(audit, job.RequestedById, job.Id);
        }

        while (!cancellationToken.IsCancellationRequested)
        {
            cancellationToken.ThrowIfCancellationRequested();

            await RenewLeaseIfOwnerAsync(db, job.Id, leaseOwner, cancellationToken);
            await db.Entry(job).ReloadAsync(cancellationToken);

            if (job.Status is FollowUpPrintJobStatus.Cancelled or FollowUpPrintJobStatus.Failed or FollowUpPrintJobStatus.Completed)
                return;

            var batch = await db.FollowUpPrintJobPayloads
                .Where(p => p.JobId == job.Id && p.PartId == null && p.Status == FollowUpPrintJobPayloadStatus.Pending)
                .OrderBy(p => p.PayloadOrdinal)
                .Take(job.BatchSize)
                .ToListAsync(cancellationToken);

            if (batch.Count == 0)
                break;

            await ProcessBatchAsync(db, job, batch, leaseOwner, cancellationToken);
            await db.Entry(job).ReloadAsync(cancellationToken);
        }

        var pendingCount = await db.FollowUpPrintJobPayloads.CountAsync(
            p => p.JobId == job.Id && p.Status == FollowUpPrintJobPayloadStatus.Pending,
            cancellationToken);

        var unhandledFailures = await db.FollowUpPrintJobPayloads.CountAsync(
            p => p.JobId == job.Id &&
                 p.Status == FollowUpPrintJobPayloadStatus.Failed &&
                 p.PartId == null,
            cancellationToken);

        await FinalizeJobProcessingAsync(db, job, leaseOwner, pendingCount, unhandledFailures, cancellationToken);

        _logger.LogInformation(
            "اكتملت معالجة مهمة الطباعة {JobId}: الحالة = {Status}، أجزاء جاهزة = {ReadyParts}، سبب الفشل = {Reason}.",
            job.Id, job.Status, job.ReadyParts, job.FailureReason ?? "—");

        if (job.Status == FollowUpPrintJobStatus.ReadyToPrint)
        {
            await FollowUpPrintAuditWriter.LogJobReadyAsync(audit, job.RequestedById, job.Id, $"parts={job.ReadyParts}");
            await notifications.CreateAsync(
                job.RequestedById,
                NotificationTypeJobReady,
                "مهمة طباعة التعقيب جاهزة",
                $"أصبحت مهمة الطباعة رقم {job.Id} جاهزة ({job.ReadyParts} جزء/أجزاء).",
                $"/follow-up-print/jobs/{job.Id}",
                cancellationToken);
        }
        else if (job.Status == FollowUpPrintJobStatus.Failed && job.FailedAt.HasValue)
        {
            await FollowUpPrintAuditWriter.LogJobFailedAsync(audit, job.RequestedById, job.Id, job.FailureReason);
        }
    }

    private async Task FinalizeJobProcessingAsync(
        AppDbContext db,
        FollowUpPrintJob job,
        string leaseOwner,
        int pendingCount,
        int unhandledFailures,
        CancellationToken cancellationToken)
    {
        if (pendingCount == 0 && unhandledFailures == 0)
        {
            job.Status = job.ReadyParts > 0 ? FollowUpPrintJobStatus.ReadyToPrint : FollowUpPrintJobStatus.Failed;
            job.ReadyAt = job.ReadyParts > 0 ? DateTime.UtcNow : job.ReadyAt;
            if (job.Status == FollowUpPrintJobStatus.Failed)
            {
                job.FailureReason ??= "لم يتم تجهيز أي جزء للطباعة.";
                job.FailedAt = DateTime.UtcNow;
            }
        }
        else if (pendingCount == 0 && unhandledFailures > 0 && job.ReadyParts > 0)
        {
            job.Status = FollowUpPrintJobStatus.ReadyToPrint;
            job.ReadyAt ??= DateTime.UtcNow;
        }
        else if (pendingCount == 0 && unhandledFailures > 0)
        {
            job.Status = FollowUpPrintJobStatus.Failed;
            job.FailureReason ??= "فشل تجهيز جميع خطابات مهمة الطباعة.";
            job.FailedAt = DateTime.UtcNow;
        }
        else if (pendingCount > 0)
        {
            job.Status = FollowUpPrintJobStatus.Processing;
            job.LeaseOwner = leaseOwner;
            job.LeaseExpiresAt = DateTime.UtcNow.AddSeconds(_options.JobLeaseSeconds);
            await db.SaveChangesAsync(cancellationToken);
            return;
        }

        job.LeaseOwner = null;
        job.LeaseExpiresAt = null;
        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task ProcessBatchAsync(
        AppDbContext db,
        FollowUpPrintJob job,
        List<FollowUpPrintJobPayload> batch,
        string leaseOwner,
        CancellationToken cancellationToken)
    {
        var partNumber = job.TotalParts + 1;
        _logger.LogInformation(
            "بدء تجهيز دفعة {PartNumber} من مهمة {JobId}: {BatchSize} خطاب/خطابات.",
            partNumber, job.Id, batch.Count);

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        var letterPayloads = new List<FollowUpPrintJobLetterPayload>();
        var documents = new List<FollowUpLetterDocumentModel>();
        var readyCount = 0;
        var failedCount = 0;

        foreach (var payload in batch)
        {
            FollowUpLetterDocumentModel? document = null;
            if (!string.IsNullOrWhiteSpace(payload.SnapshotJson) && payload.SnapshotJson != "{}")
            {
                try
                {
                    document = JsonSerializer.Deserialize<FollowUpLetterDocumentModel>(payload.SnapshotJson, JsonOptions);
                }
                catch (JsonException ex)
                {
                    _logger.LogWarning(ex, "فشل تحليل SnapshotJson للمعاملة {TxId} في مهمة {JobId}.", payload.TransactionId, job.Id);
                    document = null;
                }
            }

            if (document == null)
            {
                payload.Status = FollowUpPrintJobPayloadStatus.Failed;
                payload.FailureReason = "لقطة مستند الخطاب غير صالحة.";
                failedCount++;
                continue;
            }

            letterPayloads.Add(new FollowUpPrintJobLetterPayload
            {
                TransactionId = payload.TransactionId,
                TargetDepartmentId = payload.TargetDepartmentId,
                TargetEntityId = payload.TargetEntityId,
                TargetEntityName = payload.TargetEntityName,
                FollowUpSequence = payload.FollowUpSequence,
                ResponseDeadlineDays = payload.ResponseDeadlineDays,
            });
            documents.Add(document);
            readyCount++;
        }

        if (readyCount == 0)
        {
            _logger.LogWarning(
                "دفعة {PartNumber} من مهمة {JobId}: كل {FailedCount} خطاب/خطابات فشلت — لن يُنشأ جزء.",
                partNumber, job.Id, failedCount);
            job.ProcessedLetters += batch.Count;
            job.FailedLetters += failedCount;
            job.CurrentPart = job.TotalParts;
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            await RenewLeaseIfOwnerAsync(db, job.Id, leaseOwner, cancellationToken);
            return;
        }

        var part = new FollowUpPrintJobPart
        {
            JobId = job.Id,
            PartNumber = partNumber,
            LetterCount = documents.Count,
            EstimatedPages = Math.Max(1, documents.Count),
            PayloadJson = JsonSerializer.Serialize(letterPayloads, JsonOptions),
            CreatedAt = DateTime.UtcNow,
        };

        if (failedCount > 0)
        {
            part.Status = FollowUpPrintJobPartStatus.PartiallyReady;
            part.ReadyAt = DateTime.UtcNow;
            part.FailureReason = $"فشل {failedCount} خطاب/خطابات ضمن الجزء.";
        }
        else
        {
            part.Status = FollowUpPrintJobPartStatus.ReadyToPrint;
            part.ReadyAt = DateTime.UtcNow;
        }

        db.FollowUpPrintJobParts.Add(part);
        await db.SaveChangesAsync(cancellationToken);

        foreach (var payload in batch.Where(p => p.Status != FollowUpPrintJobPayloadStatus.Failed))
        {
            payload.PartId = part.Id;
            payload.Status = FollowUpPrintJobPayloadStatus.Assigned;
        }

        job.ProcessedLetters += batch.Count;
        job.ReadyLetters += readyCount;
        job.FailedLetters += failedCount;
        job.TotalParts = partNumber;
        if (part.Status is FollowUpPrintJobPartStatus.ReadyToPrint or FollowUpPrintJobPartStatus.PartiallyReady)
            job.ReadyParts += 1;
        job.CurrentPart = partNumber;

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        _logger.LogInformation(
            "اكتمل الجزء {PartNumber} من مهمة {JobId}: {ReadyCount} جاهز، {FailedCount} فاشل، الحالة = {Status}.",
            partNumber, job.Id, readyCount, failedCount, part.Status);

        await RenewLeaseIfOwnerAsync(db, job.Id, leaseOwner, cancellationToken);
    }

    private async Task RenewLeaseIfOwnerAsync(AppDbContext db, int jobId, string leaseOwner, CancellationToken cancellationToken)
    {
        var leaseUntil = DateTime.UtcNow.AddSeconds(_options.JobLeaseSeconds);
        var updated = await db.FollowUpPrintJobs
            .Where(j => j.Id == jobId && j.LeaseOwner == leaseOwner)
            .ExecuteUpdateAsync(
                s => s.SetProperty(j => j.LeaseExpiresAt, leaseUntil),
                cancellationToken);

        if (updated == 0)
            throw new FollowUpPrintLeaseExpiredException(jobId);
    }
}
