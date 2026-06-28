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

        await service.CreateAsync(new CreateDepartmentResponseRequest(txId, "مسودة"), submitter);
        var pending = await service.GetPendingReviewAsync();
        Assert.Empty(pending);

        await service.SubmitAsync((await service.GetMyDepartmentResponsesAsync(submitter))[0].Id, submitter);
        pending = await service.GetPendingReviewAsync();
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

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.CreateAsync(new CreateDepartmentResponseRequest(txId, "نص"), user));
    }
}
