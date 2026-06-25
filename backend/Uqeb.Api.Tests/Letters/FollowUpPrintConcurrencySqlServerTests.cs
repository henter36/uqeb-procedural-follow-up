using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Uqeb.Api.Data;
using Uqeb.Api.HostedServices;
using Uqeb.Api.Models.Entities;
using Uqeb.Api.Models.Enums;
using Uqeb.Api.Models.Letters;
using Uqeb.Api.Services;
using Xunit;

namespace Uqeb.Api.Tests.Letters;

public class FollowUpPrintConcurrencySqlServerTests : FollowUpPrintSqlServerTestBase
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
}
