using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.AspNetCore.Mvc;
using Uqeb.Api.Controllers;
using Uqeb.Api.Data;
using Uqeb.Api.DTOs.FollowUpPrint;
using Uqeb.Api.Exceptions;
using Uqeb.Api.Models.Entities;
using Uqeb.Api.Models.Enums;
using Uqeb.Api.Models.Letters;
using Uqeb.Api.Services;
using Xunit;

namespace Uqeb.Api.Tests.Letters;

public class FollowUpLetterPrintRecordServiceTests
{
    private static readonly DateTime Today = new(2025, 6, 25);

    private static FollowUpLetterPrintRecordService CreateService(
        AppDbContext db,
        DateTime today,
        IFollowUpLetterRenderService? renderService = null,
        IAuditService? audit = null) =>
        new(
            db,
            new FixedTimeZone(today),
            renderService ?? new StubRenderService(),
            LettersTestInfrastructure.CreateOptions(),
            audit ?? new NoOpAuditService(),
            NullLogger<FollowUpLetterPrintRecordService>.Instance);

    private static async Task<(Transaction Transaction, FollowUp FollowUp)> SeedTransactionWithFollowUpAsync(AppDbContext db)
    {
        await LettersTestInfrastructure.SeedUserAsync(db);
        await LettersTestInfrastructure.SeedTemplateAsync(db);

        var transaction = new Transaction
        {
            Id = 1,
            InternalTrackingNumber = "INT-1",
            IncomingNumber = "IN-1",
            IncomingDate = Today.AddDays(-30),
            Subject = "معاملة",
            Status = TransactionStatus.InProgress,
            Priority = Priority.Normal,
            CreatedById = 1,
            CreatedAt = DateTime.UtcNow,
        };
        db.Transactions.Add(transaction);

        var followUp = new FollowUp
        {
            Id = 10,
            TransactionId = transaction.Id,
            FollowUpDate = Today.AddDays(-5),
            CreatedById = 1,
            CreatedAt = Today.AddDays(-5),
        };
        db.FollowUps.Add(followUp);
        await db.SaveChangesAsync();

        return (transaction, followUp);
    }

    private static FollowUpLetterPrintRecord CreatePendingRecord(
        int transactionId,
        DateTime printRequestedAt,
        int recordId = 0) =>
        new()
        {
            Id = recordId,
            TransactionId = transactionId,
            TemplateId = 1,
            FollowUpSequence = 1,
            PrintRequestedAt = printRequestedAt,
            PrintRequestedById = 1,
            CreatedAt = printRequestedAt,
        };

    private static FollowUpLetterPrintRecord CreatePendingRecordWithSnapshot(
        int transactionId,
        DateTime printRequestedAt,
        string? documentSnapshotJson)
    {
        var record = CreatePendingRecord(transactionId, printRequestedAt);
        record.DocumentSnapshotJson = documentSnapshotJson;
        return record;
    }

