using Microsoft.EntityFrameworkCore;
using Uqeb.Api.DTOs.FollowUpPrint;
using Uqeb.Api.Exceptions;
using Uqeb.Api.Models.Enums;
using Uqeb.Api.Models.Letters;
using Uqeb.Api.Services;
using Xunit;

namespace Uqeb.Api.Tests.Letters;

/// <summary>
/// Tests for HasValidTargetShape / BuildValidCandidates in FollowUpPrintJobService.
/// A target is valid when exactly one of DepartmentId / ExternalPartyId is a positive integer.
/// Zero is treated as absent. Invalid targets are filtered before any DB write.
/// </summary>
public class FollowUpPrintTargetShapeTests
{
    private static readonly CreateFollowUpPrintJobRequest BaseRequest = new()
    {
        Filter = new FollowUpPrintFilterRequest
        {
            DaysSinceLastFollowUp = 10,
            ExcludeRecentlyPrinted = false,
        },
    };

    // ── valid shapes — should create a Pending payload ───────────────────────

    [Fact]
    public async Task CreateJobAsync_DepartmentTarget_CreatesJobAndPendingPayload()
    {
        await using var db = LettersTestInfrastructure.CreateDb(nameof(CreateJobAsync_DepartmentTarget_CreatesJobAndPendingPayload));
        await LettersTestInfrastructure.SeedUserAsync(db);
        await LettersTestInfrastructure.SeedTemplateAsync(db);

        var eligibility = new FixedCandidateEligibility([
            new EligibleCandidateWithTargets
            {
                TransactionId = 1,
                FollowUpCount = 0,
                Targets = [new FollowUpLetterTargetEntity("إدارة العمليات", DepartmentId: 5, ExternalPartyId: null)],
            },
        ]);
        var service = FollowUpPrintJobServiceTests.CreateService(db, eligibility, new StubRenderService());

        var job = await service.CreateJobAsync(BaseRequest, new TestCurrentUser(1));

        Assert.True(job.Id > 0);
        var payloads = await db.FollowUpPrintJobPayloads.Where(p => p.JobId == job.Id).ToListAsync();
        Assert.Single(payloads);
        Assert.Equal(FollowUpPrintJobPayloadStatus.Pending, payloads[0].Status);
        Assert.Equal(5, payloads[0].TargetDepartmentId);
        Assert.Null(payloads[0].TargetEntityId);
    }

    [Fact]
    public async Task CreateJobAsync_ExternalPartyTarget_CreatesJobAndPendingPayload()
    {
        await using var db = LettersTestInfrastructure.CreateDb(nameof(CreateJobAsync_ExternalPartyTarget_CreatesJobAndPendingPayload));
        await LettersTestInfrastructure.SeedUserAsync(db);
        await LettersTestInfrastructure.SeedTemplateAsync(db);

        var eligibility = new FixedCandidateEligibility([
            new EligibleCandidateWithTargets
            {
                TransactionId = 2,
                FollowUpCount = 1,
                Targets = [new FollowUpLetterTargetEntity("جهة خارجية", DepartmentId: null, ExternalPartyId: 9)],
            },
        ]);
        var service = FollowUpPrintJobServiceTests.CreateService(db, eligibility, new StubRenderService());

        var job = await service.CreateJobAsync(BaseRequest, new TestCurrentUser(1));

        Assert.True(job.Id > 0);
        var payloads = await db.FollowUpPrintJobPayloads.Where(p => p.JobId == job.Id).ToListAsync();
        Assert.Single(payloads);
        Assert.Equal(FollowUpPrintJobPayloadStatus.Pending, payloads[0].Status);
        Assert.Null(payloads[0].TargetDepartmentId);
        Assert.Equal(9, payloads[0].TargetEntityId);
    }

    /// <summary>DepartmentId=0 is treated as absent; ExternalPartyId=5 is positive → valid (entity-only payload).</summary>
    [Fact]
    public async Task CreateJobAsync_DepartmentIdZero_ExternalPartyIdPositive_CreatesEntityOnlyPayload()
    {
        await using var db = LettersTestInfrastructure.CreateDb(nameof(CreateJobAsync_DepartmentIdZero_ExternalPartyIdPositive_CreatesEntityOnlyPayload));
        await LettersTestInfrastructure.SeedUserAsync(db);
        await LettersTestInfrastructure.SeedTemplateAsync(db);

        var eligibility = new FixedCandidateEligibility([
            new EligibleCandidateWithTargets
            {
                TransactionId = 10,
                FollowUpCount = 0,
                Targets = [new FollowUpLetterTargetEntity("جهة", DepartmentId: 0, ExternalPartyId: 5)],
            },
        ]);
        var service = FollowUpPrintJobServiceTests.CreateService(db, eligibility, new StubRenderService());

        var job = await service.CreateJobAsync(BaseRequest, new TestCurrentUser(1));

        var payloads = await db.FollowUpPrintJobPayloads.Where(p => p.JobId == job.Id).ToListAsync();
        Assert.Single(payloads);
        Assert.Null(payloads[0].TargetDepartmentId);   // 0 normalized to null
        Assert.Equal(5, payloads[0].TargetEntityId);
    }

