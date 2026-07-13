using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Uqeb.Api.Data;
using Uqeb.Api.DTOs.Transactions;
using Uqeb.Api.Helpers;
using Uqeb.Api.Models.Entities;
using Uqeb.Api.Models.Enums;
using Uqeb.Api.Services;
using Xunit;

namespace Uqeb.Api.Tests;

public class TransactionWorkspaceReadModelTests
{
    private sealed class StubTrackingNumberService : ITrackingNumberService
    {
        public Task<string> GenerateNextAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult("UQEB-2026-00001");
    }

    private sealed class TestCacheInvalidation : ICacheInvalidationService
    {
        public string DashboardSummaryKey => "dashboard";
        public string BuildDashboardSummaryKey() => DashboardSummaryKey;
        public string BuildDashboardFullKey() => "dashboard:full";
        public TimeSpan DashboardCacheDuration => TimeSpan.FromMinutes(1);
        public TimeSpan ReportsPageSummaryCacheDuration => TimeSpan.FromMinutes(1);
        public TimeSpan ReferenceDataCacheDuration => TimeSpan.FromMinutes(1);
        public string BuildReportsPageSummaryKey(DTOs.Reports.ReportFilterRequest? filter) => "reports";
        public string BuildDepartmentsKey(bool activeOnly) => $"departments-{activeOnly}";
        public string BuildCategoriesKey(bool activeOnly) => $"categories-{activeOnly}";
        public string BuildExternalPartiesKey(bool activeOnly) => $"parties-{activeOnly}";
        public void InvalidateOnTransactionChange() { }
        public void InvalidateReferenceData() { }
    }

    private sealed class TestCurrentUser : ICurrentUserService
    {
        public TestCurrentUser(UserRole role, int userId = 1, int? departmentId = null)
        {
            Role = role;
            UserId = userId;
            DepartmentId = departmentId;
        }

        public int UserId { get; }
        public string Username => "tester";
        public UserRole Role { get; }
        public int? DepartmentId { get; }
        public bool IsAuthenticated => true;
    }

    private static async Task<(TransactionService Service, AppDbContext Db)> CreateServiceAsync(string dbName)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(dbName)
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        var db = new AppDbContext(options);
        db.Users.Add(new User
        {
            Id = 1,
            Username = "admin",
            PasswordHash = "hash",
            FullName = "Admin",
            Role = UserRole.Admin,
            IsActive = true
        });
        db.Users.Add(new User
        {
            Id = 2,
            Username = "dept-user",
            PasswordHash = "hash",
            FullName = "Dept User",
            Role = UserRole.DepartmentUser,
            DepartmentId = 10,
            IsActive = true
        });
        db.Departments.Add(new Department { Id = 10, Name = "المالية", NameNormalized = "المالية", IsActive = true });
        db.Departments.Add(new Department { Id = 20, Name = "الموارد", NameNormalized = "الموارد", IsActive = true });
        await db.SaveChangesAsync();

        var service = new TransactionService(
            db,
            new AuditService(db),
            new StubTrackingNumberService(),
            new TestCacheInvalidation(),
            new RecurringTransactionTemplateService(db, new AuditService(db), new StubTrackingNumberService()));