    private static string SnapshotJson(string body = "نص محفوظ") =>
        JsonSerializer.Serialize(new FollowUpLetterDocumentModel
        {
            TransactionId = 1,
            TemplateId = 1,
            Recipient = "جهة محفوظة",
            Subject = "موضوع محفوظ",
            Body = body,
            LetterNumber = "LET-1",
            GregorianDate = "2026/06/26",
            HijriDate = "1448/01/01",
        }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

    private static async Task SeedDirectPrintTargetsAsync(AppDbContext db)
    {
        db.Departments.Add(new Department { Id = 10, Name = "إدارة داخلية", NameNormalized = "إدارة داخلية" });
        db.ExternalParties.Add(new ExternalParty { Id = 20, Name = "جهة خارجية", NameNormalized = "جهة خارجية" });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task GetPendingSummaryAsync_CountsWithinAndOlderThanExclusionWindow()
    {
        await using var db = LettersTestInfrastructure.CreateDb(nameof(GetPendingSummaryAsync_CountsWithinAndOlderThanExclusionWindow));
        var service = CreateService(db, Today);
        await LettersTestInfrastructure.SeedUserAsync(db);
        await LettersTestInfrastructure.SeedTemplateAsync(db);

        db.Transactions.Add(new Transaction
        {
            Id = 1,
            InternalTrackingNumber = "INT-1",
            IncomingNumber = "IN-1",
            IncomingDate = Today.AddDays(-30),
            Subject = "معاملة",
            Status = TransactionStatus.InProgress,
            Priority = Priority.Normal,
            CreatedById = 1,
            CreatedAt = DateTime.UtcNow,
        });
        db.FollowUpLetterPrintRecords.AddRange(
            CreatePendingRecord(1, Today.AddDays(-2)),
            CreatePendingRecord(1, Today.AddDays(-5)),
            CreatePendingRecord(1, Today.AddDays(-10)));
        await db.SaveChangesAsync();

        var summary = await service.GetPendingSummaryAsync();

        Assert.Equal(3, summary.Total);
        Assert.Equal(2, summary.WithinExclusionDays);
        Assert.Equal(1, summary.OlderThanExclusionDays);
    }

    [Fact]
    public async Task LinkToFollowUpAsync_LinksPendingRecordToMatchingFollowUp()
    {
        await using var db = LettersTestInfrastructure.CreateDb(nameof(LinkToFollowUpAsync_LinksPendingRecordToMatchingFollowUp));
        var service = CreateService(db, Today);
        var (transaction, followUp) = await SeedTransactionWithFollowUpAsync(db);

        db.FollowUpLetterPrintRecords.Add(CreatePendingRecord(transaction.Id, Today.AddDays(-1)));
        await db.SaveChangesAsync();

        var recordId = await db.FollowUpLetterPrintRecords.Select(r => r.Id).SingleAsync();
        var user = new TestCurrentUser(1);

        var linked = await service.LinkToFollowUpAsync(recordId, followUp.Id, user);

        Assert.NotNull(linked);
        Assert.Equal(followUp.Id, linked!.RegisteredFollowUpId);

        var stored = await db.FollowUpLetterPrintRecords.SingleAsync();
        Assert.Equal(followUp.Id, stored.RegisteredFollowUpId);
        Assert.NotNull(stored.RegisteredAt);
    }

    [Fact]
    public async Task LinkToFollowUpAsync_RejectsFollowUpFromDifferentTransaction()
    {
        await using var db = LettersTestInfrastructure.CreateDb(nameof(LinkToFollowUpAsync_RejectsFollowUpFromDifferentTransaction));
        var service = CreateService(db, Today);
        await SeedTransactionWithFollowUpAsync(db);

        db.Transactions.Add(new Transaction
        {
            Id = 2,
            InternalTrackingNumber = "INT-2",
            IncomingNumber = "IN-2",
            IncomingDate = Today.AddDays(-20),
            Subject = "أخرى",
            Status = TransactionStatus.InProgress,
            Priority = Priority.Normal,
            CreatedById = 1,
            CreatedAt = DateTime.UtcNow,
        });
        db.FollowUps.Add(new FollowUp
        {
            Id = 20,
            TransactionId = 2,
            FollowUpDate = Today.AddDays(-3),
            CreatedById = 1,
            CreatedAt = Today.AddDays(-3),
        });
        db.FollowUpLetterPrintRecords.Add(CreatePendingRecord(transactionId: 1, printRequestedAt: Today.AddDays(-1)));
        await db.SaveChangesAsync();

        var recordId = await db.FollowUpLetterPrintRecords.Select(r => r.Id).SingleAsync();

        await Assert.ThrowsAsync<FollowUpPrintValidationException>(() =>
            service.LinkToFollowUpAsync(recordId, followUpId: 20, new TestCurrentUser(1)));
    }

    [Fact]
    public async Task ConfirmPrintAsync_IsIdempotentAndKeepsOriginalConfirmation()
    {
        await using var db = LettersTestInfrastructure.CreateDb(nameof(ConfirmPrintAsync_IsIdempotentAndKeepsOriginalConfirmation));
        var service = CreateService(db, Today);
        var (transaction, _) = await SeedTransactionWithFollowUpAsync(db);
        db.FollowUpLetterPrintRecords.Add(CreatePendingRecord(transaction.Id, Today.AddDays(-1)));
        await db.SaveChangesAsync();

        var recordId = await db.FollowUpLetterPrintRecords.Select(r => r.Id).SingleAsync();

        _ = await service.ConfirmPrintAsync(recordId, new TestCurrentUser(1));
        var firstStored = await db.FollowUpLetterPrintRecords.AsNoTracking().SingleAsync(r => r.Id == recordId);

        _ = await service.ConfirmPrintAsync(recordId, new TestCurrentUser(2));
        var secondStored = await db.FollowUpLetterPrintRecords.AsNoTracking().SingleAsync(r => r.Id == recordId);

        Assert.Equal(firstStored.PrintConfirmedAt, secondStored.PrintConfirmedAt);
        Assert.Equal(1, secondStored.PrintConfirmedById);
    }

    [Fact]
    public async Task CancelRecordAsync_IsIdempotentAndKeepsOriginalCancellation()
    {
        await using var db = LettersTestInfrastructure.CreateDb(nameof(CancelRecordAsync_IsIdempotentAndKeepsOriginalCancellation));
        var service = CreateService(db, Today);
        var (transaction, _) = await SeedTransactionWithFollowUpAsync(db);
        db.FollowUpLetterPrintRecords.Add(CreatePendingRecord(transaction.Id, Today.AddDays(-1)));
        await db.SaveChangesAsync();

        var recordId = await db.FollowUpLetterPrintRecords.Select(r => r.Id).SingleAsync();

        _ = await service.CancelRecordAsync(recordId, "سبب أول", new TestCurrentUser(1));
        var firstStored = await db.FollowUpLetterPrintRecords.AsNoTracking().SingleAsync(r => r.Id == recordId);

        _ = await service.CancelRecordAsync(recordId, "سبب ثان", new TestCurrentUser(2));
        var secondStored = await db.FollowUpLetterPrintRecords.AsNoTracking().SingleAsync(r => r.Id == recordId);

        Assert.Equal(firstStored.CancelledAt, secondStored.CancelledAt);
        Assert.Equal(1, secondStored.CancelledById);
        Assert.Equal("سبب أول", secondStored.CancellationReason);
    }

    [Fact]
    public async Task LinkToFollowUpAsync_IsIdempotentForSameFollowUpAndRejectsMove()
    {
        await using var db = LettersTestInfrastructure.CreateDb(nameof(LinkToFollowUpAsync_IsIdempotentForSameFollowUpAndRejectsMove));
        var service = CreateService(db, Today);
        var (transaction, followUp) = await SeedTransactionWithFollowUpAsync(db);
        db.FollowUps.Add(new FollowUp
        {
            Id = 11,
            TransactionId = transaction.Id,
            FollowUpDate = Today.AddDays(-4),
            CreatedById = 1,
            CreatedAt = Today.AddDays(-4),
        });
        db.FollowUpLetterPrintRecords.Add(CreatePendingRecord(transaction.Id, Today.AddDays(-1)));
        await db.SaveChangesAsync();

        var recordId = await db.FollowUpLetterPrintRecords.Select(r => r.Id).SingleAsync();

        var first = await service.LinkToFollowUpAsync(recordId, followUp.Id, new TestCurrentUser(1));
        var second = await service.LinkToFollowUpAsync(recordId, followUp.Id, new TestCurrentUser(2));

        Assert.Equal(first!.RegisteredFollowUpId, second!.RegisteredFollowUpId);
        await Assert.ThrowsAsync<FollowUpPrintConflictException>(() =>
            service.LinkToFollowUpAsync(recordId, 11, new TestCurrentUser(1)));
    }

    [Fact]
    public async Task LinkToFollowUpAsync_RejectsSameFollowUpOnDifferentRecord()
    {
        await using var db = LettersTestInfrastructure.CreateDb(nameof(LinkToFollowUpAsync_RejectsSameFollowUpOnDifferentRecord));
        var service = CreateService(db, Today);
        var (transaction, followUp) = await SeedTransactionWithFollowUpAsync(db);
        db.FollowUpLetterPrintRecords.AddRange(
            CreatePendingRecord(transaction.Id, Today.AddDays(-2)),
            CreatePendingRecord(transaction.Id, Today.AddDays(-1)));
        await db.SaveChangesAsync();

        var recordIds = await db.FollowUpLetterPrintRecords
            .OrderBy(r => r.Id)
            .Select(r => r.Id)
            .ToListAsync();

        var linked = await service.LinkToFollowUpAsync(recordIds[0], followUp.Id, new TestCurrentUser(1));

        Assert.Equal(followUp.Id, linked!.RegisteredFollowUpId);
        await Assert.ThrowsAsync<FollowUpPrintConflictException>(() =>
            service.LinkToFollowUpAsync(recordIds[1], followUp.Id, new TestCurrentUser(1)));
    }

    [Fact]
    public async Task RegisterDirectPrintRequestAsync_UsesIdempotencyAndRejectsConflictingPayload()
    {
        await using var db = LettersTestInfrastructure.CreateDb(nameof(RegisterDirectPrintRequestAsync_UsesIdempotencyAndRejectsConflictingPayload));
        var service = CreateService(db, Today);
        var (transaction, _) = await SeedTransactionWithFollowUpAsync(db);
        var user = new TestCurrentUser(1);
        var request = new CreateDirectPrintRequest
        {
            TargetEntityName = "إدارة أ",
            FollowUpSequence = 1,
            IdempotencyKey = "direct-print-key",
        };

        var first = await service.RegisterDirectPrintRequestAsync(transaction.Id, request, user);
        var second = await service.RegisterDirectPrintRequestAsync(transaction.Id, request, user);

        Assert.Equal(first.Id, second.Id);
        Assert.Equal(1, await db.FollowUpLetterPrintRecords.CountAsync());

        await Assert.ThrowsAsync<FollowUpPrintConflictException>(() =>
            service.RegisterDirectPrintRequestAsync(transaction.Id, new CreateDirectPrintRequest
            {
                TargetEntityName = "إدارة ب",
                FollowUpSequence = 1,
                IdempotencyKey = "direct-print-key",
            }, user));
    }

    [Theory]
    [InlineData(UserRole.Admin, null)]
    [InlineData(UserRole.Supervisor, null)]
    [InlineData(UserRole.DepartmentUser, 10)]
    public async Task GetPrintViewAsync_AllowsAuthorizedRolesAndAssignedDepartment(UserRole role, int? departmentId)
    {
        await using var db = LettersTestInfrastructure.CreateDb($"{nameof(GetPrintViewAsync_AllowsAuthorizedRolesAndAssignedDepartment)}_{role}_{departmentId}");
        var service = CreateService(db, Today);
        var (transaction, _) = await SeedTransactionWithFollowUpAsync(db);
        db.Departments.Add(new Department { Id = 10, Name = "إدارة", NameNormalized = "إدارة" });
        db.Assignments.Add(new Assignment
        {
            TransactionId = transaction.Id,
            DepartmentId = 10,
            AssignedDate = Today,
            CreatedById = 1,
            CreatedAt = Today,
        });
        db.FollowUpLetterPrintRecords.Add(CreatePendingRecordWithSnapshot(
            transaction.Id,
            Today.AddDays(-1),
            SnapshotJson()));
        await db.SaveChangesAsync();

        var recordId = await db.FollowUpLetterPrintRecords.Select(r => r.Id).SingleAsync();

        var view = await service.GetPrintViewAsync(recordId, new TestCurrentUser(1, role, departmentId));

        Assert.NotNull(view);
        Assert.True(view!.UsedStoredSnapshot);
    }

    [Theory]
    [InlineData(11)]
    [InlineData(null)]
    public async Task GetPrintViewAsync_RejectsDepartmentUserWithoutMatchingDepartment(int? departmentId)
    {
        await using var db = LettersTestInfrastructure.CreateDb($"{nameof(GetPrintViewAsync_RejectsDepartmentUserWithoutMatchingDepartment)}_{departmentId}");
        var service = CreateService(db, Today);
        var (transaction, _) = await SeedTransactionWithFollowUpAsync(db);
        db.Departments.Add(new Department { Id = 10, Name = "إدارة", NameNormalized = "إدارة" });
        db.Assignments.Add(new Assignment
        {
            TransactionId = transaction.Id,
            DepartmentId = 10,
            AssignedDate = Today,
            CreatedById = 1,
            CreatedAt = Today,
        });
        db.FollowUpLetterPrintRecords.Add(CreatePendingRecordWithSnapshot(
            transaction.Id,
            Today.AddDays(-1),
            SnapshotJson()));
        await db.SaveChangesAsync();

        var recordId = await db.FollowUpLetterPrintRecords.Select(r => r.Id).SingleAsync();

        await Assert.ThrowsAsync<FollowUpPrintForbiddenException>(() =>
            service.GetPrintViewAsync(recordId, new TestCurrentUser(1, UserRole.DepartmentUser, departmentId)));
    }

    [Fact]
    public async Task GetRecordPrintView_ReturnsForbiddenForDepartmentUserWithoutDepartmentId()
    {
        await using var db = LettersTestInfrastructure.CreateDb(nameof(GetRecordPrintView_ReturnsForbiddenForDepartmentUserWithoutDepartmentId));
        var service = CreateService(db, Today);
        var (transaction, _) = await SeedTransactionWithFollowUpAsync(db);
        db.FollowUpLetterPrintRecords.Add(CreatePendingRecordWithSnapshot(
            transaction.Id,
            Today.AddDays(-1),
            SnapshotJson()));
        await db.SaveChangesAsync();

        var recordId = await db.FollowUpLetterPrintRecords.Select(r => r.Id).SingleAsync();
        var controller = new FollowUpPrintController(
            eligibility: null!,
            jobs: null!,
            records: service,
            render: null!,
            currentUser: new TestCurrentUser(1, UserRole.DepartmentUser));

        var result = await controller.GetRecordPrintView(recordId, CancellationToken.None);

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(403, objectResult.StatusCode);
    }

    [Theory]
    [InlineData(nameof(CreateDirectPrintRequest.TargetDepartmentId))]
    [InlineData(nameof(CreateDirectPrintRequest.TargetEntityId))]
    [InlineData(nameof(CreateDirectPrintRequest.TargetEntityName))]
    public async Task RegisterDirectPrintRequestAsync_AcceptsSingleTargetShape(string targetShape)
    {
        await using var db = LettersTestInfrastructure.CreateDb($"{nameof(RegisterDirectPrintRequestAsync_AcceptsSingleTargetShape)}_{targetShape}");
        var service = CreateService(db, Today);
        var (transaction, _) = await SeedTransactionWithFollowUpAsync(db);
        await SeedDirectPrintTargetsAsync(db);
        var request = new CreateDirectPrintRequest
        {
            FollowUpSequence = 1,
            IdempotencyKey = $"direct-{targetShape}",
        };

        if (targetShape == nameof(CreateDirectPrintRequest.TargetDepartmentId))
            request.TargetDepartmentId = 10;
        else if (targetShape == nameof(CreateDirectPrintRequest.TargetEntityId))
            request.TargetEntityId = 20;
        else
            request.TargetEntityName = "جهة حرة";

        var record = await service.RegisterDirectPrintRequestAsync(transaction.Id, request, new TestCurrentUser(1));

        Assert.True(record.Id > 0);
        Assert.Equal(1, await db.FollowUpLetterPrintRecords.CountAsync());
        Assert.Equal(1, await db.FollowUpPrintIdempotencyKeys.CountAsync());
    }

    [Fact]
    public async Task RegisterDirectPrintRequestAsync_RejectsDepartmentAndEntityTogetherBeforePersistence()
    {
        await using var db = LettersTestInfrastructure.CreateDb(nameof(RegisterDirectPrintRequestAsync_RejectsDepartmentAndEntityTogetherBeforePersistence));
        var service = CreateService(db, Today);
        var (transaction, _) = await SeedTransactionWithFollowUpAsync(db);
        await SeedDirectPrintTargetsAsync(db);

        var ex = await Assert.ThrowsAsync<FollowUpPrintValidationException>(() =>
            service.RegisterDirectPrintRequestAsync(transaction.Id, new CreateDirectPrintRequest
            {
                TargetDepartmentId = 10,
                TargetEntityId = 20,
                FollowUpSequence = 1,
                IdempotencyKey = "invalid-shape",
            }, new TestCurrentUser(1)));

        Assert.Equal("يجب اختيار جهة واحدة فقط للطباعة.", ex.Message);
        Assert.Equal(0, await db.FollowUpLetterPrintRecords.CountAsync());
        Assert.Equal(0, await db.FollowUpPrintIdempotencyKeys.CountAsync());
    }

    [Fact]
    public async Task RegisterDirectPrintRequestAsync_WhenDocumentBuildFails_DoesNotPersistOrReserveIdempotencyKey()
    {
        await using var db = LettersTestInfrastructure.CreateDb(nameof(RegisterDirectPrintRequestAsync_WhenDocumentBuildFails_DoesNotPersistOrReserveIdempotencyKey));
        var audit = new CapturingAuditService();
        var service = CreateService(db, Today, new NullDocumentRenderService(), audit);
        var (transaction, _) = await SeedTransactionWithFollowUpAsync(db);
        var request = new CreateDirectPrintRequest
        {
            TargetEntityName = "جهة",
            FollowUpSequence = 1,
            IdempotencyKey = "null-document-key",
        };

        var ex = await Assert.ThrowsAsync<FollowUpPrintValidationException>(() =>
            service.RegisterDirectPrintRequestAsync(transaction.Id, request, new TestCurrentUser(1)));

        Assert.Equal("تعذر إنشاء خطاب الطباعة.", ex.Message);
        Assert.Equal(0, await db.FollowUpLetterPrintRecords.CountAsync());
        Assert.Equal(0, await db.FollowUpPrintIdempotencyKeys.CountAsync());
        Assert.DoesNotContain(audit.Entries, e => e.Action == AuditAction.FollowUpLetterPrintRequested);

        var recovered = await CreateService(db, Today).RegisterDirectPrintRequestAsync(
            transaction.Id,
            request,
            new TestCurrentUser(1));

        Assert.True(recovered.Id > 0);
        Assert.Equal(1, await db.FollowUpLetterPrintRecords.CountAsync());
        Assert.Equal(1, await db.FollowUpPrintIdempotencyKeys.CountAsync());
    }

    [Fact]
    public async Task GetPrintViewAsync_UsesValidStoredSnapshotWithoutRebuild()
    {
        await using var db = LettersTestInfrastructure.CreateDb(nameof(GetPrintViewAsync_UsesValidStoredSnapshotWithoutRebuild));
        var render = new TrackingRenderService();
        var service = CreateService(db, Today, render);
        var (transaction, _) = await SeedTransactionWithFollowUpAsync(db);
        db.FollowUpLetterPrintRecords.Add(CreatePendingRecordWithSnapshot(
            transaction.Id,
            Today.AddDays(-1),
            SnapshotJson("نص snapshot صالح")));
        await db.SaveChangesAsync();

        var recordId = await db.FollowUpLetterPrintRecords.Select(r => r.Id).SingleAsync();

        var view = await service.GetPrintViewAsync(recordId, new TestCurrentUser(1));

        Assert.NotNull(view);
        Assert.True(view!.UsedStoredSnapshot);
        Assert.Contains("نص snapshot صالح", view.Html);
        Assert.Equal(0, render.BuildDocumentCalls);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task GetPrintViewAsync_RebuildsWhenSnapshotIsMissing(string? snapshotJson)
    {
        await using var db = LettersTestInfrastructure.CreateDb($"{nameof(GetPrintViewAsync_RebuildsWhenSnapshotIsMissing)}_{snapshotJson ?? "null"}");
        var render = new TrackingRenderService("نص معاد البناء");
        var service = CreateService(db, Today, render);
        var (transaction, _) = await SeedTransactionWithFollowUpAsync(db);
        db.FollowUpLetterPrintRecords.Add(CreatePendingRecordWithSnapshot(
            transaction.Id,
            Today.AddDays(-1),
            snapshotJson));
        await db.SaveChangesAsync();

        var recordId = await db.FollowUpLetterPrintRecords.Select(r => r.Id).SingleAsync();

        var view = await service.GetPrintViewAsync(recordId, new TestCurrentUser(1));

        Assert.NotNull(view);
        Assert.False(view!.UsedStoredSnapshot);
        Assert.Contains("نص معاد البناء", view.Html);
        Assert.Equal(1, render.BuildDocumentCalls);
    }

    [Fact]
    public async Task GetPrintViewAsync_RebuildsWhenSnapshotJsonIsInvalid()
    {
        await using var db = LettersTestInfrastructure.CreateDb(nameof(GetPrintViewAsync_RebuildsWhenSnapshotJsonIsInvalid));
        var render = new TrackingRenderService("نص بعد snapshot تالف");
        var service = CreateService(db, Today, render);
        var (transaction, _) = await SeedTransactionWithFollowUpAsync(db);
        db.FollowUpLetterPrintRecords.Add(CreatePendingRecordWithSnapshot(
            transaction.Id,
            Today.AddDays(-1),
            "{not-json"));
        await db.SaveChangesAsync();

        var recordId = await db.FollowUpLetterPrintRecords.Select(r => r.Id).SingleAsync();

        var view = await service.GetPrintViewAsync(recordId, new TestCurrentUser(1));

        Assert.NotNull(view);
        Assert.False(view!.UsedStoredSnapshot);
        Assert.Contains("نص بعد snapshot تالف", view.Html);
        Assert.Equal(1, render.BuildDocumentCalls);
    }

    [Fact]
    public async Task GetPrintViewAsync_WhenSnapshotInvalidAndRebuildFails_ReturnsFunctionalNotFound()
    {
        await using var db = LettersTestInfrastructure.CreateDb(nameof(GetPrintViewAsync_WhenSnapshotInvalidAndRebuildFails_ReturnsFunctionalNotFound));
        var service = CreateService(db, Today, new NullDocumentRenderService());
        var (transaction, _) = await SeedTransactionWithFollowUpAsync(db);
        db.FollowUpLetterPrintRecords.Add(CreatePendingRecordWithSnapshot(
            transaction.Id,
            Today.AddDays(-1),
            "{not-json"));
        await db.SaveChangesAsync();

        var recordId = await db.FollowUpLetterPrintRecords.Select(r => r.Id).SingleAsync();

        var view = await service.GetPrintViewAsync(recordId, new TestCurrentUser(1));

        Assert.Null(view);
    }

    [Fact]
    public async Task GetPendingListAsync_ReturnsOnlyUnlinkedNonCancelledRecords()
    {
        await using var db = LettersTestInfrastructure.CreateDb(nameof(GetPendingListAsync_ReturnsOnlyUnlinkedNonCancelledRecords));
        var service = CreateService(db, Today);
        await LettersTestInfrastructure.SeedUserAsync(db);
        await LettersTestInfrastructure.SeedTemplateAsync(db);

        db.Transactions.Add(new Transaction
        {
            Id = 1,
            InternalTrackingNumber = "INT-1",
            IncomingNumber = "IN-1",
            IncomingDate = Today.AddDays(-30),
            Subject = "معاملة",
            Status = TransactionStatus.InProgress,
            Priority = Priority.Normal,
            CreatedById = 1,
            CreatedAt = DateTime.UtcNow,
        });

        db.FollowUpLetterPrintRecords.AddRange(
            CreatePendingRecord(1, Today.AddDays(-1)),
            new FollowUpLetterPrintRecord
            {
                TransactionId = 1,
                TemplateId = 1,
                FollowUpSequence = 1,
                PrintRequestedAt = Today.AddDays(-2),
                PrintRequestedById = 1,
                RegisteredFollowUpId = 99,
                CreatedAt = Today.AddDays(-2),
            },
            new FollowUpLetterPrintRecord
            {
                TransactionId = 1,
                TemplateId = 1,
                FollowUpSequence = 1,
                PrintRequestedAt = Today.AddDays(-3),
                PrintRequestedById = 1,
                IsCancelled = true,
                CreatedAt = Today.AddDays(-3),
            });
        await db.SaveChangesAsync();

        var pending = await service.GetPendingListAsync();

        Assert.Single(pending);
        Assert.False(pending[0].IsCancelled);
        Assert.Null(pending[0].RegisteredFollowUpId);
    }

    private sealed class NullDocumentRenderService : StubRenderService
    {
        public override Task<FollowUpLetterDocumentModel?> BuildDocumentAsync(FollowUpLetterBuildRequest request) =>
            Task.FromResult<FollowUpLetterDocumentModel?>(null);
    }

    private sealed class TrackingRenderService(string body = "نص معاد البناء") : StubRenderService
    {
        public int BuildDocumentCalls { get; private set; }

        public override Task<FollowUpLetterDocumentModel?> BuildDocumentAsync(FollowUpLetterBuildRequest request)
        {
            BuildDocumentCalls++;
            return Task.FromResult<FollowUpLetterDocumentModel?>(new FollowUpLetterDocumentModel
            {
                TransactionId = request.TransactionId,
                TemplateId = request.TemplateId,
                Recipient = request.Target.Name,
                Subject = "موضوع",
                Body = body,
                LetterNumber = "LET-R",
                GregorianDate = "2026/06/26",
                HijriDate = "1448/01/01",
            });
        }
    }
}