    /// <summary>DepartmentId=5 is positive; ExternalPartyId=0 is treated as absent → valid (dept-only payload).</summary>
    [Fact]
    public async Task CreateJobAsync_DepartmentIdPositive_ExternalPartyIdZero_CreatesDeptOnlyPayload()
    {
        await using var db = LettersTestInfrastructure.CreateDb(nameof(CreateJobAsync_DepartmentIdPositive_ExternalPartyIdZero_CreatesDeptOnlyPayload));
        await LettersTestInfrastructure.SeedUserAsync(db);
        await LettersTestInfrastructure.SeedTemplateAsync(db);

        var eligibility = new FixedCandidateEligibility([
            new EligibleCandidateWithTargets
            {
                TransactionId = 11,
                FollowUpCount = 0,
                Targets = [new FollowUpLetterTargetEntity("إدارة", DepartmentId: 5, ExternalPartyId: 0)],
            },
        ]);
        var service = FollowUpPrintJobServiceTests.CreateService(db, eligibility, new StubRenderService());

        var job = await service.CreateJobAsync(BaseRequest, new TestCurrentUser(1));

        var payloads = await db.FollowUpPrintJobPayloads.Where(p => p.JobId == job.Id).ToListAsync();
        Assert.Single(payloads);
        Assert.Equal(5, payloads[0].TargetDepartmentId);
        Assert.Null(payloads[0].TargetEntityId);       // 0 normalized to null
    }

    // ── invalid shapes — no Job must be created ──────────────────────────────

    [Fact]
    public async Task CreateJobAsync_BothIdsNull_ThrowsValidationException_NoJobCreated()
    {
        await using var db = LettersTestInfrastructure.CreateDb(nameof(CreateJobAsync_BothIdsNull_ThrowsValidationException_NoJobCreated));
        await LettersTestInfrastructure.SeedUserAsync(db);
        await LettersTestInfrastructure.SeedTemplateAsync(db);

        var eligibility = new FixedCandidateEligibility([
            new EligibleCandidateWithTargets
            {
                TransactionId = 20,
                FollowUpCount = 0,
                Targets = [new FollowUpLetterTargetEntity("اسم فقط", DepartmentId: null, ExternalPartyId: null)],
            },
        ]);
        var service = FollowUpPrintJobServiceTests.CreateService(db, eligibility, new StubRenderService());

        await Assert.ThrowsAsync<FollowUpPrintValidationException>(
            () => service.CreateJobAsync(BaseRequest, new TestCurrentUser(1)));

        Assert.Equal(0, await db.FollowUpPrintJobs.CountAsync());
    }

    [Fact]
    public async Task CreateJobAsync_BothIdsSet_ThrowsValidationException_NoJobCreated()
    {
        await using var db = LettersTestInfrastructure.CreateDb(nameof(CreateJobAsync_BothIdsSet_ThrowsValidationException_NoJobCreated));
        await LettersTestInfrastructure.SeedUserAsync(db);
        await LettersTestInfrastructure.SeedTemplateAsync(db);

        var eligibility = new FixedCandidateEligibility([
            new EligibleCandidateWithTargets
            {
                TransactionId = 21,
                FollowUpCount = 0,
                Targets = [new FollowUpLetterTargetEntity("جهة مزدوجة", DepartmentId: 5, ExternalPartyId: 9)],
            },
        ]);
        var service = FollowUpPrintJobServiceTests.CreateService(db, eligibility, new StubRenderService());

        await Assert.ThrowsAsync<FollowUpPrintValidationException>(
            () => service.CreateJobAsync(BaseRequest, new TestCurrentUser(1)));

        Assert.Equal(0, await db.FollowUpPrintJobs.CountAsync());
    }

