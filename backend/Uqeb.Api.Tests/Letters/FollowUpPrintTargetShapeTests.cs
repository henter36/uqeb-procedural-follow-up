using Microsoft.EntityFrameworkCore;
using Uqeb.Api.DTOs.FollowUpPrint;
using Uqeb.Api.Models.Enums;
using Uqeb.Api.Models.Letters;
using Uqeb.Api.Services;
using Xunit;

namespace Uqeb.Api.Tests.Letters;

/// <summary>
/// Tests for CK_FollowUpPrintJobPayloads_TargetShape enforcement in CreateJobAsync.
/// The CHECK constraint requires exactly one of TargetDepartmentId / TargetEntityId to be non-null.
/// Name-only fallback targets (both null) must be skipped — not inserted — to avoid a 500.
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

    // ── valid department target ──────────────────────────────────────────────

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

    // ── valid external-party target ──────────────────────────────────────────

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

    // ── name-only fallback target (both IDs null) — must not 500 ────────────

    [Fact]
    public async Task CreateJobAsync_NameOnlyTarget_BothIdsNull_SkipsPayloadWithoutError()
    {
        await using var db = LettersTestInfrastructure.CreateDb(nameof(CreateJobAsync_NameOnlyTarget_BothIdsNull_SkipsPayloadWithoutError));
        await LettersTestInfrastructure.SeedUserAsync(db);
        await LettersTestInfrastructure.SeedTemplateAsync(db);

        var eligibility = new FixedCandidateEligibility([
            new EligibleCandidateWithTargets
            {
                TransactionId = 3,
                FollowUpCount = 0,
                // fallback: name only, both IDs null → violates CK_FollowUpPrintJobPayloads_TargetShape if inserted
                Targets = [new FollowUpLetterTargetEntity("جهة فقط بالاسم", DepartmentId: null, ExternalPartyId: null)],
            },
            new EligibleCandidateWithTargets
            {
                TransactionId = 4,
                FollowUpCount = 0,
                Targets = [new FollowUpLetterTargetEntity("إدارة 1", DepartmentId: 1, ExternalPartyId: null)],
            },
        ]);
        var service = FollowUpPrintJobServiceTests.CreateService(db, eligibility, new StubRenderService());

        // Must not throw — the name-only target must be silently skipped
        var job = await service.CreateJobAsync(BaseRequest, new TestCurrentUser(1));

        Assert.True(job.Id > 0);
        var payloads = await db.FollowUpPrintJobPayloads.Where(p => p.JobId == job.Id).ToListAsync();
        // Only the valid target (transaction 4) generates a payload
        Assert.Single(payloads);
        Assert.Equal(4, payloads[0].TransactionId);
        Assert.Equal(FollowUpPrintJobPayloadStatus.Pending, payloads[0].Status);

        // Counts adjusted: transaction 3 excluded (name-only)
        var stored = await db.FollowUpPrintJobs.SingleAsync(j => j.Id == job.Id);
        Assert.Equal(1, stored.TotalTransactions);
        Assert.Equal(1, stored.TotalLetters);
    }

    // ── mixed: valid + name-only — only valid ones become payloads ───────────

    [Fact]
    public async Task CreateJobAsync_MixedTargets_OnlyValidShapeTargetsStoredAsPayloads()
    {
        await using var db = LettersTestInfrastructure.CreateDb(nameof(CreateJobAsync_MixedTargets_OnlyValidShapeTargetsStoredAsPayloads));
        await LettersTestInfrastructure.SeedUserAsync(db);
        await LettersTestInfrastructure.SeedTemplateAsync(db);

        // Transaction 5 has two targets: one valid (dept), one name-only (fallback)
        var eligibility = new FixedCandidateEligibility([
            new EligibleCandidateWithTargets
            {
                TransactionId = 5,
                FollowUpCount = 2,
                Targets =
                [
                    new FollowUpLetterTargetEntity("إدارة صالحة", DepartmentId: 10, ExternalPartyId: null),
                    new FollowUpLetterTargetEntity("اسم فقط", DepartmentId: null, ExternalPartyId: null),
                ],
            },
        ]);
        var service = FollowUpPrintJobServiceTests.CreateService(db, eligibility, new StubRenderService());

        var job = await service.CreateJobAsync(BaseRequest, new TestCurrentUser(1));

        Assert.True(job.Id > 0);
        var payloads = await db.FollowUpPrintJobPayloads.Where(p => p.JobId == job.Id).ToListAsync();
        Assert.Single(payloads);
        Assert.Equal(10, payloads[0].TargetDepartmentId);
        Assert.Null(payloads[0].TargetEntityId);

        var stored = await db.FollowUpPrintJobs.SingleAsync(j => j.Id == job.Id);
        Assert.Equal(1, stored.TotalTransactions);
        Assert.Equal(1, stored.TotalLetters);
    }

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
