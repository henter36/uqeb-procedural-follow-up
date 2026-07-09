using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Uqeb.Api.Authorization;
using Uqeb.Api.Controllers;
using Uqeb.Api.Data;
using Uqeb.Api.Models.Entities;
using Uqeb.Api.Models.Enums;
using Uqeb.Api.Reporting.DataQuality;
using Uqeb.Api.Services;
using Xunit;

namespace Uqeb.Api.Tests;

public sealed class DataQualityTests
{
    private static readonly DateTime TodayUtc = new(2026, 7, 8, 9, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task GetSummary_WhenOverdueMoreThanDaysProvided_ReturnsOnlyTransactionsAboveThreshold()
    {
        await using var db = CreateDb(nameof(GetSummary_WhenOverdueMoreThanDaysProvided_ReturnsOnlyTransactionsAboveThreshold));
        SeedUser(db);
        db.Transactions.Add(CreateTransaction(1, responseDueDate: new DateTime(2026, 7, 3), requiresResponse: true));
        db.Transactions.Add(CreateTransaction(2, responseDueDate: new DateTime(2026, 6, 18), requiresResponse: true));
        await db.SaveChangesAsync();

        var summary = await CreateService(db).GetSummaryAsync(new DataQualityQueryDto { OverdueMoreThanDays = 10 });

        var issue = Assert.Single(summary.Issues);
        Assert.Equal(2, issue.TransactionId);
        Assert.Equal(DataQualityService.OverdueDurationRuleCode, issue.RuleCode);
        Assert.Equal(20, issue.DaysValue);
    }

    [Fact]
    public async Task GetSummary_WhenReferralDateAfterIncomingDateEnabled_ReturnsMatchingAssignments()
    {
        await using var db = CreateDb(nameof(GetSummary_WhenReferralDateAfterIncomingDateEnabled_ReturnsMatchingAssignments));
        SeedUser(db);
        var department = new Department { Id = 10, Name = "الشؤون المالية", NameNormalized = "الشؤون المالية", IsActive = true };
        var transaction = CreateTransaction(1, incomingDate: new DateTime(2026, 7, 1));
        transaction.Assignments.Add(new Assignment
        {
            Department = department,
            AssignedDate = new DateTime(2026, 7, 2),
            CreatedById = 1
        });
        db.Transactions.Add(transaction);
        await db.SaveChangesAsync();

        var summary = await CreateService(db).GetSummaryAsync(new DataQualityQueryDto
        {
            IncludeReferralDateAfterIncomingDate = true
        });

        var issue = Assert.Single(summary.Issues);
        Assert.Equal(DataQualityService.ReferralDateAfterIncomingDateRuleCode, issue.RuleCode);
        Assert.Equal(1, issue.DaysValue);
        Assert.Equal("AssignedDate", issue.FieldName);
    }

    [Fact]
    public async Task GetSummary_WhenReferralDateEqualsIncomingDate_DoesNotReturnReferralAfterIssue()
    {
        await using var db = CreateDb(nameof(GetSummary_WhenReferralDateEqualsIncomingDate_DoesNotReturnReferralAfterIssue));
        SeedUser(db);
        var department = new Department { Id = 10, Name = "الشؤون المالية", NameNormalized = "الشؤون المالية", IsActive = true };
        var transaction = CreateTransaction(1, incomingDate: new DateTime(2026, 7, 1));
        transaction.Assignments.Add(new Assignment
        {
            Department = department,
            AssignedDate = new DateTime(2026, 7, 1),
            CreatedById = 1
        });
        db.Transactions.Add(transaction);
        await db.SaveChangesAsync();

        var summary = await CreateService(db).GetSummaryAsync(new DataQualityQueryDto
        {
            IncludeReferralDateAfterIncomingDate = true
        });

        Assert.Empty(summary.Issues);
    }

    [Fact]
    public async Task GetSummary_WhenResponsePeriodLessThanDaysProvided_ReturnsShortResponsePeriod()
    {
        await using var db = CreateDb(nameof(GetSummary_WhenResponsePeriodLessThanDaysProvided_ReturnsShortResponsePeriod));
        SeedUser(db);
        db.Transactions.Add(CreateTransaction(
            1,
            incomingDate: new DateTime(2026, 7, 1),
            responseDueDate: new DateTime(2026, 7, 3),
            requiresResponse: true));
        await db.SaveChangesAsync();

        var summary = await CreateService(db).GetSummaryAsync(new DataQualityQueryDto
        {
            ResponsePeriodLessThanDays = 5
        });

        var issue = Assert.Single(summary.Issues);
        Assert.Equal(DataQualityService.ResponsePeriodLessThanThresholdRuleCode, issue.RuleCode);
        Assert.Equal(2, issue.DaysValue);
    }

    [Fact]
    public async Task GetSummary_WhenResponsePeriodEqualsThreshold_DoesNotReturn()
    {
        await using var db = CreateDb(nameof(GetSummary_WhenResponsePeriodEqualsThreshold_DoesNotReturn));
        SeedUser(db);
        db.Transactions.Add(CreateTransaction(
            1,
            incomingDate: new DateTime(2026, 7, 1),
            responseDueDate: new DateTime(2026, 7, 6),
            requiresResponse: true));
        await db.SaveChangesAsync();

        var summary = await CreateService(db).GetSummaryAsync(new DataQualityQueryDto
        {
            ResponsePeriodLessThanDays = 5
        });

        Assert.Empty(summary.Issues);
    }

    [Fact]
    public async Task GetSummary_TotalIssues_IsNotReducedByLimit()
    {
        await using var db = CreateDb(nameof(GetSummary_TotalIssues_IsNotReducedByLimit));
        SeedUser(db);
        db.Transactions.Add(CreateTransaction(1, responseDueDate: new DateTime(2026, 6, 18), requiresResponse: true));
        db.Transactions.Add(CreateTransaction(2, responseDueDate: new DateTime(2026, 6, 17), requiresResponse: true));
        db.Transactions.Add(CreateTransaction(3, responseDueDate: new DateTime(2026, 6, 16), requiresResponse: true));
        await db.SaveChangesAsync();

        var summary = await CreateService(db).GetSummaryAsync(new DataQualityQueryDto
        {
            OverdueMoreThanDays = 10,
            Limit = 1
        });

        Assert.Equal(3, summary.TotalIssues);
        Assert.Single(summary.Issues);
    }

    [Fact]
    public async Task GetSummary_WhenNegativeThreshold_ReturnsBadRequest()
    {
        await using var db = CreateDb(nameof(GetSummary_WhenNegativeThreshold_ReturnsBadRequest));
        var controller = CreateController(db);

        var result = await controller.GetSummary(new DataQualityQueryDto { OverdueMoreThanDays = -1 }, CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task GetSummary_ByDefault_ExcludesReviewedIssues()
    {
        await using var db = CreateDb(nameof(GetSummary_ByDefault_ExcludesReviewedIssues));
        SeedUser(db);
        db.Transactions.Add(CreateTransaction(1, responseDueDate: new DateTime(2026, 6, 18), requiresResponse: true));
        db.DataQualityReviews.Add(new DataQualityReview
        {
            IssueKey = "tx:1:overdue-duration",
            TransactionId = 1,
            RuleCode = DataQualityService.OverdueDurationRuleCode,
            IsReviewed = true,
            ReviewedByUserId = 1
        });
        await db.SaveChangesAsync();

        var summary = await CreateService(db).GetSummaryAsync(new DataQualityQueryDto { OverdueMoreThanDays = 10 });

        Assert.Empty(summary.Issues);
    }

    [Fact]
    public async Task GetSummary_WhenIncludeReviewed_ReturnsReviewedAndUnreviewed()
    {
        await using var db = CreateDb(nameof(GetSummary_WhenIncludeReviewed_ReturnsReviewedAndUnreviewed));
        SeedUser(db);
        db.Transactions.Add(CreateTransaction(1, responseDueDate: new DateTime(2026, 6, 18), requiresResponse: true));
        db.Transactions.Add(CreateTransaction(2, responseDueDate: new DateTime(2026, 6, 17), requiresResponse: true));
        db.DataQualityReviews.Add(new DataQualityReview
        {
            IssueKey = "tx:1:overdue-duration",
            TransactionId = 1,
            RuleCode = DataQualityService.OverdueDurationRuleCode,
            IsReviewed = true,
            ReviewedByUserId = 1
        });
        await db.SaveChangesAsync();

        var summary = await CreateService(db).GetSummaryAsync(new DataQualityQueryDto
        {
            OverdueMoreThanDays = 10,
            IncludeReviewed = true
        });

        Assert.Equal(2, summary.Issues.Count);
        Assert.Contains(summary.Issues, x => x.IsReviewed);
        Assert.Contains(summary.Issues, x => !x.IsReviewed);
    }

    [Fact]
    public async Task GetSummary_WhenReviewedOnly_ReturnsOnlyReviewed()
    {
        await using var db = CreateDb(nameof(GetSummary_WhenReviewedOnly_ReturnsOnlyReviewed));
        SeedUser(db);
        db.Transactions.Add(CreateTransaction(1, responseDueDate: new DateTime(2026, 6, 18), requiresResponse: true));
        db.Transactions.Add(CreateTransaction(2, responseDueDate: new DateTime(2026, 6, 17), requiresResponse: true));
        db.DataQualityReviews.Add(new DataQualityReview
        {
            IssueKey = "tx:1:overdue-duration",
            TransactionId = 1,
            RuleCode = DataQualityService.OverdueDurationRuleCode,
            IsReviewed = true,
            ReviewedByUserId = 1
        });
        await db.SaveChangesAsync();

        var summary = await CreateService(db).GetSummaryAsync(new DataQualityQueryDto
        {
            OverdueMoreThanDays = 10,
            IncludeReviewed = true,
            ReviewedOnly = true
        });

        var issue = Assert.Single(summary.Issues);
        Assert.True(issue.IsReviewed);
        Assert.Equal(1, issue.TransactionId);
    }

    [Fact]
    public async Task MarkReviewed_UpsertsReviewRecord()
    {
        await using var db = CreateDb(nameof(MarkReviewed_UpsertsReviewRecord));
        SeedUser(db);
        var controller = CreateReviewsController(db);

        var result = await controller.MarkReviewed(new MarkDataQualityReviewRequest
        {
            IssueKey = "tx:1:overdue-duration",
            TransactionId = 1,
            RuleCode = DataQualityService.OverdueDurationRuleCode,
            Note = "تمت المراجعة"
        }, CancellationToken.None);

        Assert.IsType<NoContentResult>(result);
        var review = await db.DataQualityReviews.SingleAsync();
        Assert.True(review.IsReviewed);
        Assert.Equal("تمت المراجعة", review.ReviewNote);
    }

    [Fact]
    public async Task UnmarkReviewed_MakesIssueVisibleAgain()
    {
        await using var db = CreateDb(nameof(UnmarkReviewed_MakesIssueVisibleAgain));
        SeedUser(db);
        db.Transactions.Add(CreateTransaction(1, responseDueDate: new DateTime(2026, 6, 18), requiresResponse: true));
        db.DataQualityReviews.Add(new DataQualityReview
        {
            IssueKey = "tx:1:overdue-duration",
            TransactionId = 1,
            RuleCode = DataQualityService.OverdueDurationRuleCode,
            IsReviewed = true,
            ReviewedByUserId = 1
        });
        await db.SaveChangesAsync();
        var controller = CreateReviewsController(db);

        await controller.UnmarkReviewed("tx:1:overdue-duration", CancellationToken.None);
        var summary = await CreateService(db).GetSummaryAsync(new DataQualityQueryDto { OverdueMoreThanDays = 10 });

        var issue = Assert.Single(summary.Issues);
        Assert.False(issue.IsReviewed);
    }

    [Fact]
    public async Task MarkReviewed_WritesAuditLog()
    {
        await using var db = CreateDb(nameof(MarkReviewed_WritesAuditLog));
        SeedUser(db);
        var controller = CreateReviewsController(db);

        await controller.MarkReviewed(new MarkDataQualityReviewRequest
        {
            IssueKey = "tx:1:overdue-duration",
            TransactionId = 1,
            RuleCode = DataQualityService.OverdueDurationRuleCode
        }, CancellationToken.None);

        var audit = await db.AuditLogs.SingleAsync(x => x.Action == AuditAction.MarkDataQualityIssueReviewed);
        Assert.NotNull(audit.EntityId);
    }

    [Fact]
    public async Task UnmarkReviewed_WritesAuditLog()
    {
        await using var db = CreateDb(nameof(UnmarkReviewed_WritesAuditLog));
        SeedUser(db);
        db.DataQualityReviews.Add(new DataQualityReview
        {
            IssueKey = "tx:1:overdue-duration",
            TransactionId = 1,
            RuleCode = DataQualityService.OverdueDurationRuleCode,
            IsReviewed = true,
            ReviewedByUserId = 1
        });
        await db.SaveChangesAsync();
        var controller = CreateReviewsController(db);

        await controller.UnmarkReviewed("tx:1:overdue-duration", CancellationToken.None);

        Assert.True(await db.AuditLogs.AnyAsync(x => x.Action == AuditAction.UnmarkDataQualityIssueReviewed));
    }

    [Fact]
    public async Task GetSummary_WhenNullableDefaultsAreOmitted_UsesDefaultReviewAndLimitBehavior()
    {
        await using var db = CreateDb(nameof(GetSummary_WhenNullableDefaultsAreOmitted_UsesDefaultReviewAndLimitBehavior));
        SeedUser(db);
        db.Transactions.Add(CreateTransaction(1, responseDueDate: new DateTime(2026, 6, 18), requiresResponse: true));
        db.Transactions.Add(CreateTransaction(2, responseDueDate: new DateTime(2026, 6, 17), requiresResponse: true));
        db.DataQualityReviews.Add(new DataQualityReview
        {
            IssueKey = "tx:1:overdue-duration",
            TransactionId = 1,
            RuleCode = DataQualityService.OverdueDurationRuleCode,
            IsReviewed = true,
            ReviewedByUserId = 1
        });
        await db.SaveChangesAsync();

        var summary = await CreateService(db).GetSummaryAsync(new DataQualityQueryDto
        {
            OverdueMoreThanDays = 10
        });

        var issue = Assert.Single(summary.Issues);
        Assert.Equal(1, summary.TotalIssues);
        Assert.Equal(2, issue.TransactionId);
    }

    [Fact]
    public void Endpoints_RequireAuthenticationAndPermissions()
    {
        Assert.Contains(typeof(Microsoft.AspNetCore.Authorization.AuthorizeAttribute), typeof(DataQualityController).GetCustomAttributes(inherit: true).Select(x => x.GetType()));
        Assert.Contains(typeof(Microsoft.AspNetCore.Authorization.AuthorizeAttribute), typeof(DataQualityReviewsController).GetCustomAttributes(inherit: true).Select(x => x.GetType()));

        var summary = GetRequiredMethod(nameof(DataQualityController.GetSummary));
        var mark = GetRequiredMethod<DataQualityReviewsController>(nameof(DataQualityReviewsController.MarkReviewed));
        var unmark = GetRequiredMethod<DataQualityReviewsController>(nameof(DataQualityReviewsController.UnmarkReviewed));

        AssertPermission(summary, PermissionCode.DataQualityView);
        AssertPermission(mark, PermissionCode.DataQualityReview);
        AssertPermission(unmark, PermissionCode.DataQualityReview);
    }

    private static void AssertPermission(System.Reflection.MethodInfo method, PermissionCode permission)
    {
        var attribute = method.GetCustomAttributes(inherit: true)
            .OfType<RequirePermissionAttribute>()
            .Single();
        var field = typeof(RequirePermissionAttribute).GetField("_permission", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        Assert.NotNull(field);
        Assert.Equal(permission, field.GetValue(attribute));
    }

    private static System.Reflection.MethodInfo GetRequiredMethod(string name) =>
        GetRequiredMethod<DataQualityController>(name);

    private static System.Reflection.MethodInfo GetRequiredMethod<TController>(string name)
    {
        var method = typeof(TController).GetMethod(name);
        Assert.NotNull(method);
        return method;
    }

    private static AppDbContext CreateDb(string name) =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"data-quality-{name}-{Guid.NewGuid():N}")
            .Options);

    private static DataQualityService CreateService(AppDbContext db) =>
        new(db, new FixedClock(TodayUtc));

    private static DataQualityController CreateController(AppDbContext db)
    {
        var service = CreateService(db);
        return new DataQualityController(service);
    }

    private static DataQualityReviewsController CreateReviewsController(AppDbContext db)
    {
        var currentUser = new TestCurrentUser();
        return new DataQualityReviewsController(db, currentUser, new AuditService(db));
    }

    private static Transaction CreateTransaction(
        int id,
        DateTime? incomingDate = null,
        DateTime? responseDueDate = null,
        bool requiresResponse = false) => new()
        {
            Id = id,
            InternalTrackingNumber = $"TRK-{id:000}",
            IncomingNumber = $"IN-{id:000}",
            IncomingDate = incomingDate ?? new DateTime(2026, 7, 1),
            Subject = $"موضوع {id}",
            RequiresResponse = requiresResponse,
            ResponseDueDate = responseDueDate,
            Status = TransactionStatus.New,
            CreatedById = 1,
            CreatedAt = new DateTime(2026, 7, 1)
        };

    private static void SeedUser(AppDbContext db)
    {
        db.Users.Add(new User
        {
            Id = 1,
            Username = "admin",
            FullName = "مدير النظام",
            PasswordHash = "unused",
            Role = UserRole.Admin,
            IsActive = true
        });
    }

    private sealed class FixedClock(DateTime utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(utcNow);
    }

    private sealed class TestCurrentUser : ICurrentUserService
    {
        public int UserId => 1;
        public string Username => "admin";
        public UserRole Role => UserRole.Admin;
        public int? DepartmentId => null;
        public bool IsAuthenticated => true;
    }
}