    [Fact]
    public async Task CreateJobAsync_DepartmentIdZero_ExternalPartyIdNull_ThrowsValidationException_NoJobCreated()
    {
        await using var db = LettersTestInfrastructure.CreateDb(nameof(CreateJobAsync_DepartmentIdZero_ExternalPartyIdNull_ThrowsValidationException_NoJobCreated));
        await LettersTestInfrastructure.SeedUserAsync(db);
        await LettersTestInfrastructure.SeedTemplateAsync(db);

        var eligibility = new FixedCandidateEligibility([
            new EligibleCandidateWithTargets
            {
                TransactionId = 22,
                FollowUpCount = 0,
                Targets = [new FollowUpLetterTargetEntity("إدارة صفر", DepartmentId: 0, ExternalPartyId: null)],
            },
        ]);
        var service = FollowUpPrintJobServiceTests.CreateService(db, eligibility, new StubRenderService());

        await Assert.ThrowsAsync<FollowUpPrintValidationException>(
            () => service.CreateJobAsync(BaseRequest, new TestCurrentUser(1)));

        Assert.Equal(0, await db.FollowUpPrintJobs.CountAsync());
    }

    [Fact]
    public async Task CreateJobAsync_DepartmentIdNull_ExternalPartyIdZero_ThrowsValidationException_NoJobCreated()
    {
        await using var db = LettersTestInfrastructure.CreateDb(nameof(CreateJobAsync_DepartmentIdNull_ExternalPartyIdZero_ThrowsValidationException_NoJobCreated));
        await LettersTestInfrastructure.SeedUserAsync(db);
        await LettersTestInfrastructure.SeedTemplateAsync(db);

        var eligibility = new FixedCandidateEligibility([
            new EligibleCandidateWithTargets
            {
                TransactionId = 23,
                FollowUpCount = 0,
                Targets = [new FollowUpLetterTargetEntity("طرف خارجي صفر", DepartmentId: null, ExternalPartyId: 0)],
            },
        ]);
        var service = FollowUpPrintJobServiceTests.CreateService(db, eligibility, new StubRenderService());

        await Assert.ThrowsAsync<FollowUpPrintValidationException>(
            () => service.CreateJobAsync(BaseRequest, new TestCurrentUser(1)));

        Assert.Equal(0, await db.FollowUpPrintJobs.CountAsync());
    }

    // ── all targets invalid → 422, no job ────────────────────────────────────

    [Fact]
    public async Task CreateJobAsync_AllTargetsInvalid_ThrowsValidationException_NoJobCreated()
    {
        await using var db = LettersTestInfrastructure.CreateDb(nameof(CreateJobAsync_AllTargetsInvalid_ThrowsValidationException_NoJobCreated));
        await LettersTestInfrastructure.SeedUserAsync(db);
        await LettersTestInfrastructure.SeedTemplateAsync(db);

        var eligibility = new FixedCandidateEligibility([
            new EligibleCandidateWithTargets
            {
                TransactionId = 30,
                Targets = [new FollowUpLetterTargetEntity("اسم فقط", DepartmentId: null, ExternalPartyId: null)],
            },
            new EligibleCandidateWithTargets
            {
                TransactionId = 31,
                Targets = [new FollowUpLetterTargetEntity("مزدوجة", DepartmentId: 2, ExternalPartyId: 3)],
            },
            new EligibleCandidateWithTargets
            {
                TransactionId = 32,
                Targets = [new FollowUpLetterTargetEntity("صفر", DepartmentId: 0, ExternalPartyId: 0)],
            },
        ]);
        var service = FollowUpPrintJobServiceTests.CreateService(db, eligibility, new StubRenderService());

        await Assert.ThrowsAsync<FollowUpPrintValidationException>(
            () => service.CreateJobAsync(BaseRequest, new TestCurrentUser(1)));

        Assert.Equal(0, await db.FollowUpPrintJobs.CountAsync());
        Assert.Equal(0, await db.FollowUpPrintJobPayloads.CountAsync());
    }

    // ── mixed: valid + invalid — only valid ones become payloads ─────────────

