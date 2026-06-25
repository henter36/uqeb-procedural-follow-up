using Microsoft.EntityFrameworkCore;
using Uqeb.Api.Data;
using Uqeb.Api.DTOs.FollowUpPrint;
using Uqeb.Api.Models.Entities;
using Uqeb.Api.Models.Enums;
using Uqeb.Api.Services;
using Xunit;

namespace Uqeb.Api.Tests.Letters;

public class FollowUpPrintEligibilityServiceTests
{
    private static readonly DateTime Today = new(2025, 6, 25);

    private static FollowUpPrintEligibilityService CreateService(
        AppDbContext db,
        DateTime today,
        StubRenderService? renderService = null) =>
        new(
            db,
            renderService ?? new StubRenderService(new FollowUpLetterTargetEntity("جهة", 1, null)),
            new FixedTimeZone(today),
            LettersTestInfrastructure.CreateOptions());

    private static async Task SeedEligibleTransactionAsync(
        AppDbContext db,
        int transactionId,
        DateTime referenceDate,
        DateTime? lastPrintRequestedAt = null,
        string? incomingNumber = null)
    {
        db.Transactions.Add(new Transaction
        {
            Id = transactionId,
            InternalTrackingNumber = $"INT-{transactionId}",
            IncomingNumber = incomingNumber ?? $"IN-{transactionId}",
            IncomingDate = referenceDate.AddDays(-30),
            Subject = $"معاملة {transactionId}",
            Status = TransactionStatus.InProgress,
            Priority = Priority.Normal,
            CreatedAt = DateTime.UtcNow,
        });

        db.FollowUps.Add(new FollowUp
        {
            TransactionId = transactionId,
            FollowUpDate = referenceDate,
            CreatedById = 1,
            CreatedAt = referenceDate,
        });

        if (lastPrintRequestedAt.HasValue)
        {
            db.FollowUpLetterPrintRecords.Add(new FollowUpLetterPrintRecord
            {
                TransactionId = transactionId,
                TemplateId = 1,
                FollowUpSequence = 2,
                PrintRequestedAt = lastPrintRequestedAt.Value,
                PrintRequestedById = 1,
                CreatedAt = lastPrintRequestedAt.Value,
            });
        }

        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task GetEligibleAsync_ExcludesClosedTransactions()
    {
        await using var db = LettersTestInfrastructure.CreateDb(nameof(GetEligibleAsync_ExcludesClosedTransactions));
        var service = CreateService(db, Today);

        db.Transactions.Add(new Transaction
        {
            Id = 1,
            InternalTrackingNumber = "INT-1",
            IncomingNumber = "IN-1",
            IncomingDate = Today.AddDays(-40),
            Subject = "مغلقة",
            Status = TransactionStatus.Closed,
            ClosedAt = Today.AddDays(-1),
            Priority = Priority.Normal,
            CreatedAt = DateTime.UtcNow,
        });
        db.FollowUps.Add(new FollowUp
        {
            TransactionId = 1,
            FollowUpDate = Today.AddDays(-20),
            CreatedById = 1,
            CreatedAt = Today.AddDays(-20),
        });
        await db.SaveChangesAsync();

        var result = await service.GetEligibleAsync(
            new FollowUpPrintFilterRequest
            {
                DaysSinceLastFollowUp = 10,
                ExcludeRecentlyPrinted = false,
                Page = 1,
                PageSize = 25,
            },
            new TestCurrentUser(1));

        Assert.Empty(result.Items);
        Assert.Equal(0, result.TotalCount);
    }

    [Fact]
    public async Task GetEligibleAsync_ExclusionDaysFilter_IsIndependentFromDaysSinceLastFollowUp()
    {
        await using var db = LettersTestInfrastructure.CreateDb(nameof(GetEligibleAsync_ExclusionDaysFilter_IsIndependentFromDaysSinceLastFollowUp));
        var service = CreateService(db, Today);

        var referenceDate = Today.AddDays(-15);
        var printRequestedAt = Today.AddDays(-8);
        await SeedEligibleTransactionAsync(db, transactionId: 1, referenceDate, printRequestedAt);

        var withSevenDayExclusion = await service.GetEligibleAsync(
            new FollowUpPrintFilterRequest
            {
                DaysSinceLastFollowUp = 10,
                ExcludeRecentlyPrinted = true,
                PrintedLetterExclusionDays = 7,
                Page = 1,
                PageSize = 25,
            },
            new TestCurrentUser(1));

        var withTenDayExclusion = await service.GetEligibleAsync(
            new FollowUpPrintFilterRequest
            {
                DaysSinceLastFollowUp = 10,
                ExcludeRecentlyPrinted = true,
                PrintedLetterExclusionDays = 10,
                Page = 1,
                PageSize = 25,
            },
            new TestCurrentUser(1));

        Assert.Single(withSevenDayExclusion.Items);
        Assert.Equal(0, withTenDayExclusion.TotalCount);
        Assert.Empty(withTenDayExclusion.Items);
    }

    [Fact]
    public async Task GetEligibleAsync_PaginatesInDatabase()
    {
        await using var db = LettersTestInfrastructure.CreateDb(nameof(GetEligibleAsync_PaginatesInDatabase));
        var service = CreateService(db, Today);

        await SeedEligibleTransactionAsync(db, 1, Today.AddDays(-20), incomingNumber: "IN-C");
        await SeedEligibleTransactionAsync(db, 2, Today.AddDays(-30), incomingNumber: "IN-A");
        await SeedEligibleTransactionAsync(db, 3, Today.AddDays(-25), incomingNumber: "IN-B");

        var page1 = await service.GetEligibleAsync(
            new FollowUpPrintFilterRequest
            {
                DaysSinceLastFollowUp = 10,
                ExcludeRecentlyPrinted = false,
                Page = 1,
                PageSize = 2,
            },
            new TestCurrentUser(1));

        var page2 = await service.GetEligibleAsync(
            new FollowUpPrintFilterRequest
            {
                DaysSinceLastFollowUp = 10,
                ExcludeRecentlyPrinted = false,
                Page = 2,
                PageSize = 2,
            },
            new TestCurrentUser(1));

        Assert.Equal(3, page1.TotalCount);
        Assert.Equal(2, page1.Items.Count);
        Assert.Equal("IN-A", page1.Items[0].IncomingNumber);
        Assert.Equal("IN-B", page1.Items[1].IncomingNumber);

        Assert.Equal(3, page2.TotalCount);
        Assert.Single(page2.Items);
        Assert.Equal("IN-C", page2.Items[0].IncomingNumber);
    }

    [Fact]
    public async Task PreviewAsync_CountsRecentlyPrintedExclusionsSeparately()
    {
        await using var db = LettersTestInfrastructure.CreateDb(nameof(PreviewAsync_CountsRecentlyPrintedExclusionsSeparately));
        var service = CreateService(db, Today);

        await SeedEligibleTransactionAsync(db, 1, Today.AddDays(-15), Today.AddDays(-2));
        await SeedEligibleTransactionAsync(db, 2, Today.AddDays(-15));

        var preview = await service.PreviewAsync(
            new FollowUpPrintFilterRequest
            {
                DaysSinceLastFollowUp = 10,
                ExcludeRecentlyPrinted = true,
                PrintedLetterExclusionDays = 7,
            },
            batchSize: 25,
            new TestCurrentUser(1));

        Assert.Equal(2, preview.MatchedCount);
        Assert.Equal(1, preview.EligibleTransactionCount);
        Assert.Equal(1, preview.RecentlyPrintedExcludedCount);
    }

    [Fact]
    public async Task PreviewAsync_CountsNotDueYetSeparately()
    {
        await using var db = LettersTestInfrastructure.CreateDb(nameof(PreviewAsync_CountsNotDueYetSeparately));
        var service = CreateService(db, Today);

        await SeedEligibleTransactionAsync(db, 1, Today.AddDays(-5));
        await SeedEligibleTransactionAsync(db, 2, Today.AddDays(-15));

        var preview = await service.PreviewAsync(
            new FollowUpPrintFilterRequest
            {
                DaysSinceLastFollowUp = 10,
                ExcludeRecentlyPrinted = false,
            },
            batchSize: 25,
            new TestCurrentUser(1));

        Assert.Equal(2, preview.MatchedCount);
        Assert.Equal(1, preview.NotDueYetCount);
        Assert.Equal(1, preview.EligibleTransactionCount);
    }

    [Fact]
    public async Task PreviewAsync_CountsNoTargetUsingBulkResolution()
    {
        await using var db = LettersTestInfrastructure.CreateDb(nameof(PreviewAsync_CountsNoTargetUsingBulkResolution));
        var renderService = new PerTransactionRenderService(
            new Dictionary<int, IReadOnlyList<FollowUpLetterTargetEntity>>
            {
                [1] = [new FollowUpLetterTargetEntity("جهة", 1, null)],
                [2] = [],
            });
        var service = CreateService(db, Today, renderService);

        await SeedEligibleTransactionAsync(db, 1, Today.AddDays(-15));
        await SeedEligibleTransactionAsync(db, 2, Today.AddDays(-15));

        var preview = await service.PreviewAsync(
            new FollowUpPrintFilterRequest
            {
                DaysSinceLastFollowUp = 10,
                ExcludeRecentlyPrinted = false,
            },
            batchSize: 25,
            new TestCurrentUser(1));

        Assert.Equal(2, preview.MatchedCount);
        Assert.Equal(1, preview.EligibleTransactionCount);
        Assert.Equal(1, preview.NoTargetCount);
        Assert.Equal(1, preview.EstimatedLetterCount);
    }

    [Fact]
    public async Task BuildEligibleCandidatesWithTargetsAsync_UsesBulkResolution()
    {
        await using var db = LettersTestInfrastructure.CreateDb(nameof(BuildEligibleCandidatesWithTargetsAsync_UsesBulkResolution));
        var renderService = new PerTransactionRenderService(
            new Dictionary<int, IReadOnlyList<FollowUpLetterTargetEntity>>
            {
                [1] = [new FollowUpLetterTargetEntity("جهة 1", 1, null)],
                [2] = [],
            });
        var service = CreateService(db, Today, renderService);

        await SeedEligibleTransactionAsync(db, 1, Today.AddDays(-15));
        await SeedEligibleTransactionAsync(db, 2, Today.AddDays(-15));

        var candidates = await service.BuildEligibleCandidatesWithTargetsAsync(
            new FollowUpPrintFilterRequest
            {
                DaysSinceLastFollowUp = 10,
                ExcludeRecentlyPrinted = false,
            },
            new TestCurrentUser(1));

        Assert.Single(candidates);
        Assert.Equal(1, candidates[0].TransactionId);
        Assert.Equal("جهة 1", candidates[0].Targets[0].Name);
    }

    [Fact]
    public async Task BuildLetterPayloadsAsync_UsesExpectedFollowUpSequenceFromCount()
    {
        await using var db = LettersTestInfrastructure.CreateDb(nameof(BuildLetterPayloadsAsync_UsesExpectedFollowUpSequenceFromCount));
        var service = CreateService(db, Today);

        db.Transactions.Add(new Transaction
        {
            Id = 10,
            InternalTrackingNumber = "INT-10",
            IncomingNumber = "IN-10",
            IncomingDate = Today.AddDays(-40),
            Subject = "متعددة التعقيبات",
            Status = TransactionStatus.InProgress,
            Priority = Priority.Normal,
            CreatedAt = DateTime.UtcNow,
        });
        db.FollowUps.AddRange(
            new FollowUp { TransactionId = 10, FollowUpDate = Today.AddDays(-30), CreatedById = 1, CreatedAt = Today.AddDays(-30) },
            new FollowUp { TransactionId = 10, FollowUpDate = Today.AddDays(-20), CreatedById = 1, CreatedAt = Today.AddDays(-20) });
        await db.SaveChangesAsync();

        var payloads = await service.BuildLetterPayloadsAsync(new FollowUpPrintFilterSnapshot
        {
            DaysSinceLastFollowUp = 10,
            ExcludeRecentlyPrinted = false,
            PrintedLetterExclusionDays = 7,
        });

        Assert.Single(payloads);
        Assert.Equal(3, payloads[0].FollowUpSequence);
    }

    private sealed class PerTransactionRenderService : StubRenderService
    {
        private readonly Dictionary<int, IReadOnlyList<FollowUpLetterTargetEntity>> _targetsByTransaction;

        public PerTransactionRenderService(Dictionary<int, IReadOnlyList<FollowUpLetterTargetEntity>> targetsByTransaction)
            : base(new FollowUpLetterTargetEntity("unused", 1, null))
        {
            _targetsByTransaction = targetsByTransaction;
        }

        public override Task<Dictionary<int, IReadOnlyList<FollowUpLetterTargetEntity>>> ResolveTargetEntitiesBulkAsync(
            IReadOnlyList<int> transactionIds,
            CancellationToken cancellationToken = default)
        {
            var result = transactionIds
                .Distinct()
                .ToDictionary(
                    id => id,
                    id => _targetsByTransaction.TryGetValue(id, out var targets)
                        ? targets
                        : (IReadOnlyList<FollowUpLetterTargetEntity>)[]);
            return Task.FromResult(result);
        }
    }
}
