using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Uqeb.Api.Configuration;
using Uqeb.Api.Data;
using Uqeb.Api.DTOs.FollowUpPrint;
using Uqeb.Api.Models.Entities;
using Uqeb.Api.Models.Enums;
using Uqeb.Api.Models.Letters;
using Uqeb.Api.Services;
using Xunit;

namespace Uqeb.Api.Tests.Letters;

public class FollowUpPrintJobServiceTests
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    internal static FollowUpPrintJobService CreateService(
        AppDbContext db,
        FollowUpPrintEligibilityService eligibility,
        StubRenderService renderService,
        IFollowUpPrintAccessService? access = null,
        IAuditService? audit = null,
        FollowUpLettersOptions? options = null) =>
        new(
            db,
            eligibility,
            renderService,
            access ?? new FollowUpPrintAccessService(db),
            audit ?? new NoOpAuditService(),
            LettersTestInfrastructure.CreateOptions(options));

    internal static async Task<(FollowUpPrintJob Job, FollowUpPrintJobPart Part)> SeedReadyPartAsync(AppDbContext db)
    {
        await LettersTestInfrastructure.SeedUserAsync(db);
        var template = await LettersTestInfrastructure.SeedTemplateAsync(db);

        var document = new FollowUpLetterDocumentModel { TransactionId = 1, Recipient = "جهة" };
        var payloadEntity = new FollowUpPrintJobPayload
        {
            TransactionId = 1,
            TargetDepartmentId = 1,
            TargetEntityName = "جهة",
            FollowUpSequence = 1,
            ResponseDeadlineDays = 7,
            PayloadOrdinal = 1,
            SnapshotJson = JsonSerializer.Serialize(document, JsonOptions),
            Status = FollowUpPrintJobPayloadStatus.Assigned,
        };

        var letterPayload = new FollowUpPrintJobLetterPayload
        {
            TransactionId = 1,
            TargetDepartmentId = 1,
            TargetEntityName = "جهة",
            FollowUpSequence = 1,
            ResponseDeadlineDays = 7,
        };

        var job = new FollowUpPrintJob
        {
            RequestedById = 1,
            Status = FollowUpPrintJobStatus.ReadyToPrint,
            FilterSnapshotJson = "{}",
            TemplateId = template.Id,
            ResponseDeadlineDays = 7,
            BatchSize = 25,
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
            PayloadJson = JsonSerializer.Serialize(new List<FollowUpPrintJobLetterPayload> { letterPayload }, JsonOptions),
            CreatedAt = DateTime.UtcNow,
        };
        db.FollowUpPrintJobParts.Add(part);
        await db.SaveChangesAsync();

        payloadEntity.JobId = job.Id;
        payloadEntity.PartId = part.Id;
        db.FollowUpPrintJobPayloads.Add(payloadEntity);

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
    public async Task CreateJobAsync_AllowsNewJob_WhenExistingJobIsReadyToPrint()
    {
        var today = new DateTime(2025, 6, 25);
        await using var db = LettersTestInfrastructure.CreateDb(nameof(CreateJobAsync_AllowsNewJob_WhenExistingJobIsReadyToPrint));
        await LettersTestInfrastructure.SeedUserAsync(db);
        await LettersTestInfrastructure.SeedTemplateAsync(db);

        db.FollowUpPrintJobs.Add(new FollowUpPrintJob
        {
            RequestedById = 1,
            Status = FollowUpPrintJobStatus.ReadyToPrint,
            FilterSnapshotJson = "{}",
            TemplateId = 1,
            BatchSize = 25,
            TotalTransactions = 1,
            TotalLetters = 1,
            CreatedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();

        db.Transactions.Add(new Transaction
        {
            Id = 10,
            InternalTrackingNumber = "INT-10",
            IncomingNumber = "IN-10",
            IncomingDate = today.AddDays(-40),
            Subject = "معاملة",
            Status = TransactionStatus.InProgress,
            Priority = Priority.Normal,
            CreatedAt = DateTime.UtcNow,
        });
        db.FollowUps.Add(new FollowUp
        {
            TransactionId = 10,
            FollowUpDate = today.AddDays(-15),
            CreatedById = 1,
            CreatedAt = today.AddDays(-15),
        });
        await db.SaveChangesAsync();

        var render = new StubRenderService(new FollowUpLetterTargetEntity("جهة", 1, null));
        var options = new FollowUpLettersOptions
        {
            MaxConcurrentPrintJobs = 1,
            MaxConcurrentJobsPerUser = 1,
        };
        var eligibility = new FollowUpPrintEligibilityService(
            db,
            render,
            new FixedTimeZone(today),
            LettersTestInfrastructure.CreateOptions(options));
        var service = CreateService(db, eligibility, render, options: options);

        var job = await service.CreateJobAsync(
            new CreateFollowUpPrintJobRequest
            {
                Filter = new FollowUpPrintFilterRequest
                {
                    DaysSinceLastFollowUp = 10,
                    ExcludeRecentlyPrinted = false,
                },
            },
            new TestCurrentUser(1));

        Assert.True(job.Id > 0);
        Assert.Equal(FollowUpPrintJobStatus.Queued, job.Status);
    }

    [Fact]
    public async Task CreateJobAsync_BlocksNewProcessingJob_WhenUserHasQueuedJob()
    {
        await using var db = LettersTestInfrastructure.CreateDb(nameof(CreateJobAsync_BlocksNewProcessingJob_WhenUserHasQueuedJob));
        await LettersTestInfrastructure.SeedUserAsync(db);
        await LettersTestInfrastructure.SeedTemplateAsync(db);

        db.FollowUpPrintJobs.Add(new FollowUpPrintJob
        {
            RequestedById = 1,
            Status = FollowUpPrintJobStatus.Queued,
            FilterSnapshotJson = "{}",
            TemplateId = 1,
            BatchSize = 25,
            TotalTransactions = 1,
            TotalLetters = 1,
            CreatedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();

        var today = new DateTime(2025, 6, 25);
        db.Transactions.Add(new Transaction
        {
            Id = 11,
            InternalTrackingNumber = "INT-11",
            IncomingNumber = "IN-11",
            IncomingDate = today.AddDays(-40),
            Subject = "معاملة",
            Status = TransactionStatus.InProgress,
            Priority = Priority.Normal,
            CreatedAt = DateTime.UtcNow,
        });
        db.FollowUps.Add(new FollowUp
        {
            TransactionId = 11,
            FollowUpDate = today.AddDays(-15),
            CreatedById = 1,
            CreatedAt = today.AddDays(-15),
        });
        await db.SaveChangesAsync();

        var render = new StubRenderService(new FollowUpLetterTargetEntity("جهة", 1, null));
        var options = new FollowUpLettersOptions { MaxConcurrentJobsPerUser = 1 };
        var eligibility = new FollowUpPrintEligibilityService(
            db,
            render,
            new FixedTimeZone(today),
            LettersTestInfrastructure.CreateOptions(options));
        var service = CreateService(db, eligibility, render, options: options);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.CreateJobAsync(
                new CreateFollowUpPrintJobRequest
                {
                    Filter = new FollowUpPrintFilterRequest
                    {
                        DaysSinceLastFollowUp = 10,
                        ExcludeRecentlyPrinted = false,
                    },
                },
                new TestCurrentUser(1)));
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

        Assert.True(preview.EligibleTransactionCount > 0);
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

    [Fact]
    public async Task MarkPartPrintRequestedAsync_IsIdempotentWhenAlreadyPrinted()
    {
        await using var db = LettersTestInfrastructure.CreateDb(nameof(MarkPartPrintRequestedAsync_IsIdempotentWhenAlreadyPrinted));
        var today = new DateTime(2025, 6, 25);
        var eligibility = new FollowUpPrintEligibilityService(
            db,
            new StubRenderService(),
            new FixedTimeZone(today),
            LettersTestInfrastructure.CreateOptions());
        var service = CreateService(db, eligibility, new StubRenderService());
        var (job, part) = await SeedReadyPartAsync(db);
        var user = new TestCurrentUser(1);

        var first = await service.MarkPartPrintRequestedAsync(job.Id, part.PartNumber, user);
        var second = await service.MarkPartPrintRequestedAsync(job.Id, part.PartNumber, user);

        Assert.Equal(first.Count, second.Count);
        Assert.Equal(1, await db.FollowUpLetterPrintRecords.CountAsync());
    }

    [Fact]
    public async Task GetPartPrintViewHtmlAsync_UsesStoredSnapshots()
    {
        await using var db = LettersTestInfrastructure.CreateDb(nameof(GetPartPrintViewHtmlAsync_UsesStoredSnapshots));
        var today = new DateTime(2025, 6, 25);
        var render = new TrackingRenderService();
        var eligibility = new FollowUpPrintEligibilityService(
            db,
            render,
            new FixedTimeZone(today),
            LettersTestInfrastructure.CreateOptions());
        var service = CreateService(db, eligibility, render);
        var (job, part) = await SeedReadyPartAsync(db);

        var html = await service.GetPartPrintViewHtmlAsync(job.Id, part.PartNumber, new TestCurrentUser(1));

        Assert.False(string.IsNullOrWhiteSpace(html));
        Assert.Equal(0, render.BuildDocumentCalls);
    }

    private sealed class TrackingRenderService : StubRenderService
    {
        public int BuildDocumentCalls { get; private set; }

        public override Task<FollowUpLetterDocumentModel?> BuildDocumentAsync(FollowUpLetterBuildRequest request)
        {
            BuildDocumentCalls++;
            return base.BuildDocumentAsync(request);
        }
    }

    // ─── Pagination semantics (section 9) ─────────────────────────────────────
    // Job creation captures ALL matching results, not just the current UI page.
    // Changing Page/PageSize must not alter the request hash or payload set.

    [Fact]
    public void RequestHash_PageAndPageSize_DoNotAffectHash()
    {
        var base64Request = new CreateFollowUpPrintJobRequest
        {
            Filter = new FollowUpPrintFilterRequest { DaysSinceLastFollowUp = 10, DepartmentId = 1 },
        };
        var page2Request = new CreateFollowUpPrintJobRequest
        {
            Filter = new FollowUpPrintFilterRequest { DaysSinceLastFollowUp = 10, DepartmentId = 1, Page = 2, PageSize = 50 },
        };
        var page10Request = new CreateFollowUpPrintJobRequest
        {
            Filter = new FollowUpPrintFilterRequest { DaysSinceLastFollowUp = 10, DepartmentId = 1, Page = 10, PageSize = 100 },
        };

        var hash1 = FollowUpPrintRequestHash.Compute(base64Request, 25);
        var hash2 = FollowUpPrintRequestHash.Compute(page2Request, 25);
        var hash3 = FollowUpPrintRequestHash.Compute(page10Request, 25);

        Assert.Equal(hash1, hash2);
        Assert.Equal(hash1, hash3);
    }

    [Fact]
    public void RequestHash_BatchSizeChange_ProducesDifferentHash()
    {
        var request = new CreateFollowUpPrintJobRequest
        {
            Filter = new FollowUpPrintFilterRequest { DaysSinceLastFollowUp = 10 },
        };

        var hash25 = FollowUpPrintRequestHash.Compute(request, 25);
        var hash50 = FollowUpPrintRequestHash.Compute(request, 50);

        Assert.NotEqual(hash25, hash50);
    }
}