    [Fact]
    public async Task CreateJobAsync_MixedTargets_OnlyValidShapeTargetsStoredAsPayloads()
    {
        await using var db = LettersTestInfrastructure.CreateDb(nameof(CreateJobAsync_MixedTargets_OnlyValidShapeTargetsStoredAsPayloads));
        await LettersTestInfrastructure.SeedUserAsync(db);
        await LettersTestInfrastructure.SeedTemplateAsync(db);

        // TransactionId=40: one valid target (dept) + one invalid (both null) → 1 payload
        // TransactionId=41: two invalid targets (both null + both set) → excluded entirely
        // TransactionId=42: one valid target (entity) → 1 payload
        var eligibility = new FixedCandidateEligibility([
            new EligibleCandidateWithTargets
            {
                TransactionId = 40,
                FollowUpCount = 0,
                Targets =
                [
                    new FollowUpLetterTargetEntity("إدارة صالحة", DepartmentId: 10, ExternalPartyId: null),
                    new FollowUpLetterTargetEntity("اسم فقط",     DepartmentId: null, ExternalPartyId: null),
                ],
            },
            new EligibleCandidateWithTargets
            {
                TransactionId = 41,
                FollowUpCount = 0,
                Targets =
                [
                    new FollowUpLetterTargetEntity("مزدوجة", DepartmentId: 5, ExternalPartyId: 7),
                    new FollowUpLetterTargetEntity("صفر",    DepartmentId: 0, ExternalPartyId: null),
                ],
            },
            new EligibleCandidateWithTargets
            {
                TransactionId = 42,
                FollowUpCount = 1,
                Targets = [new FollowUpLetterTargetEntity("طرف خارجي", DepartmentId: null, ExternalPartyId: 15)],
            },
        ]);
        var service = FollowUpPrintJobServiceTests.CreateService(db, eligibility, new StubRenderService());

        var job = await service.CreateJobAsync(BaseRequest, new TestCurrentUser(1));

        Assert.True(job.Id > 0);
        var payloads = await db.FollowUpPrintJobPayloads.Where(p => p.JobId == job.Id).ToListAsync();
        Assert.Equal(2, payloads.Count);
        Assert.Contains(payloads, p => p.TransactionId == 40 && p.TargetDepartmentId == 10 && p.TargetEntityId == null);
        Assert.Contains(payloads, p => p.TransactionId == 42 && p.TargetEntityId == 15 && p.TargetDepartmentId == null);
        Assert.DoesNotContain(payloads, p => p.TransactionId == 41);

        var stored = await db.FollowUpPrintJobs.SingleAsync(j => j.Id == job.Id);
        Assert.Equal(2, stored.TotalTransactions);
        Assert.Equal(2, stored.TotalLetters);
    }

    // ── de-duplication: same transaction + same ID → one payload ─────────────

    [Fact]
    public async Task CreateJobAsync_SameDepartmentIdTwice_CreatesOnePayload()
    {
        await using var db = LettersTestInfrastructure.CreateDb(nameof(CreateJobAsync_SameDepartmentIdTwice_CreatesOnePayload));
        await LettersTestInfrastructure.SeedUserAsync(db);
        await LettersTestInfrastructure.SeedTemplateAsync(db);

        var eligibility = new FixedCandidateEligibility([
            new EligibleCandidateWithTargets
            {
                TransactionId = 60,
                Targets =
                [
                    new FollowUpLetterTargetEntity("إدارة المالية", DepartmentId: 7, ExternalPartyId: null),
                    new FollowUpLetterTargetEntity("إدارة المالية (مكرر)", DepartmentId: 7, ExternalPartyId: null),
                ],
            },
        ]);
        var service = FollowUpPrintJobServiceTests.CreateService(db, eligibility, new StubRenderService());

        var job = await service.CreateJobAsync(BaseRequest, new TestCurrentUser(1));

        var payloads = await db.FollowUpPrintJobPayloads.Where(p => p.JobId == job.Id).ToListAsync();
        Assert.Single(payloads);
        Assert.Equal(7, payloads[0].TargetDepartmentId);

        var stored = await db.FollowUpPrintJobs.SingleAsync(j => j.Id == job.Id);
        Assert.Equal(1, stored.TotalLetters);
        Assert.Equal(1, stored.TotalTransactions);
    }

    [Fact]
    public async Task CreateJobAsync_SameExternalPartyIdTwice_CreatesOnePayload()
    {
        await using var db = LettersTestInfrastructure.CreateDb(nameof(CreateJobAsync_SameExternalPartyIdTwice_CreatesOnePayload));
        await LettersTestInfrastructure.SeedUserAsync(db);
        await LettersTestInfrastructure.SeedTemplateAsync(db);

        var eligibility = new FixedCandidateEligibility([
            new EligibleCandidateWithTargets
            {
                TransactionId = 61,
                Targets =
                [
                    new FollowUpLetterTargetEntity("جهة خارجية", DepartmentId: null, ExternalPartyId: 12),
                    new FollowUpLetterTargetEntity("جهة خارجية (مكرر)", DepartmentId: null, ExternalPartyId: 12),
                ],
            },
        ]);
        var service = FollowUpPrintJobServiceTests.CreateService(db, eligibility, new StubRenderService());

        var job = await service.CreateJobAsync(BaseRequest, new TestCurrentUser(1));

        var payloads = await db.FollowUpPrintJobPayloads.Where(p => p.JobId == job.Id).ToListAsync();
        Assert.Single(payloads);
        Assert.Equal(12, payloads[0].TargetEntityId);
    }

