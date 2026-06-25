using Microsoft.EntityFrameworkCore;
using Uqeb.Api.Data;
using Uqeb.Api.Models.Entities;
using Uqeb.Api.Models.Enums;
using Uqeb.Api.Services;
using Xunit;

namespace Uqeb.Api.Tests.Letters;

public class FollowUpLetterPrintRecordServiceTests
{
    private static readonly DateTime Today = new(2025, 6, 25);

    private static FollowUpLetterPrintRecordService CreateService(AppDbContext db, DateTime today) =>
        new(db, new FixedTimeZone(today), LettersTestInfrastructure.CreateOptions(), new NoOpAuditService());

    private static async Task<(Transaction Transaction, FollowUp FollowUp)> SeedTransactionWithFollowUpAsync(AppDbContext db)
    {
        await LettersTestInfrastructure.SeedUserAsync(db);
        await LettersTestInfrastructure.SeedTemplateAsync(db);

        var transaction = new Transaction
        {
            Id = 1,
            InternalTrackingNumber = "INT-1",
            IncomingNumber = "IN-1",
            IncomingDate = Today.AddDays(-30),
            Subject = "معاملة",
            Status = TransactionStatus.InProgress,
            Priority = Priority.Normal,
            CreatedById = 1,
            CreatedAt = DateTime.UtcNow,
        };
        db.Transactions.Add(transaction);

        var followUp = new FollowUp
        {
            Id = 10,
            TransactionId = transaction.Id,
            FollowUpDate = Today.AddDays(-5),
            CreatedById = 1,
            CreatedAt = Today.AddDays(-5),
        };
        db.FollowUps.Add(followUp);
        await db.SaveChangesAsync();

        return (transaction, followUp);
    }

    private static FollowUpLetterPrintRecord CreatePendingRecord(
        int transactionId,
        DateTime printRequestedAt,
        int recordId = 0) =>
        new()
        {
            Id = recordId,
            TransactionId = transactionId,
            TemplateId = 1,
            FollowUpSequence = 1,
            PrintRequestedAt = printRequestedAt,
            PrintRequestedById = 1,
            CreatedAt = printRequestedAt,
        };

    [Fact]
    public async Task GetPendingSummaryAsync_CountsWithinAndOlderThanExclusionWindow()
    {
        await using var db = LettersTestInfrastructure.CreateDb(nameof(GetPendingSummaryAsync_CountsWithinAndOlderThanExclusionWindow));
        var service = CreateService(db, Today);
        await LettersTestInfrastructure.SeedUserAsync(db);
        await LettersTestInfrastructure.SeedTemplateAsync(db);

        db.Transactions.Add(new Transaction
        {
            Id = 1,
            InternalTrackingNumber = "INT-1",
            IncomingNumber = "IN-1",
            IncomingDate = Today.AddDays(-30),
            Subject = "معاملة",
            Status = TransactionStatus.InProgress,
            Priority = Priority.Normal,
            CreatedById = 1,
            CreatedAt = DateTime.UtcNow,
        });
        db.FollowUpLetterPrintRecords.AddRange(
            CreatePendingRecord(1, Today.AddDays(-2)),
            CreatePendingRecord(1, Today.AddDays(-5)),
            CreatePendingRecord(1, Today.AddDays(-10)));
        await db.SaveChangesAsync();

        var summary = await service.GetPendingSummaryAsync();

        Assert.Equal(3, summary.Total);
        Assert.Equal(2, summary.WithinExclusionDays);
        Assert.Equal(1, summary.OlderThanExclusionDays);
    }

