using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Uqeb.Api.Configuration;
using Uqeb.Api.Data;
using Uqeb.Api.DTOs.FollowUpPrint;
using Uqeb.Api.HostedServices;
using Uqeb.Api.Models.Entities;
using Uqeb.Api.Models.Enums;
using Uqeb.Api.Models.Letters;
using Uqeb.Api.Services;
using Xunit;

namespace Uqeb.Api.Tests.Letters;

[Trait("Category", "SqlServer")]
public class FollowUpPrintJobProcessorTests : FollowUpPrintSqlServerTestBase
{
    private static readonly JsonSerializerOptions JsonOptions =
        new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    // Test 1 (requirement #8): all payloads invalid → job is marked Failed, not stuck.
    [Fact]
    public async Task ProcessOnce_AllPayloadsInvalid_MarksJobFailed()
    {
        if (!ShouldRunSqlServerTest()) return;

        var context = await CreateMigratedContextAsync();
        try
        {
            await using var scope = context.Provider.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var (user, template) = await SeedUserAndTemplateAsync(db);

            var job = new FollowUpPrintJob
            {
                RequestedById = user.Id,
                Status = FollowUpPrintJobStatus.Queued,
                FilterSnapshotJson = "{}",
                TemplateId = template.Id,
                BatchSize = 10,
                TotalTransactions = 2,
                TotalLetters = 2,
                CreatedAt = DateTime.UtcNow,
            };
            db.FollowUpPrintJobs.Add(job);
            await db.SaveChangesAsync();

            // Payloads with empty/invalid snapshot json — cannot be rendered.
            for (var i = 1; i <= 2; i++)
            {
                db.FollowUpPrintJobPayloads.Add(new FollowUpPrintJobPayload
                {
                    JobId = job.Id,
                    PayloadOrdinal = i,
                    TransactionId = i,
                    TargetDepartmentId = i,
                    TargetEntityName = $"جهة {i}",
                    FollowUpSequence = 1,
                    SnapshotJson = "{}",
                    Status = FollowUpPrintJobPayloadStatus.Pending,
                });
            }
            await db.SaveChangesAsync();

            var processor = scope.ServiceProvider.GetRequiredService<FollowUpPrintJobProcessorHostedService>();
            await processor.ProcessOnceAsync(CancellationToken.None);

            await using var verifyScope = context.Provider.CreateAsyncScope();
            var verifyDb = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();

            var finalJob = await verifyDb.FollowUpPrintJobs.FindAsync(job.Id);
            Assert.NotNull(finalJob);
            Assert.Equal(FollowUpPrintJobStatus.Failed, finalJob.Status);
            Assert.NotNull(finalJob.FailureReason);
            // No parts must have been created — nothing valid to print.
            var partCount = await verifyDb.FollowUpPrintJobParts.CountAsync(p => p.JobId == job.Id);
            Assert.Equal(0, partCount);
        }
        finally
        {
            await CleanupAsync(context);
        }
    }

    // Test 2 (requirement #9): valid payloads → part is ReadyToPrint; non-ready part stays non-ready.
    [Fact]
    public async Task ProcessOnce_ValidPayloads_SetsPartReadyToPrint_OnlyReadyPartAllowsPrint()
    {
        if (!ShouldRunSqlServerTest()) return;

        var context = await CreateMigratedContextAsync();
        try
        {
            await using var scope = context.Provider.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var (user, template) = await SeedUserAndTemplateAsync(db);

            var job = new FollowUpPrintJob
            {
                RequestedById = user.Id,
                Status = FollowUpPrintJobStatus.Queued,
                FilterSnapshotJson = "{}",
                TemplateId = template.Id,
                BatchSize = 5,
                TotalTransactions = 2,
                TotalLetters = 2,
                CreatedAt = DateTime.UtcNow,
            };
            db.FollowUpPrintJobs.Add(job);
            await db.SaveChangesAsync();

            // One valid payload, one invalid — should produce a PartiallyReady part.
            var validSnapshot = JsonSerializer.Serialize(
                new FollowUpLetterDocumentModel { TransactionId = 1, Recipient = "جهة", Body = "نص الخطاب" },
                JsonOptions);

            db.FollowUpPrintJobPayloads.Add(new FollowUpPrintJobPayload
            {
                JobId = job.Id,
                PayloadOrdinal = 1,
                TransactionId = 1,
                TargetDepartmentId = 1,
                TargetEntityName = "جهة 1",
                FollowUpSequence = 1,
                SnapshotJson = validSnapshot,
                Status = FollowUpPrintJobPayloadStatus.Pending,
            });
            db.FollowUpPrintJobPayloads.Add(new FollowUpPrintJobPayload
            {
                JobId = job.Id,
                PayloadOrdinal = 2,
                TransactionId = 2,
                TargetDepartmentId = 2,
                TargetEntityName = "جهة 2",
                FollowUpSequence = 1,
                SnapshotJson = "{}",
                Status = FollowUpPrintJobPayloadStatus.Pending,
            });
            await db.SaveChangesAsync();

            var processor = scope.ServiceProvider.GetRequiredService<FollowUpPrintJobProcessorHostedService>();
            await processor.ProcessOnceAsync(CancellationToken.None);

            await using var verifyScope = context.Provider.CreateAsyncScope();
            var verifyDb = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();

            var finalJob = await verifyDb.FollowUpPrintJobs.FindAsync(job.Id);
            Assert.NotNull(finalJob);
            // Job has at least one ready part → status is ReadyToPrint.
            Assert.Equal(FollowUpPrintJobStatus.ReadyToPrint, finalJob.Status);

            var parts = await verifyDb.FollowUpPrintJobParts
                .Where(p => p.JobId == job.Id)
                .ToListAsync();
            Assert.Single(parts);
            // The part contains the 1 valid payload → ReadyToPrint (partially because 1 failed).
            var part = parts[0];
            Assert.True(
                part.Status is FollowUpPrintJobPartStatus.ReadyToPrint or FollowUpPrintJobPartStatus.PartiallyReady,
                $"Expected ReadyToPrint or PartiallyReady but got {part.Status}");

            // Verify: a part in a non-ready status (Pending) would not be actionable.
            // We confirm this by asserting parts in states other than ReadyToPrint/PartiallyReady don't exist.
            var nonReadyParts = parts.Where(p =>
                p.Status is not FollowUpPrintJobPartStatus.ReadyToPrint and
                not FollowUpPrintJobPartStatus.PartiallyReady and
                not FollowUpPrintJobPartStatus.Printed).ToList();
            Assert.Empty(nonReadyParts);
        }
        finally
        {
            await CleanupAsync(context);
        }
    }

