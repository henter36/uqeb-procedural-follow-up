using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Uqeb.Api.Data;
using Uqeb.Api.DTOs.FollowUpPrint;
using Uqeb.Api.Models.Entities;
using Uqeb.Api.Models.Enums;
using Uqeb.Api.Services;
using Xunit;

namespace Uqeb.Api.Tests.Letters;

public class FollowUpPrintAuditTests
{
    [Fact]
    public async Task CreateJobAsync_LogsJobQueued()
    {
        var audit = new CapturingAuditService();
        await using var db = LettersTestInfrastructure.CreateDb(nameof(CreateJobAsync_LogsJobQueued));
        await SeedEligibleJobDataAsync(db);

        var render = new StubRenderService(new FollowUpLetterTargetEntity("جهة", 1, null));
        var eligibility = new FollowUpPrintEligibilityService(
            db,
            render,
            new FixedTimeZone(new DateTime(2025, 6, 25)),
            LettersTestInfrastructure.CreateOptions());
        var service = FollowUpPrintJobServiceTests.CreateService(db, eligibility, render, audit: audit);

        await service.CreateJobAsync(new CreateFollowUpPrintJobRequest
        {
            Filter = new FollowUpPrintFilterRequest
            {
                DaysSinceLastFollowUp = 10,
                ExcludeRecentlyPrinted = false,
                Page = 1,
                PageSize = 25,
            },
        }, new TestCurrentUser(1));

        Assert.Contains(audit.Entries, e => e.Action == AuditAction.FollowUpPrintJobQueued);
    }

