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

public class AssignmentResponseDatesTests
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

        db.Users.Add(new User { Id = 1, Username = "admin", PasswordHash = "h", FullName = "Admin", Role = UserRole.Admin, IsActive = true });
        db.Users.Add(new User { Id = 2, Username = "dept", PasswordHash = "h", FullName = "Dept", Role = UserRole.DepartmentUser, DepartmentId = 10, IsActive = true });
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

    private static async Task<Transaction> SeedTransactionAsync(AppDbContext db, int id = 100,
        DateTime? incomingDate = null)
    {
        var t = new Transaction
        {
            Id = id,
            InternalTrackingNumber = $"UQEB-{id}",
            IncomingNumber = $"IN-{id}",
            IncomingDate = incomingDate ?? new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            Subject = "معاملة",
            IncomingSourceType = IncomingSourceType.External,
            IncomingFrom = "جهة",
            RequiresResponse = true,
            ResponseType = ResponseType.External,
            Priority = Priority.Normal,
            Status = TransactionStatus.Assigned,
            CreatedById = 1,
            CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
        };
        db.Transactions.Add(t);
        await db.SaveChangesAsync();
        return t;
    }

    [Fact]
    public async Task GetAssignmentsAsync_includes_LetterNumber()
    {
        var (service, db) = await CreateServiceAsync(nameof(GetAssignmentsAsync_includes_LetterNumber));
        var t = await SeedTransactionAsync(db, 1);

        db.Assignments.Add(new Assignment
        {
            TransactionId = 1,
            DepartmentId = 10,
            AssignedDate = new DateTime(2026, 1, 5, 0, 0, 0, DateTimeKind.Utc),
            LetterNumber = "خ-2026/100",
            RequiresReply = true,
            ReplyStatus = ReplyStatus.Pending,
            Status = AssignmentStatus.Active,
            CreatedById = 1,
            CreatedAt = t.CreatedAt
        });
        await db.SaveChangesAsync();

        var result = await service.GetAssignmentsAsync(1, new TestCurrentUser(UserRole.Admin));

        Assert.NotNull(result);
        Assert.Single(result);
        Assert.Equal("خ-2026/100", result[0].LetterNumber);
    }

    [Fact]
    public async Task GetAssignmentsAsync_calculates_DepartmentCompletionDays_from_assignment_ReplyDate()
    {
        // Regression test: completion date/days must come from the assignment's own
        // (approved) ReplyDate, not from a DepartmentResponse.SubmittedAt draft/pending
        // timestamp — a response can be submitted for review long before it's approved.
        var (service, db) = await CreateServiceAsync(nameof(GetAssignmentsAsync_calculates_DepartmentCompletionDays_from_assignment_ReplyDate));
        var incomingDate = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        await SeedTransactionAsync(db, 1, incomingDate);

        var replyDate = new DateTime(2026, 1, 11, 0, 0, 0, DateTimeKind.Utc);
        db.Assignments.Add(new Assignment
        {
            TransactionId = 1,
            DepartmentId = 10,
            AssignedDate = incomingDate,
            RequiresReply = true,
            ReplyStatus = ReplyStatus.Replied,
            ReplyDate = replyDate,
            Status = AssignmentStatus.Completed,
            CreatedById = 1,
            CreatedAt = incomingDate
        });

        // A much later SubmittedAt on the backing response must NOT leak into ResponseDate.
        var submittedAt = new DateTime(2026, 1, 20, 0, 0, 0, DateTimeKind.Utc);
        db.DepartmentResponses.Add(new DepartmentResponse
        {
            TransactionId = 1,
            DepartmentId = 10,
            ResponseText = "إفادة المالية",
            Status = DepartmentResponseStatus.Approved,
            SubmittedByUserId = 1,
            SubmittedAt = submittedAt,
            CreatedAt = submittedAt
        });
        await db.SaveChangesAsync();

        var result = await service.GetAssignmentsAsync(1, new TestCurrentUser(UserRole.Admin));

        Assert.NotNull(result);
        Assert.Single(result);
        Assert.Equal(replyDate, result[0].ResponseDate);
        Assert.Equal(10, result[0].DepartmentCompletionDays);
    }

    [Fact]
    public async Task GetAssignmentsAsync_DepartmentCompletionDays_is_null_when_no_response()
    {
        var (service, db) = await CreateServiceAsync(nameof(GetAssignmentsAsync_DepartmentCompletionDays_is_null_when_no_response));
        await SeedTransactionAsync(db, 1);

        db.Assignments.Add(new Assignment
        {
            TransactionId = 1,
            DepartmentId = 10,
            AssignedDate = new DateTime(2026, 1, 5, 0, 0, 0, DateTimeKind.Utc),
            RequiresReply = true,
            ReplyStatus = ReplyStatus.Pending,
            Status = AssignmentStatus.Active,
            CreatedById = 1,
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var result = await service.GetAssignmentsAsync(1, new TestCurrentUser(UserRole.Admin));

        Assert.NotNull(result);
        Assert.Single(result);
        Assert.Null(result[0].ResponseDate);
        Assert.Null(result[0].DepartmentCompletionDays);
    }

    [Fact]
    public async Task GetAssignmentsAsync_DepartmentCompletionDays_is_null_while_response_only_submitted_not_approved()
    {
        // A department can submit a response for review without it being approved yet;
        // the assignment must still read as "not completed" until ApproveAsync runs.
        var (service, db) = await CreateServiceAsync(nameof(GetAssignmentsAsync_DepartmentCompletionDays_is_null_while_response_only_submitted_not_approved));
        var incomingDate = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        await SeedTransactionAsync(db, 1, incomingDate);

        db.Assignments.Add(new Assignment
        {
            TransactionId = 1,
            DepartmentId = 10,
            AssignedDate = incomingDate,
            RequiresReply = true,
            ReplyStatus = ReplyStatus.Pending,
            Status = AssignmentStatus.Active,
            CreatedById = 1,
            CreatedAt = incomingDate
        });
        db.DepartmentResponses.Add(new DepartmentResponse
        {
            TransactionId = 1,
            DepartmentId = 10,
            ResponseText = "إفادة المالية",
            Status = DepartmentResponseStatus.SubmittedForReview,
            SubmittedByUserId = 1,
            SubmittedAt = new DateTime(2026, 1, 6, 0, 0, 0, DateTimeKind.Utc),
            CreatedAt = incomingDate
        });
        await db.SaveChangesAsync();

        var result = await service.GetAssignmentsAsync(1, new TestCurrentUser(UserRole.Admin));

        Assert.NotNull(result);
        Assert.Single(result);
        Assert.Equal("Pending", result[0].ReplyStatus);
        Assert.Null(result[0].ResponseDate);
        Assert.Null(result[0].DepartmentCompletionDays);
        // A response that is only submitted-for-review (not yet Approved) is not "completed":
        // do not expose its id as an editable DepartmentResponseId, otherwise the row would
        // look editable via the admin-edit-response endpoint before it is actually finished.
        Assert.Null(result[0].DepartmentResponseId);
    }

    [Fact]
    public async Task GetAssignmentsAsync_ReturnsUniqueApprovedResponseIdsForEachRepliedDepartment()
    {
        // Regression test for a real workspace payload where المالية showed replyStatus
        // "Replied" with responseSummary/responseDate populated but departmentResponseId:
        // null. Every replied row must carry its own, real, unique DepartmentResponse.Id;
        // a row with no completed response must not expose one at all.
        var (service, db) = await CreateServiceAsync(nameof(GetAssignmentsAsync_ReturnsUniqueApprovedResponseIdsForEachRepliedDepartment));
        db.Departments.Add(new Department { Id = 30, Name = "الشؤون الإدارية", NameNormalized = "الشؤون الإدارية", IsActive = true });
        var incomingDate = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        await SeedTransactionAsync(db, 1, incomingDate);

        db.Assignments.Add(new Assignment
        {
            TransactionId = 1,
            DepartmentId = 10,
            AssignedDate = incomingDate,
            RequiresReply = true,
            ReplyStatus = ReplyStatus.Replied,
            ReplyDate = new DateTime(2026, 1, 10, 0, 0, 0, DateTimeKind.Utc),
            ReplySummary = "طذطذ",
            Status = AssignmentStatus.Completed,
            CreatedById = 1,
            CreatedAt = incomingDate
        });
        var financeResponse = new DepartmentResponse
        {
            TransactionId = 1,
            DepartmentId = 10,
            ResponseText = "طذطذ",
            Status = DepartmentResponseStatus.Approved,
            SubmittedByUserId = 1,
            SubmittedAt = new DateTime(2026, 1, 10, 0, 0, 0, DateTimeKind.Utc),
            CreatedAt = incomingDate
        };
        db.DepartmentResponses.Add(financeResponse);

        db.Assignments.Add(new Assignment
        {
            TransactionId = 1,
            DepartmentId = 20,
            AssignedDate = incomingDate,
            DueDate = new DateTime(2026, 1, 5, 0, 0, 0, DateTimeKind.Utc),
            RequiresReply = true,
            ReplyStatus = ReplyStatus.Pending,
            Status = AssignmentStatus.Active,
            CreatedById = 1,
            CreatedAt = incomingDate
        });

        db.Assignments.Add(new Assignment
        {
            TransactionId = 1,
            DepartmentId = 30,
            AssignedDate = incomingDate,
            RequiresReply = true,
            ReplyStatus = ReplyStatus.Replied,
            ReplyDate = new DateTime(2026, 1, 12, 0, 0, 0, DateTimeKind.Utc),
            ReplySummary = "إفادة الشؤون الإدارية",
            Status = AssignmentStatus.Completed,
            CreatedById = 1,
            CreatedAt = incomingDate
        });
        var adminAffairsResponse = new DepartmentResponse
        {
            TransactionId = 1,
            DepartmentId = 30,
            ResponseText = "إفادة الشؤون الإدارية",
            Status = DepartmentResponseStatus.Approved,
            SubmittedByUserId = 1,
            SubmittedAt = new DateTime(2026, 1, 12, 0, 0, 0, DateTimeKind.Utc),
            CreatedAt = incomingDate
        };
        db.DepartmentResponses.Add(adminAffairsResponse);
        await db.SaveChangesAsync();

        var result = await service.GetAssignmentsAsync(1, new TestCurrentUser(UserRole.Admin));

        Assert.NotNull(result);
        Assert.Equal(3, result.Count);

        var repliedRows = result.Where(r => r.ReplyStatus == "Replied").ToList();
        Assert.Equal(2, repliedRows.Count);
        foreach (var row in repliedRows)
        {
            Assert.NotNull(row.DepartmentResponseId);
            Assert.NotNull(row.ResponseDate);
            Assert.NotNull(row.ReplySummary);
        }

        var financeRow = result.Single(r => r.DepartmentId == 10);
        var adminAffairsRow = result.Single(r => r.DepartmentId == 30);
        var pendingRow = result.Single(r => r.DepartmentId == 20);

        Assert.Equal(financeResponse.Id, financeRow.DepartmentResponseId);
        Assert.Equal(adminAffairsResponse.Id, adminAffairsRow.DepartmentResponseId);
        Assert.NotEqual(financeRow.DepartmentResponseId, adminAffairsRow.DepartmentResponseId);
        Assert.Null(pendingRow.DepartmentResponseId);

        var realResponseIds = await db.DepartmentResponses.Select(r => r.Id).ToListAsync();
        Assert.Contains(financeRow.DepartmentResponseId!.Value, realResponseIds);
        Assert.Contains(adminAffairsRow.DepartmentResponseId!.Value, realResponseIds);
    }

    [Fact]
    public async Task ReplyAssignmentAsync_creates_a_backing_department_response_so_the_row_stays_editable()
    {
        // Regression test for the real bug: recording a reply directly via
        // ReplyAssignmentAsync (no prior DepartmentResponse submission/approval) must not
        // leave the row with replyStatus=Replied and departmentResponseId=null — otherwise
        // the admin-edit-response badge can never open for that row.
        var (service, db) = await CreateServiceAsync(nameof(ReplyAssignmentAsync_creates_a_backing_department_response_so_the_row_stays_editable));
        var incomingDate = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        await SeedTransactionAsync(db, 1, incomingDate);
        db.Assignments.Add(new Assignment
        {
            Id = 501,
            TransactionId = 1,
            DepartmentId = 10,
            AssignedDate = incomingDate,
            RequiresReply = true,
            ReplyStatus = ReplyStatus.Pending,
            Status = AssignmentStatus.Active,
            CreatedById = 1,
            CreatedAt = incomingDate
        });
        await db.SaveChangesAsync();
        Assert.Empty(db.DepartmentResponses);

        var replyDate = new DateTime(2026, 7, 7, 0, 0, 0, DateTimeKind.Utc);
        var directReply = await service.ReplyAssignmentAsync(1, 501,
            new ReplyAssignmentRequest { ReplyDate = replyDate, ReplySummary = "طذطذ" },
            new TestCurrentUser(UserRole.Admin));

        Assert.NotNull(directReply);
        Assert.NotNull(directReply!.DepartmentResponseId);

        var backingResponse = await db.DepartmentResponses.SingleAsync();
        Assert.Equal(1, backingResponse.TransactionId);
        Assert.Equal(10, backingResponse.DepartmentId);
        Assert.Equal(DepartmentResponseStatus.Approved, backingResponse.Status);
        Assert.Equal("طذطذ", backingResponse.ResponseText);
        Assert.Equal(directReply.DepartmentResponseId, backingResponse.Id);

        // ResponseDate is the operational completion date sourced from the entered
        // ReplyDate; SubmittedAt must remain a technical "recorded now" timestamp, not a
        // copy of the entered date — otherwise it re-introduces the exact SubmittedAt-as-
        // completion-date bug already fixed for the department-response submission flow.
        Assert.Equal(replyDate.Date, backingResponse.ResponseDate);
        Assert.NotEqual(replyDate, backingResponse.SubmittedAt);

        var backingAssignment = await db.Assignments.SingleAsync(a => a.Id == 501);
        Assert.Equal(replyDate.Date, backingAssignment.ReplyDate);

        var refreshed = await service.GetAssignmentsAsync(1, new TestCurrentUser(UserRole.Admin));
        Assert.NotNull(refreshed);
        var row = Assert.Single(refreshed);
        Assert.Equal("Replied", row.ReplyStatus);
        Assert.NotNull(row.DepartmentResponseId);
        Assert.Equal(backingResponse.Id, row.DepartmentResponseId);
        Assert.NotNull(row.ResponseDate);
        Assert.NotNull(row.ReplySummary);
    }

    [Fact]
    public async Task GetAssignmentsAsync_DepartmentResponseId_prefers_approved_response_when_multiple_exist()
    {
        // A department can be returned-for-correction/rejected and resubmit; the approved
        // response (the one that actually drove completion) must win deterministically.
        var (service, db) = await CreateServiceAsync(nameof(GetAssignmentsAsync_DepartmentResponseId_prefers_approved_response_when_multiple_exist));
        var incomingDate = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        await SeedTransactionAsync(db, 1, incomingDate);

        db.Assignments.Add(new Assignment
        {
            TransactionId = 1,
            DepartmentId = 10,
            AssignedDate = incomingDate,
            RequiresReply = true,
            ReplyStatus = ReplyStatus.Replied,
            ReplyDate = new DateTime(2026, 1, 10, 0, 0, 0, DateTimeKind.Utc),
            Status = AssignmentStatus.Completed,
            CreatedById = 1,
            CreatedAt = incomingDate
        });
        var rejected = new DepartmentResponse
        {
            TransactionId = 1,
            DepartmentId = 10,
            ResponseText = "مسودة أولى",
            Status = DepartmentResponseStatus.Rejected,
            SubmittedByUserId = 1,
            SubmittedAt = new DateTime(2026, 1, 3, 0, 0, 0, DateTimeKind.Utc),
            CreatedAt = incomingDate
        };
        var approved = new DepartmentResponse
        {
            TransactionId = 1,
            DepartmentId = 10,
            ResponseText = "الإفادة المعتمدة",
            Status = DepartmentResponseStatus.Approved,
            SubmittedByUserId = 1,
            SubmittedAt = new DateTime(2026, 1, 10, 0, 0, 0, DateTimeKind.Utc),
            CreatedAt = incomingDate
        };
        db.DepartmentResponses.Add(rejected);
        db.DepartmentResponses.Add(approved);
        await db.SaveChangesAsync();

        var result = await service.GetAssignmentsAsync(1, new TestCurrentUser(UserRole.Admin));

        Assert.NotNull(result);
        Assert.Single(result);
        Assert.Equal(approved.Id, result[0].DepartmentResponseId);
    }

    [Fact]
    public async Task GetAssignmentsAsync_CompletionDays_is_independent_per_department()
    {
        var (service, db) = await CreateServiceAsync(nameof(GetAssignmentsAsync_CompletionDays_is_independent_per_department));
        var incomingDate = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        await SeedTransactionAsync(db, 1, incomingDate);

        db.Assignments.Add(new Assignment
        {
            TransactionId = 1,
            DepartmentId = 10,
            AssignedDate = incomingDate,
            RequiresReply = true,
            ReplyStatus = ReplyStatus.Replied,
            ReplyDate = new DateTime(2026, 1, 6, 0, 0, 0, DateTimeKind.Utc),
            Status = AssignmentStatus.Completed,
            CreatedById = 1,
            CreatedAt = incomingDate
        });
        db.Assignments.Add(new Assignment
        {
            TransactionId = 1,
            DepartmentId = 20,
            AssignedDate = incomingDate,
            RequiresReply = true,
            ReplyStatus = ReplyStatus.Replied,
            ReplyDate = new DateTime(2026, 1, 16, 0, 0, 0, DateTimeKind.Utc),
            Status = AssignmentStatus.Completed,
            CreatedById = 1,
            CreatedAt = incomingDate
        });
        await db.SaveChangesAsync();

        var result = await service.GetAssignmentsAsync(1, new TestCurrentUser(UserRole.Admin));

        Assert.NotNull(result);
        Assert.Equal(2, result.Count);

        var fin = result.Single(r => r.DepartmentId == 10);
        var hr = result.Single(r => r.DepartmentId == 20);

        Assert.Equal(5, fin.DepartmentCompletionDays);
        Assert.Equal(15, hr.DepartmentCompletionDays);
    }

    [Fact]
    public async Task GetAssignmentsAsync_CanAdminEdit_is_true_for_Admin_false_for_others()
    {
        var (service, db) = await CreateServiceAsync(nameof(GetAssignmentsAsync_CanAdminEdit_is_true_for_Admin_false_for_others));
        await SeedTransactionAsync(db, 1);

        db.Assignments.Add(new Assignment
        {
            TransactionId = 1,
            DepartmentId = 10,
            AssignedDate = new DateTime(2026, 1, 5, 0, 0, 0, DateTimeKind.Utc),
            RequiresReply = true,
            ReplyStatus = ReplyStatus.Pending,
            Status = AssignmentStatus.Active,
            CreatedById = 1,
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var adminResult = await service.GetAssignmentsAsync(1, new TestCurrentUser(UserRole.Admin));
        var supervisorResult = await service.GetAssignmentsAsync(1, new TestCurrentUser(UserRole.Supervisor));

        Assert.True(adminResult![0].CanAdminEdit);
        Assert.False(supervisorResult![0].CanAdminEdit);
    }

    [Fact]
    public async Task AdminEditTransactionDates_recalculates_due_date_when_incoming_date_changes()
    {
        var (service, db) = await CreateServiceAsync(nameof(AdminEditTransactionDates_recalculates_due_date_when_incoming_date_changes));
        var t = await SeedTransactionAsync(db, 1, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        t.ResponseDueDays = 5;
        t.ResponseDueDate = new DateTime(2026, 1, 6, 0, 0, 0, DateTimeKind.Utc);
        await db.SaveChangesAsync();

        var updated = await service.AdminEditTransactionDatesAsync(
            1,
            new AdminEditTransactionDatesRequest
            {
                IncomingDate = new DateTime(2026, 1, 3, 0, 0, 0, DateTimeKind.Utc),
                Reason = "تصحيح تاريخ الوارد"
            },
            userId: 1);

        Assert.NotNull(updated);
        Assert.NotNull(updated.ResponseDueDate);
        Assert.Equal(new DateTime(2026, 1, 8), updated.ResponseDueDate.Value.Date);
        Assert.Equal(5, updated.ResponseDueDays);
    }

    [Fact]
    public async Task AdminEditTransactionDates_recalculates_due_days_when_due_date_changes()
    {
        var (service, db) = await CreateServiceAsync(nameof(AdminEditTransactionDates_recalculates_due_days_when_due_date_changes));
        var t = await SeedTransactionAsync(db, 1, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        t.ResponseDueDays = 5;
        t.ResponseDueDate = new DateTime(2026, 1, 6, 0, 0, 0, DateTimeKind.Utc);
        await db.SaveChangesAsync();

        var updated = await service.AdminEditTransactionDatesAsync(
            1,
            new AdminEditTransactionDatesRequest
            {
                ResponseDueDate = new DateTime(2026, 1, 10, 0, 0, 0, DateTimeKind.Utc),
                Reason = "تصحيح تاريخ الاستحقاق"
            },
            userId: 1);

        Assert.NotNull(updated);
        Assert.NotNull(updated.ResponseDueDate);
        Assert.Equal(new DateTime(2026, 1, 10), updated.ResponseDueDate.Value.Date);
        Assert.Equal(9, updated.ResponseDueDays);
    }

    [Fact]
    public async Task UpdateAsync_rejects_incoming_date_after_existing_follow_up()
    {
        var (service, db) = await CreateServiceAsync(nameof(UpdateAsync_rejects_incoming_date_after_existing_follow_up));
        await SeedTransactionAsync(db, 1, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        db.FollowUps.Add(new FollowUp
        {
            TransactionId = 1,
            FollowUpDate = new DateTime(2026, 1, 5, 0, 0, 0, DateTimeKind.Utc),
            RequiresReply = true,
            ReplyStatus = ReplyStatus.Pending,
            CreatedById = 1,
            CreatedAt = new DateTime(2026, 1, 5, 0, 0, 0, DateTimeKind.Utc)
        });
        await db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.UpdateAsync(
                1,
                new UpdateTransactionRequest { IncomingDate = new DateTime(2026, 1, 6, 0, 0, 0, DateTimeKind.Utc) },
                userId: 1,
                role: UserRole.Admin));

        Assert.Contains("تعقيب", ex.Message);
    }

    [Fact]
    public async Task AdminEditAssignmentAsync_updates_LetterNumber_and_logs_audit()
    {
        var (service, db) = await CreateServiceAsync(nameof(AdminEditAssignmentAsync_updates_LetterNumber_and_logs_audit));
        var incomingDate = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        await SeedTransactionAsync(db, 1, incomingDate);

        var assignment = new Assignment
        {
            Id = 501,
            TransactionId = 1,
            DepartmentId = 10,
            AssignedDate = incomingDate,
            RequiresReply = true,
            ReplyStatus = ReplyStatus.Pending,
            Status = AssignmentStatus.Active,
            CreatedById = 1,
            CreatedAt = incomingDate
        };
        db.Assignments.Add(assignment);
        await db.SaveChangesAsync();

        var result = await service.AdminEditAssignmentAsync(1, 501,
            new AdminEditAssignmentRequest
            {
                LetterNumber = "خ-2026/500",
                RequiredAction = "مراجعة المستندات",
            }, userId: 1);

        Assert.NotNull(result);
        Assert.Equal("خ-2026/500", result.LetterNumber);
        Assert.Equal("مراجعة المستندات", result.RequiredAction);

        var log = await db.AuditLogs.FirstOrDefaultAsync(a =>
            a.Action == AuditAction.AdminEditAssignment && a.EntityId == 501);
        Assert.NotNull(log);
        Assert.Contains("2026/500", log.NewValue);
    }

    [Fact]
    public async Task AdminEditAssignmentAsync_returns_null_for_missing_assignment()
    {
        var (service, _) = await CreateServiceAsync(nameof(AdminEditAssignmentAsync_returns_null_for_missing_assignment));

        var result = await service.AdminEditAssignmentAsync(1, 999,
            new AdminEditAssignmentRequest { LetterNumber = "خ-001" }, userId: 1);

        Assert.Null(result);
    }

    [Fact]
    public async Task AdminEditTransactionDates_Allows_Changing_Incoming_And_DueDate_Together_When_Final_State_Valid()
    {
        var (service, db) = await CreateServiceAsync(nameof(AdminEditTransactionDates_Allows_Changing_Incoming_And_DueDate_Together_When_Final_State_Valid));
        var tx = await SeedTransactionAsync(db, 1, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        tx.ResponseDueDate = new DateTime(2026, 1, 10, 0, 0, 0, DateTimeKind.Utc);
        await db.SaveChangesAsync();

        var result = await service.AdminEditTransactionDatesAsync(1, new AdminEditTransactionDatesRequest
        {
            IncomingDate = new DateTime(2026, 1, 15, 0, 0, 0, DateTimeKind.Utc),
            ResponseDueDate = new DateTime(2026, 1, 20, 0, 0, 0, DateTimeKind.Utc),
            Reason = "تصحيح التواريخ"
        }, userId: 1);

        Assert.NotNull(result);
        var persisted = await db.Transactions.FindAsync(1);
        Assert.Equal(new DateTime(2026, 1, 15, 0, 0, 0, DateTimeKind.Utc), persisted?.IncomingDate);
        Assert.Equal(new DateTime(2026, 1, 20, 0, 0, 0, DateTimeKind.Utc), persisted?.ResponseDueDate);
    }

    [Fact]
    public async Task AdminEditTransactionDates_Rejects_Final_DueDate_Before_IncomingDate()
    {
        var (service, db) = await CreateServiceAsync(nameof(AdminEditTransactionDates_Rejects_Final_DueDate_Before_IncomingDate));
        await SeedTransactionAsync(db, 1, new DateTime(2026, 1, 10, 0, 0, 0, DateTimeKind.Utc));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.AdminEditTransactionDatesAsync(1, new AdminEditTransactionDatesRequest
            {
                ResponseDueDate = new DateTime(2026, 1, 9, 0, 0, 0, DateTimeKind.Utc),
                Reason = "تصحيح التواريخ"
            }, userId: 1));

        Assert.Equal("تاريخ استحقاق المعاملة لا يمكن أن يسبق تاريخ الوارد.", ex.Message);
    }

    [Fact]
    public async Task AdminEditTransactionDates_Rejects_Final_ClosedAt_Before_IncomingDate()
    {
        var (service, db) = await CreateServiceAsync(nameof(AdminEditTransactionDates_Rejects_Final_ClosedAt_Before_IncomingDate));
        await SeedTransactionAsync(db, 1, new DateTime(2026, 1, 10, 0, 0, 0, DateTimeKind.Utc));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.AdminEditTransactionDatesAsync(1, new AdminEditTransactionDatesRequest
            {
                ClosedAt = new DateTime(2026, 1, 9, 0, 0, 0, DateTimeKind.Utc),
                Reason = "تصحيح التواريخ"
            }, userId: 1));

        Assert.Equal("تاريخ إغلاق المعاملة لا يمكن أن يسبق تاريخ الوارد.", ex.Message);
    }

    [Fact]
    public async Task AdminEditTransactionDates_ExplicitNullClosedAt_ClearsClosedAt()
    {
        // Regression guard: sending { closedAt: null } to clear the close date must not
        // silently fall back to the old ClosedAt value. IsClosedAtSpecified (not
        // ClosedAt.HasValue) must drive whether the field is touched at all.
        var (service, db) = await CreateServiceAsync(nameof(AdminEditTransactionDates_ExplicitNullClosedAt_ClearsClosedAt));
        var t = await SeedTransactionAsync(db, 1, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        t.ClosedAt = new DateTime(2026, 1, 15, 0, 0, 0, DateTimeKind.Utc);
        t.Status = TransactionStatus.Closed;
        await db.SaveChangesAsync();

        var updated = await service.AdminEditTransactionDatesAsync(1, new AdminEditTransactionDatesRequest
        {
            ClosedAt = null,
            Reason = "مسح تاريخ الإغلاق",
        }, userId: 1);

        Assert.NotNull(updated);
        var persisted = await db.Transactions.SingleAsync(x => x.Id == 1);
        Assert.Null(persisted.ClosedAt);
    }

    [Fact]
    public async Task AdminEditTransactionDates_Rejects_ClosedAt_Before_ResponseCompletedDate()
    {
        var (service, db) = await CreateServiceAsync(nameof(AdminEditTransactionDates_Rejects_ClosedAt_Before_ResponseCompletedDate));
        var t = await SeedTransactionAsync(db, 1, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        t.RequiresResponse = true;
        t.ResponseCompletedDate = new DateTime(2026, 1, 20, 0, 0, 0, DateTimeKind.Utc);
        await db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.AdminEditTransactionDatesAsync(1, new AdminEditTransactionDatesRequest
            {
                ClosedAt = new DateTime(2026, 1, 10, 0, 0, 0, DateTimeKind.Utc),
                Reason = "تصحيح تاريخ الإغلاق",
            }, userId: 1));

        Assert.Equal("تاريخ إغلاق المعاملة لا يمكن أن يسبق تاريخ الإفادة.", ex.Message);
    }

    [Fact]
    public async Task AdminEditTransactionDates_OmittingClosedAt_PreservesExistingClosedAt()
    {
        var (service, db) = await CreateServiceAsync(nameof(AdminEditTransactionDates_OmittingClosedAt_PreservesExistingClosedAt));
        var t = await SeedTransactionAsync(db, 1, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        var originalClosedAt = new DateTime(2026, 1, 15, 0, 0, 0, DateTimeKind.Utc);
        t.ClosedAt = originalClosedAt;
        t.Status = TransactionStatus.Closed;
        await db.SaveChangesAsync();

        await service.AdminEditTransactionDatesAsync(1, new AdminEditTransactionDatesRequest
        {
            ResponseDueDays = 5,
            Reason = "تعديل غير متعلق بالإغلاق",
        }, userId: 1);

        var persisted = await db.Transactions.SingleAsync(x => x.Id == 1);
        Assert.Equal(originalClosedAt, persisted.ClosedAt);
    }

    [Fact]
    public async Task AdminEditTransactionDates_MovingResponseDueDateToFuture_ClearsResponseOverdueStatus()
    {
        var (service, db) = await CreateServiceAsync(nameof(AdminEditTransactionDates_MovingResponseDueDateToFuture_ClearsResponseOverdueStatus));
        var incomingDate = DateTime.UtcNow.Date.AddDays(-30);
        var t = await SeedTransactionAsync(db, 1, incomingDate);
        t.ResponseDueDate = DateTime.UtcNow.Date.AddDays(-5);
        t.ResponseDueDays = 25;
        await db.SaveChangesAsync();

        Assert.True(WorkflowHelper.IsResponseOverdue(t, DateTime.UtcNow));

        var updated = await service.AdminEditTransactionDatesAsync(1, new AdminEditTransactionDatesRequest
        {
            ResponseDueDate = DateTime.UtcNow.Date.AddDays(10),
            Reason = "تمديد موعد الاستحقاق",
        }, userId: 1);

        Assert.NotNull(updated);
        Assert.False(updated.IsResponseOverdue);
        Assert.False(updated.IsOverdue);
    }

    [Fact]
    public async Task AdminEditTransactionDates_MovingResponseDueDateToPast_SetsResponseOverdueStatus()
    {
        var (service, db) = await CreateServiceAsync(nameof(AdminEditTransactionDates_MovingResponseDueDateToPast_SetsResponseOverdueStatus));
        var incomingDate = DateTime.UtcNow.Date.AddDays(-30);
        var t = await SeedTransactionAsync(db, 1, incomingDate);
        t.ResponseDueDate = DateTime.UtcNow.Date.AddDays(10);
        t.ResponseDueDays = 40;
        await db.SaveChangesAsync();

        Assert.False(WorkflowHelper.IsResponseOverdue(t, DateTime.UtcNow));

        var updated = await service.AdminEditTransactionDatesAsync(1, new AdminEditTransactionDatesRequest
        {
            ResponseDueDate = DateTime.UtcNow.Date.AddDays(-5),
            Reason = "تصحيح موعد الاستحقاق إلى الماضي",
        }, userId: 1);

        Assert.NotNull(updated);
        Assert.True(updated.IsResponseOverdue);
        Assert.True(updated.IsOverdue);
    }

    [Fact]
    public async Task AdminEditAssignment_Rejects_AssignedDate_Before_IncomingDate()
    {
        var (service, db) = await CreateServiceAsync(nameof(AdminEditAssignment_Rejects_AssignedDate_Before_IncomingDate));
        var incomingDate = new DateTime(2026, 1, 10, 0, 0, 0, DateTimeKind.Utc);
        await SeedTransactionAsync(db, 1, incomingDate);
        db.Assignments.Add(new Assignment
        {
            Id = 501,
            TransactionId = 1,
            DepartmentId = 10,
            AssignedDate = incomingDate,
            RequiresReply = true,
            ReplyStatus = ReplyStatus.Pending,
            Status = AssignmentStatus.Active,
            CreatedById = 1,
            CreatedAt = incomingDate
        });
        await db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.AdminEditAssignmentAsync(1, 501, new AdminEditAssignmentRequest
            {
                AssignedDate = new DateTime(2026, 1, 9, 0, 0, 0, DateTimeKind.Utc)
            }, userId: 1));

        Assert.Equal("تاريخ الإحالة لا يمكن أن يسبق تاريخ الوارد.", ex.Message);
    }

    [Fact]
    public async Task AdminEditAssignment_Rejects_DueDate_Before_AssignedDate()
    {
        var (service, db) = await CreateServiceAsync(nameof(AdminEditAssignment_Rejects_DueDate_Before_AssignedDate));
        var incomingDate = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        await SeedTransactionAsync(db, 1, incomingDate);
        db.Assignments.Add(new Assignment
        {
            Id = 501,
            TransactionId = 1,
            DepartmentId = 10,
            AssignedDate = new DateTime(2026, 1, 10, 0, 0, 0, DateTimeKind.Utc),
            RequiresReply = true,
            ReplyStatus = ReplyStatus.Pending,
            Status = AssignmentStatus.Active,
            CreatedById = 1,
            CreatedAt = incomingDate
        });
        await db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.AdminEditAssignmentAsync(1, 501, new AdminEditAssignmentRequest
            {
                DueDate = new DateTime(2026, 1, 9, 0, 0, 0, DateTimeKind.Utc)
            }, userId: 1));

        Assert.Equal("تاريخ استحقاق الإدارة لا يمكن أن يسبق تاريخ الإحالة.", ex.Message);
    }

    [Fact]
    public async Task AdminEditAssignment_Allows_Valid_Date_Order()
    {
        var (service, db) = await CreateServiceAsync(nameof(AdminEditAssignment_Allows_Valid_Date_Order));
        var incomingDate = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        await SeedTransactionAsync(db, 1, incomingDate);
        db.Assignments.Add(new Assignment
        {
            Id = 501,
            TransactionId = 1,
            DepartmentId = 10,
            AssignedDate = new DateTime(2026, 1, 5, 0, 0, 0, DateTimeKind.Utc),
            RequiresReply = true,
            ReplyStatus = ReplyStatus.Pending,
            Status = AssignmentStatus.Active,
            CreatedById = 1,
            CreatedAt = incomingDate
        });
        await db.SaveChangesAsync();

        var result = await service.AdminEditAssignmentAsync(1, 501, new AdminEditAssignmentRequest
        {
            AssignedDate = new DateTime(2026, 1, 10, 0, 0, 0, DateTimeKind.Utc),
            DueDate = new DateTime(2026, 1, 15, 0, 0, 0, DateTimeKind.Utc)
        }, userId: 1);

        Assert.NotNull(result);
        Assert.Equal(new DateTime(2026, 1, 10, 0, 0, 0, DateTimeKind.Utc), result.AssignedDate);
        Assert.Equal(new DateTime(2026, 1, 15, 0, 0, 0, DateTimeKind.Utc), result.DueDate);
    }

    [Fact]
    public async Task AddAssignmentAsync_persists_LetterNumber()
    {
        var (service, db) = await CreateServiceAsync(nameof(AddAssignmentAsync_persists_LetterNumber));
        var incomingDate = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        await SeedTransactionAsync(db, 1, incomingDate);

        var result = await service.AddAssignmentAsync(1, new CreateAssignmentRequest
        {
            DepartmentId = 10,
            AssignedDate = incomingDate,
            LetterNumber = "خ-2026/999",
        }, userId: 1);

        Assert.Equal("خ-2026/999", result.LetterNumber);
        var persisted = await db.Assignments.FirstOrDefaultAsync(a => a.TransactionId == 1 && a.DepartmentId == 10);
        Assert.Equal("خ-2026/999", persisted?.LetterNumber);
    }

    private static async Task<(Transaction Transaction, FollowUp FollowUp)> SeedRepliedFollowUpAsync(AppDbContext db, int transactionId = 1)
    {
        var incomingDate = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var t = await SeedTransactionAsync(db, transactionId, incomingDate);
        var followUp = new FollowUp
        {
            TransactionId = transactionId,
            FollowUpNumber = "111",
            FollowUpDate = new DateTime(2026, 6, 20, 0, 0, 0, DateTimeKind.Utc),
            RequiresReply = true,
            ReplyStatus = ReplyStatus.Replied,
            ReplyDate = new DateTime(2026, 6, 28, 0, 0, 0, DateTimeKind.Utc),
            ReplySummary = "jh",
            CreatedById = 1,
            CreatedAt = incomingDate
        };
        db.FollowUps.Add(followUp);
        await db.SaveChangesAsync();
        return (t, followUp);
    }

    [Fact]
    public async Task EditFollowUpReplyAsync_UpdatesReplyDateAndSummaryAndLogsAudit()
    {
        var (service, db) = await CreateServiceAsync(nameof(EditFollowUpReplyAsync_UpdatesReplyDateAndSummaryAndLogsAudit));
        var (_, followUp) = await SeedRepliedFollowUpAsync(db);

        var newReplyDate = new DateTime(2026, 6, 30, 0, 0, 0, DateTimeKind.Utc);
        var result = await service.EditFollowUpReplyAsync(1, followUp.Id,
            new ReplyFollowUpRequest { ReplyDate = newReplyDate, ReplySummary = "ملخص محدث" },
            new TestCurrentUser(UserRole.Admin));

        Assert.NotNull(result);
        Assert.Equal(newReplyDate, result!.ReplyDate);
        Assert.Equal("ملخص محدث", result.ReplySummary);
        Assert.Equal("Replied", result.ReplyStatus);

        var log = await db.AuditLogs.SingleAsync(a => a.Action == AuditAction.EditFollowUpReply && a.EntityId == followUp.Id);
        Assert.Contains("jh", log.OldValue);
        Assert.Contains("2026-06-30", log.NewValue);
        Assert.NotEqual(log.OldValue, log.NewValue);
    }

    [Fact]
    public async Task EditFollowUpReplyAsync_AllowsSupervisorRole()
    {
        var (service, db) = await CreateServiceAsync(nameof(EditFollowUpReplyAsync_AllowsSupervisorRole));
        var (_, followUp) = await SeedRepliedFollowUpAsync(db);

        var result = await service.EditFollowUpReplyAsync(1, followUp.Id,
            new ReplyFollowUpRequest { ReplyDate = followUp.ReplyDate!.Value, ReplySummary = "تعديل من مشرف" },
            new TestCurrentUser(UserRole.Supervisor));

        Assert.NotNull(result);
        Assert.Equal("تعديل من مشرف", result!.ReplySummary);
    }

    [Fact]
    public async Task EditFollowUpReplyAsync_RejectsUnauthorizedRole()
    {
        var (service, db) = await CreateServiceAsync(nameof(EditFollowUpReplyAsync_RejectsUnauthorizedRole));
        var (_, followUp) = await SeedRepliedFollowUpAsync(db);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            service.EditFollowUpReplyAsync(1, followUp.Id,
                new ReplyFollowUpRequest { ReplyDate = followUp.ReplyDate!.Value, ReplySummary = "محاولة غير مصرح بها" },
                new TestCurrentUser(UserRole.DataEntry)));
    }

    [Theory]
    [InlineData(TransactionStatus.Closed)]
    [InlineData(TransactionStatus.Cancelled)]
    [InlineData(TransactionStatus.Archived)]
    public async Task EditFollowUpReplyAsync_RejectsTerminalTransaction(TransactionStatus status)
    {
        var (service, db) = await CreateServiceAsync($"{nameof(EditFollowUpReplyAsync_RejectsTerminalTransaction)}_{status}");
        var (transaction, followUp) = await SeedRepliedFollowUpAsync(db);
        transaction.Status = status;
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.EditFollowUpReplyAsync(1, followUp.Id,
                new ReplyFollowUpRequest { ReplyDate = followUp.ReplyDate!.Value, ReplySummary = "محاولة تعديل معاملة منتهية" },
                new TestCurrentUser(UserRole.Admin)));
    }

    [Fact]
    public async Task EditFollowUpReplyAsync_RejectsWhenNotYetReplied()
    {
        var (service, db) = await CreateServiceAsync(nameof(EditFollowUpReplyAsync_RejectsWhenNotYetReplied));
        var incomingDate = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        await SeedTransactionAsync(db, 1, incomingDate);
        var followUp = new FollowUp
        {
            TransactionId = 1,
            FollowUpNumber = "222",
            FollowUpDate = new DateTime(2026, 6, 20, 0, 0, 0, DateTimeKind.Utc),
            RequiresReply = true,
            ReplyStatus = ReplyStatus.Pending,
            CreatedById = 1,
            CreatedAt = incomingDate
        };
        db.FollowUps.Add(followUp);
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.EditFollowUpReplyAsync(1, followUp.Id,
                new ReplyFollowUpRequest { ReplyDate = new DateTime(2026, 6, 25, 0, 0, 0, DateTimeKind.Utc), ReplySummary = "محاولة تعديل رد غير مسجل" },
                new TestCurrentUser(UserRole.Admin)));
    }

    private static async Task<(Transaction Transaction, Assignment Assignment)> SeedRepliedAssignmentWithoutDepartmentResponseAsync(
        AppDbContext db, int transactionId = 1, int departmentId = 10, string replySummary = "طذطذ")
    {
        var incomingDate = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var t = await SeedTransactionAsync(db, transactionId, incomingDate);
        var assignment = new Assignment
        {
            TransactionId = transactionId,
            DepartmentId = departmentId,
            AssignedDate = incomingDate,
            RequiresReply = true,
            ReplyStatus = ReplyStatus.Replied,
            ReplyDate = new DateTime(2026, 7, 7, 0, 0, 0, DateTimeKind.Utc),
            ReplySummary = replySummary,
            Status = AssignmentStatus.Completed,
            CreatedById = 1,
            CreatedAt = incomingDate
        };
        db.Assignments.Add(assignment);
        await db.SaveChangesAsync();
        return (t, assignment);
    }

    [Fact]
    public async Task EditAssignmentReplyAsync_AllowsAdminToEditByAssignmentIdWhenDepartmentResponseIdIsNull()
    {
        // Regression test for the real bug: المالية shows replyStatus=Replied with
        // replySummary/replyDate populated but no DepartmentResponse row was ever created
        // (departmentResponseId=null). The edit must key off assignment.id and must not
        // require or create a DepartmentResponse.
        var (service, db) = await CreateServiceAsync(nameof(EditAssignmentReplyAsync_AllowsAdminToEditByAssignmentIdWhenDepartmentResponseIdIsNull));
        var (_, assignment) = await SeedRepliedAssignmentWithoutDepartmentResponseAsync(db);
        Assert.Empty(db.DepartmentResponses);

        var newReplyDate = new DateTime(2026, 1, 15, 0, 0, 0, DateTimeKind.Utc);
        var result = await service.EditAssignmentReplyAsync(1, assignment.Id,
            new ReplyAssignmentRequest { ReplyDate = newReplyDate, ReplySummary = "ملخص محدث" },
            new TestCurrentUser(UserRole.Admin));

        Assert.NotNull(result);
        Assert.Equal(newReplyDate, result!.ReplyDate);
        Assert.Equal("ملخص محدث", result.ReplySummary);
        Assert.Equal("Replied", result.ReplyStatus);
        Assert.Null(result.DepartmentResponseId);
        Assert.Empty(db.DepartmentResponses);

        var log = await db.AuditLogs.SingleAsync(a => a.Action == AuditAction.EditAssignmentReply && a.EntityId == assignment.Id);
        Assert.Contains("2026-07-07", log.OldValue);
        Assert.Contains("2026-01-15", log.NewValue);
        Assert.NotEqual(log.OldValue, log.NewValue);
    }

    [Fact]
    public async Task EditAssignmentReplyAsync_AllowsSupervisorRole()
    {
        var (service, db) = await CreateServiceAsync(nameof(EditAssignmentReplyAsync_AllowsSupervisorRole));
        var (_, assignment) = await SeedRepliedAssignmentWithoutDepartmentResponseAsync(db);

        var result = await service.EditAssignmentReplyAsync(1, assignment.Id,
            new ReplyAssignmentRequest { ReplyDate = assignment.ReplyDate!.Value, ReplySummary = "تعديل من مشرف" },
            new TestCurrentUser(UserRole.Supervisor));

        Assert.NotNull(result);
        Assert.Equal("تعديل من مشرف", result!.ReplySummary);
    }

    [Fact]
    public async Task EditAssignmentReplyAsync_RejectsUnauthorizedRole()
    {
        var (service, db) = await CreateServiceAsync(nameof(EditAssignmentReplyAsync_RejectsUnauthorizedRole));
        var (_, assignment) = await SeedRepliedAssignmentWithoutDepartmentResponseAsync(db);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            service.EditAssignmentReplyAsync(1, assignment.Id,
                new ReplyAssignmentRequest { ReplyDate = assignment.ReplyDate!.Value, ReplySummary = "محاولة غير مصرح بها" },
                new TestCurrentUser(UserRole.DataEntry)));
    }

    [Theory]
    [InlineData(TransactionStatus.Closed)]
    [InlineData(TransactionStatus.Cancelled)]
    [InlineData(TransactionStatus.Archived)]
    public async Task EditAssignmentReplyAsync_RejectsTerminalTransaction(TransactionStatus status)
    {
        var (service, db) = await CreateServiceAsync($"{nameof(EditAssignmentReplyAsync_RejectsTerminalTransaction)}_{status}");
        var (transaction, assignment) = await SeedRepliedAssignmentWithoutDepartmentResponseAsync(db);
        transaction.Status = status;
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.EditAssignmentReplyAsync(1, assignment.Id,
                new ReplyAssignmentRequest { ReplyDate = assignment.ReplyDate!.Value, ReplySummary = "محاولة تعديل معاملة منتهية" },
                new TestCurrentUser(UserRole.Admin)));
    }

    [Fact]
    public async Task EditAssignmentReplyAsync_DoesNotRequireTransactionLevelResponseCompleted()
    {
        var (service, db) = await CreateServiceAsync(nameof(EditAssignmentReplyAsync_DoesNotRequireTransactionLevelResponseCompleted));
        var (transaction, assignment) = await SeedRepliedAssignmentWithoutDepartmentResponseAsync(db);
        Assert.False(transaction.ResponseCompleted);

        var result = await service.EditAssignmentReplyAsync(1, assignment.Id,
            new ReplyAssignmentRequest { ReplyDate = assignment.ReplyDate!.Value, ReplySummary = "تعديل بدون اكتمال إفادة المعاملة" },
            new TestCurrentUser(UserRole.Admin));

        Assert.NotNull(result);
        transaction = await db.Transactions.SingleAsync(t => t.Id == 1);
        Assert.False(transaction.ResponseCompleted);
    }

    [Fact]
    public async Task EditAssignmentReplyAsync_EditsCorrectRowWhenMultipleDepartmentsAreReplied()
    {
        // Each assignment row must be independently editable by its own assignment.id;
        // editing one department's reply must not touch another's.
        var (service, db) = await CreateServiceAsync(nameof(EditAssignmentReplyAsync_EditsCorrectRowWhenMultipleDepartmentsAreReplied));
        db.Departments.Add(new Department { Id = 40, Name = "الشؤون الإدارية", NameNormalized = "الشؤون الإدارية", IsActive = true });
        var incomingDate = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        await SeedTransactionAsync(db, 1, incomingDate);

        var finance = new Assignment
        {
            TransactionId = 1, DepartmentId = 10, AssignedDate = incomingDate, RequiresReply = true,
            ReplyStatus = ReplyStatus.Replied, ReplyDate = incomingDate, ReplySummary = "إفادة المالية",
            Status = AssignmentStatus.Completed, CreatedById = 1, CreatedAt = incomingDate
        };
        var hr = new Assignment
        {
            TransactionId = 1, DepartmentId = 20, AssignedDate = incomingDate, RequiresReply = true,
            ReplyStatus = ReplyStatus.Replied, ReplyDate = incomingDate, ReplySummary = "إفادة الموارد البشرية",
            Status = AssignmentStatus.Completed, CreatedById = 1, CreatedAt = incomingDate
        };
        var adminAffairs = new Assignment
        {
            TransactionId = 1, DepartmentId = 40, AssignedDate = incomingDate, RequiresReply = true,
            ReplyStatus = ReplyStatus.Replied, ReplyDate = incomingDate, ReplySummary = "إفادة الشؤون الإدارية",
            Status = AssignmentStatus.Completed, CreatedById = 1, CreatedAt = incomingDate
        };
        db.Assignments.AddRange(finance, hr, adminAffairs);
        await db.SaveChangesAsync();

        var updatedDate = new DateTime(2026, 7, 7, 0, 0, 0, DateTimeKind.Utc);
        var result = await service.EditAssignmentReplyAsync(1, finance.Id,
            new ReplyAssignmentRequest { ReplyDate = updatedDate, ReplySummary = "إفادة المالية المحدثة" },
            new TestCurrentUser(UserRole.Admin));

        Assert.NotNull(result);
        Assert.Equal("إفادة المالية المحدثة", result!.ReplySummary);

        var persistedFinance = await db.Assignments.SingleAsync(a => a.Id == finance.Id);
        var persistedHr = await db.Assignments.SingleAsync(a => a.Id == hr.Id);
        var persistedAdminAffairs = await db.Assignments.SingleAsync(a => a.Id == adminAffairs.Id);

        Assert.Equal("إفادة المالية المحدثة", persistedFinance.ReplySummary);
        Assert.Equal(updatedDate, persistedFinance.ReplyDate);
        Assert.Equal("إفادة الموارد البشرية", persistedHr.ReplySummary);
        Assert.Equal(incomingDate, persistedHr.ReplyDate);
        Assert.Equal("إفادة الشؤون الإدارية", persistedAdminAffairs.ReplySummary);
        Assert.Equal(incomingDate, persistedAdminAffairs.ReplyDate);
    }

    // ── Procedural completion for reporting (workspace DTO wiring) ─────────────

    [Fact]
    public async Task GetBasicByIdAsync_SingleRequiredReferral_ExposesItsReplyDateAsProceduralCompletionDate()
    {
        var (service, db) = await CreateServiceAsync(nameof(GetBasicByIdAsync_SingleRequiredReferral_ExposesItsReplyDateAsProceduralCompletionDate));
        var incomingDate = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        await SeedTransactionAsync(db, 1, incomingDate);
        var replyDate = new DateTime(2026, 7, 7, 0, 0, 0, DateTimeKind.Utc);
        db.Assignments.Add(new Assignment
        {
            Id = 501,
            TransactionId = 1,
            DepartmentId = 10,
            AssignedDate = incomingDate,
            RequiresReply = true,
            ReplyStatus = ReplyStatus.Replied,
            ReplyDate = replyDate,
            Status = AssignmentStatus.Completed,
            CreatedById = 1,
            CreatedAt = incomingDate
        });
        await db.SaveChangesAsync();

        var result = await service.GetBasicByIdAsync(1, new TestCurrentUser(UserRole.Admin));

        Assert.NotNull(result);
        Assert.True(result!.IsProcedurallyCompleteForReporting);
        Assert.Equal(replyDate, result.ProceduralCompletionDateForReporting);
        // Never implies actual closure or a formally-registered final response.
        Assert.NotEqual("Closed", result.Status);
        Assert.False(result.ResponseCompleted);
    }

    [Fact]
    public async Task GetBasicByIdAsync_MultipleRequiredReferrals_ExposesLatestReplyDateAsProceduralCompletionDate()
    {
        var (service, db) = await CreateServiceAsync(nameof(GetBasicByIdAsync_MultipleRequiredReferrals_ExposesLatestReplyDateAsProceduralCompletionDate));
        var incomingDate = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        await SeedTransactionAsync(db, 1, incomingDate);
        db.Assignments.AddRange(
            new Assignment
            {
                Id = 501,
                TransactionId = 1,
                DepartmentId = 10,
                AssignedDate = incomingDate,
                RequiresReply = true,
                ReplyStatus = ReplyStatus.Replied,
                ReplyDate = new DateTime(2026, 7, 5, 0, 0, 0, DateTimeKind.Utc),
                Status = AssignmentStatus.Completed,
                CreatedById = 1,
                CreatedAt = incomingDate
            },
            new Assignment
            {
                Id = 502,
                TransactionId = 1,
                DepartmentId = 20,
                AssignedDate = incomingDate,
                RequiresReply = true,
                ReplyStatus = ReplyStatus.Replied,
                ReplyDate = new DateTime(2026, 7, 9, 0, 0, 0, DateTimeKind.Utc),
                Status = AssignmentStatus.Completed,
                CreatedById = 1,
                CreatedAt = incomingDate
            });
        await db.SaveChangesAsync();

        var result = await service.GetBasicByIdAsync(1, new TestCurrentUser(UserRole.Admin));

        Assert.NotNull(result);
        Assert.True(result!.IsProcedurallyCompleteForReporting);
        Assert.Equal(new DateTime(2026, 7, 9, 0, 0, 0, DateTimeKind.Utc), result.ProceduralCompletionDateForReporting);
    }

    [Fact]
    public async Task GetBasicByIdAsync_IncompleteReferral_HasNullProceduralCompletionDate()
    {
        var (service, db) = await CreateServiceAsync(nameof(GetBasicByIdAsync_IncompleteReferral_HasNullProceduralCompletionDate));
        var incomingDate = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        await SeedTransactionAsync(db, 1, incomingDate);
        db.Assignments.AddRange(
            new Assignment
            {
                Id = 501,
                TransactionId = 1,
                DepartmentId = 10,
                AssignedDate = incomingDate,
                RequiresReply = true,
                ReplyStatus = ReplyStatus.Replied,
                ReplyDate = new DateTime(2026, 7, 5, 0, 0, 0, DateTimeKind.Utc),
                Status = AssignmentStatus.Completed,
                CreatedById = 1,
                CreatedAt = incomingDate
            },
            new Assignment
            {
                Id = 502,
                TransactionId = 1,
                DepartmentId = 20,
                AssignedDate = incomingDate,
                RequiresReply = true,
                ReplyStatus = ReplyStatus.Pending,
                Status = AssignmentStatus.Active,
                CreatedById = 1,
                CreatedAt = incomingDate
            });
        await db.SaveChangesAsync();

        var result = await service.GetBasicByIdAsync(1, new TestCurrentUser(UserRole.Admin));

        Assert.NotNull(result);
        Assert.False(result!.IsProcedurallyCompleteForReporting);
        Assert.Null(result.ProceduralCompletionDateForReporting);
    }

    [Fact]
    public async Task GetBasicByIdAsync_CancelledReferral_DoesNotBlockProceduralCompletion()
    {
        var (service, db) = await CreateServiceAsync(nameof(GetBasicByIdAsync_CancelledReferral_DoesNotBlockProceduralCompletion));
        var incomingDate = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        await SeedTransactionAsync(db, 1, incomingDate);
        var replyDate = new DateTime(2026, 7, 7, 0, 0, 0, DateTimeKind.Utc);
        db.Assignments.AddRange(
            new Assignment
            {
                Id = 501,
                TransactionId = 1,
                DepartmentId = 10,
                AssignedDate = incomingDate,
                RequiresReply = true,
                ReplyStatus = ReplyStatus.Replied,
                ReplyDate = replyDate,
                Status = AssignmentStatus.Completed,
                CreatedById = 1,
                CreatedAt = incomingDate
            },
            new Assignment
            {
                Id = 502,
                TransactionId = 1,
                DepartmentId = 20,
                AssignedDate = incomingDate,
                RequiresReply = true,
                ReplyStatus = ReplyStatus.Pending,
                ReplyDate = null,
                Status = AssignmentStatus.Cancelled,
                CreatedById = 1,
                CreatedAt = incomingDate
            });
        await db.SaveChangesAsync();

        var result = await service.GetBasicByIdAsync(1, new TestCurrentUser(UserRole.Admin));

        Assert.NotNull(result);
        Assert.True(result!.IsProcedurallyCompleteForReporting);
        Assert.Equal(replyDate, result.ProceduralCompletionDateForReporting);
        Assert.NotEqual("Closed", result.Status);
        Assert.False(result.ResponseCompleted);
    }

    [Fact]
    public async Task GetBasicByIdAsync_NoReferrals_UsesManualResponseCompletedDate()
    {
        var (service, db) = await CreateServiceAsync(nameof(GetBasicByIdAsync_NoReferrals_UsesManualResponseCompletedDate));
        var incomingDate = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var t = await SeedTransactionAsync(db, 1, incomingDate);
        t.ResponseCompleted = true;
        t.ResponseCompletedDate = new DateTime(2026, 1, 10, 0, 0, 0, DateTimeKind.Utc);
        await db.SaveChangesAsync();

        var result = await service.GetBasicByIdAsync(1, new TestCurrentUser(UserRole.Admin));

        Assert.NotNull(result);
        Assert.False(result!.IsProcedurallyCompleteForReporting);
        Assert.Equal(t.ResponseCompletedDate, result.ProceduralCompletionDateForReporting);
    }
}
