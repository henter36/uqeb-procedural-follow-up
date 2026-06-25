using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Uqeb.Api.Data;
using Uqeb.Api.DTOs.FollowUpPrint;
using Uqeb.Api.Models.Entities;
using Uqeb.Api.Models.Enums;
using Uqeb.Api.Services;
using Xunit;

namespace Uqeb.Api.Tests.Letters;

public class FollowUpPrintJobServiceTests
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private static FollowUpPrintJobService CreateService(
        AppDbContext db,
        FollowUpPrintEligibilityService eligibility,
        StubRenderService renderService) =>
        new(
            db,
            eligibility,
            renderService,
            LettersTestInfrastructure.CreateOptions());

    private static async Task<(FollowUpPrintJob Job, FollowUpPrintJobPart Part)> SeedReadyPartAsync(AppDbContext db)
    {
        await LettersTestInfrastructure.SeedUserAsync(db);
        var template = await LettersTestInfrastructure.SeedTemplateAsync(db);

        var payloads = new List<FollowUpPrintJobLetterPayload>
        {
            new()
            {
                TransactionId = 1,
                TargetDepartmentId = 1,
                TargetEntityName = "جهة",
                FollowUpSequence = 1,
                ResponseDeadlineDays = 7,
            },
        };

        var job = new FollowUpPrintJob
        {
            RequestedById = 1,
            Status = FollowUpPrintJobStatus.ReadyToPrint,
            FilterSnapshotJson = "{}",
            TemplateId = template.Id,
            ResponseDeadlineDays = 7,
            TotalTransactions = 1,
            TotalLetters = 1,
            ReadyParts = 1,
            TotalParts = 1,
            CreatedAt = DateTime.UtcNow,
        };
        db.FollowUpPrintJobs.Add(job);
        await db.SaveChangesAsync();

        var part = new FollowUpPrintJobPart
        {
            JobId = job.Id,
            PartNumber = 1,
            Status = FollowUpPrintJobPartStatus.ReadyToPrint,
            LetterCount = 1,
            EstimatedPages = 1,
            PayloadJson = JsonSerializer.Serialize(payloads, JsonOptions),
            CreatedAt = DateTime.UtcNow,
        };
        db.FollowUpPrintJobParts.Add(part);
        await db.SaveChangesAsync();

        db.Transactions.Add(new Transaction
        {
            Id = 1,
            InternalTrackingNumber = "INT-1",
            IncomingNumber = "IN-1",
            IncomingDate = DateTime.UtcNow.AddDays(-20),
            Subject = "اختبار",
            Status = TransactionStatus.InProgress,
            Priority = Priority.Normal,
            CreatedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();

        return (job, part);
    }

    [Fact]
    public async Task PreviewJobAsync_DoesNotCreatePrintRecords()
    {
        await using var db = LettersTestInfrastructure.CreateDb(nameof(PreviewJobAsync_DoesNotCreatePrintRecords));
        var today = new DateTime(2025, 6, 25);
        var eligibility = new FollowUpPrintEligibilityService(
            db,
            new StubRenderService(new FollowUpLetterTargetEntity("جهة", 1, null)),
            new FixedTimeZone(today),
            LettersTestInfrastructure.CreateOptions());
        var service = CreateService(db, eligibility, new StubRenderService());

        db.Transactions.Add(new Transaction
        {
            Id = 5,
            InternalTrackingNumber = "INT-5",
            IncomingNumber = "IN-5",
            IncomingDate = today.AddDays(-40),
            Subject = "معاملة",
            Status = TransactionStatus.InProgress,
            Priority = Priority.Normal,
            CreatedAt = DateTime.UtcNow,
        });
        db.FollowUps.Add(new FollowUp
        {
            TransactionId = 5,
            FollowUpDate = today.AddDays(-15),
            CreatedById = 1,
            CreatedAt = today.AddDays(-15),
        });
        await db.SaveChangesAsync();

        var preview = await service.PreviewJobAsync(
            new CreateFollowUpPrintJobRequest
            {
                Filter = new FollowUpPrintFilterRequest
                {
                    DaysSinceLastFollowUp = 10,
                    ExcludeRecentlyPrinted = false,
                },
            },
            new TestCurrentUser(1));

        Assert.True(preview.EligibleCount > 0);
        Assert.Equal(0, await db.FollowUpLetterPrintRecords.CountAsync());
    }

    [Fact]
    public async Task MarkPartPrintRequestedAsync_CreatesPrintRecords()
    {
        await using var db = LettersTestInfrastructure.CreateDb(nameof(MarkPartPrintRequestedAsync_CreatesPrintRecords));
        var today = new DateTime(2025, 6, 25);
        var eligibility = new FollowUpPrintEligibilityService(
            db,
            new StubRenderService(),
            new FixedTimeZone(today),
            LettersTestInfrastructure.CreateOptions());
        var service = CreateService(db, eligibility, new StubRenderService());
        var (job, part) = await SeedReadyPartAsync(db);
        var user = new TestCurrentUser(1);

        Assert.Equal(0, await db.FollowUpLetterPrintRecords.CountAsync());

        var records = await service.MarkPartPrintRequestedAsync(job.Id, part.PartNumber, user);

        Assert.Single(records);
        Assert.Equal(1, records[0].TransactionId);
        Assert.Equal(1, records[0].FollowUpSequence);
        Assert.Equal(1, await db.FollowUpLetterPrintRecords.CountAsync());

        var stored = await db.FollowUpLetterPrintRecords.SingleAsync();
        Assert.Equal(job.Id, stored.BatchJobId);
        Assert.Equal(part.Id, stored.BatchJobPartId);
        Assert.Equal(part.Job.TemplateId, stored.TemplateId);

        var updatedPart = await db.FollowUpPrintJobParts.SingleAsync(p => p.Id == part.Id);
        Assert.Equal(FollowUpPrintJobPartStatus.Printed, updatedPart.Status);
        Assert.NotNull(updatedPart.PrintedAt);
    }
}
