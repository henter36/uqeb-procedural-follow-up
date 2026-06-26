using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Uqeb.Api.Data;
using Uqeb.Api.DTOs.FollowUpPrint;
using Uqeb.Api.Exceptions;
using Uqeb.Api.Models.Entities;
using Uqeb.Api.Models.Enums;
using Uqeb.Api.Models.Letters;
using Uqeb.Api.Services;
using Xunit;

namespace Uqeb.Api.Tests.Letters;

[Trait("Category", "SqlServer")]
public class FollowUpPrintIdempotencySqlServerTests
    : FollowUpPrintSqlServerTestBase
{
    [Fact]
    public async Task CreateJobAsync_ConcurrentSameKey_ReturnsSameJob()
    {
        if (!ShouldRunSqlServerTest())
            return;

        var context = await CreateMigratedContextAsync();
        try
        {
            await SeedEligibleDataAsync(context);

            var tasks = Enumerable.Range(0, 4)
                .Select(_ => Task.Run(async () =>
                {
                    await using var scope = context.Provider.CreateAsyncScope();
                    var service = CreateJobService(scope);
                    return await service.CreateJobAsync(BuildRequest("shared-key"), new TestCurrentUser(1));
                }))
                .ToArray();

            var results = await Task.WhenAll(tasks);
            var distinctJobIds = results.Select(r => r.Id).Distinct().ToList();

            Assert.Single(distinctJobIds);

            await using var verifyScope = context.Provider.CreateAsyncScope();
            var db = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.Equal(1, await db.FollowUpPrintJobs.CountAsync());
            Assert.Equal(1, await db.FollowUpPrintIdempotencyKeys.CountAsync(k => k.Key == "shared-key"));
        }
        finally
        {
            await CleanupAsync(context);
        }
    }

    [Fact]
    public async Task CreateJobAsync_DifferentPayloadWithSameKey_ThrowsConflict()
    {
        if (!ShouldRunSqlServerTest())
            return;

        var context = await CreateMigratedContextAsync();
        try
        {
            await SeedEligibleDataAsync(context);

            await using var scope = context.Provider.CreateAsyncScope();
            var service = CreateJobService(scope);
            _ = await service.CreateJobAsync(BuildRequest("conflict-key"), new TestCurrentUser(1));

            var conflicting = BuildRequest("conflict-key");
            conflicting.Filter.DaysSinceLastFollowUp = 30;

            await Assert.ThrowsAsync<FollowUpPrintConflictException>(
                () => service.CreateJobAsync(conflicting, new TestCurrentUser(1)));
        }
        finally
        {
            await CleanupAsync(context);
        }
    }

    [Fact]
    public async Task CreateJobAsync_OrphanKeyWithoutJob_CreatesNewJob()
    {
        if (!ShouldRunSqlServerTest())
            return;

        var context = await CreateMigratedContextAsync();
        try
        {
            await SeedEligibleDataAsync(context);

            await using var seedScope = context.Provider.CreateAsyncScope();
            var db = seedScope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.FollowUpPrintIdempotencyKeys.Add(new FollowUpPrintIdempotencyKey
            {
                UserId = 1,
                Key = "orphan-key",
                Operation = "create-job",
                RequestHash = FollowUpPrintRequestHash.Compute(BuildRequest("orphan-key"), 25),
                ResultId = 99999,
                CreatedAt = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();

            await using var scope = context.Provider.CreateAsyncScope();
            var service = CreateJobService(scope);
            var job = await service.CreateJobAsync(BuildRequest("orphan-key"), new TestCurrentUser(1));

            Assert.True(job.Id > 0);
            Assert.Equal(1, await scope.ServiceProvider.GetRequiredService<AppDbContext>().FollowUpPrintJobs.CountAsync());
        }
        finally
        {
            await CleanupAsync(context);
        }
    }

    private static FollowUpPrintJobService CreateJobService(IServiceScope scope)
    {
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var render = new StubRenderService(new FollowUpLetterTargetEntity("جهة", 1, null));
        var eligibility = new FollowUpPrintEligibilityService(
            db,
            render,
            new FixedTimeZone(new DateTime(2025, 6, 25)),
            LettersTestInfrastructure.CreateOptions());
        return new FollowUpPrintJobService(
            db,
            eligibility,
            render,
            new FollowUpPrintAccessService(db),
            new NoOpAuditService(),
            LettersTestInfrastructure.CreateOptions());
    }

    private static CreateFollowUpPrintJobRequest BuildRequest(string idempotencyKey) => new()
    {
        IdempotencyKey = idempotencyKey,
        Filter = new FollowUpPrintFilterRequest
        {
            DaysSinceLastFollowUp = 10,
            ExcludeRecentlyPrinted = false,
            PrintedLetterExclusionDays = 7,
            Page = 1,
            PageSize = 25,
        },
    };

    private static async Task SeedEligibleDataAsync(FollowUpPrintSqlServerContext context)
    {
        await using var scope = context.Provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var user = new User
        {
            Username = "admin",
            PasswordHash = "hash",
            FullName = "Admin",
            Role = UserRole.Admin,
            IsActive = true,
        };
        db.Users.Add(user);

        var department = new Department { Name = "المالية", NameNormalized = "المالية", IsActive = true };
        db.Departments.Add(department);

        var template = new LetterTemplate
        {
            Code = FollowUpLetterRenderService.FollowUpTemplateCode,
            Name = "Follow Up",
            TemplateType = LetterTemplateType.FollowUp,
            Content = FollowUpLetterRenderService.DefaultFollowUpContent,
            IsActive = true,
            IsDefault = true,
        };
        db.LetterTemplates.Add(template);
        await db.SaveChangesAsync();

        var referenceDate = new DateTime(2025, 6, 10);
        db.Transactions.Add(new Transaction
        {
            InternalTrackingNumber = "INT-1",
            IncomingNumber = "IN-1",
            IncomingDate = referenceDate.AddDays(-30),
            Subject = "اختبار",
            Status = TransactionStatus.InProgress,
            Priority = Priority.Normal,
            CreatedById = user.Id,
            CreatedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();

        db.TransactionOutgoingDepartments.Add(new TransactionOutgoingDepartment
        {
            TransactionId = db.Transactions.Single().Id,
            DepartmentId = department.Id,
            CreatedById = user.Id,
        });
        db.FollowUps.Add(new FollowUp
        {
            TransactionId = db.Transactions.Single().Id,
            FollowUpDate = referenceDate,
            CreatedById = user.Id,
            CreatedAt = referenceDate,
        });
        await db.SaveChangesAsync();
    }
}