        return (service, db);
    }

    private static async Task<Transaction> SeedTransactionAsync(AppDbContext db, int id = 100)
    {
        var transaction = new Transaction
        {
            Id = id,
            InternalTrackingNumber = $"UQEB-2026-{id:00000}",
            IncomingNumber = $"IN-{id}",
            IncomingDate = DateTime.UtcNow.AddDays(-7),
            Subject = "معاملة مساحة العمل",
            IncomingSourceType = IncomingSourceType.External,
            IncomingFrom = "جهة",
            RequiresResponse = true,
            ResponseType = ResponseType.External,
            ResponseDueDate = DateTime.UtcNow.AddDays(3),
            ResponseDueDays = 10,
            Priority = Priority.Normal,
            Status = TransactionStatus.InProgress,
            CreatedById = 1,
            CreatedAt = DateTime.UtcNow.AddDays(-7)
        };
        db.Transactions.Add(transaction);
        await db.SaveChangesAsync();
        return transaction;
    }

    [Fact]
    public async Task GetWorkspaceAsync_returns_null_when_transaction_missing()
    {
        var (service, _) = await CreateServiceAsync(nameof(GetWorkspaceAsync_returns_null_when_transaction_missing));
        var result = await service.GetWorkspaceAsync(999, new TestCurrentUser(UserRole.Admin));
        Assert.Null(result);
    }

    [Fact]
    public async Task GetWorkspaceAsync_returns_null_when_department_user_has_no_assignment()
    {
        var (service, db) = await CreateServiceAsync(nameof(GetWorkspaceAsync_returns_null_when_department_user_has_no_assignment));
        await SeedTransactionAsync(db);

        var result = await service.GetWorkspaceAsync(100, new TestCurrentUser(UserRole.DepartmentUser, userId: 2, departmentId: 10));
        Assert.Null(result);
    }

    [Fact]
    public void BuildTemporalFacts_tolerates_legacy_assignment_status_values()
    {
        var transaction = new Transaction
        {
            Id = 100,
            IncomingDate = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            RequiresResponse = false,
            ResponseCompleted = false,
            Status = TransactionStatus.InProgress,
            CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
        };
        var assignments = new List<AssignmentDto>
        {
            new()
            {
                Id = 10,
                DepartmentId = 10,
                DepartmentName = "المالية",
                RequiresReply = true,
                ReplyStatus = "LegacyPending",
                Status = "LegacyActive",
                DueDate = new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc)
            }
        };

        var facts = TransactionWorkspaceHelper.BuildTemporalFacts(
            transaction,
            assignments,
            new DateTime(2026, 1, 5, 0, 0, 0, DateTimeKind.Utc));

        Assert.True(facts.IsOverdue);
        Assert.Equal(3, facts.DaysOverdue);
    }

    [Fact]
    public async Task SearchAsync_DepartmentUser_ReturnsOnlyOwnDepartmentTransactions()
    {
        var (service, db) = await CreateServiceAsync(nameof(SearchAsync_DepartmentUser_ReturnsOnlyOwnDepartmentTransactions));
        var own = await SeedTransactionAsync(db, 101);
        var other = await SeedTransactionAsync(db, 102);
        var cancelled = await SeedTransactionAsync(db, 103);
        cancelled.Status = TransactionStatus.Cancelled;
        db.Assignments.AddRange(
            new Assignment
            {
                TransactionId = own.Id,
                DepartmentId = 10,
                AssignedDate = DateTime.UtcNow,
                RequiresReply = true,
                ReplyStatus = ReplyStatus.Pending,
                Status = AssignmentStatus.Active,
                CreatedById = 1
            },
            new Assignment
            {
                TransactionId = other.Id,
                DepartmentId = 20,
                AssignedDate = DateTime.UtcNow,
                RequiresReply = true,
                ReplyStatus = ReplyStatus.Pending,
                Status = AssignmentStatus.Active,
                CreatedById = 1
            },
            new Assignment
            {
                TransactionId = cancelled.Id,
                DepartmentId = 10,
                AssignedDate = DateTime.UtcNow,
                RequiresReply = true,
                ReplyStatus = ReplyStatus.Pending,
                Status = AssignmentStatus.Cancelled,
                CreatedById = 1
            });
        await db.SaveChangesAsync();

        var result = await service.SearchAsync(
            new TransactionSearchRequest { Page = 1, PageSize = 20 },
            new TestCurrentUser(UserRole.DepartmentUser, userId: 2, departmentId: 10));

        Assert.Contains(result.Items, tx => tx.Id == own.Id);
        Assert.DoesNotContain(result.Items, tx => tx.Id == other.Id);
        Assert.DoesNotContain(result.Items, tx => tx.Id == cancelled.Id);
    }

    [Fact]
    public async Task SearchAsync_DefaultStatusScope_ReturnsActiveOnly()
    {
        var (service, db) = await CreateServiceAsync(nameof(SearchAsync_DefaultStatusScope_ReturnsActiveOnly));
        var active = await SeedTransactionAsync(db, 110);
        var closed = await SeedTransactionAsync(db, 111);
        var cancelled = await SeedTransactionAsync(db, 112);
        var archived = await SeedTransactionAsync(db, 113);
        closed.Status = TransactionStatus.Closed;
        cancelled.Status = TransactionStatus.Cancelled;
        archived.Status = TransactionStatus.Archived;
        await db.SaveChangesAsync();

        var result = await service.SearchAsync(
            new TransactionSearchRequest { Page = 1, PageSize = 20 },
            new TestCurrentUser(UserRole.Admin));

        Assert.Contains(result.Items, tx => tx.Id == active.Id);
        Assert.DoesNotContain(result.Items, tx => tx.Id == closed.Id);
        Assert.DoesNotContain(result.Items, tx => tx.Id == cancelled.Id);
        Assert.DoesNotContain(result.Items, tx => tx.Id == archived.Id);
    }

    [Fact]
    public async Task SearchAsync_ActiveStatusScope_ExcludesClosedCancelledAndArchived()
    {
        var (service, db) = await CreateServiceAsync(nameof(SearchAsync_ActiveStatusScope_ExcludesClosedCancelledAndArchived));
        var active = await SeedTransactionAsync(db, 114);
        var closed = await SeedTransactionAsync(db, 115);
        var cancelled = await SeedTransactionAsync(db, 116);
        var archived = await SeedTransactionAsync(db, 117);
        closed.Status = TransactionStatus.Closed;
        cancelled.Status = TransactionStatus.Cancelled;
        archived.Status = TransactionStatus.Archived;
        await db.SaveChangesAsync();

        var result = await service.SearchAsync(
            new TransactionSearchRequest { StatusScope = "active", Page = 1, PageSize = 20 },
            new TestCurrentUser(UserRole.Admin));

        Assert.Contains(result.Items, tx => tx.Id == active.Id);
        Assert.DoesNotContain(result.Items, tx => tx.Id == closed.Id);
        Assert.DoesNotContain(result.Items, tx => tx.Id == cancelled.Id);
        Assert.DoesNotContain(result.Items, tx => tx.Id == archived.Id);
    }

    [Fact]
    public async Task SearchAsync_ClosedStatusScope_ReturnsClosedOnly()
    {
        var (service, db) = await CreateServiceAsync(nameof(SearchAsync_ClosedStatusScope_ReturnsClosedOnly));
        var active = await SeedTransactionAsync(db, 116);
        var closed = await SeedTransactionAsync(db, 117);
        closed.Status = TransactionStatus.Closed;
        await db.SaveChangesAsync();

        var result = await service.SearchAsync(
            new TransactionSearchRequest { StatusScope = "closed", Page = 1, PageSize = 20 },
            new TestCurrentUser(UserRole.Admin));

        Assert.DoesNotContain(result.Items, tx => tx.Id == active.Id);
        Assert.Contains(result.Items, tx => tx.Id == closed.Id);
        Assert.All(result.Items, tx => Assert.Equal(nameof(TransactionStatus.Closed), tx.Status));
    }

    [Fact]
    public async Task SearchAsync_AllStatusScope_ReturnsActiveAndClosed()
    {
        var (service, db) = await CreateServiceAsync(nameof(SearchAsync_AllStatusScope_ReturnsActiveAndClosed));
        var active = await SeedTransactionAsync(db, 118);
        var closed = await SeedTransactionAsync(db, 119);
        closed.Status = TransactionStatus.Closed;
        await db.SaveChangesAsync();

        var result = await service.SearchAsync(
            new TransactionSearchRequest { StatusScope = "all", Page = 1, PageSize = 20 },
            new TestCurrentUser(UserRole.Admin));

        Assert.Contains(result.Items, tx => tx.Id == active.Id);
        Assert.Contains(result.Items, tx => tx.Id == closed.Id);
    }

    [Fact]
    public async Task SearchAsync_DepartmentUser_AllStatusScope_ReturnsOnlyOwnDepartmentTransactions()
    {
        var (service, db) = await CreateServiceAsync(nameof(SearchAsync_DepartmentUser_AllStatusScope_ReturnsOnlyOwnDepartmentTransactions));
        var ownClosed = await SeedTransactionAsync(db, 120);
        var otherClosed = await SeedTransactionAsync(db, 121);
        ownClosed.Status = TransactionStatus.Closed;
        otherClosed.Status = TransactionStatus.Closed;
        db.Assignments.AddRange(
            new Assignment
            {
                TransactionId = ownClosed.Id,
                DepartmentId = 10,
                AssignedDate = DateTime.UtcNow,
                RequiresReply = true,
                ReplyStatus = ReplyStatus.Pending,
                Status = AssignmentStatus.Active,
                CreatedById = 1
            },
            new Assignment
            {
                TransactionId = otherClosed.Id,
                DepartmentId = 20,
                AssignedDate = DateTime.UtcNow,
                RequiresReply = true,
                ReplyStatus = ReplyStatus.Pending,
                Status = AssignmentStatus.Active,
                CreatedById = 1
            });
        await db.SaveChangesAsync();

        var result = await service.SearchAsync(
            new TransactionSearchRequest { StatusScope = "all", Page = 1, PageSize = 20 },
            new TestCurrentUser(UserRole.DepartmentUser, userId: 2, departmentId: 10));

        Assert.Contains(result.Items, tx => tx.Id == ownClosed.Id);
        Assert.DoesNotContain(result.Items, tx => tx.Id == otherClosed.Id);
    }

    [Fact]
    public async Task SearchAsync_InvalidStatusScope_ThrowsInvalidOperation()
    {
        var (service, db) = await CreateServiceAsync(nameof(SearchAsync_InvalidStatusScope_ThrowsInvalidOperation));
        await SeedTransactionAsync(db, 122);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.SearchAsync(
                new TransactionSearchRequest { StatusScope = "invalid", Page = 1, PageSize = 20 },
                new TestCurrentUser(UserRole.Admin)));

        Assert.Equal("نطاق حالة المعاملات غير صالح.", exception.Message);
    }

    [Fact]
    public async Task SearchAsync_DepartmentUserWithoutDepartment_ThrowsUnauthorized()
    {
        var (service, _) = await CreateServiceAsync(nameof(SearchAsync_DepartmentUserWithoutDepartment_ThrowsUnauthorized));

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            service.SearchAsync(
                new TransactionSearchRequest { Page = 1, PageSize = 20 },
                new TestCurrentUser(UserRole.DepartmentUser, userId: 2)));
    }

    [Fact]
    public async Task GetBasicByIdAsync_DepartmentUserCannotOpenOtherDepartmentTransaction()
    {
        var (service, db) = await CreateServiceAsync(nameof(GetBasicByIdAsync_DepartmentUserCannotOpenOtherDepartmentTransaction));
        var other = await SeedTransactionAsync(db, 104);
        db.Assignments.Add(new Assignment
        {
            TransactionId = other.Id,
            DepartmentId = 20,
            AssignedDate = DateTime.UtcNow,
            RequiresReply = true,
            ReplyStatus = ReplyStatus.Pending,
            Status = AssignmentStatus.Active,
            CreatedById = 1
        });
        await db.SaveChangesAsync();

        var result = await service.GetBasicByIdAsync(
            other.Id,
            new TestCurrentUser(UserRole.DepartmentUser, userId: 2, departmentId: 10));

        Assert.Null(result);
    }

    [Fact]
    public async Task GetBasicByIdAsync_DepartmentUserWithoutDepartment_ThrowsUnauthorized()
    {
        var (service, db) = await CreateServiceAsync(nameof(GetBasicByIdAsync_DepartmentUserWithoutDepartment_ThrowsUnauthorized));
        var transaction = await SeedTransactionAsync(db, 105);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            service.GetBasicByIdAsync(
                transaction.Id,
                new TestCurrentUser(UserRole.DepartmentUser, userId: 2)));
    }

    [Fact]
    public async Task GetWorkspaceAsync_returns_consolidated_payload_for_admin()
    {
        var (service, db) = await CreateServiceAsync(nameof(GetWorkspaceAsync_returns_consolidated_payload_for_admin));
        var transaction = await SeedTransactionAsync(db);

        db.Assignments.Add(new Assignment
        {
            TransactionId = transaction.Id,
            DepartmentId = 10,
            AssignedDate = DateTime.UtcNow.AddDays(-2),
            RequiresReply = true,
            ReplyStatus = ReplyStatus.Pending,
            Status = AssignmentStatus.Active,
            DueDate = DateTime.UtcNow.AddDays(1),
            CreatedById = 1
        });
        db.FollowUps.Add(new FollowUp
        {
            TransactionId = transaction.Id,
            FollowUpDate = DateTime.UtcNow.AddDays(-1),
            RequiresReply = false,
            ReplyStatus = ReplyStatus.Pending,
            CreatedById = 1
        });
        db.Attachments.Add(new Attachment
        {
            TransactionId = transaction.Id,
            OriginalFileName = "doc.pdf",
            StoredFileName = "stored.pdf",
            FilePath = "/tmp/doc.pdf",
            ContentType = "application/pdf",
            FileSize = 100,
            UploadedById = 1,
            UploadedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var result = await service.GetWorkspaceAsync(transaction.Id, new TestCurrentUser(UserRole.Admin));

        Assert.NotNull(result);
        Assert.Equal(transaction.Id, result!.Transaction.Id);
        Assert.Single(result.Assignments);
        Assert.Single(result.FollowUps);
        Assert.Single(result.Attachments);
        Assert.True(result.TemporalFacts.IsOpen);
        Assert.True(result.AllowedActions.CanEdit);
        Assert.True(result.AllowedActions.ShowMutationActions);
        Assert.True(result.AllowedActions.CanRegisterResponse);
    }

    [Fact]
    public async Task GetWorkspaceAsync_handles_assignment_with_missing_related_department_and_user()
    {
        var (service, db) = await CreateServiceAsync(nameof(GetWorkspaceAsync_handles_assignment_with_missing_related_department_and_user));
        var transaction = await SeedTransactionAsync(db, 205);
        transaction.IncomingDate = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        db.Assignments.Add(new Assignment
        {
            TransactionId = transaction.Id,
            DepartmentId = 999,
            AssignedDate = new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc),
            RequiresReply = true,
            ReplyStatus = ReplyStatus.Pending,
            Status = AssignmentStatus.Active,
            CreatedById = 999,
            CreatedAt = new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc)
        });
        db.DepartmentResponses.Add(new DepartmentResponse
        {
            TransactionId = transaction.Id,
            DepartmentId = 999,
            ResponseText = "إفادة إدارة محذوفة",
            Status = DepartmentResponseStatus.SubmittedForReview,
            SubmittedByUserId = 1,
            SubmittedAt = new DateTime(2026, 1, 6, 0, 0, 0, DateTimeKind.Utc),
            CreatedAt = new DateTime(2026, 1, 6, 0, 0, 0, DateTimeKind.Utc)
        });
        await db.SaveChangesAsync();

        var result = await service.GetWorkspaceAsync(transaction.Id, new TestCurrentUser(UserRole.Admin));

        Assert.NotNull(result);
        var assignment = Assert.Single(result!.Assignments);
        Assert.Equal("إدارة غير معروفة", assignment.DepartmentName);
        Assert.Equal("", assignment.CreatedByName);
        // The response is only submitted-for-review (not approved), so the assignment's own
        // ReplyDate is still unset and completion fields must not be derived from SubmittedAt.
        Assert.Null(assignment.ResponseDate);
        Assert.Null(assignment.DepartmentCompletionDays);
        Assert.Contains("إدارة غير معروفة", result.Transaction.PendingDepartmentNames);
        Assert.NotNull(result.TemporalFacts);
    }

    [Fact]
    public async Task GetWorkspaceAsync_includes_data_for_department_user_with_assignment()
    {
        var (service, db) = await CreateServiceAsync(nameof(GetWorkspaceAsync_includes_data_for_department_user_with_assignment));
        var transaction = await SeedTransactionAsync(db);
        db.Assignments.Add(new Assignment
        {
            TransactionId = transaction.Id,
            DepartmentId = 10,
            AssignedDate = DateTime.UtcNow.AddDays(-1),
            RequiresReply = true,
            ReplyStatus = ReplyStatus.Pending,
            Status = AssignmentStatus.Active,
            CreatedById = 1
        });
        await db.SaveChangesAsync();

        var result = await service.GetWorkspaceAsync(
            transaction.Id,
            new TestCurrentUser(UserRole.DepartmentUser, userId: 2, departmentId: 10));

        Assert.NotNull(result);
        Assert.Single(result!.Assignments);
        Assert.False(result.AllowedActions.ShowMutationActions);
        Assert.False(result.AllowedActions.CanReply);
    }

    [Fact]
    public async Task SearchAsync_OverdueOnly_IncludesAssignmentRepliedAfterDueDate()
    {
        var (service, db) = await CreateServiceAsync(nameof(SearchAsync_OverdueOnly_IncludesAssignmentRepliedAfterDueDate));
        var completedLate = await SeedTransactionAsync(db, 120);
        completedLate.RequiresResponse = false;
        completedLate.ResponseDueDate = null;
        var notOverdue = await SeedTransactionAsync(db, 121);
        notOverdue.RequiresResponse = false;
        notOverdue.ResponseDueDate = null;
        await db.SaveChangesAsync();

        db.Assignments.Add(new Assignment
        {
            TransactionId = completedLate.Id,
            DepartmentId = 10,
            AssignedDate = DateTime.UtcNow.AddDays(-10),
            DueDate = DateTime.UtcNow.AddDays(-5),
            RequiresReply = true,
            ReplyStatus = ReplyStatus.Replied,
            ReplyDate = DateTime.UtcNow.AddDays(-1),
            Status = AssignmentStatus.Completed,
            CreatedById = 1
        });
        db.Assignments.Add(new Assignment
        {
            TransactionId = notOverdue.Id,
            DepartmentId = 10,
            AssignedDate = DateTime.UtcNow.AddDays(-10),
            DueDate = DateTime.UtcNow.AddDays(-5),
            RequiresReply = true,
            ReplyStatus = ReplyStatus.Replied,
            ReplyDate = DateTime.UtcNow.AddDays(-6),
            Status = AssignmentStatus.Completed,
            CreatedById = 1
        });
        await db.SaveChangesAsync();

        var result = await service.SearchAsync(
            new TransactionSearchRequest { OverdueOnly = true, Page = 1, PageSize = 20 },
            new TestCurrentUser(UserRole.Admin));

        var lateItem = Assert.Single(result.Items, tx => tx.Id == completedLate.Id);
        Assert.True(lateItem.IsOverdue);
        Assert.DoesNotContain(result.Items, tx => tx.Id == notOverdue.Id);
    }

    [Fact]
    public async Task SearchAsync_OverdueFilters_ExcludeOnTimeResponseCompletedWithLaterClosure()
    {
        var (service, db) = await CreateServiceAsync(nameof(SearchAsync_OverdueFilters_ExcludeOnTimeResponseCompletedWithLaterClosure));
        var dueDate = new DateTime(2026, 1, 5, 0, 0, 0, DateTimeKind.Utc);
        var transaction = await SeedTransactionAsync(db, 122);
        transaction.RequiresResponse = true;
        transaction.ResponseCompleted = true;
        transaction.ResponseCompletedDate = dueDate;
        transaction.ResponseDueDate = dueDate;
        transaction.ClosedAt = dueDate.AddDays(7);
        transaction.Status = TransactionStatus.Closed;
        await db.SaveChangesAsync();

        var overdueOnly = await service.SearchAsync(
            new TransactionSearchRequest { StatusScope = "all", OverdueOnly = true, Page = 1, PageSize = 20 },
            new TestCurrentUser(UserRole.Admin));
        var responseOverdue = await service.SearchAsync(
            new TransactionSearchRequest { StatusScope = "all", ResponseOverdue = true, Page = 1, PageSize = 20 },
            new TestCurrentUser(UserRole.Admin));
        var all = await service.SearchAsync(
            new TransactionSearchRequest { StatusScope = "all", Page = 1, PageSize = 20 },
            new TestCurrentUser(UserRole.Admin));

        Assert.DoesNotContain(overdueOnly.Items, tx => tx.Id == transaction.Id);
        Assert.DoesNotContain(responseOverdue.Items, tx => tx.Id == transaction.Id);
        var row = Assert.Single(all.Items, tx => tx.Id == transaction.Id);
        Assert.False(row.IsResponseOverdue);
        Assert.False(row.IsOverdue);
    }

    [Fact]
    public async Task SearchAsync_OverdueFilters_IncludeLateResponseCompletedWithLaterClosure()
    {
        var (service, db) = await CreateServiceAsync(nameof(SearchAsync_OverdueFilters_IncludeLateResponseCompletedWithLaterClosure));
        var dueDate = new DateTime(2026, 1, 5, 0, 0, 0, DateTimeKind.Utc);
        var transaction = await SeedTransactionAsync(db, 123);
        transaction.RequiresResponse = true;
        transaction.ResponseCompleted = true;
        transaction.ResponseCompletedDate = dueDate.AddDays(2);
        transaction.ResponseDueDate = dueDate;
        transaction.ClosedAt = dueDate.AddDays(7);
        transaction.Status = TransactionStatus.Closed;
        await db.SaveChangesAsync();

        var overdueOnly = await service.SearchAsync(
            new TransactionSearchRequest { StatusScope = "all", OverdueOnly = true, Page = 1, PageSize = 20 },
            new TestCurrentUser(UserRole.Admin));
        var responseOverdue = await service.SearchAsync(
            new TransactionSearchRequest { StatusScope = "all", ResponseOverdue = true, Page = 1, PageSize = 20 },
            new TestCurrentUser(UserRole.Admin));

        var overdueRow = Assert.Single(overdueOnly.Items, tx => tx.Id == transaction.Id);
        var responseRow = Assert.Single(responseOverdue.Items, tx => tx.Id == transaction.Id);
        Assert.True(overdueRow.IsResponseOverdue);
        Assert.True(overdueRow.IsOverdue);
        Assert.True(responseRow.IsResponseOverdue);
    }

    [Fact]
    public async Task SearchAsync_GlobalSearch_FindsByIncomingNumberSubjectPartyDepartmentAndOutgoingNumber()
    {
        var (service, db) = await CreateServiceAsync(nameof(SearchAsync_GlobalSearch_FindsByIncomingNumberSubjectPartyDepartmentAndOutgoingNumber));
        var party = new ExternalParty { Id = 30, Name = "جهة الاختبار", NameNormalized = ReferenceNameNormalizer.NormalizeKey("جهة الاختبار") };
        db.ExternalParties.Add(party);
        var transaction = await SeedTransactionAsync(db, 130);
        transaction.IncomingNumber = "IN-GLOBAL-130";
        transaction.Subject = "موضوع البحث الشامل";
        transaction.IncomingFromPartyId = party.Id;
        transaction.OutgoingNumber = "OUT-GLOBAL-9";
        db.TransactionOutgoingDepartments.Add(new TransactionOutgoingDepartment
        {
            TransactionId = transaction.Id,
            DepartmentId = 20,
            CreatedById = 1
        });
        await db.SaveChangesAsync();

        await AssertSearchReturnsAsync(service, "GLOBAL-130", transaction.Id);
        await AssertSearchReturnsAsync(service, "البحث الشامل", transaction.Id);
        await AssertSearchReturnsAsync(service, "جهة الاختبار", transaction.Id);
        await AssertSearchReturnsAsync(service, "الموارد", transaction.Id);
        await AssertSearchReturnsAsync(service, "OUT-GLOBAL", transaction.Id);
    }

    [Fact]
    public async Task SearchAsync_GlobalSearch_FindsResponseAndFollowUpText()
    {
        var (service, db) = await CreateServiceAsync(nameof(SearchAsync_GlobalSearch_FindsResponseAndFollowUpText));
        var transaction = await SeedTransactionAsync(db, 131);
        transaction.UpdatedById = 2;
        db.DepartmentResponses.Add(new DepartmentResponse
        {
            TransactionId = transaction.Id,
            DepartmentId = 10,
            ResponseText = "إفادة تفصيلية قابلة للبحث",
            SubmittedByUserId = 2
        });
        db.FollowUps.Add(new FollowUp
        {
            TransactionId = transaction.Id,
            FollowUpNumber = "FU-GLOBAL-1",
            FollowUpDate = DateTime.UtcNow.Date,
            Notes = "تعقيب مهم قابل للبحث",
            RequiresReply = false,
            CreatedById = 1
        });
        db.Attachments.Add(new Attachment
        {
            TransactionId = transaction.Id,
            OriginalFileName = "مرفق-بحث-شامل.pdf",
            StoredFileName = "stored.pdf",
            FilePath = "/tmp/stored.pdf",
            UploadedById = 1
        });
        await db.SaveChangesAsync();

        await AssertSearchReturnsAsync(service, "تفصيلية", transaction.Id);
        await AssertSearchReturnsAsync(service, "تعقيب مهم", transaction.Id);
        await AssertSearchReturnsAsync(service, "مرفق-بحث", transaction.Id);
        await AssertSearchReturnsAsync(service, "Admin", transaction.Id);
        await AssertSearchReturnsAsync(service, "Dept User", transaction.Id);
    }

    [Fact]
    public async Task SearchAsync_GlobalSearch_FindsStatusAndPriorityLabels()
    {
        var (service, db) = await CreateServiceAsync(nameof(SearchAsync_GlobalSearch_FindsStatusAndPriorityLabels));
        var transaction = await SeedTransactionAsync(db, 139);
        transaction.Status = TransactionStatus.ResponseCompleted;
        transaction.Priority = Priority.VeryUrgent;
        await db.SaveChangesAsync();

        await AssertSearchReturnsAsync(service, "تمت الافادة", transaction.Id);
        await AssertSearchReturnsAsync(service, "عاجل جدا", transaction.Id);
    }

    [Fact]
    public async Task SearchAsync_GlobalSearch_ComposesWithFiltersAndPaginationCounts()
    {
        var (service, db) = await CreateServiceAsync(nameof(SearchAsync_GlobalSearch_ComposesWithFiltersAndPaginationCounts));
        var first = await SeedTransactionAsync(db, 132);
        first.Subject = "مطابقة مشتركة";
        first.Status = TransactionStatus.New;
        var second = await SeedTransactionAsync(db, 133);
        second.Subject = "مطابقة مشتركة";
        second.Status = TransactionStatus.Closed;
        second.ClosedAt = DateTime.UtcNow.Date;
        var third = await SeedTransactionAsync(db, 134);
        third.Subject = "مطابقة مشتركة";
        third.Status = TransactionStatus.New;
        await db.SaveChangesAsync();

        var filtered = await service.SearchAsync(
            new TransactionSearchRequest
            {
                SearchText = "مطابقة",
                Status = nameof(TransactionStatus.New),
                StatusScope = "all",
                Page = 1,
                PageSize = 1,
                SortBy = "IncomingNumber",
                SortDesc = false
            },
            new TestCurrentUser(UserRole.Admin));

        Assert.Equal(2, filtered.TotalCount);
        Assert.Equal(2, filtered.TotalPages);
        Assert.Single(filtered.Items);
        Assert.DoesNotContain(filtered.Items, tx => tx.Id == second.Id);
    }

    [Fact]
    public async Task SearchAsync_GlobalSearch_NormalizesArabicHamzaAndDigits()
    {
        var (service, db) = await CreateServiceAsync(nameof(SearchAsync_GlobalSearch_NormalizesArabicHamzaAndDigits));
        var transaction = await SeedTransactionAsync(db, 135);
        transaction.Subject = "أرشيف 123";
        await db.SaveChangesAsync();

        await AssertSearchReturnsAsync(service, "ارشيف ١٢٣", transaction.Id);
    }

    [Fact]
    public async Task SearchAsync_GlobalSearch_FindsDateWithArabicAndEnglishDigitsIgnoringTimeOfDay()
    {
        var (service, db) = await CreateServiceAsync(nameof(SearchAsync_GlobalSearch_FindsDateWithArabicAndEnglishDigitsIgnoringTimeOfDay));
        var transaction = await SeedTransactionAsync(db, 138);
        transaction.IncomingDate = new DateTime(2026, 1, 2, 18, 45, 0, DateTimeKind.Utc);
        await db.SaveChangesAsync();

        await AssertSearchReturnsAsync(service, "2026-01-02", transaction.Id);
        await AssertSearchReturnsAsync(service, "٢٠٢٦٠١٠٢", transaction.Id);
    }

    [Fact]
    public async Task SearchAsync_GlobalSearch_RespectsDepartmentUserScope()
    {
        var (service, db) = await CreateServiceAsync(nameof(SearchAsync_GlobalSearch_RespectsDepartmentUserScope));
        var visible = await SeedTransactionAsync(db, 136);
        visible.Subject = "سرية البحث";
        var hidden = await SeedTransactionAsync(db, 137);
        hidden.Subject = "سرية البحث";
        db.Assignments.Add(new Assignment
        {
            TransactionId = visible.Id,
            DepartmentId = 10,
            AssignedDate = DateTime.UtcNow.Date,
            RequiresReply = true,
            ReplyStatus = ReplyStatus.Pending,
            Status = AssignmentStatus.Active,
            CreatedById = 1
        });
        await db.SaveChangesAsync();

        var result = await service.SearchAsync(
            new TransactionSearchRequest { SearchText = "سرية البحث", StatusScope = "all", Page = 1, PageSize = 20 },
            new TestCurrentUser(UserRole.DepartmentUser, userId: 2, departmentId: 10));

        var item = Assert.Single(result.Items);
        Assert.Equal(visible.Id, item.Id);
        Assert.DoesNotContain(result.Items, tx => tx.Id == hidden.Id);
    }

    private static async Task AssertSearchReturnsAsync(TransactionService service, string searchText, int transactionId)
    {
        var result = await service.SearchAsync(
            new TransactionSearchRequest { SearchText = searchText, StatusScope = "all", Page = 1, PageSize = 20 },
            new TestCurrentUser(UserRole.Admin));

        Assert.Contains(result.Items, tx => tx.Id == transactionId);
    }

    [Fact]
    public async Task GetWorkspaceAsync_does_not_include_audit_logs()
    {
        var (service, db) = await CreateServiceAsync(nameof(GetWorkspaceAsync_does_not_include_audit_logs));
        var transaction = await SeedTransactionAsync(db);
        db.AuditLogs.Add(new AuditLog
        {
            TransactionId = transaction.Id,
            UserId = 1,
            Action = AuditAction.Create,
            EntityName = "Transaction",
            EntityId = transaction.Id,
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var result = await service.GetWorkspaceAsync(transaction.Id, new TestCurrentUser(UserRole.Admin));

        Assert.NotNull(result);
        Assert.Empty(result!.Transaction.AuditLogs);
    }
}
