using System.Data.Common;
using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Uqeb.Api.Data;
using Uqeb.Api.DTOs.FollowUpPrint;
using Uqeb.Api.Models.Entities;
using Uqeb.Api.Models.Enums;
using Uqeb.Api.Models.Letters;
using Uqeb.Api.Services;
using Xunit;
using Xunit.Abstractions;

namespace Uqeb.Api.Tests.Letters;

/// <summary>
/// Benchmarks follow-up print eligibility queries against SQL Server.
/// Full 10k run: set REQUIRE_FOLLOWUP_PRINT_BENCHMARK=1.
/// CI uses 1k rows with the same per-query bounds scaled proportionally.
/// </summary>
[Trait("Category", "SqlServer")]
public class FollowUpPrintEligibilityBenchmarkSqlServerTests
    : FollowUpPrintSqlServerTestBase
{
    private const int FullBenchmarkRows = 10_000;
    private const int CiBenchmarkRows = 1_000;
    private const int FullMaxQueryCount = 12;
    private const int FullMaxElapsedMs = 15_000;

    private readonly ITestOutputHelper _output;

    public FollowUpPrintEligibilityBenchmarkSqlServerTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public async Task EligibilityQueryAndPreview_StayWithinBounds()
    {
        if (!ShouldRunBenchmark())
        {
            _output.WriteLine("Skipped: set REQUIRE_FOLLOWUP_PRINT_BENCHMARK=1 for full benchmark.");
            return;
        }

        if (!ShouldRunSqlServerTest())
            return;

        var rowCount = IsCiBenchmark() ? CiBenchmarkRows : FullBenchmarkRows;
        var maxQueries = IsCiBenchmark() ? 8 : FullMaxQueryCount;
        var maxElapsedMs = IsCiBenchmark() ? 5_000 : FullMaxElapsedMs;

        var context = await CreateMigratedContextAsync();
        try
        {
            await SeedEligibleTransactionsAsync(context, rowCount);

            await using var scope = context.Provider.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var counter = new QueryCountingInterceptor();
            var countedOptions = new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlServer(context.ConnectionString)
                .AddInterceptors(counter)
                .Options;

            await using var countedDb = new AppDbContext(countedOptions);
            var render = new StubRenderService(new FollowUpLetterTargetEntity("جهة", 1, null));
            var service = new FollowUpPrintEligibilityService(
                countedDb,
                render,
                new FixedTimeZone(new DateTime(2025, 6, 25)),
                LettersTestInfrastructure.CreateOptions());

            var filter = new FollowUpPrintFilterRequest
            {
                DaysSinceLastFollowUp = 10,
                ExcludeRecentlyPrinted = false,
                PrintedLetterExclusionDays = 7,
                Page = 1,
                PageSize = 25,
            };

            var stopwatch = Stopwatch.StartNew();
            var eligible = await service.GetEligibleAsync(filter, new TestCurrentUser(1));
            var preview = await service.PreviewAsync(filter, 25, new TestCurrentUser(1));
            stopwatch.Stop();

            _output.WriteLine($"rows={rowCount}; eligible={eligible.TotalCount}; previewLetters={preview.EstimatedLetterCount}; queries={counter.Count}; elapsedMs={stopwatch.ElapsedMilliseconds}");

            Assert.True(eligible.TotalCount > 0);
            Assert.True(preview.EstimatedLetterCount > 0);
            Assert.True(stopwatch.ElapsedMilliseconds < maxElapsedMs, $"Elapsed {stopwatch.ElapsedMilliseconds}ms exceeded {maxElapsedMs}ms.");
            Assert.True(counter.Count <= maxQueries, $"Query count {counter.Count} exceeded bound {maxQueries}.");
        }
        finally
        {
            await CleanupAsync(context);
        }
    }

    private static bool ShouldRunBenchmark() =>
        IsCiBenchmark() ||
        string.Equals(Environment.GetEnvironmentVariable("REQUIRE_FOLLOWUP_PRINT_BENCHMARK"), "1", StringComparison.Ordinal);

    private static bool IsCiBenchmark() =>
        string.Equals(Environment.GetEnvironmentVariable("REQUIRE_FOLLOWUP_PRINT_SQLSERVER_TESTS"), "1", StringComparison.Ordinal);

    private static async Task SeedEligibleTransactionsAsync(FollowUpPrintSqlServerContext context, int count)
    {
        await using var scope = context.Provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var user = new User
        {
            Username = "bench-admin",
            PasswordHash = "hash",
            FullName = "Bench Admin",
            Role = UserRole.Admin,
            IsActive = true,
        };
        db.Users.Add(user);

        var department = new Department { Name = "Bench", NameNormalized = "bench", IsActive = true };
        db.Departments.Add(department);
        await db.SaveChangesAsync();

        var referenceDate = new DateTime(2025, 6, 10);
        for (var i = 1; i <= count; i++)
        {
            db.Transactions.Add(new Transaction
            {
                InternalTrackingNumber = $"INT-{i:000000}",
                IncomingNumber = $"IN-{i:000000}",
                IncomingDate = referenceDate.AddDays(-30),
                Subject = $"Benchmark {i}",
                Status = TransactionStatus.InProgress,
                Priority = Priority.Normal,
                CreatedById = user.Id,
                CreatedAt = DateTime.UtcNow,
            });
        }

        await db.SaveChangesAsync();

        var transactionIds = await db.Transactions.Select(t => t.Id).ToListAsync();
        foreach (var transactionId in transactionIds)
        {
            db.TransactionOutgoingDepartments.Add(new TransactionOutgoingDepartment
            {
                TransactionId = transactionId,
                DepartmentId = department.Id,
                CreatedById = user.Id,
            });
            db.FollowUps.Add(new FollowUp
            {
                TransactionId = transactionId,
                FollowUpDate = referenceDate,
                CreatedById = user.Id,
                CreatedAt = referenceDate,
            });
        }

        await db.SaveChangesAsync();
    }

    private sealed class QueryCountingInterceptor : DbCommandInterceptor
    {
        public int Count { get; private set; }

        public override InterceptionResult<DbDataReader> ReaderExecuting(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result)
        {
            Count++;
            return base.ReaderExecuting(command, eventData, result);
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            Count++;
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }
}