    [Fact]
    public async Task LinkToFollowUpAsync_LinksPendingRecordToMatchingFollowUp()
    {
        await using var db = LettersTestInfrastructure.CreateDb(nameof(LinkToFollowUpAsync_LinksPendingRecordToMatchingFollowUp));
        var service = CreateService(db, Today);
        var (transaction, followUp) = await SeedTransactionWithFollowUpAsync(db);

        db.FollowUpLetterPrintRecords.Add(CreatePendingRecord(transaction.Id, Today.AddDays(-1)));
        await db.SaveChangesAsync();

        var recordId = await db.FollowUpLetterPrintRecords.Select(r => r.Id).SingleAsync();
        var user = new TestCurrentUser(1);

        var linked = await service.LinkToFollowUpAsync(recordId, followUp.Id, user);

        Assert.NotNull(linked);
        Assert.Equal(followUp.Id, linked!.RegisteredFollowUpId);

        var stored = await db.FollowUpLetterPrintRecords.SingleAsync();
        Assert.Equal(followUp.Id, stored.RegisteredFollowUpId);
        Assert.NotNull(stored.RegisteredAt);
    }

    [Fact]
    public async Task LinkToFollowUpAsync_RejectsFollowUpFromDifferentTransaction()
    {
        await using var db = LettersTestInfrastructure.CreateDb(nameof(LinkToFollowUpAsync_RejectsFollowUpFromDifferentTransaction));
        var service = CreateService(db, Today);
        await SeedTransactionWithFollowUpAsync(db);

        db.Transactions.Add(new Transaction
        {
            Id = 2,
            InternalTrackingNumber = "INT-2",
            IncomingNumber = "IN-2",
            IncomingDate = Today.AddDays(-20),
            Subject = "أخرى",
            Status = TransactionStatus.InProgress,
            Priority = Priority.Normal,
            CreatedById = 1,
            CreatedAt = DateTime.UtcNow,
        });
        db.FollowUps.Add(new FollowUp
        {
            Id = 20,
            TransactionId = 2,
            FollowUpDate = Today.AddDays(-3),
            CreatedById = 1,
            CreatedAt = Today.AddDays(-3),
        });
        db.FollowUpLetterPrintRecords.Add(CreatePendingRecord(transactionId: 1, printRequestedAt: Today.AddDays(-1)));
        await db.SaveChangesAsync();

        var recordId = await db.FollowUpLetterPrintRecords.Select(r => r.Id).SingleAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.LinkToFollowUpAsync(recordId, followUpId: 20, new TestCurrentUser(1)));
    }

    [Fact]
    public async Task GetPendingListAsync_ReturnsOnlyUnlinkedNonCancelledRecords()
    {
        await using var db = LettersTestInfrastructure.CreateDb(nameof(GetPendingListAsync_ReturnsOnlyUnlinkedNonCancelledRecords));
        var service = CreateService(db, Today);
        await LettersTestInfrastructure.SeedUserAsync(db);
        await LettersTestInfrastructure.SeedTemplateAsync(db);

        db.Transactions.Add(new Transaction
        {
            Id = 1,
            InternalTrackingNumber = "INT-1",
            IncomingNumber = "IN-1",
            IncomingDate = Today.AddDays(-30),
            Subject = "معاملة",
            Status = TransactionStatus.InProgress,
            Priority = Priority.Normal,
            CreatedById = 1,
            CreatedAt = DateTime.UtcNow,
        });

        db.FollowUpLetterPrintRecords.AddRange(
            CreatePendingRecord(1, Today.AddDays(-1)),
            new FollowUpLetterPrintRecord
            {
                TransactionId = 1,
                TemplateId = 1,
                FollowUpSequence = 1,
                PrintRequestedAt = Today.AddDays(-2),
                PrintRequestedById = 1,
                RegisteredFollowUpId = 99,
                CreatedAt = Today.AddDays(-2),
            },
            new FollowUpLetterPrintRecord
            {
                TransactionId = 1,
                TemplateId = 1,
                FollowUpSequence = 1,
                PrintRequestedAt = Today.AddDays(-3),
                PrintRequestedById = 1,
                IsCancelled = true,
                CreatedAt = Today.AddDays(-3),
            });
        await db.SaveChangesAsync();

        var pending = await service.GetPendingListAsync();

        Assert.Single(pending);
        Assert.False(pending[0].IsCancelled);
        Assert.Null(pending[0].RegisteredFollowUpId);
    }
}
