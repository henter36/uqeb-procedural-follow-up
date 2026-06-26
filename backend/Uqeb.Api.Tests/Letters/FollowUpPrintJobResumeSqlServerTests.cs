using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Uqeb.Api.Data;
using Uqeb.Api.DTOs.FollowUpPrint;
using Uqeb.Api.Exceptions;
using Uqeb.Api.HostedServices;
using Uqeb.Api.Models.Entities;
using Uqeb.Api.Models.Enums;
using Uqeb.Api.Models.Letters;
using Uqeb.Api.Services;
using Xunit;

namespace Uqeb.Api.Tests.Letters;

[Trait("Category", "SqlServer")]
public class FollowUpPrintJobResumeSqlServerTests
    : FollowUpPrintSqlServerTestBase
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    [Fact]
    public async Task ProcessorResume_DoesNotDuplicatePayloadAssignments()
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
                Username = $"print-user-{Guid.NewGuid():N}",
                PasswordHash = "hash",
                FullName = "Print User",
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
                BatchSize = 2,
                TotalTransactions = 3,
                TotalLetters = 3,
                CreatedAt = DateTime.UtcNow,
            };
            db.FollowUpPrintJobs.Add(job);
            await db.SaveChangesAsync();

            for (var i = 1; i <= 3; i++)
            {
                var snapshot = JsonSerializer.Serialize(
                    new FollowUpLetterDocumentModel { TransactionId = i, Recipient = "جهة", Body = "test" },
                    JsonOptions);

                db.FollowUpPrintJobPayloads.Add(new FollowUpPrintJobPayload
                {
                    JobId = job.Id,
                    PayloadOrdinal = i,
                    TransactionId = i,
                    TargetDepartmentId = i,
                    TargetEntityId = null,
                    TargetEntityName = $"إدارة {i}",
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

            var assignedCount = await verifyDb.FollowUpPrintJobPayloads.CountAsync(p => p.JobId == job.Id && p.PartId != null);
            var partCount = await verifyDb.FollowUpPrintJobParts.CountAsync(p => p.JobId == job.Id);
            var jobStatus = await verifyDb.FollowUpPrintJobs
                .Where(j => j.Id == job.Id)
                .Select(j => j.Status)
                .FirstAsync();

            Assert.Equal(3, assignedCount);
            Assert.Equal(2, partCount);
            Assert.Equal(FollowUpPrintJobStatus.ReadyToPrint, jobStatus);
            Assert.All(
                await verifyDb.FollowUpPrintJobPayloads.Where(p => p.JobId == job.Id).ToListAsync(),
                p => Assert.NotNull(p.PartId));
        }
        finally
        {
            await CleanupAsync(context);
        }
    }
}
