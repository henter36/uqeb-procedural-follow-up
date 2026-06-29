using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Uqeb.Api.Data;
using Uqeb.Api.DTOs.DepartmentResponses;
using Uqeb.Api.Models.Entities;
using Uqeb.Api.Models.Enums;
using Uqeb.Api.Services;
using Xunit;

namespace Uqeb.Api.Tests;

public class DepartmentResponseServiceTests
{
    private static AppDbContext CreateDb(string name)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(name)
            .Options;
        return new AppDbContext(options);
    }

    private static IConfiguration BuildConfig(string? storagePath = null)
    {
        var dict = new Dictionary<string, string?>();
        if (storagePath != null) dict["FileStorage:Path"] = storagePath;
        return new ConfigurationBuilder().AddInMemoryCollection(dict).Build();
    }

    private sealed class FakeUser : ICurrentUserService
    {
        public int UserId { get; init; } = 1;
        public string Username { get; init; } = "user";
        public UserRole Role { get; init; } = UserRole.DepartmentUser;
        public int? DepartmentId { get; init; }
        public bool IsAuthenticated => true;
    }

    private static async Task<(AppDbContext db, int transactionId, int departmentId, int userId)> SeedAsync(string dbName)
    {
        var db = CreateDb(dbName);

        var dept = new Department { Name = "إدارة الاختبار", NameNormalized = "إدارة الاختبار", Code = "TEST" };
        db.Departments.Add(dept);

        var user = new User { Username = "u1", PasswordHash = "x", FullName = "المستخدم الأول", Role = UserRole.DepartmentUser };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        user.DepartmentId = dept.Id;
        await db.SaveChangesAsync();

        var tx = new Transaction
        {
            InternalTrackingNumber = "TX-0001",
            IncomingNumber = "IN-001",
            IncomingDate = DateTime.UtcNow,
            Subject = "موضوع الاختبار",
            Status = TransactionStatus.Assigned,
            CreatedById = user.Id,
        };
        db.Transactions.Add(tx);

        var assignment = new Assignment
        {
            TransactionId = tx.Id,
            DepartmentId = dept.Id,
            AssignedDate = DateTime.UtcNow,
            RequiresReply = true,
            ReplyStatus = ReplyStatus.Pending,
            Status = AssignmentStatus.Active,
            CreatedById = user.Id,
        };
        db.Assignments.Add(assignment);
        await db.SaveChangesAsync();

        return (db, tx.Id, dept.Id, user.Id);
    }

    private static DepartmentResponseService BuildService(AppDbContext db, IConfiguration? config = null)
    {
        var tmpPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        config ??= BuildConfig(tmpPath);
        return new DepartmentResponseService(db, new AuditService(db), config);
    }

    [Fact]
    public async Task Create_CreatesResponseAsDraft()
    {
        var (db, txId, deptId, userId) = await SeedAsync(nameof(Create_CreatesResponseAsDraft));
        var service = BuildService(db);
        var user = new FakeUser { UserId = userId, DepartmentId = deptId };

        var dto = await service.CreateAsync(
            new CreateDepartmentResponseRequest(txId, "نص الرد"),
            user);

        Assert.Equal("Draft", dto.Status);
        Assert.Equal(txId, dto.TransactionId);
        Assert.Equal("نص الرد", dto.ResponseText);
        Assert.Null(dto.SubmittedAt);
    }

    [Fact]
    public async Task Create_RejectsDuplicate()
    {
        var (db, txId, deptId, userId) = await SeedAsync(nameof(Create_RejectsDuplicate));
        var service = BuildService(db);
        var user = new FakeUser { UserId = userId, DepartmentId = deptId };

        await service.CreateAsync(new CreateDepartmentResponseRequest(txId, "أول"), user);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.CreateAsync(new CreateDepartmentResponseRequest(txId, "ثانٍ"), user));
    }

    [Fact]
    public async Task Submit_TransitionsToSubmittedForReview()
    {
        var (db, txId, deptId, userId) = await SeedAsync(nameof(Submit_TransitionsToSubmittedForReview));
        var service = BuildService(db);
        var user = new FakeUser { UserId = userId, DepartmentId = deptId };

        var created = await service.CreateAsync(new CreateDepartmentResponseRequest(txId, "نص"), user);
        var submitted = await service.SubmitAsync(created.Id, user);

        Assert.Equal("SubmittedForReview", submitted.Status);
        Assert.NotNull(submitted.SubmittedAt);
    }

    [Fact]
    public async Task Submit_RejectsIfAlreadyApproved()
    {
        var (db, txId, deptId, userId) = await SeedAsync(nameof(Submit_RejectsIfAlreadyApproved));
        var service = BuildService(db);
        var submitter = new FakeUser { UserId = userId, DepartmentId = deptId };
        var reviewer = new FakeUser { UserId = userId, Role = UserRole.Supervisor, DepartmentId = null };

        var created = await service.CreateAsync(new CreateDepartmentResponseRequest(txId, "نص"), submitter);
        await service.SubmitAsync(created.Id, submitter);
        await service.ApproveAsync(created.Id, reviewer);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.SubmitAsync(created.Id, submitter));
    }

    [Fact]
    public async Task Approve_TransitionsToApproved()
    {
        var (db, txId, deptId, userId) = await SeedAsync(nameof(Approve_TransitionsToApproved));
        var service = BuildService(db);
        var submitter = new FakeUser { UserId = userId, DepartmentId = deptId };
        var reviewer = new FakeUser { UserId = userId, Role = UserRole.Supervisor, DepartmentId = null };

        var created = await service.CreateAsync(new CreateDepartmentResponseRequest(txId, "نص"), submitter);
        await service.SubmitAsync(created.Id, submitter);
        var approved = await service.ApproveAsync(created.Id, reviewer);

        Assert.Equal("Approved", approved.Status);
        Assert.NotNull(approved.ReviewedByName);
    }

    [Fact]
    public async Task ReturnForCorrection_RequiresNote()
    {
        var (db, txId, deptId, userId) = await SeedAsync(nameof(ReturnForCorrection_RequiresNote));
        var service = BuildService(db);
        var submitter = new FakeUser { UserId = userId, DepartmentId = deptId };
        var reviewer = new FakeUser { UserId = userId, Role = UserRole.Supervisor, DepartmentId = null };

        var created = await service.CreateAsync(new CreateDepartmentResponseRequest(txId, "نص"), submitter);
        await service.SubmitAsync(created.Id, submitter);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.ReturnForCorrectionAsync(created.Id, new ReviewDepartmentResponseRequest(null), reviewer));
    }

    [Fact]
    public async Task ReturnForCorrection_AllowsResubmission()
    {
        var (db, txId, deptId, userId) = await SeedAsync(nameof(ReturnForCorrection_AllowsResubmission));
        var service = BuildService(db);
        var submitter = new FakeUser { UserId = userId, DepartmentId = deptId };
        var reviewer = new FakeUser { UserId = userId, Role = UserRole.Supervisor, DepartmentId = null };

        var created = await service.CreateAsync(new CreateDepartmentResponseRequest(txId, "نص"), submitter);
        await service.SubmitAsync(created.Id, submitter);
        var returned = await service.ReturnForCorrectionAsync(created.Id, new ReviewDepartmentResponseRequest("يحتاج إصلاح"), reviewer);
        Assert.Equal("ReturnedForCorrection", returned.Status);

        await service.UpdateAsync(created.Id, new UpdateDepartmentResponseRequest("نص معدّل"), submitter);
        var resubmitted = await service.SubmitAsync(created.Id, submitter);
        Assert.Equal("SubmittedForReview", resubmitted.Status);
    }

    [Fact]
    public async Task Reject_RequiresNote()
    {
        var (db, txId, deptId, userId) = await SeedAsync(nameof(Reject_RequiresNote));
        var service = BuildService(db);
        var submitter = new FakeUser { UserId = userId, DepartmentId = deptId };
        var reviewer = new FakeUser { UserId = userId, Role = UserRole.Supervisor, DepartmentId = null };

        var created = await service.CreateAsync(new CreateDepartmentResponseRequest(txId, "نص"), submitter);
        await service.SubmitAsync(created.Id, submitter);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.RejectAsync(created.Id, new ReviewDepartmentResponseRequest(""), reviewer));
    }

    [Fact]
    public async Task Reject_TransitionsToRejected()
    {
        var (db, txId, deptId, userId) = await SeedAsync(nameof(Reject_TransitionsToRejected));
        var service = BuildService(db);
        var submitter = new FakeUser { UserId = userId, DepartmentId = deptId };
        var reviewer = new FakeUser { UserId = userId, Role = UserRole.Supervisor, DepartmentId = null };

        var created = await service.CreateAsync(new CreateDepartmentResponseRequest(txId, "نص"), submitter);
        await service.SubmitAsync(created.Id, submitter);
        var rejected = await service.RejectAsync(created.Id, new ReviewDepartmentResponseRequest("لا يفي بالمتطلبات"), reviewer);

        Assert.Equal("Rejected", rejected.Status);
        Assert.Equal("لا يفي بالمتطلبات", rejected.ReviewNote);
    }

    [Fact]
    public async Task GetById_DepartmentUserCannotReadOtherDepartment()
    {
        var (db, txId, deptId, userId) = await SeedAsync(nameof(GetById_DepartmentUserCannotReadOtherDepartment));
        var service = BuildService(db);
        var owner = new FakeUser { UserId = userId, DepartmentId = deptId };

        var created = await service.CreateAsync(new CreateDepartmentResponseRequest(txId, "نص"), owner);

        var anotherDeptUser = new FakeUser { UserId = userId, DepartmentId = deptId + 999 };
        var result = await service.GetByIdAsync(created.Id, anotherDeptUser);

        Assert.Null(result);
    }

    [Fact]
    public async Task Update_ForbiddenAfterSubmission()
    {
        var (db, txId, deptId, userId) = await SeedAsync(nameof(Update_ForbiddenAfterSubmission));
        var service = BuildService(db);
        var user = new FakeUser { UserId = userId, DepartmentId = deptId };

        var created = await service.CreateAsync(new CreateDepartmentResponseRequest(txId, "نص"), user);
        await service.SubmitAsync(created.Id, user);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.UpdateAsync(created.Id, new UpdateDepartmentResponseRequest("تعديل"), user));
    }

    [Fact]
    public async Task GetPendingReview_ReturnsOnlySubmittedItems()
    {
        var (db, txId, deptId, userId) = await SeedAsync(nameof(GetPendingReview_ReturnsOnlySubmittedItems));
        var service = BuildService(db);
        var submitter = new FakeUser { UserId = userId, DepartmentId = deptId };
        var admin = new FakeUser { UserId = userId, Role = UserRole.Admin };

        await service.CreateAsync(new CreateDepartmentResponseRequest(txId, "مسودة"), submitter);
        var pending = await service.GetPendingReviewAsync(admin);
        Assert.Empty(pending);

        await service.SubmitAsync((await service.GetMyDepartmentResponsesAsync(submitter))[0].Id, submitter);
        pending = await service.GetPendingReviewAsync(admin);
        Assert.Single(pending);
    }

    [Fact]
    public async Task AuditLog_CreatedOnCreate()
    {
        var (db, txId, deptId, userId) = await SeedAsync(nameof(AuditLog_CreatedOnCreate));
        var service = BuildService(db);
        var user = new FakeUser { UserId = userId, DepartmentId = deptId };

        await service.CreateAsync(new CreateDepartmentResponseRequest(txId, "نص"), user);

        var log = await db.AuditLogs.FirstOrDefaultAsync(l => l.Action == AuditAction.DepartmentResponseCreated);
        Assert.NotNull(log);
        Assert.Equal(txId, log.TransactionId);
    }

    [Fact]
    public async Task AuditLog_WrittenOnApprove()
    {
        var (db, txId, deptId, userId) = await SeedAsync(nameof(AuditLog_WrittenOnApprove));
        var service = BuildService(db);
        var submitter = new FakeUser { UserId = userId, DepartmentId = deptId };
        var reviewer = new FakeUser { UserId = userId, Role = UserRole.Supervisor, DepartmentId = null };

        var created = await service.CreateAsync(new CreateDepartmentResponseRequest(txId, "نص"), submitter);
        await service.SubmitAsync(created.Id, submitter);
        await service.ApproveAsync(created.Id, reviewer);

        var log = await db.AuditLogs.FirstOrDefaultAsync(l => l.Action == AuditAction.DepartmentResponseApproved);
        Assert.NotNull(log);
    }

    [Fact]
    public async Task Create_RejectsUserWithNoDepartment()
    {
        var (db, txId, _, userId) = await SeedAsync(nameof(Create_RejectsUserWithNoDepartment));
        var service = BuildService(db);
        var user = new FakeUser { UserId = userId, DepartmentId = null };

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            service.CreateAsync(new CreateDepartmentResponseRequest(txId, "نص"), user));
    }

    [Fact]
    public async Task GetMyDepartmentResponsesAsync_DepartmentUserWithNullDept_ReturnsEmpty()
    {
        var (db, txId, deptId, userId) = await SeedAsync(nameof(GetMyDepartmentResponsesAsync_DepartmentUserWithNullDept_ReturnsEmpty));
        var service = BuildService(db);
        var owner = new FakeUser { UserId = userId, DepartmentId = deptId };
        await service.CreateAsync(new CreateDepartmentResponseRequest(txId, "نص"), owner);

        var noDeptUser = new FakeUser { UserId = userId, DepartmentId = null };
        var result = await service.GetMyDepartmentResponsesAsync(noDeptUser);

        Assert.Empty(result);
    }

    [Fact]
    public async Task GetDepartmentTransactions_ReturnsBothWithAndWithoutResponse()
    {
        var (db, txId, deptId, userId) = await SeedAsync(nameof(GetDepartmentTransactions_ReturnsBothWithAndWithoutResponse));
        var service = BuildService(db);
        var user = new FakeUser { UserId = userId, DepartmentId = deptId };

        // No response created yet — transaction should still appear
        var itemsBefore = await service.GetDepartmentTransactionsAsync(user);
        Assert.Single(itemsBefore);
        Assert.Null(itemsBefore[0].DepartmentResponseId);
        Assert.True(itemsBefore[0].CanCreateResponse);

        // Create a response
        await service.CreateAsync(new CreateDepartmentResponseRequest(txId, "نص"), user);

        var itemsAfter = await service.GetDepartmentTransactionsAsync(user);
        Assert.Single(itemsAfter);
        Assert.NotNull(itemsAfter[0].DepartmentResponseId);
        Assert.Equal("Draft", itemsAfter[0].DepartmentResponseStatus);
        Assert.True(itemsAfter[0].CanEditResponse);
    }

    [Fact]
    public async Task GetDepartmentTransactions_NullDept_ReturnsEmpty()
    {
        var (db, _, _, userId) = await SeedAsync(nameof(GetDepartmentTransactions_NullDept_ReturnsEmpty));
        var service = BuildService(db);
        var noDeptUser = new FakeUser { UserId = userId, DepartmentId = null };

        var result = await service.GetDepartmentTransactionsAsync(noDeptUser);

        Assert.Empty(result);
    }

    // ─── Authorization tests ───────────────────────────────────────────────────

    [Fact]
    public async Task GetPendingReview_DepartmentUserForbidden()
    {
        var (db, _, _, userId) = await SeedAsync(nameof(GetPendingReview_DepartmentUserForbidden));
        var service = BuildService(db);
        var deptUser = new FakeUser { UserId = userId, Role = UserRole.DepartmentUser };

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            service.GetPendingReviewAsync(deptUser));
    }

    [Fact]
    public async Task Approve_DepartmentUserForbidden()
    {
        var (db, txId, deptId, userId) = await SeedAsync(nameof(Approve_DepartmentUserForbidden));
        var service = BuildService(db);
        var submitter = new FakeUser { UserId = userId, DepartmentId = deptId };
        var deptUser = new FakeUser { UserId = userId, Role = UserRole.DepartmentUser };

        var created = await service.CreateAsync(new CreateDepartmentResponseRequest(txId, "نص"), submitter);
        await service.SubmitAsync(created.Id, submitter);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            service.ApproveAsync(created.Id, deptUser));
    }

    [Fact]
    public async Task ReturnForCorrection_DepartmentUserForbidden()
    {
        var (db, txId, deptId, userId) = await SeedAsync(nameof(ReturnForCorrection_DepartmentUserForbidden));
        var service = BuildService(db);
        var submitter = new FakeUser { UserId = userId, DepartmentId = deptId };
        var deptUser = new FakeUser { UserId = userId, Role = UserRole.DepartmentUser };

        var created = await service.CreateAsync(new CreateDepartmentResponseRequest(txId, "نص"), submitter);
        await service.SubmitAsync(created.Id, submitter);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            service.ReturnForCorrectionAsync(created.Id, new ReviewDepartmentResponseRequest("ملاحظة"), deptUser));
    }

    [Fact]
    public async Task Reject_DepartmentUserForbidden()
    {
        var (db, txId, deptId, userId) = await SeedAsync(nameof(Reject_DepartmentUserForbidden));
        var service = BuildService(db);
        var submitter = new FakeUser { UserId = userId, DepartmentId = deptId };
        var deptUser = new FakeUser { UserId = userId, Role = UserRole.DepartmentUser };

        var created = await service.CreateAsync(new CreateDepartmentResponseRequest(txId, "نص"), submitter);
        await service.SubmitAsync(created.Id, submitter);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            service.RejectAsync(created.Id, new ReviewDepartmentResponseRequest("سبب"), deptUser));
    }

    [Fact]
    public async Task Admin_CanApprovePendingReview()
    {
        var (db, txId, deptId, userId) = await SeedAsync(nameof(Admin_CanApprovePendingReview));
        var service = BuildService(db);
        var submitter = new FakeUser { UserId = userId, DepartmentId = deptId };
        var admin = new FakeUser { UserId = userId, Role = UserRole.Admin };

        var created = await service.CreateAsync(new CreateDepartmentResponseRequest(txId, "نص"), submitter);
        await service.SubmitAsync(created.Id, submitter);
        var approved = await service.ApproveAsync(created.Id, admin);

        Assert.Equal("Approved", approved.Status);
    }

    [Fact]
    public async Task DataEntry_CanApprovePendingReview()
    {
        var (db, txId, deptId, userId) = await SeedAsync(nameof(DataEntry_CanApprovePendingReview));
        var service = BuildService(db);
        var submitter = new FakeUser { UserId = userId, DepartmentId = deptId };
        var dataEntry = new FakeUser { UserId = userId, Role = UserRole.DataEntry };

        var created = await service.CreateAsync(new CreateDepartmentResponseRequest(txId, "نص"), submitter);
        await service.SubmitAsync(created.Id, submitter);
        var approved = await service.ApproveAsync(created.Id, dataEntry);

        Assert.Equal("Approved", approved.Status);
    }

    [Fact]
    public async Task GetPendingReview_DataEntryCanAccess()
    {
        var (db, txId, deptId, userId) = await SeedAsync(nameof(GetPendingReview_DataEntryCanAccess));
        var service = BuildService(db);
        var submitter = new FakeUser { UserId = userId, DepartmentId = deptId };
        var dataEntry = new FakeUser { UserId = userId, Role = UserRole.DataEntry };

        await service.CreateAsync(new CreateDepartmentResponseRequest(txId, "نص"), submitter);
        await service.SubmitAsync((await service.GetMyDepartmentResponsesAsync(submitter))[0].Id, submitter);

        var pending = await service.GetPendingReviewAsync(dataEntry);
        Assert.Single(pending);
    }

    [Theory]
    [InlineData(UserRole.Admin)]
    [InlineData(UserRole.Supervisor)]
    [InlineData(UserRole.DataEntry)]
    public async Task GetPendingReview_ReviewerRoles_ReturnEmptyListWhenNoPending(UserRole role)
    {
        var (db, _, _, userId) = await SeedAsync($"{nameof(GetPendingReview_ReviewerRoles_ReturnEmptyListWhenNoPending)}_{role}");
        var service = BuildService(db);
        var reviewer = new FakeUser { UserId = userId, Role = role };

        var pending = await service.GetPendingReviewAsync(reviewer);

        Assert.Empty(pending);
    }

    // ─── Resubmission clears review state ─────────────────────────────────────

    [Fact]
    public async Task Resubmit_AfterReturnedForCorrection_ClearsReviewState()
    {
        var (db, txId, deptId, userId) = await SeedAsync(nameof(Resubmit_AfterReturnedForCorrection_ClearsReviewState));
        var service = BuildService(db);
        var submitter = new FakeUser { UserId = userId, DepartmentId = deptId };
        var reviewer = new FakeUser { UserId = userId, Role = UserRole.Supervisor };

        var created = await service.CreateAsync(new CreateDepartmentResponseRequest(txId, "نص"), submitter);
        await service.SubmitAsync(created.Id, submitter);
        await service.ReturnForCorrectionAsync(created.Id, new ReviewDepartmentResponseRequest("يحتاج إصلاح"), reviewer);

        // Resubmit — review state should be cleared
        var resubmitted = await service.SubmitAsync(created.Id, submitter);

        Assert.Equal("SubmittedForReview", resubmitted.Status);
        Assert.Null(resubmitted.ReviewNote);
        Assert.Null(resubmitted.ReviewedAt);
        Assert.Null(resubmitted.ReviewedByName);
    }

    [Fact]
    public async Task Resubmit_AuditLog_RecordsReturnedForCorrectionToSubmitted()
    {
        var (db, txId, deptId, userId) = await SeedAsync(nameof(Resubmit_AuditLog_RecordsReturnedForCorrectionToSubmitted));
        var service = BuildService(db);
        var submitter = new FakeUser { UserId = userId, DepartmentId = deptId };
        var reviewer = new FakeUser { UserId = userId, Role = UserRole.Supervisor };

        var created = await service.CreateAsync(new CreateDepartmentResponseRequest(txId, "نص"), submitter);
        await service.SubmitAsync(created.Id, submitter);
        await service.ReturnForCorrectionAsync(created.Id, new ReviewDepartmentResponseRequest("ملاحظة"), reviewer);
        await service.SubmitAsync(created.Id, submitter);

        var log = await db.AuditLogs
            .Where(l => l.Action == AuditAction.DepartmentResponseSubmitted && l.OldValue == "ReturnedForCorrection")
            .FirstOrDefaultAsync();
        Assert.NotNull(log);
        Assert.Equal("SubmittedForReview", log.NewValue);
    }

    // ─── Audit EntityId test ───────────────────────────────────────────────────

    [Fact]
    public async Task AuditLog_CreatedOnCreate_HasEntityId()
    {
        var (db, txId, deptId, userId) = await SeedAsync(nameof(AuditLog_CreatedOnCreate_HasEntityId));
        var service = BuildService(db);
        var user = new FakeUser { UserId = userId, DepartmentId = deptId };

        var dto = await service.CreateAsync(new CreateDepartmentResponseRequest(txId, "نص"), user);

        var log = await db.AuditLogs.FirstOrDefaultAsync(l => l.Action == AuditAction.DepartmentResponseCreated);
        Assert.NotNull(log);
        Assert.Equal(txId, log.TransactionId);
        Assert.NotNull(log.EntityId);
        Assert.Equal(dto.Id, log.EntityId);
    }

    // ─── SHA-256 integrity test ────────────────────────────────────────────────

    [Fact]
    public async Task Download_RejectsIfFileContentChanged()
    {
        var tmpDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tmpDir);
        try
        {
            var (db, txId, deptId, userId) = await SeedAsync(nameof(Download_RejectsIfFileContentChanged));
            var service = BuildService(db, BuildConfig(tmpDir));
            var user = new FakeUser { UserId = userId, DepartmentId = deptId };

            var created = await service.CreateAsync(new CreateDepartmentResponseRequest(txId, "نص"), user);

            // Upload a real file
            var fileContent = System.Text.Encoding.UTF8.GetBytes("محتوى الملف الأصلي");
            var formFile = new FormFile(
                new System.IO.MemoryStream(fileContent),
                0,
                fileContent.Length,
                "file",
                "test.pdf")
            {
                Headers = new HeaderDictionary(),
                ContentType = "application/pdf"
            };

            var attachmentDto = await service.UploadAttachmentAsync(created.Id, formFile, user);

            // Tamper with the file on disk
            var attachment = await db.DepartmentResponseAttachments.FindAsync(attachmentDto.Id);
            Assert.NotNull(attachment);
            await File.WriteAllBytesAsync(attachment.StoragePath, System.Text.Encoding.UTF8.GetBytes("محتوى مزوّر"));

            // Download should reject
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.DownloadAttachmentAsync(created.Id, attachmentDto.Id, user));
        }
        finally
        {
            Directory.Delete(tmpDir, true);
        }
    }

    [Fact]
    public async Task Create_AdminCanCreateWithExplicitDepartmentId()
    {
        var (db, txId, deptId, userId) = await SeedAsync(nameof(Create_AdminCanCreateWithExplicitDepartmentId));
        var service = BuildService(db);
        var admin = new FakeUser { UserId = userId, Role = UserRole.Admin, DepartmentId = null };

        var dto = await service.CreateAsync(
            new CreateDepartmentResponseRequest(txId, "نص", deptId),
            admin);

        Assert.Equal("Draft", dto.Status);
        Assert.Equal(deptId, dto.DepartmentId);
    }

    [Fact]
    public async Task Create_AdminWithoutDepartmentIdOrRequestDepartmentIdThrows()
    {
        var (db, txId, _, userId) = await SeedAsync(nameof(Create_AdminWithoutDepartmentIdOrRequestDepartmentIdThrows));
        var service = BuildService(db);
        var admin = new FakeUser { UserId = userId, Role = UserRole.Admin, DepartmentId = null };

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.CreateAsync(new CreateDepartmentResponseRequest(txId, "نص"), admin));
    }

    [Fact]
    public async Task Create_DepartmentUserCannotCreateForUnassignedTransaction()
    {
        var (db, _, deptId, userId) = await SeedAsync(nameof(Create_DepartmentUserCannotCreateForUnassignedTransaction));
        var service = BuildService(db);
        var user = new FakeUser { UserId = userId, DepartmentId = deptId };

        // Add a second transaction with no assignment to this dept
        var user2 = await db.Users.FirstAsync();
        var tx2 = new Transaction
        {
            InternalTrackingNumber = "TX-9999",
            IncomingNumber = "IN-999",
            IncomingDate = DateTime.UtcNow,
            Subject = "معاملة غير مسندة",
            Status = TransactionStatus.New,
            CreatedById = user2.Id,
        };
        db.Transactions.Add(tx2);
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.CreateAsync(new CreateDepartmentResponseRequest(tx2.Id, "نص"), user));
    }

    [Fact]
    public async Task Create_DepartmentUserCannotCreateForDifferentDepartmentId()
    {
        var (db, txId, deptId, userId) = await SeedAsync(nameof(Create_DepartmentUserCannotCreateForDifferentDepartmentId));
        var service = BuildService(db);
        var user = new FakeUser { UserId = userId, DepartmentId = deptId };
        var otherDept = new Department { Name = "إدارة أخرى", NameNormalized = "إدارة أخرى", Code = "OTHER" };
        db.Departments.Add(otherDept);
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            service.CreateAsync(new CreateDepartmentResponseRequest(txId, "نص", otherDept.Id), user));
    }

    [Fact]
    public async Task Upload_RejectsFileLargerThan10MB()
    {
        var (db, txId, deptId, userId) = await SeedAsync(nameof(Upload_RejectsFileLargerThan10MB));
        var service = BuildService(db);
        var user = new FakeUser { UserId = userId, DepartmentId = deptId };

        var created = await service.CreateAsync(new CreateDepartmentResponseRequest(txId, "نص"), user);

        // FormFile length > 10 MB without allocating the full buffer
        const long oversizeBytes = 10L * 1024 * 1024 + 1;
        var oversizedFile = new FormFile(Stream.Null, 0, oversizeBytes, "file", "large.pdf")
        {
            Headers = new HeaderDictionary(),
            ContentType = "application/pdf",
        };

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.UploadAttachmentAsync(created.Id, oversizedFile, user));
    }

    [Fact]
    public async Task Upload_AcceptsFileExactlyAt10MB()
    {
        var tmpDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tmpDir);
        try
        {
            var (db, txId, deptId, userId) = await SeedAsync(nameof(Upload_AcceptsFileExactlyAt10MB));
            var service = BuildService(db, BuildConfig(tmpDir));
            var user = new FakeUser { UserId = userId, DepartmentId = deptId };

            var created = await service.CreateAsync(new CreateDepartmentResponseRequest(txId, "نص"), user);

            const long exactBytes = 10L * 1024 * 1024;
            var fileContent = new byte[exactBytes];
            var borderlineFile = new FormFile(
                new MemoryStream(fileContent), 0, exactBytes, "file", "border.pdf")
            {
                Headers = new HeaderDictionary(),
                ContentType = "application/pdf",
            };

            var result = await service.UploadAttachmentAsync(created.Id, borderlineFile, user);
            Assert.Equal(exactBytes, result.FileSizeBytes);
        }
        finally
        {
            Directory.Delete(tmpDir, true);
        }
    }

    // ─── GetMyStats tests ──────────────────────────────────────────────────────

    [Fact]
    public async Task GetMyStats_NoDepartment_ReturnsAllZeros()
    {
        var (db, _, _, userId) = await SeedAsync(nameof(GetMyStats_NoDepartment_ReturnsAllZeros));
        var service = BuildService(db);
        var user = new FakeUser { UserId = userId, DepartmentId = null };

        var stats = await service.GetMyStatsAsync(user);

        Assert.Equal(0, stats.TotalAssigned);
        Assert.Equal(0, stats.PendingResponse);
        Assert.Equal(0, stats.Draft);
        Assert.Equal(0, stats.SubmittedForReview);
        Assert.Equal(0, stats.ReturnedForCorrection);
        Assert.Equal(0, stats.Approved);
        Assert.Equal(0, stats.Rejected);
    }

    [Fact]
    public async Task GetMyStats_NoResponses_ReturnsAllPending()
    {
        var (db, _, deptId, userId) = await SeedAsync(nameof(GetMyStats_NoResponses_ReturnsAllPending));
        var service = BuildService(db);
        var user = new FakeUser { UserId = userId, DepartmentId = deptId };

        var stats = await service.GetMyStatsAsync(user);

        Assert.Equal(1, stats.TotalAssigned);
        Assert.Equal(1, stats.PendingResponse);
        Assert.Equal(0, stats.Draft);
        Assert.Equal(0, stats.SubmittedForReview);
        Assert.Equal(0, stats.Approved);
    }

    [Fact]
    public async Task GetMyStats_WithDraftResponse_CountsCorrectly()
    {
        var (db, txId, deptId, userId) = await SeedAsync(nameof(GetMyStats_WithDraftResponse_CountsCorrectly));
        var service = BuildService(db);
        var user = new FakeUser { UserId = userId, DepartmentId = deptId };

        await service.CreateAsync(new CreateDepartmentResponseRequest(txId, "نص"), user);

        var stats = await service.GetMyStatsAsync(user);

        Assert.Equal(1, stats.TotalAssigned);
        Assert.Equal(0, stats.PendingResponse);
        Assert.Equal(1, stats.Draft);
        Assert.Equal(0, stats.SubmittedForReview);
    }

    [Fact]
    public async Task GetMyStats_WithSubmittedResponse_CountsCorrectly()
    {
        var (db, txId, deptId, userId) = await SeedAsync(nameof(GetMyStats_WithSubmittedResponse_CountsCorrectly));
        var service = BuildService(db);
        var user = new FakeUser { UserId = userId, DepartmentId = deptId };

        var created = await service.CreateAsync(new CreateDepartmentResponseRequest(txId, "نص"), user);
        await service.SubmitAsync(created.Id, user);

        var stats = await service.GetMyStatsAsync(user);

        Assert.Equal(1, stats.TotalAssigned);
        Assert.Equal(0, stats.PendingResponse);
        Assert.Equal(0, stats.Draft);
        Assert.Equal(1, stats.SubmittedForReview);
    }

    [Fact]
    public async Task GetMyStats_WithApprovedResponse_CountsCorrectly()
    {
        var (db, txId, deptId, userId) = await SeedAsync(nameof(GetMyStats_WithApprovedResponse_CountsCorrectly));
        var service = BuildService(db);
        var submitter = new FakeUser { UserId = userId, DepartmentId = deptId };
        var reviewer = new FakeUser { UserId = userId, Role = UserRole.Supervisor };

        var created = await service.CreateAsync(new CreateDepartmentResponseRequest(txId, "نص"), submitter);
        await service.SubmitAsync(created.Id, submitter);
        await service.ApproveAsync(created.Id, reviewer);

        var stats = await service.GetMyStatsAsync(submitter);

        Assert.Equal(1, stats.TotalAssigned);
        Assert.Equal(0, stats.PendingResponse);
        Assert.Equal(0, stats.Draft);
        Assert.Equal(0, stats.SubmittedForReview);
        Assert.Equal(1, stats.Approved);
    }

    [Fact]
    public async Task GetMyStats_WithReturnedResponse_CountsCorrectly()
    {
        var (db, txId, deptId, userId) = await SeedAsync(nameof(GetMyStats_WithReturnedResponse_CountsCorrectly));
        var service = BuildService(db);
        var submitter = new FakeUser { UserId = userId, DepartmentId = deptId };
        var reviewer = new FakeUser { UserId = userId, Role = UserRole.Supervisor };

        var created = await service.CreateAsync(new CreateDepartmentResponseRequest(txId, "نص"), submitter);
        await service.SubmitAsync(created.Id, submitter);
        await service.ReturnForCorrectionAsync(created.Id, new ReviewDepartmentResponseRequest("يحتاج مراجعة"), reviewer);

        var stats = await service.GetMyStatsAsync(submitter);

        Assert.Equal(1, stats.TotalAssigned);
        Assert.Equal(0, stats.PendingResponse);
        Assert.Equal(1, stats.ReturnedForCorrection);
        Assert.Equal(0, stats.Draft);
    }

    [Fact]
    public async Task GetMyStats_DoesNotSeeOtherDepartmentData()
    {
        var (db, txId, deptId, userId) = await SeedAsync(nameof(GetMyStats_DoesNotSeeOtherDepartmentData));
        var service = BuildService(db);
        var owner = new FakeUser { UserId = userId, DepartmentId = deptId };

        await service.CreateAsync(new CreateDepartmentResponseRequest(txId, "نص"), owner);

        var otherDeptUser = new FakeUser { UserId = userId, DepartmentId = deptId + 999 };
        var stats = await service.GetMyStatsAsync(otherDeptUser);

        Assert.Equal(0, stats.TotalAssigned);
        Assert.Equal(0, stats.Draft);
    }

    [Fact]
    public async Task GetMyStats_DoesNotModifyData()
    {
        var (db, _, deptId, userId) = await SeedAsync(nameof(GetMyStats_DoesNotModifyData));
        var service = BuildService(db);
        var user = new FakeUser { UserId = userId, DepartmentId = deptId };

        var beforeCount = await db.DepartmentResponses.CountAsync();
        await service.GetMyStatsAsync(user);
        var afterCount = await db.DepartmentResponses.CountAsync();

        Assert.Equal(beforeCount, afterCount);
    }

    [Fact]
    public async Task GetMyStats_IgnoresResponsesForInactiveAssignments()
    {
        // Tx-A: seeded active assignment, no response → PendingResponse
        // Tx-B: active assignment + draft response → Draft
        // Tx-C: completed assignment + approved response → must NOT appear in any counter
        var (db, _, deptId, userId) = await SeedAsync(nameof(GetMyStats_IgnoresResponsesForInactiveAssignments));
        var service = BuildService(db);
        var submitter = new FakeUser { UserId = userId, DepartmentId = deptId };

        // Tx-B: active assignment + draft response via service
        var txB = new Transaction {
            InternalTrackingNumber = "TX-IGN-B", IncomingNumber = "IN-IGN-B",
            IncomingDate = DateTime.UtcNow, Subject = "معاملة ب",
            Status = TransactionStatus.Assigned, CreatedById = userId,
        };
        db.Transactions.Add(txB);
        db.Assignments.Add(new Assignment {
            Transaction = txB, DepartmentId = deptId, AssignedDate = DateTime.UtcNow,
            RequiresReply = true, ReplyStatus = ReplyStatus.Pending,
            Status = AssignmentStatus.Active, CreatedById = userId,
        });
        await db.SaveChangesAsync();
        await service.CreateAsync(new CreateDepartmentResponseRequest(txB.Id, "نص ب"), submitter);

        // Tx-C: completed assignment + approved response (historical — must be excluded)
        var txC = new Transaction {
            InternalTrackingNumber = "TX-IGN-C", IncomingNumber = "IN-IGN-C",
            IncomingDate = DateTime.UtcNow, Subject = "معاملة ج",
            Status = TransactionStatus.Assigned, CreatedById = userId,
        };
        db.Transactions.Add(txC);
        db.Assignments.Add(new Assignment {
            Transaction = txC, DepartmentId = deptId, AssignedDate = DateTime.UtcNow,
            RequiresReply = true, ReplyStatus = ReplyStatus.Replied,
            Status = AssignmentStatus.Completed, CreatedById = userId,
        });
        await db.SaveChangesAsync();
        db.DepartmentResponses.Add(new DepartmentResponse {
            TransactionId = txC.Id, DepartmentId = deptId,
            ResponseText = "إفادة تاريخية",
            Status = DepartmentResponseStatus.Approved,
            SubmittedByUserId = userId,
        });
        await db.SaveChangesAsync();

        var stats = await service.GetMyStatsAsync(submitter);

        Assert.Equal(2, stats.TotalAssigned);   // Tx-A + Tx-B (active only)
        Assert.Equal(1, stats.PendingResponse); // Tx-A has no response
        Assert.Equal(1, stats.Draft);           // Tx-B draft
        Assert.Equal(0, stats.Approved);        // Tx-C excluded
        Assert.Equal(0, stats.SubmittedForReview);
        Assert.Equal(0, stats.ReturnedForCorrection);
        Assert.Equal(0, stats.Rejected);
    }

    [Fact]
    public async Task GetMyStats_DoesNotDoubleCountDuplicateActiveAssignmentsForSameTransaction()
    {
        // Two Active assignments for the same TransactionId must count as 1 via Distinct()
        var (db, txId, deptId, userId) = await SeedAsync(nameof(GetMyStats_DoesNotDoubleCountDuplicateActiveAssignmentsForSameTransaction));
        var service = BuildService(db);

        db.Assignments.Add(new Assignment {
            TransactionId = txId, DepartmentId = deptId, AssignedDate = DateTime.UtcNow,
            RequiresReply = true, ReplyStatus = ReplyStatus.Pending,
            Status = AssignmentStatus.Active, CreatedById = userId,
        });
        await db.SaveChangesAsync();

        var user = new FakeUser { UserId = userId, DepartmentId = deptId };
        var stats = await service.GetMyStatsAsync(user);

        Assert.Equal(1, stats.TotalAssigned);   // Distinct TransactionId
        Assert.Equal(1, stats.PendingResponse);
        Assert.Equal(0, stats.Draft);
    }
}
