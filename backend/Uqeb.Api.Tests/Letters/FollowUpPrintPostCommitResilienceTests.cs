using Microsoft.EntityFrameworkCore;
using Uqeb.Api.DTOs.FollowUpPrint;
using Uqeb.Api.Models.Entities;
using Uqeb.Api.Models.Enums;
using Uqeb.Api.Models.Letters;
using Uqeb.Api.Services;
using Xunit;

namespace Uqeb.Api.Tests.Letters;

/// <summary>
/// Tests for CreateJobAsync resilience to failures that occur after the DB transaction
/// has already been committed (audit write, GetJobAsync read).
/// Root cause: lines after CommitAsync had no error handling, so a cancelled CancellationToken
/// or a transient audit failure turned an already-committed job into a 500 response.
/// </summary>
public class FollowUpPrintPostCommitResilienceTests
{
    private static readonly CreateFollowUpPrintJobRequest BaseRequest = new()
    {
        Filter = new FollowUpPrintFilterRequest
        {
            DaysSinceLastFollowUp = 10,
            ExcludeRecentlyPrinted = false,
        },
    };

    private static readonly IReadOnlyList<EligibleCandidateWithTargets> OneCandidate =
    [
        new EligibleCandidateWithTargets
        {
            TransactionId = 1,
            FollowUpCount = 0,
            Targets = [new FollowUpLetterTargetEntity("إدارة 1", DepartmentId: 5, ExternalPartyId: null)],
        },
    ];

    // ── audit failure is best-effort ─────────────────────────────────────────

    [Fact]
    public async Task CreateJobAsync_AuditThrows_StillReturnsJobDto()
    {
        await using var db = LettersTestInfrastructure.CreateDb(nameof(CreateJobAsync_AuditThrows_StillReturnsJobDto));
        await LettersTestInfrastructure.SeedUserAsync(db);
        await LettersTestInfrastructure.SeedTemplateAsync(db);

        var eligibility = new SimpleEligibility(OneCandidate);
        var service = FollowUpPrintJobServiceTests.CreateService(
            db,
            eligibility,
            new StubRenderService(),
            audit: new ThrowingAuditService());

        var result = await service.CreateJobAsync(BaseRequest, new TestCurrentUser(1));

        Assert.True(result.Id > 0, "Job must be returned even when audit throws.");
        Assert.Equal(FollowUpPrintJobStatus.Queued, result.Status);

        var persisted = await db.FollowUpPrintJobs.FindAsync(result.Id);
        Assert.NotNull(persisted);
    }

    // ── GetJobAsync cancelled CT falls back to entity ────────────────────────

    [Fact]
    public async Task CreateJobAsync_GetJobAsyncUnavailable_ReturnsFallbackDtoWithCorrectId()
    {
        await using var db = LettersTestInfrastructure.CreateDb(nameof(CreateJobAsync_GetJobAsyncUnavailable_ReturnsFallbackDtoWithCorrectId));
        await LettersTestInfrastructure.SeedUserAsync(db);
        await LettersTestInfrastructure.SeedTemplateAsync(db);

        var eligibility = new SimpleEligibility(OneCandidate);
        // Access service that throws on EnsureCanViewJobAsync — forces GetJobAsync to fail.
        var accessService = new ForbidViewAccessService();
        var service = FollowUpPrintJobServiceTests.CreateService(
            db,
            eligibility,
            new StubRenderService(),
            access: accessService);

        var result = await service.CreateJobAsync(BaseRequest, new TestCurrentUser(1));

        Assert.True(result.Id > 0, "Fallback DTO must carry the committed job's Id.");
        Assert.Equal(FollowUpPrintJobStatus.Queued, result.Status);
        Assert.Empty(result.Parts);
    }

    // ── idempotency: retry after post-commit failure returns same job ─────────

    [Fact]
    public async Task CreateJobAsync_IdempotencyRetry_ReturnsSameJobNoDuplicate()
    {
        const string idempotencyKey = "retry-test-key";
        await using var db = LettersTestInfrastructure.CreateDb(nameof(CreateJobAsync_IdempotencyRetry_ReturnsSameJobNoDuplicate));
        await LettersTestInfrastructure.SeedUserAsync(db);
        await LettersTestInfrastructure.SeedTemplateAsync(db);

        var eligibility = new SimpleEligibility(OneCandidate);
        var service = FollowUpPrintJobServiceTests.CreateService(db, eligibility, new StubRenderService());

        var request = new CreateFollowUpPrintJobRequest
        {
            IdempotencyKey = idempotencyKey,
            Filter = BaseRequest.Filter,
        };

        var first = await service.CreateJobAsync(request, new TestCurrentUser(1));
        var retry = await service.CreateJobAsync(request, new TestCurrentUser(1));

        Assert.Equal(first.Id, retry.Id);
        Assert.Equal(1, await db.FollowUpPrintJobs.CountAsync());
    }