    [Fact]
    public async Task CancelJobAsync_LogsJobCancelled()
    {
        var audit = new CapturingAuditService();
        await using var db = LettersTestInfrastructure.CreateDb(nameof(CancelJobAsync_LogsJobCancelled));
        await LettersTestInfrastructure.SeedUserAsync(db);
        db.FollowUpPrintJobs.Add(new FollowUpPrintJob
        {
            RequestedById = 1,
            Status = FollowUpPrintJobStatus.Queued,
            FilterSnapshotJson = "{}",
            TemplateId = 1,
            BatchSize = 25,
            CreatedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();

        var service = FollowUpPrintJobServiceTests.CreateService(
            db,
            new FollowUpPrintEligibilityService(db, new StubRenderService(), new FixedTimeZone(DateTime.UtcNow), LettersTestInfrastructure.CreateOptions()),
            new StubRenderService(),
            audit: audit);

        await service.CancelJobAsync(1, new TestCurrentUser(1));

        Assert.Contains(audit.Entries, e => e.Action == AuditAction.FollowUpPrintJobCancelled);
    }

    [Fact]
    public async Task RetryJobAsync_LogsJobRetryRequested()
    {
        var audit = new CapturingAuditService();
        await using var db = LettersTestInfrastructure.CreateDb(nameof(RetryJobAsync_LogsJobRetryRequested));
        await LettersTestInfrastructure.SeedUserAsync(db);
        db.FollowUpPrintJobs.Add(new FollowUpPrintJob
        {
            Id = 1,
            RequestedById = 1,
            Status = FollowUpPrintJobStatus.Failed,
            FilterSnapshotJson = "{}",
            TemplateId = 1,
            BatchSize = 25,
            CreatedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();

        var service = FollowUpPrintJobServiceTests.CreateService(
            db,
            new FollowUpPrintEligibilityService(db, new StubRenderService(), new FixedTimeZone(DateTime.UtcNow), LettersTestInfrastructure.CreateOptions()),
            new StubRenderService(),
            audit: audit);

        await service.RetryJobAsync(1, new TestCurrentUser(1));

        Assert.Contains(audit.Entries, e => e.Action == AuditAction.FollowUpPrintJobRetryRequested);
    }

    [Fact]
    public async Task MarkPartPrintRequestedAsync_LogsPartPrintRequestedAndJobCompleted()
    {
        var audit = new CapturingAuditService();
        await using var db = LettersTestInfrastructure.CreateDb(nameof(MarkPartPrintRequestedAsync_LogsPartPrintRequestedAndJobCompleted));
        var (job, part) = await FollowUpPrintJobServiceTests.SeedReadyPartAsync(db);

        var service = FollowUpPrintJobServiceTests.CreateService(
            db,
            new FollowUpPrintEligibilityService(db, new StubRenderService(), new FixedTimeZone(DateTime.UtcNow), LettersTestInfrastructure.CreateOptions()),
            new StubRenderService(),
            audit: audit);

        await service.MarkPartPrintRequestedAsync(job.Id, part.PartNumber, new TestCurrentUser(1));

        Assert.Contains(audit.Entries, e => e.Action == AuditAction.FollowUpLetterPrintRequested);
        Assert.Contains(audit.Entries, e => e.Action == AuditAction.FollowUpPrintJobCompleted);
    }

    [Fact]
    public async Task Processor_LogsLeaseRecovered_WhenReclaimingExpiredLease()
    {
        var audit = new CapturingAuditService();
        await FollowUpPrintAuditWriter.LogJobLeaseRecoveredAsync(audit, 1, 99, "leaseOwner=stale");

        var entry = Assert.Single(audit.Entries);
        Assert.Equal(AuditAction.FollowUpPrintJobLeaseRecovered, entry.Action);
        Assert.Contains(FollowUpPrintAuditEvents.JobLeaseRecovered, entry.NewValue, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Processor_LogsJobStartedAndJobReady_ViaAuditWriter()
    {
        var audit = new CapturingAuditService();
        await FollowUpPrintAuditWriter.LogJobStartedAsync(audit, 1, 7);
        await FollowUpPrintAuditWriter.LogJobReadyAsync(audit, 1, 7, "parts=1");

        Assert.Contains(audit.Entries, e => e.Action == AuditAction.FollowUpPrintJobStarted);
        Assert.Contains(audit.Entries, e => e.Action == AuditAction.FollowUpPrintJobReady);
    }

    [Fact]
    public async Task PrintRecordService_LogsConfirmCancelLinkAndReprint()
    {
        var audit = new CapturingAuditService();
        await using var db = LettersTestInfrastructure.CreateDb(nameof(PrintRecordService_LogsConfirmCancelLinkAndReprint));
        var (recordId, followUpId) = await SeedPrintRecordAsync(db);

        var service = new FollowUpLetterPrintRecordService(
            db,
            new FixedTimeZone(new DateTime(2025, 6, 25)),
            new StubRenderService(),
            LettersTestInfrastructure.CreateOptions(),
            audit,
            NullLogger<FollowUpLetterPrintRecordService>.Instance);

        await service.ConfirmPrintAsync(recordId, new TestCurrentUser(1));
        await service.LinkToFollowUpAsync(recordId, followUpId, new TestCurrentUser(1));
        await service.ReprintAsync(recordId, new TestCurrentUser(1));

        var cancelRecord = new FollowUpLetterPrintRecord
        {
            TransactionId = 1,
            TemplateId = 1,
            FollowUpSequence = 1,
            PrintRequestedAt = DateTime.UtcNow,
            PrintRequestedById = 1,
            CreatedAt = DateTime.UtcNow,
        };
        db.FollowUpLetterPrintRecords.Add(cancelRecord);
        await db.SaveChangesAsync();
        await service.CancelRecordAsync(cancelRecord.Id, "test", new TestCurrentUser(1));

        Assert.Contains(audit.Entries, e => e.Action == AuditAction.FollowUpLetterPrintConfirmed);
        Assert.Contains(audit.Entries, e => e.Action == AuditAction.FollowUpPrintLinkedToRegisteredFollowUp);
        Assert.Contains(audit.Entries, e => e.Action == AuditAction.FollowUpLetterReprinted);
        Assert.Contains(audit.Entries, e => e.Action == AuditAction.FollowUpLetterPrintCancelled);
    }

    [Theory]
    [InlineData(AuditAction.FollowUpPrintJobQueued, FollowUpPrintAuditEvents.JobQueued)]
    [InlineData(AuditAction.FollowUpPrintJobStarted, FollowUpPrintAuditEvents.JobStarted)]
    [InlineData(AuditAction.FollowUpPrintJobReady, FollowUpPrintAuditEvents.JobReady)]
    [InlineData(AuditAction.FollowUpPrintJobCompleted, FollowUpPrintAuditEvents.JobCompleted)]
    [InlineData(AuditAction.FollowUpPrintJobFailed, FollowUpPrintAuditEvents.JobFailed)]
    [InlineData(AuditAction.FollowUpPrintJobLeaseRecovered, FollowUpPrintAuditEvents.JobLeaseRecovered)]
    [InlineData(AuditAction.FollowUpPrintJobRetryRequested, FollowUpPrintAuditEvents.JobRetryRequested)]
    public async Task AuditWriter_UsesExpectedResourceTypeAndEvent(AuditAction action, string eventName)
    {
        var audit = new CapturingAuditService();
        var jobId = 42;

        switch (action)
        {
            case AuditAction.FollowUpPrintJobQueued:
                await FollowUpPrintAuditWriter.LogJobQueuedAsync(audit, 1, jobId);
                break;
            case AuditAction.FollowUpPrintJobStarted:
                await FollowUpPrintAuditWriter.LogJobStartedAsync(audit, 1, jobId);
                break;
            case AuditAction.FollowUpPrintJobReady:
                await FollowUpPrintAuditWriter.LogJobReadyAsync(audit, 1, jobId);
                break;
            case AuditAction.FollowUpPrintJobCompleted:
                await FollowUpPrintAuditWriter.LogJobCompletedAsync(audit, 1, jobId);
                break;
            case AuditAction.FollowUpPrintJobFailed:
                await FollowUpPrintAuditWriter.LogJobFailedAsync(audit, 1, jobId);
                break;
            case AuditAction.FollowUpPrintJobLeaseRecovered:
                await FollowUpPrintAuditWriter.LogJobLeaseRecoveredAsync(audit, 1, jobId);
                break;
            case AuditAction.FollowUpPrintJobRetryRequested:
                await FollowUpPrintAuditWriter.LogJobRetryRequestedAsync(audit, 1, jobId);
                break;
        }

        var entry = Assert.Single(audit.Entries);
        Assert.Equal(action, entry.Action);
        Assert.Equal(FollowUpPrintAuditWriter.JobResourceType, entry.EntityName);
        Assert.Equal(jobId, entry.EntityId);
        Assert.Contains(eventName, entry.NewValue, StringComparison.Ordinal);
    }

    private static async Task SeedEligibleJobDataAsync(AppDbContext db)
    {
        await LettersTestInfrastructure.SeedUserAsync(db);
        await LettersTestInfrastructure.SeedTemplateAsync(db);

        db.Departments.Add(new Department { Id = 1, Name = "المالية", NameNormalized = "المالية", IsActive = true });
        db.Transactions.Add(new Transaction
        {
            Id = 1,
            InternalTrackingNumber = "INT-1",
            IncomingNumber = "IN-1",
            IncomingDate = new DateTime(2025, 5, 1),
            Subject = "اختبار",
            Status = TransactionStatus.InProgress,
            Priority = Priority.Normal,
            CreatedAt = DateTime.UtcNow,
        });
        db.TransactionOutgoingDepartments.Add(new TransactionOutgoingDepartment { TransactionId = 1, DepartmentId = 1 });
        db.FollowUps.Add(new FollowUp
        {
            TransactionId = 1,
            FollowUpDate = new DateTime(2025, 6, 10),
            CreatedById = 1,
            CreatedAt = new DateTime(2025, 6, 10),
        });
        await db.SaveChangesAsync();
    }

    private static async Task<(int RecordId, int FollowUpId)> SeedPrintRecordAsync(AppDbContext db)
    {
        await LettersTestInfrastructure.SeedUserAsync(db);
        db.Transactions.Add(new Transaction
        {
            Id = 1,
            InternalTrackingNumber = "INT-1",
            IncomingNumber = "IN-1",
            IncomingDate = DateTime.UtcNow,
            Subject = "اختبار",
            Status = TransactionStatus.InProgress,
            Priority = Priority.Normal,
            CreatedAt = DateTime.UtcNow,
        });
        var followUp = new FollowUp
        {
            TransactionId = 1,
            FollowUpDate = DateTime.UtcNow,
            CreatedById = 1,
            CreatedAt = DateTime.UtcNow,
        };
        db.FollowUps.Add(followUp);
        var record = new FollowUpLetterPrintRecord
        {
            TransactionId = 1,
            TemplateId = 1,
            FollowUpSequence = 1,
            PrintRequestedAt = DateTime.UtcNow,
            PrintRequestedById = 1,
            CreatedAt = DateTime.UtcNow,
        };
        db.FollowUpLetterPrintRecords.Add(record);
        await db.SaveChangesAsync();
        return (record.Id, followUp.Id);
    }
}