    [Fact]
    public async Task CreateJobAsync_DifferentDepartmentIds_CreatesTwoPayloads()
    {
        await using var db = LettersTestInfrastructure.CreateDb(nameof(CreateJobAsync_DifferentDepartmentIds_CreatesTwoPayloads));
        await LettersTestInfrastructure.SeedUserAsync(db);
        await LettersTestInfrastructure.SeedTemplateAsync(db);

        var eligibility = new FixedCandidateEligibility([
            new EligibleCandidateWithTargets
            {
                TransactionId = 62,
                Targets =
                [
                    new FollowUpLetterTargetEntity("إدارة أ", DepartmentId: 8, ExternalPartyId: null),
                    new FollowUpLetterTargetEntity("إدارة ب", DepartmentId: 9, ExternalPartyId: null),
                ],
            },
        ]);
        var service = FollowUpPrintJobServiceTests.CreateService(db, eligibility, new StubRenderService());

        var job = await service.CreateJobAsync(BaseRequest, new TestCurrentUser(1));

        var payloads = await db.FollowUpPrintJobPayloads.Where(p => p.JobId == job.Id).ToListAsync();
        Assert.Equal(2, payloads.Count);
        Assert.Contains(payloads, p => p.TargetDepartmentId == 8);
        Assert.Contains(payloads, p => p.TargetDepartmentId == 9);

        var stored = await db.FollowUpPrintJobs.SingleAsync(j => j.Id == job.Id);
        Assert.Equal(2, stored.TotalLetters);
        Assert.Equal(1, stored.TotalTransactions);
    }

    [Fact]
    public async Task CreateJobAsync_DeptIdAndEntityId_CreatesTwoDistinctPayloads()
    {
        await using var db = LettersTestInfrastructure.CreateDb(nameof(CreateJobAsync_DeptIdAndEntityId_CreatesTwoDistinctPayloads));
        await LettersTestInfrastructure.SeedUserAsync(db);
        await LettersTestInfrastructure.SeedTemplateAsync(db);

        var eligibility = new FixedCandidateEligibility([
            new EligibleCandidateWithTargets
            {
                TransactionId = 63,
                Targets =
                [
                    new FollowUpLetterTargetEntity("إدارة",      DepartmentId: 4,  ExternalPartyId: null),
                    new FollowUpLetterTargetEntity("طرف خارجي",  DepartmentId: null, ExternalPartyId: 6),
                ],
            },
        ]);
        var service = FollowUpPrintJobServiceTests.CreateService(db, eligibility, new StubRenderService());

        var job = await service.CreateJobAsync(BaseRequest, new TestCurrentUser(1));

        var payloads = await db.FollowUpPrintJobPayloads.Where(p => p.JobId == job.Id).ToListAsync();
        Assert.Equal(2, payloads.Count);
        Assert.Contains(payloads, p => p.TargetDepartmentId == 4 && p.TargetEntityId == null);
        Assert.Contains(payloads, p => p.TargetEntityId == 6 && p.TargetDepartmentId == null);
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private sealed class FixedCandidateEligibility(IReadOnlyList<EligibleCandidateWithTargets> candidates)
        : IFollowUpPrintEligibilityService
    {
        public Task<PagedEligibleTransactionsDto> GetEligibleAsync(
            FollowUpPrintFilterRequest filter,
            ICurrentUserService currentUser,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new PagedEligibleTransactionsDto());

        public Task<FollowUpPrintEligibilityPreviewDto> PreviewAsync(
            FollowUpPrintFilterRequest filter,
            int batchSize,
            ICurrentUserService currentUser,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new FollowUpPrintEligibilityPreviewDto());

        public Task<IReadOnlyList<FollowUpPrintJobLetterPayload>> BuildLetterPayloadsAsync(
            FollowUpPrintFilterSnapshot filter,
            int? maxCount = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<FollowUpPrintJobLetterPayload>>([]);

        public Task<IReadOnlyList<EligibleCandidateWithTargets>> BuildEligibleCandidatesWithTargetsAsync(
            FollowUpPrintFilterRequest filter,
            ICurrentUserService currentUser,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(candidates);
    }
}