    // ── no duplicate when idempotencyKey absent ──────────────────────────────

    [Fact]
    public async Task CreateJobAsync_ValidRequest_ReturnsAcceptableJobDto()
    {
        await using var db = LettersTestInfrastructure.CreateDb(nameof(CreateJobAsync_ValidRequest_ReturnsAcceptableJobDto));
        await LettersTestInfrastructure.SeedUserAsync(db);
        await LettersTestInfrastructure.SeedTemplateAsync(db);

        var eligibility = new SimpleEligibility(OneCandidate);
        var service = FollowUpPrintJobServiceTests.CreateService(db, eligibility, new StubRenderService());

        var result = await service.CreateJobAsync(BaseRequest, new TestCurrentUser(1));

        Assert.True(result.Id > 0);
        Assert.Equal(FollowUpPrintJobStatus.Queued, result.Status);
        Assert.Equal(1, result.TotalTransactions);
        Assert.Equal(1, result.TotalLetters);

        var payloads = await db.FollowUpPrintJobPayloads.Where(p => p.JobId == result.Id).ToListAsync();
        Assert.Single(payloads);
        Assert.Equal(FollowUpPrintJobPayloadStatus.Pending, payloads[0].Status);
        Assert.Equal(5, payloads[0].TargetDepartmentId);
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private sealed class SimpleEligibility(IReadOnlyList<EligibleCandidateWithTargets> candidates)
        : IFollowUpPrintEligibilityService
    {
        public Task<PagedEligibleTransactionsDto> GetEligibleAsync(FollowUpPrintFilterRequest filter, ICurrentUserService currentUser, CancellationToken cancellationToken = default)
            => Task.FromResult(new PagedEligibleTransactionsDto());

        public Task<FollowUpPrintEligibilityPreviewDto> PreviewAsync(FollowUpPrintFilterRequest filter, int batchSize, ICurrentUserService currentUser, CancellationToken cancellationToken = default)
            => Task.FromResult(new FollowUpPrintEligibilityPreviewDto());

        public Task<IReadOnlyList<FollowUpPrintJobLetterPayload>> BuildLetterPayloadsAsync(FollowUpPrintFilterSnapshot filter, int? maxCount = null, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<FollowUpPrintJobLetterPayload>>([]);

        public Task<IReadOnlyList<EligibleCandidateWithTargets>> BuildEligibleCandidatesWithTargetsAsync(FollowUpPrintFilterRequest filter, ICurrentUserService currentUser, CancellationToken cancellationToken = default)
            => Task.FromResult(candidates);
    }

    private sealed class ThrowingAuditService : IAuditService
    {
        public AuditLog TrackLog(int userId, AuditAction action, string? entityName, int? entityId, int? transactionId, string? oldValue, string? newValue)
        {
            throw new InvalidOperationException("Simulated audit failure.");
        }

        public Task LogAsync(int userId, AuditAction action, string? entityName, int? entityId, int? transactionId, string? oldValue, string? newValue)
            => throw new InvalidOperationException("Simulated audit failure.");
    }

    private sealed class ForbidViewAccessService : IFollowUpPrintAccessService
    {
        public Task EnsureCanViewJobAsync(int jobId, ICurrentUserService currentUser, CancellationToken cancellationToken = default)
            => throw new Exceptions.FollowUpPrintForbiddenException("Simulated access denial for GetJobAsync fallback test.");

        public Task EnsureCanMutateJobAsync(int jobId, ICurrentUserService currentUser, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task EnsureCanViewPartAsync(int jobId, int partNumber, ICurrentUserService currentUser, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task EnsureCanPrintPartAsync(int jobId, int partNumber, ICurrentUserService currentUser, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task EnsureCanViewPrintRecordAsync(int recordId, ICurrentUserService currentUser, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public System.Linq.IQueryable<Uqeb.Api.Models.Entities.FollowUpPrintJob> ApplyJobListScope(
            System.Linq.IQueryable<Uqeb.Api.Models.Entities.FollowUpPrintJob> query,
            ICurrentUserService currentUser) => query;
    }
}