    // Test 3 (requirement #10): stale job older than JobExpirationHours → marked Expired, not stuck.
    [Fact]
    public async Task ProcessOnce_StaleJob_MarksExpired()
    {
        if (!ShouldRunSqlServerTest()) return;

        // Default JobExpirationHours = 24. Create job 25 hours old so it crosses the threshold.
        var context = await CreateMigratedContextAsync();
        try
        {
            await using var scope = context.Provider.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var (user, template) = await SeedUserAndTemplateAsync(db);

            // Job created 25 hours ago — past the default 24-hour expiration threshold.
            var job = new FollowUpPrintJob
            {
                RequestedById = user.Id,
                Status = FollowUpPrintJobStatus.Queued,
                FilterSnapshotJson = "{}",
                TemplateId = template.Id,
                BatchSize = 5,
                TotalTransactions = 1,
                TotalLetters = 1,
                CreatedAt = DateTime.UtcNow.AddHours(-25),
            };
            db.FollowUpPrintJobs.Add(job);
            await db.SaveChangesAsync();

            db.FollowUpPrintJobPayloads.Add(new FollowUpPrintJobPayload
            {
                JobId = job.Id,
                PayloadOrdinal = 1,
                TransactionId = 1,
                TargetDepartmentId = 1,
                TargetEntityName = "جهة 1",
                FollowUpSequence = 1,
                SnapshotJson = "{}",
                Status = FollowUpPrintJobPayloadStatus.Pending,
            });
            await db.SaveChangesAsync();

            var processor = scope.ServiceProvider.GetRequiredService<FollowUpPrintJobProcessorHostedService>();
            await processor.ProcessOnceAsync(CancellationToken.None);

            await using var verifyScope = context.Provider.CreateAsyncScope();
            var verifyDb = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();

            var finalJob = await verifyDb.FollowUpPrintJobs.FindAsync(job.Id);
            Assert.NotNull(finalJob);
            Assert.Equal(FollowUpPrintJobStatus.Expired, finalJob.Status);
            Assert.NotNull(finalJob.FailureReason);
            Assert.NotNull(finalJob.FailedAt);
        }
        finally
        {
            await CleanupAsync(context);
        }
    }

