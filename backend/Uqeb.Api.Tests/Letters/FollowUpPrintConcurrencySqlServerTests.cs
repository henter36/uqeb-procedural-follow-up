using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Uqeb.Api.Configuration;
using Uqeb.Api.Data;
using Uqeb.Api.Exceptions;
using Uqeb.Api.HostedServices;
using Uqeb.Api.Models.Entities;
using Uqeb.Api.Models.Enums;
using Uqeb.Api.Models.Letters;
using Uqeb.Api.Services;
using Xunit;

namespace Uqeb.Api.Tests.Letters;

[Trait("Category", "SqlServer")]
public class FollowUpPrintConcurrencySqlServerTests
    : FollowUpPrintSqlServerTestBase
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    [Fact]
    public async Task TwoWorkersClaimSameJob_DoNotCreateDuplicateParts()
    {
        if (!ShouldRunSqlServerTest())
            return;

        var context = await CreateMigratedContextAsync();
        try
        {
            await using var scope = context.Provider.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var user = new User
            {
                Username = $"worker-user-{Guid.NewGuid():N}",
                PasswordHash = "hash",
                FullName = "Worker",
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

            var job = new FollowUpPrintJob
            {
                RequestedById = user.Id,
                Status = FollowUpPrintJobStatus.Queued,
                FilterSnapshotJson = "{}",
                TemplateId = template.Id,
                BatchSize = 5,
                TotalTransactions = 4,
                TotalLetters = 4,
                CreatedAt = DateTime.UtcNow,
            };
            db.FollowUpPrintJobs.Add(job);
            await db.SaveChangesAsync();

            for (var i = 1; i <= 4; i++)
            {
                db.FollowUpPrintJobPayloads.Add(new FollowUpPrintJobPayload
                {
                    JobId = job.Id,
                    PayloadOrdinal = i,
                    TransactionId = i,
                    TargetDepartmentId = 1,
                    TargetEntityName = $"جهة {i}",
                    FollowUpSequence = 1,
                    SnapshotJson = JsonSerializer.Serialize(
                        new FollowUpLetterDocumentModel { TransactionId = i, Recipient = "جهة", Body = "test" },
                        JsonOptions),
                    Status = FollowUpPrintJobPayloadStatus.Pending,
                });
            }

            await db.SaveChangesAsync();

            var workerTasks = Enumerable.Range(0, 2)
                .Select(_ => Task.Run(async () =>
                {
                    await using var workerScope = context.Provider.CreateAsyncScope();
                    var processor = workerScope.ServiceProvider.GetRequiredService<FollowUpPrintJobProcessorHostedService>();
                    await processor.ProcessOnceAsync(CancellationToken.None);
                }))
                .ToArray();

            await Task.WhenAll(workerTasks);

            await using var verifyScope = context.Provider.CreateAsyncScope();
            var verifyDb = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();

            var partCount = await verifyDb.FollowUpPrintJobParts.CountAsync(p => p.JobId == job.Id);
            var assignedPayloads = await verifyDb.FollowUpPrintJobPayloads.CountAsync(p => p.JobId == job.Id && p.PartId != null);

            Assert.Equal(1, partCount);
            Assert.Equal(4, assignedPayloads);
        }
        finally
        {
            await CleanupAsync(context);
        }
    }

    [Fact]
    public async Task ProcessingJob_WithExpiredLease_IsRecoveredBySecondWorker_WithoutDuplicateParts()
    {
        if (!ShouldRunSqlServerTest())
            return;

        var context = await CreateMigratedContextAsync();
        try
        {
            await using var setupScope = context.Provider.CreateAsyncScope();
            var db = setupScope.ServiceProvider.GetRequiredService<AppDbContext>();

            var user = new User
            {
                Username = $"recovery-user-{Guid.NewGuid():N}",
                PasswordHash = "h",
                FullName = "Recovery",
                Role = UserRole.Admin,
                IsActive = true,
            };
            db.Users.Add(user);

            var template = new LetterTemplate
            {
                Code = $"recovery-tpl-{Guid.NewGuid():N}",
                Name = "Recovery",
                TemplateType = LetterTemplateType.FollowUp,
                Content = FollowUpLetterRenderService.DefaultFollowUpContent,
                IsActive = true,
                IsDefault = true,
            };
            db.LetterTemplates.Add(template);
            await db.SaveChangesAsync();

            // Job stuck in Processing with an expired lease (simulates worker crash).
            var staleOwner = "stale-worker:" + Guid.NewGuid().ToString("N");
            var job = new FollowUpPrintJob
            {
                RequestedById = user.Id,
                Status = FollowUpPrintJobStatus.Processing,
                FilterSnapshotJson = "{}",
                TemplateId = template.Id,
                BatchSize = 25,
                TotalTransactions = 2,
                TotalLetters = 2,
                LeaseOwner = staleOwner,
                LeaseExpiresAt = DateTime.UtcNow.AddSeconds(-60),
                StartedAt = DateTime.UtcNow.AddMinutes(-5),
                CreatedAt = DateTime.UtcNow.AddMinutes(-5),
            };
            db.FollowUpPrintJobs.Add(job);
            await db.SaveChangesAsync();

            for (var i = 1; i <= 2; i++)
            {
                db.FollowUpPrintJobPayloads.Add(new FollowUpPrintJobPayload
                {
                    JobId = job.Id,
                    PayloadOrdinal = i,
                    TransactionId = i,
                    TargetDepartmentId = i,
                    TargetEntityId = null,
                    TargetEntityName = $"إدارة {i}",
                    FollowUpSequence = 1,
                    SnapshotJson = JsonSerializer.Serialize(
                        new FollowUpLetterDocumentModel { TransactionId = i, Recipient = "جهة", Body = "test" },
                        JsonOptions),
                    Status = FollowUpPrintJobPayloadStatus.Pending,
                });
            }
            await db.SaveChangesAsync();

            // New worker recovers the job.
            await using var workerScope = context.Provider.CreateAsyncScope();
            var processor = workerScope.ServiceProvider.GetRequiredService<FollowUpPrintJobProcessorHostedService>();
            await processor.ProcessOnceAsync(CancellationToken.None);

            await using var verifyScope = context.Provider.CreateAsyncScope();
            var verifyDb = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();

            var finalJob = await verifyDb.FollowUpPrintJobs.FirstAsync(j => j.Id == job.Id);
            var partCount = await verifyDb.FollowUpPrintJobParts.CountAsync(p => p.JobId == job.Id);
            var assignedPayloads = await verifyDb.FollowUpPrintJobPayloads.CountAsync(p => p.JobId == job.Id && p.PartId != null);

            Assert.Equal(FollowUpPrintJobStatus.ReadyToPrint, finalJob.Status);
            Assert.Equal(1, partCount);
            Assert.Equal(2, assignedPayloads);
            Assert.NotEqual(staleOwner, finalJob.LeaseOwner);
        }
        finally
        {
            await CleanupAsync(context);
        }
    }

    [Fact]
    public async Task ProcessingJob_ClaimedState_WithExpiredLease_IsRecoverable()
    {
        // Verifies that a job left in Claimed status (e.g., from old code or unexpected crash)
        // is also picked up by a new worker, because Claimed is in ClaimableStatuses.
        if (!ShouldRunSqlServerTest())
            return;

        var context = await CreateMigratedContextAsync();
        try
        {
            await using var setupScope = context.Provider.CreateAsyncScope();
            var db = setupScope.ServiceProvider.GetRequiredService<AppDbContext>();

            var user = new User { Username = $"claimed-{Guid.NewGuid():N}", PasswordHash = "h", FullName = "U", Role = UserRole.Admin, IsActive = true };
            db.Users.Add(user);
            var template = new LetterTemplate
            {
                Code = $"claimed-tpl-{Guid.NewGuid():N}",
                Name = "T",
                TemplateType = LetterTemplateType.FollowUp,
                Content = FollowUpLetterRenderService.DefaultFollowUpContent,
                IsActive = true,
                IsDefault = true,
            };
            db.LetterTemplates.Add(template);
            await db.SaveChangesAsync();

            var job = new FollowUpPrintJob
            {
                RequestedById = user.Id,
                Status = FollowUpPrintJobStatus.Claimed,
                FilterSnapshotJson = "{}",
                TemplateId = template.Id,
                BatchSize = 25,
                TotalTransactions = 1,
                TotalLetters = 1,
                LeaseOwner = "stale-claimed:" + Guid.NewGuid().ToString("N"),
                LeaseExpiresAt = DateTime.UtcNow.AddSeconds(-30),
                CreatedAt = DateTime.UtcNow.AddMinutes(-2),
            };
            db.FollowUpPrintJobs.Add(job);
            await db.SaveChangesAsync();

            db.FollowUpPrintJobPayloads.Add(new FollowUpPrintJobPayload
            {
                JobId = job.Id,
                PayloadOrdinal = 1,
                TransactionId = 1,
                TargetDepartmentId = 1,
                TargetEntityId = null,
                TargetEntityName = "إدارة",
                FollowUpSequence = 1,
                SnapshotJson = JsonSerializer.Serialize(
                    new FollowUpLetterDocumentModel { TransactionId = 1, Recipient = "جهة", Body = "test" },
                    JsonOptions),
                Status = FollowUpPrintJobPayloadStatus.Pending,
            });
            await db.SaveChangesAsync();

            await using var workerScope = context.Provider.CreateAsyncScope();
            var processor = workerScope.ServiceProvider.GetRequiredService<FollowUpPrintJobProcessorHostedService>();
            await processor.ProcessOnceAsync(CancellationToken.None);

            await using var verifyScope = context.Provider.CreateAsyncScope();
            var verifyDb = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();

            var finalStatus = await verifyDb.FollowUpPrintJobs.Where(j => j.Id == job.Id).Select(j => j.Status).FirstAsync();
            Assert.Equal(FollowUpPrintJobStatus.ReadyToPrint, finalStatus);
        }
        finally
        {
            await CleanupAsync(context);
        }
    }

    [Fact]
    public async Task ConcurrentFollowUpLink_AttachesFollowUpToOnlyOnePrintRecord()
    {
        if (!ShouldRunSqlServerTest())
            return;

        var context = await CreateMigratedContextAsync();
        try
        {
            int recordId1;
            int recordId2;
            int followUpId;

            await using (var setupScope = context.Provider.CreateAsyncScope())
            {
                var db = setupScope.ServiceProvider.GetRequiredService<AppDbContext>();

                var user = new User
                {
                    Username = $"link-user-{Guid.NewGuid():N}",
                    PasswordHash = "hash",
                    FullName = "Link User",
                    Role = UserRole.Admin,
                    IsActive = true,
                };
                db.Users.Add(user);

                var template = new LetterTemplate
                {
                    Code = $"link-tpl-{Guid.NewGuid():N}",
                    Name = "Follow Up",
                    TemplateType = LetterTemplateType.FollowUp,
                    Content = FollowUpLetterRenderService.DefaultFollowUpContent,
                    IsActive = true,
                    IsDefault = true,
                };
                db.LetterTemplates.Add(template);
                await db.SaveChangesAsync();

                var transaction = new Transaction
                {
                    InternalTrackingNumber = $"INT-{Guid.NewGuid():N}",
                    IncomingNumber = $"IN-{Guid.NewGuid():N}",
                    IncomingDate = DateTime.UtcNow.AddDays(-30),
                    Subject = "Concurrent link",
                    Status = TransactionStatus.InProgress,
                    Priority = Priority.Normal,
                    CreatedById = user.Id,
                    CreatedAt = DateTime.UtcNow,
                };
                db.Transactions.Add(transaction);
                await db.SaveChangesAsync();

                var followUp = new FollowUp
                {
                    TransactionId = transaction.Id,
                    FollowUpDate = DateTime.UtcNow.AddDays(-3),
                    CreatedById = user.Id,
                    CreatedAt = DateTime.UtcNow.AddDays(-3),
                };
                db.FollowUps.Add(followUp);

                db.FollowUpLetterPrintRecords.AddRange(
                    new FollowUpLetterPrintRecord
                    {
                        TransactionId = transaction.Id,
                        TemplateId = template.Id,
                        FollowUpSequence = 1,
                        PrintRequestedAt = DateTime.UtcNow.AddMinutes(-2),
                        PrintRequestedById = user.Id,
                        CreatedAt = DateTime.UtcNow.AddMinutes(-2),
                    },
                    new FollowUpLetterPrintRecord
                    {
                        TransactionId = transaction.Id,
                        TemplateId = template.Id,
                        FollowUpSequence = 1,
                        PrintRequestedAt = DateTime.UtcNow.AddMinutes(-1),
                        PrintRequestedById = user.Id,
                        CreatedAt = DateTime.UtcNow.AddMinutes(-1),
                    });
                await db.SaveChangesAsync();

                followUpId = followUp.Id;
                var recordIds = await db.FollowUpLetterPrintRecords
                    .OrderBy(r => r.Id)
                    .Select(r => r.Id)
                    .ToListAsync();
                recordId1 = recordIds[0];
                recordId2 = recordIds[1];
            }

            async Task<Exception?> LinkAsync(int recordId)
            {
                await using var scope = context.Provider.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var service = new FollowUpLetterPrintRecordService(
                    db,
                    new FixedTimeZone(DateTime.UtcNow.Date),
                    new StubRenderService(),
                    Options.Create(new FollowUpLettersOptions()),
                    new NoOpAuditService());

                try
                {
                    _ = await service.LinkToFollowUpAsync(recordId, followUpId, new TestCurrentUser(1));
                    return null;
                }
                catch (Exception ex)
                {
                    return ex;
                }
            }

            var results = await Task.WhenAll(LinkAsync(recordId1), LinkAsync(recordId2));

            Assert.Equal(1, results.Count(ex => ex == null));
            Assert.Single(results.OfType<FollowUpPrintConflictException>());

            await using var verifyScope = context.Provider.CreateAsyncScope();
            var verifyDb = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.Equal(1, await verifyDb.FollowUpLetterPrintRecords.CountAsync(r => r.RegisteredFollowUpId == followUpId));
        }
        finally
        {
            await CleanupAsync(context);
        }
    }
}
