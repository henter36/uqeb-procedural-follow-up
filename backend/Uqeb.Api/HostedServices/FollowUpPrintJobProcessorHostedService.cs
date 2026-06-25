using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Uqeb.Api.Configuration;
using Uqeb.Api.Data;
using Uqeb.Api.DTOs.FollowUpPrint;
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
                _logger.LogError(ex, "فشلت دورة معالجة مهام طباعة التعقيب.");
            }

            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
        }
    }

    private async Task ProcessOnceAsync(CancellationToken cancellationToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var eligibility = scope.ServiceProvider.GetRequiredService<IFollowUpPrintEligibilityService>();
        var renderService = scope.ServiceProvider.GetRequiredService<IFollowUpLetterRenderService>();
        var notifications = scope.ServiceProvider.GetRequiredService<IUserNotificationService>();

        var now = DateTime.UtcNow;
        var leaseOwner = Environment.MachineName + ":" + Guid.NewGuid().ToString("N");

        var job = await db.FollowUpPrintJobs
            .Where(j =>
                ClaimableStatuses.Contains(j.Status) &&
                (j.LeaseExpiresAt == null || j.LeaseExpiresAt < now))
            .OrderBy(j => j.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (job == null)
            return;

        job.Status = FollowUpPrintJobStatus.Claimed;
        job.LeaseOwner = leaseOwner;
        job.LeaseExpiresAt = now.AddSeconds(_options.JobLeaseSeconds);
        job.StartedAt ??= now;

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return;
        }

        try
        {
            await ProcessJobAsync(db, eligibility, renderService, notifications, job, leaseOwner, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "فشلت معالجة مهمة الطباعة {JobId}", job.Id);
            job.Status = FollowUpPrintJobStatus.Failed;
            job.FailureReason = "تعذر إكمال مهمة الطباعة.";
            job.FailedAt = DateTime.UtcNow;
            job.LeaseOwner = null;
            job.LeaseExpiresAt = null;
            await db.SaveChangesAsync(cancellationToken);

            await notifications.CreateAsync(
                job.RequestedById,
                NotificationTypeJobFailed,
                "فشلت مهمة طباعة التعقيب",
                $"تعذر إكمال مهمة الطباعة رقم {job.Id}.",
                $"/follow-up-print/jobs/{job.Id}",
                cancellationToken);
        }
    }

    private async Task ProcessJobAsync(
        AppDbContext db,
        IFollowUpPrintEligibilityService eligibility,
        IFollowUpLetterRenderService renderService,
        IUserNotificationService notifications,
        FollowUpPrintJob job,
        string leaseOwner,
        CancellationToken cancellationToken)
    {
        job.Status = FollowUpPrintJobStatus.Processing;
        await RenewLeaseAsync(job, leaseOwner, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);

        var filter = JsonSerializer.Deserialize<FollowUpPrintFilterSnapshot>(job.FilterSnapshotJson, JsonOptions)
            ?? new FollowUpPrintFilterSnapshot();

        var payloads = (await eligibility.BuildLetterPayloadsAsync(filter, cancellationToken: cancellationToken)).ToList();
        if (payloads.Count == 0)
        {
            job.Status = FollowUpPrintJobStatus.Failed;
            job.FailureReason = "لم يتم العثور على خطابات مطابقة للفلاتر.";
            job.FailedAt = DateTime.UtcNow;
            job.LeaseOwner = null;
            job.LeaseExpiresAt = null;
            await db.SaveChangesAsync(cancellationToken);
            return;
        }

        var batchSize = Math.Clamp(_options.DefaultBatchPrintSize, 1, _options.AbsoluteMaxBatchPrintSize);
        var partNumber = await db.FollowUpPrintJobParts.CountAsync(p => p.JobId == job.Id, cancellationToken);
        var processed = 0;

        for (var offset = 0; offset < payloads.Count; offset += batchSize)
        {
            await RenewLeaseAsync(job, leaseOwner, cancellationToken);
            await db.SaveChangesAsync(cancellationToken);

            partNumber += 1;
            var batch = payloads.Skip(offset).Take(batchSize).ToList();
            var documents = new List<FollowUpLetterDocumentModel>();

            foreach (var payload in batch)
            {
                var target = new FollowUpLetterTargetEntity(
                    payload.TargetEntityName,
                    payload.TargetDepartmentId,
                    payload.TargetEntityId);

                var document = await renderService.BuildDocumentAsync(
                    payload.TransactionId,
                    target,
                    new SystemUser(job.RequestedById),
                    job.TemplateId,
                    followUpSequenceOverride: payload.FollowUpSequence,
                    responseDeadlineDays: payload.ResponseDeadlineDays ?? job.ResponseDeadlineDays,
                    cancellationToken: cancellationToken);

                if (document != null)
                    documents.Add(document);
            }

            var part = new FollowUpPrintJobPart
            {
                JobId = job.Id,
                PartNumber = partNumber,
                Status = FollowUpPrintJobPartStatus.ReadyToPrint,
                LetterCount = batch.Count,
                EstimatedPages = Math.Max(1, documents.Count),
                PayloadJson = JsonSerializer.Serialize(batch, JsonOptions),
                CreatedAt = DateTime.UtcNow,
                ReadyAt = DateTime.UtcNow,
            };

            db.FollowUpPrintJobParts.Add(part);
            processed += batch.Count;
            job.ProcessedLetters = processed;
            job.ReadyLetters = processed;
            job.TotalParts = partNumber;
            job.ReadyParts = partNumber;
            job.CurrentPart = partNumber;
        }

        job.TotalLetters = payloads.Count;
        job.TotalTransactions = payloads.Select(p => p.TransactionId).Distinct().Count();
        job.Status = FollowUpPrintJobStatus.ReadyToPrint;
        job.ReadyAt = DateTime.UtcNow;
        job.LeaseOwner = null;
        job.LeaseExpiresAt = null;
        await db.SaveChangesAsync(cancellationToken);

        await notifications.CreateAsync(
            job.RequestedById,
            NotificationTypeJobReady,
            "مهمة طباعة التعقيب جاهزة",
            $"أصبحت مهمة الطباعة رقم {job.Id} جاهزة ({job.ReadyParts} جزء/أجزاء).",
            $"/follow-up-print/jobs/{job.Id}",
            cancellationToken);
    }

    private Task RenewLeaseAsync(FollowUpPrintJob job, string leaseOwner, CancellationToken cancellationToken)
    {
        job.LeaseOwner = leaseOwner;
        job.LeaseExpiresAt = DateTime.UtcNow.AddSeconds(_options.JobLeaseSeconds);
        return Task.CompletedTask;
    }

    private sealed class SystemUser(int userId) : ICurrentUserService
    {
        public int UserId { get; } = userId;
        public string Username { get; } = "system";
        public UserRole Role { get; } = UserRole.Admin;
        public int? DepartmentId => null;
        public bool IsAuthenticated => true;
    }
}