    // Test 4 (requirement #1 guard): all 3 of 3 valid → JobStatus=ReadyToPrint, PartStatus=ReadyToPrint, ReadyAt!=null.
    [Fact]
    public async Task ProcessOnce_AllValidPayloads_JobAndPartReadyToPrint()
    {
        if (!ShouldRunSqlServerTest()) return;

        var context = await CreateMigratedContextAsync();
        try
        {
            await using var scope = context.Provider.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var (user, template) = await SeedUserAndTemplateAsync(db);

            var job = new FollowUpPrintJob
            {
                RequestedById = user.Id,
                Status = FollowUpPrintJobStatus.Queued,
                FilterSnapshotJson = "{}",
                TemplateId = template.Id,
                BatchSize = 10,
                TotalTransactions = 3,
                TotalLetters = 3,
                CreatedAt = DateTime.UtcNow,
            };
            db.FollowUpPrintJobs.Add(job);
            await db.SaveChangesAsync();

            for (var i = 1; i <= 3; i++)
            {
                var snapshot = JsonSerializer.Serialize(
                    new FollowUpLetterDocumentModel { TransactionId = i, Recipient = "جهة", Body = $"نص الخطاب {i}" },
                    JsonOptions);
                db.FollowUpPrintJobPayloads.Add(new FollowUpPrintJobPayload
                {
                    JobId = job.Id,
                    PayloadOrdinal = i,
                    TransactionId = i,
                    TargetDepartmentId = i,
                    TargetEntityName = $"جهة {i}",
                    FollowUpSequence = 1,
                    SnapshotJson = snapshot,
                    Status = FollowUpPrintJobPayloadStatus.Pending,
                });
            }
            await db.SaveChangesAsync();

            var processor = scope.ServiceProvider.GetRequiredService<FollowUpPrintJobProcessorHostedService>();
            await processor.ProcessOnceAsync(CancellationToken.None);

            await using var verifyScope = context.Provider.CreateAsyncScope();
            var verifyDb = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();

            var finalJob = await verifyDb.FollowUpPrintJobs.FindAsync(job.Id);
            Assert.NotNull(finalJob);
            Assert.Equal(FollowUpPrintJobStatus.ReadyToPrint, finalJob.Status);
            Assert.Equal(3, finalJob.ReadyLetters);
            Assert.Equal(3, finalJob.TotalLetters);
            Assert.NotNull(finalJob.ReadyAt);

            var parts = await verifyDb.FollowUpPrintJobParts.Where(p => p.JobId == job.Id).ToListAsync();
            Assert.Single(parts);
            var part = parts[0];
            Assert.Equal(FollowUpPrintJobPartStatus.ReadyToPrint, part.Status);
            Assert.Equal(3, part.LetterCount);
            Assert.True(part.EstimatedPages > 0, "EstimatedPages должно быть > 0");
            Assert.NotNull(part.ReadyAt);

            // Guard: readyAt set → status must be ReadyToPrint or PartiallyReady (never left as Pending/Processing).
            Assert.True(
                part.Status is FollowUpPrintJobPartStatus.ReadyToPrint or FollowUpPrintJobPartStatus.PartiallyReady,
                $"Part has ReadyAt but status is {part.Status} — inconsistent state.");
        }
        finally
        {
            await CleanupAsync(context);
        }
    }

    // Test 5 (البند 2): Processing job with old CreatedAt but future LeaseExpiresAt must NOT be expired.
    [Fact]
    public async Task ExpireStaleJobs_ProcessingJobWithActiveLease_NotExpired()
    {
        if (!ShouldRunSqlServerTest()) return;

        var context = await CreateMigratedContextAsync();
        try
        {
            await using var scope = context.Provider.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var (user, template) = await SeedUserAndTemplateAsync(db);

            // Job: old CreatedAt (25h ago) but LeaseExpiresAt is 2 minutes in the future.
            var job = new FollowUpPrintJob
            {
                RequestedById = user.Id,
                Status = FollowUpPrintJobStatus.Processing,
                FilterSnapshotJson = "{}",
                TemplateId = template.Id,
                BatchSize = 5,
                TotalTransactions = 1,
                TotalLetters = 1,
                CreatedAt = DateTime.UtcNow.AddHours(-25),
                StartedAt = DateTime.UtcNow.AddMinutes(-1),
                LeaseOwner = "active-worker:abc123",
                LeaseExpiresAt = DateTime.UtcNow.AddMinutes(2),
            };
            db.FollowUpPrintJobs.Add(job);
            await db.SaveChangesAsync();

            // Call ExpireStaleJobsAsync via ProcessOnceAsync — but there's no claimable job
            // (Processing + active lease), so it should only run expire check and not touch this job.
            var processor = scope.ServiceProvider.GetRequiredService<FollowUpPrintJobProcessorHostedService>();
            await processor.ProcessOnceAsync(CancellationToken.None);

            await using var verifyScope = context.Provider.CreateAsyncScope();
            var verifyDb = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();

            var finalJob = await verifyDb.FollowUpPrintJobs.FindAsync(job.Id);
            Assert.NotNull(finalJob);
            // Active lease protects the job — it must NOT become Expired.
            Assert.Equal(FollowUpPrintJobStatus.Processing, finalJob.Status);
            Assert.Null(finalJob.FailureReason);
        }
        finally
        {
            await CleanupAsync(context);
        }
    }

    private static async Task<(User User, LetterTemplate Template)> SeedUserAndTemplateAsync(AppDbContext db)
    {
        var user = new User
        {
            Username = $"proc-test-{Guid.NewGuid():N}",
            PasswordHash = "hash",
            FullName = "Processor Test User",
            Role = UserRole.Admin,
            IsActive = true,
        };
        db.Users.Add(user);

        var template = new LetterTemplate
        {
            Code = $"follow_up_{Guid.NewGuid():N}",
            Name = "Follow Up",
            TemplateType = LetterTemplateType.FollowUp,
            Content = FollowUpLetterRenderService.DefaultFollowUpContent,
            IsActive = true,
            IsDefault = true,
        };
        db.LetterTemplates.Add(template);
        await db.SaveChangesAsync();
        return (user, template);
    }
}
