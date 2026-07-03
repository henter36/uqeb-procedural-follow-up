using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Uqeb.Api.Data;
using Uqeb.Api.DTOs.RecurringTemplates;
using Uqeb.Api.Helpers;
using Uqeb.Api.Models.Entities;
using Uqeb.Api.Models.Enums;
using Uqeb.Api.Services;
using Xunit;

namespace Uqeb.Api.Tests;

public class RecurringTransactionTemplateServiceTests
{
    private sealed class StubTrackingNumberService : ITrackingNumberService
    {
        private int _counter;

        public Task<string> GenerateNextAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult($"UQEB-2026-{++_counter:D5}");
    }

    private static async Task<(RecurringTransactionTemplateService Service, AppDbContext Db)> CreateServiceAsync(string dbName)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(dbName)
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        var db = new AppDbContext(options);

        db.Users.Add(new User { Id = 1, Username = "admin", PasswordHash = "h", FullName = "Admin", Role = UserRole.Admin, IsActive = true });
        db.Departments.Add(new Department { Id = 10, Name = "التشغيل", NameNormalized = "التشغيل", IsActive = true });
        db.Departments.Add(new Department { Id = 20, Name = "المالية", NameNormalized = "المالية", IsActive = true });
        db.Categories.Add(new Category { Id = 1, Name = "تقارير دورية", NameNormalized = "تقارير دورية", IsActive = true });
        await db.SaveChangesAsync();

        var service = new RecurringTransactionTemplateService(db, new AuditService(db), new StubTrackingNumberService());
        return (service, db);
    }

    private static CreateRecurringTemplateRequest ValidMonthlyRequest() => new()
    {
        Title = "تقرير شهري من إدارة التشغيل",
        SubjectTemplate = "تقرير شهري من إدارة التشغيل",
        RecurrenceType = "Monthly",
        StartDate = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        IncomingSourceType = "Internal",
        IncomingFromDepartmentId = 10,
        CategoryId = 1,
        Priority = "Normal",
        ResponseType = "Internal",
        RequiresResponse = true,
        DefaultRequiredAction = "تزويدنا بالتقرير الشهري",
        DueDaysAfterPeriodEnd = 10,
        DepartmentIds = new List<int> { 10 }
    };

    private static CreateRecurringTemplateRequest ValidQuarterlyRequest() => new()
    {
        Title = "تقرير ربع سنوي من إدارة التشغيل",
        SubjectTemplate = "تقرير ربع سنوي من إدارة التشغيل",
        RecurrenceType = "Quarterly",
        StartDate = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        IncomingSourceType = "Internal",
        IncomingFromDepartmentId = 10,
        CategoryId = 1,
        Priority = "Normal",
        ResponseType = "Internal",
        RequiresResponse = true,
        DefaultRequiredAction = "تزويدنا بالتقرير الربع سنوي",
        DueDaysAfterPeriodEnd = 10,
        DepartmentIds = new List<int> { 10, 20 }
    };

    [Fact]
    public async Task CreateAsync_creates_monthly_template()
    {
        var (service, _) = await CreateServiceAsync(nameof(CreateAsync_creates_monthly_template));
        var result = await service.CreateAsync(ValidMonthlyRequest(), userId: 1);

        Assert.Equal("Monthly", result.RecurrenceType);
        Assert.Equal("Active", result.Status);
        Assert.Single(result.Departments);
    }

    [Fact]
    public async Task CreateAsync_creates_quarterly_template()
    {
        var (service, _) = await CreateServiceAsync(nameof(CreateAsync_creates_quarterly_template));
        var result = await service.CreateAsync(ValidQuarterlyRequest(), userId: 1);

        Assert.Equal("Quarterly", result.RecurrenceType);
        Assert.Equal(2, result.Departments.Count);
    }

    [Fact]
    public async Task CreateAsync_rejects_missing_RecurrenceType()
    {
        var (service, _) = await CreateServiceAsync(nameof(CreateAsync_rejects_missing_RecurrenceType));
        var request = ValidMonthlyRequest();
        request.RecurrenceType = null;

        var ex = await Assert.ThrowsAsync<FieldValidationException>(() => service.CreateAsync(request, userId: 1));
        Assert.True(ex.FieldErrors.ContainsKey(nameof(request.RecurrenceType)));
    }

    [Fact]
    public async Task CreateAsync_rejects_missing_SubjectTemplate()
    {
        var (service, _) = await CreateServiceAsync(nameof(CreateAsync_rejects_missing_SubjectTemplate));
        var request = ValidMonthlyRequest();
        request.SubjectTemplate = "";

        var ex = await Assert.ThrowsAsync<FieldValidationException>(() => service.CreateAsync(request, userId: 1));
        Assert.True(ex.FieldErrors.ContainsKey(nameof(request.SubjectTemplate)));
    }

    [Fact]
    public async Task CreateAsync_rejects_invalid_DueDaysAfterPeriodEnd()
    {
        var (service, _) = await CreateServiceAsync(nameof(CreateAsync_rejects_invalid_DueDaysAfterPeriodEnd));
        var request = ValidMonthlyRequest();
        request.DueDaysAfterPeriodEnd = -1;

        var ex = await Assert.ThrowsAsync<FieldValidationException>(() => service.CreateAsync(request, userId: 1));
        Assert.True(ex.FieldErrors.ContainsKey(nameof(request.DueDaysAfterPeriodEnd)));
    }

    [Fact]
    public async Task CreateAsync_rejects_missing_DueDaysAfterPeriodEnd()
    {
        var (service, _) = await CreateServiceAsync(nameof(CreateAsync_rejects_missing_DueDaysAfterPeriodEnd));
        var request = ValidMonthlyRequest();
        request.DueDaysAfterPeriodEnd = null;

        var ex = await Assert.ThrowsAsync<FieldValidationException>(() => service.CreateAsync(request, userId: 1));
        Assert.True(ex.FieldErrors.ContainsKey(nameof(request.DueDaysAfterPeriodEnd)));
    }

    [Fact]
    public async Task CreateAsync_rejects_missing_StartDate_instead_of_defaulting()
    {
        var (service, _) = await CreateServiceAsync(nameof(CreateAsync_rejects_missing_StartDate_instead_of_defaulting));
        var request = ValidMonthlyRequest();
        request.StartDate = null;

        var ex = await Assert.ThrowsAsync<FieldValidationException>(() => service.CreateAsync(request, userId: 1));
        Assert.True(ex.FieldErrors.ContainsKey(nameof(request.StartDate)));
    }

    [Fact]
    public async Task CreateAsync_rejects_EndDate_before_StartDate()
    {
        var (service, _) = await CreateServiceAsync(nameof(CreateAsync_rejects_EndDate_before_StartDate));
        var request = ValidMonthlyRequest();
        request.EndDate = request.StartDate!.Value.AddDays(-1);

        var ex = await Assert.ThrowsAsync<FieldValidationException>(() => service.CreateAsync(request, userId: 1));
        Assert.True(ex.FieldErrors.ContainsKey(nameof(request.EndDate)));
    }

    [Fact]
    public async Task CreateAsync_logs_audit()
    {
        var (service, db) = await CreateServiceAsync(nameof(CreateAsync_logs_audit));
        var result = await service.CreateAsync(ValidMonthlyRequest(), userId: 1);

        var log = await db.AuditLogs.FirstOrDefaultAsync(a => a.Action == AuditAction.CreateRecurringTemplate && a.EntityId == result.Id);
        Assert.NotNull(log);
    }

    [Fact]
    public async Task UpdateAsync_updates_fields_and_logs_audit()
    {
        var (service, db) = await CreateServiceAsync(nameof(UpdateAsync_updates_fields_and_logs_audit));
        var created = await service.CreateAsync(ValidMonthlyRequest(), userId: 1);

        var updateRequest = new CreateRecurringTemplateRequest
        {
            Title = "تقرير شهري محدث",
            SubjectTemplate = "تقرير شهري محدث",
            RecurrenceType = "Monthly",
            StartDate = created.StartDate,
            IncomingSourceType = "Internal",
            IncomingFromDepartmentId = 10,
            CategoryId = 1,
            Priority = "Urgent",
            ResponseType = "Internal",
            RequiresResponse = true,
            DefaultRequiredAction = "إجراء محدث",
            DueDaysAfterPeriodEnd = 15,
            DepartmentIds = new List<int> { 10 }
        };

        var result = await service.UpdateAsync(created.Id, updateRequest, userId: 1);

        Assert.NotNull(result);
        Assert.Equal("تقرير شهري محدث", result!.Title);
        Assert.Equal("Urgent", result.Priority);

        var log = await db.AuditLogs.FirstOrDefaultAsync(a => a.Action == AuditAction.UpdateRecurringTemplate && a.EntityId == created.Id);
        Assert.NotNull(log);
    }

    [Fact]
    public async Task UpdateAsync_removes_unselected_departments()
    {
        var (service, _) = await CreateServiceAsync(nameof(UpdateAsync_removes_unselected_departments));
        var created = await service.CreateAsync(ValidQuarterlyRequest(), userId: 1);
        Assert.Equal(2, created.Departments.Count);

        var updateRequest = BuildUpdateRequestFrom(created, new List<int> { 10 });
        var result = await service.UpdateAsync(created.Id, updateRequest, userId: 1);

        Assert.NotNull(result);
        Assert.Single(result!.Departments);
        Assert.Equal(10, result.Departments[0].DepartmentId);
    }

    [Fact]
    public async Task UpdateAsync_adds_new_departments_with_sort_order()
    {
        var (service, _) = await CreateServiceAsync(nameof(UpdateAsync_adds_new_departments_with_sort_order));
        var request = ValidMonthlyRequest();
        request.DepartmentIds = new List<int> { 10 };
        var created = await service.CreateAsync(request, userId: 1);

        var updateRequest = BuildUpdateRequestFrom(created, new List<int> { 10, 20 });
        var result = await service.UpdateAsync(created.Id, updateRequest, userId: 1);

        Assert.NotNull(result);
        Assert.Equal(2, result!.Departments.Count);
        Assert.Equal(10, result.Departments[0].DepartmentId);
        Assert.Equal(0, result.Departments[0].SortOrder);
        Assert.Equal(20, result.Departments[1].DepartmentId);
        Assert.Equal(1, result.Departments[1].SortOrder);
    }

    [Fact]
    public async Task UpdateAsync_updates_existing_department_sort_order()
    {
        var (service, _) = await CreateServiceAsync(nameof(UpdateAsync_updates_existing_department_sort_order));
        var created = await service.CreateAsync(ValidQuarterlyRequest(), userId: 1);
        Assert.Equal(10, created.Departments[0].DepartmentId);
        Assert.Equal(20, created.Departments[1].DepartmentId);

        var updateRequest = BuildUpdateRequestFrom(created, new List<int> { 20, 10 });
        var result = await service.UpdateAsync(created.Id, updateRequest, userId: 1);

        Assert.NotNull(result);
        Assert.Equal(20, result!.Departments[0].DepartmentId);
        Assert.Equal(0, result.Departments[0].SortOrder);
        Assert.Equal(10, result.Departments[1].DepartmentId);
        Assert.Equal(1, result.Departments[1].SortOrder);
    }

    private static CreateRecurringTemplateRequest BuildUpdateRequestFrom(RecurringTemplateDetailDto template, List<int> departmentIds) => new()
    {
        Title = template.Title,
        SubjectTemplate = template.SubjectTemplate,
        RecurrenceType = template.RecurrenceType,
        StartDate = template.StartDate,
        EndDate = template.EndDate,
        IncomingSourceType = template.IncomingSourceType,
        IncomingFromPartyId = template.IncomingFromPartyId,
        IncomingFromDepartmentId = template.IncomingFromDepartmentId,
        CategoryId = template.CategoryId,
        Priority = template.Priority,
        ResponseType = template.ResponseType,
        RequiresResponse = template.RequiresResponse,
        DefaultRequiredAction = template.DefaultRequiredAction,
        DueDaysAfterPeriodEnd = template.DueDaysAfterPeriodEnd,
        DefaultReplyDueDays = template.DefaultReplyDueDays,
        Notes = template.Notes,
        DepartmentIds = departmentIds,
    };

    [Fact]
    public async Task PauseAsync_pauses_active_template()
    {
        var (service, _) = await CreateServiceAsync(nameof(PauseAsync_pauses_active_template));
        var created = await service.CreateAsync(ValidMonthlyRequest(), userId: 1);

        var result = await service.PauseAsync(created.Id, userId: 1);

        Assert.NotNull(result);
        Assert.Equal("Paused", result!.Status);
    }

    [Fact]
    public async Task GenerateAsync_blocked_when_template_Paused()
    {
        var (service, _) = await CreateServiceAsync(nameof(GenerateAsync_blocked_when_template_Paused));
        var created = await service.CreateAsync(ValidMonthlyRequest(), userId: 1);
        await service.PauseAsync(created.Id, userId: 1);

        await Assert.ThrowsAsync<RecurringTemplatePausedException>(() =>
            service.GenerateAsync(created.Id, new GenerateRecurringTransactionRequest
            {
                PeriodKey = "2026-01",
                IncomingDate = new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc),
                ReferralDate = new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc)
            }, userId: 1));
    }

    [Fact]
    public async Task ResumeAsync_reactivates_paused_template()
    {
        var (service, _) = await CreateServiceAsync(nameof(ResumeAsync_reactivates_paused_template));
        var created = await service.CreateAsync(ValidMonthlyRequest(), userId: 1);
        await service.PauseAsync(created.Id, userId: 1);

        var result = await service.ResumeAsync(created.Id, userId: 1);

        Assert.NotNull(result);
        Assert.Equal("Active", result!.Status);
    }

    [Fact]
    public async Task TerminateAsync_requires_reason()
    {
        var (service, _) = await CreateServiceAsync(nameof(TerminateAsync_requires_reason));
        var created = await service.CreateAsync(ValidMonthlyRequest(), userId: 1);

        await Assert.ThrowsAsync<FieldValidationException>(() =>
            service.TerminateAsync(created.Id, new TerminateRecurringTemplateRequest { Reason = "" }, userId: 1));
    }

    [Fact]
    public async Task TerminateAsync_terminates_template_and_logs_audit()
    {
        var (service, db) = await CreateServiceAsync(nameof(TerminateAsync_terminates_template_and_logs_audit));
        var created = await service.CreateAsync(ValidMonthlyRequest(), userId: 1);

        var result = await service.TerminateAsync(created.Id, new TerminateRecurringTemplateRequest { Reason = "توقف العمل بهذا التقرير" }, userId: 1);

        Assert.NotNull(result);
        Assert.Equal("Terminated", result!.Status);
        Assert.Equal("توقف العمل بهذا التقرير", result.TerminationReason);

        var log = await db.AuditLogs.FirstOrDefaultAsync(a => a.Action == AuditAction.TerminateRecurringTemplate && a.EntityId == created.Id);
        Assert.NotNull(log);
    }

    [Fact]
    public async Task GenerateAsync_blocked_when_template_Terminated()
    {
        var (service, _) = await CreateServiceAsync(nameof(GenerateAsync_blocked_when_template_Terminated));
        var created = await service.CreateAsync(ValidMonthlyRequest(), userId: 1);
        await service.TerminateAsync(created.Id, new TerminateRecurringTemplateRequest { Reason = "سبب" }, userId: 1);

        await Assert.ThrowsAsync<RecurringTemplateTerminatedException>(() =>
            service.GenerateAsync(created.Id, new GenerateRecurringTransactionRequest
            {
                PeriodKey = "2026-01",
                IncomingDate = new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc),
                ReferralDate = new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc)
            }, userId: 1));
    }

    [Fact]
    public async Task ResumeAsync_rejects_reactivating_Terminated_template()
    {
        var (service, _) = await CreateServiceAsync(nameof(ResumeAsync_rejects_reactivating_Terminated_template));
        var created = await service.CreateAsync(ValidMonthlyRequest(), userId: 1);
        await service.TerminateAsync(created.Id, new TerminateRecurringTemplateRequest { Reason = "سبب" }, userId: 1);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ResumeAsync(created.Id, userId: 1));
    }

    [Fact]
    public async Task GenerateAsync_rejects_period_before_StartDate()
    {
        var (service, _) = await CreateServiceAsync(nameof(GenerateAsync_rejects_period_before_StartDate));
        var request = ValidMonthlyRequest();
        request.StartDate = new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc);
        var created = await service.CreateAsync(request, userId: 1);

        await Assert.ThrowsAsync<RecurringTemplatePeriodOutOfRangeException>(() =>
            service.GenerateAsync(created.Id, new GenerateRecurringTransactionRequest
            {
                PeriodKey = "2026-01",
                IncomingDate = new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc),
                ReferralDate = new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc)
            }, userId: 1));
    }

    [Fact]
    public async Task GenerateAsync_rejects_period_after_EndDate()
    {
        var (service, _) = await CreateServiceAsync(nameof(GenerateAsync_rejects_period_after_EndDate));
        var request = ValidMonthlyRequest();
        request.EndDate = new DateTime(2026, 3, 31, 0, 0, 0, DateTimeKind.Utc);
        var created = await service.CreateAsync(request, userId: 1);

        await Assert.ThrowsAsync<RecurringTemplatePeriodOutOfRangeException>(() =>
            service.GenerateAsync(created.Id, new GenerateRecurringTransactionRequest
            {
                PeriodKey = "2026-05",
                IncomingDate = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc),
                ReferralDate = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc)
            }, userId: 1));
    }

    [Fact]
    public async Task GenerateAsync_creates_monthly_transaction_with_assignments()
    {
        var (service, db) = await CreateServiceAsync(nameof(GenerateAsync_creates_monthly_transaction_with_assignments));
        var created = await service.CreateAsync(ValidMonthlyRequest(), userId: 1);

        var result = await service.GenerateAsync(created.Id, new GenerateRecurringTransactionRequest
        {
            PeriodKey = "2026-01",
            IncomingDate = new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc),
            ReferralDate = new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc),
            ReferralLetterNumber = "ABC-123"
        }, userId: 1);

        Assert.NotNull(result);
        Assert.Equal("2026-01", result!.PeriodKey);
        Assert.Equal("يناير 2026", result.PeriodLabel);
        Assert.Equal(new DateTime(2026, 2, 10, 0, 0, 0, DateTimeKind.Utc), result.DueDate);

        var tx = await db.Transactions.Include(t => t.Assignments).FirstOrDefaultAsync(t => t.Id == result.TransactionId);
        Assert.NotNull(tx);
        Assert.Equal(created.Id, tx!.RecurringTemplateId);
        Assert.Equal("2026-01", tx.RecurringPeriodKey);
        Assert.Equal("يناير 2026", tx.RecurringPeriodLabel);
        Assert.Contains("يناير 2026", tx.Subject);
        Assert.Single(tx.Assignments);
        Assert.Equal("ABC-123", tx.Assignments.First().LetterNumber);
        Assert.Equal("ABC-123", tx.OutgoingNumber);
    }

    [Fact]
    public async Task GenerateAsync_creates_quarterly_transaction()
    {
        var (service, db) = await CreateServiceAsync(nameof(GenerateAsync_creates_quarterly_transaction));
        var created = await service.CreateAsync(ValidQuarterlyRequest(), userId: 1);

        var result = await service.GenerateAsync(created.Id, new GenerateRecurringTransactionRequest
        {
            PeriodKey = "2026-Q1",
            IncomingDate = new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc),
            ReferralDate = new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc)
        }, userId: 1);

        Assert.NotNull(result);
        Assert.Equal("الربع الأول 2026", result!.PeriodLabel);
        Assert.Equal(new DateTime(2026, 4, 10, 0, 0, 0, DateTimeKind.Utc), result.DueDate);

        var tx = await db.Transactions.Include(t => t.Assignments).FirstOrDefaultAsync(t => t.Id == result.TransactionId);
        Assert.Equal(2, tx!.Assignments.Count);
    }

    [Fact]
    public async Task GenerateAsync_rejects_duplicate_period_for_same_template()
    {
        var (service, _) = await CreateServiceAsync(nameof(GenerateAsync_rejects_duplicate_period_for_same_template));
        var created = await service.CreateAsync(ValidMonthlyRequest(), userId: 1);

        await service.GenerateAsync(created.Id, new GenerateRecurringTransactionRequest
        {
            PeriodKey = "2026-01",
            IncomingDate = new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc),
            ReferralDate = new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc)
        }, userId: 1);

        await Assert.ThrowsAsync<RecurringTemplatePeriodAlreadyGeneratedException>(() =>
            service.GenerateAsync(created.Id, new GenerateRecurringTransactionRequest
            {
                PeriodKey = "2026-01",
                IncomingDate = new DateTime(2026, 2, 2, 0, 0, 0, DateTimeKind.Utc),
                ReferralDate = new DateTime(2026, 2, 2, 0, 0, 0, DateTimeKind.Utc)
            }, userId: 1));
    }

    [Fact]
    public async Task GenerateAsync_allows_same_period_for_different_template()
    {
        var (service, _) = await CreateServiceAsync(nameof(GenerateAsync_allows_same_period_for_different_template));
        var template1 = await service.CreateAsync(ValidMonthlyRequest(), userId: 1);
        var template2 = await service.CreateAsync(ValidMonthlyRequest(), userId: 1);

        await service.GenerateAsync(template1.Id, new GenerateRecurringTransactionRequest
        {
            PeriodKey = "2026-01",
            IncomingDate = new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc),
            ReferralDate = new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc)
        }, userId: 1);

        var result2 = await service.GenerateAsync(template2.Id, new GenerateRecurringTransactionRequest
        {
            PeriodKey = "2026-01",
            IncomingDate = new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc),
            ReferralDate = new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc)
        }, userId: 1);

        Assert.NotNull(result2);
    }

    [Fact]
    public async Task GenerateAsync_rejects_future_IncomingDate()
    {
        var (service, _) = await CreateServiceAsync(nameof(GenerateAsync_rejects_future_IncomingDate));
        var created = await service.CreateAsync(ValidMonthlyRequest(), userId: 1);

        var ex = await Assert.ThrowsAsync<FieldValidationException>(() =>
            service.GenerateAsync(created.Id, new GenerateRecurringTransactionRequest
            {
                PeriodKey = "2026-01",
                IncomingDate = DateTime.UtcNow.AddDays(5),
                ReferralDate = new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc)
            }, userId: 1));

        Assert.True(ex.FieldErrors.ContainsKey("IncomingDate"));
    }

    [Fact]
    public async Task GenerateAsync_rejects_future_ReferralDate()
    {
        var (service, _) = await CreateServiceAsync(nameof(GenerateAsync_rejects_future_ReferralDate));
        var created = await service.CreateAsync(ValidMonthlyRequest(), userId: 1);

        var ex = await Assert.ThrowsAsync<FieldValidationException>(() =>
            service.GenerateAsync(created.Id, new GenerateRecurringTransactionRequest
            {
                PeriodKey = "2026-01",
                IncomingDate = new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc),
                ReferralDate = DateTime.UtcNow.AddDays(5)
            }, userId: 1));

        Assert.True(ex.FieldErrors.ContainsKey("ReferralDate"));
    }

    [Fact]
    public async Task GenerateAsync_rejects_missing_IncomingDate_instead_of_defaulting()
    {
        var (service, _) = await CreateServiceAsync(nameof(GenerateAsync_rejects_missing_IncomingDate_instead_of_defaulting));
        var created = await service.CreateAsync(ValidMonthlyRequest(), userId: 1);

        var ex = await Assert.ThrowsAsync<FieldValidationException>(() =>
            service.GenerateAsync(created.Id, new GenerateRecurringTransactionRequest
            {
                PeriodKey = "2026-01",
                IncomingDate = null,
                ReferralDate = new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc)
            }, userId: 1));

        Assert.True(ex.FieldErrors.ContainsKey("IncomingDate"));
    }

    [Fact]
    public async Task GenerateAsync_rejects_missing_ReferralDate_instead_of_defaulting()
    {
        var (service, _) = await CreateServiceAsync(nameof(GenerateAsync_rejects_missing_ReferralDate_instead_of_defaulting));
        var created = await service.CreateAsync(ValidMonthlyRequest(), userId: 1);

        var ex = await Assert.ThrowsAsync<FieldValidationException>(() =>
            service.GenerateAsync(created.Id, new GenerateRecurringTransactionRequest
            {
                PeriodKey = "2026-01",
                IncomingDate = new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc),
                ReferralDate = null
            }, userId: 1));

        Assert.True(ex.FieldErrors.ContainsKey("ReferralDate"));
    }

    [Fact]
    public async Task GenerateAsync_accepts_future_DueDate()
    {
        var (service, _) = await CreateServiceAsync(nameof(GenerateAsync_accepts_future_DueDate));
        var created = await service.CreateAsync(ValidMonthlyRequest(), userId: 1);

        var result = await service.GenerateAsync(created.Id, new GenerateRecurringTransactionRequest
        {
            PeriodKey = "2026-07",
            IncomingDate = new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc),
            ReferralDate = new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc)
        }, userId: 1);

        Assert.NotNull(result);
        Assert.True(result!.DueDate > DateTime.UtcNow);
    }

    [Fact]
    public async Task GenerateAsync_updates_LastGeneratedPeriodKey_on_template()
    {
        var (service, db) = await CreateServiceAsync(nameof(GenerateAsync_updates_LastGeneratedPeriodKey_on_template));
        var created = await service.CreateAsync(ValidMonthlyRequest(), userId: 1);

        await service.GenerateAsync(created.Id, new GenerateRecurringTransactionRequest
        {
            PeriodKey = "2026-01",
            IncomingDate = new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc),
            ReferralDate = new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc)
        }, userId: 1);

        var template = await db.RecurringTransactionTemplates.FindAsync(created.Id);
        Assert.Equal("2026-01", template!.LastGeneratedPeriodKey);
    }

    [Fact]
    public async Task GenerateAsync_logs_audit_with_transaction_and_period()
    {
        var (service, db) = await CreateServiceAsync(nameof(GenerateAsync_logs_audit_with_transaction_and_period));
        var created = await service.CreateAsync(ValidMonthlyRequest(), userId: 1);

        var result = await service.GenerateAsync(created.Id, new GenerateRecurringTransactionRequest
        {
            PeriodKey = "2026-01",
            IncomingDate = new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc),
            ReferralDate = new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc)
        }, userId: 1);

        var log = await db.AuditLogs.FirstOrDefaultAsync(a =>
            a.Action == AuditAction.GenerateRecurringTransaction && a.TransactionId == result!.TransactionId);
        Assert.NotNull(log);
    }

    [Fact]
    public async Task CloseAssociatedTransaction_does_not_change_template_status()
    {
        var (service, db) = await CreateServiceAsync(nameof(CloseAssociatedTransaction_does_not_change_template_status));
        var created = await service.CreateAsync(ValidMonthlyRequest(), userId: 1);

        var result = await service.GenerateAsync(created.Id, new GenerateRecurringTransactionRequest
        {
            PeriodKey = "2026-01",
            IncomingDate = new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc),
            ReferralDate = new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc)
        }, userId: 1);

        var tx = await db.Transactions.FindAsync(result!.TransactionId);
        tx!.Status = TransactionStatus.Closed;
        tx.ClosedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();

        var template = await service.GetByIdAsync(created.Id);
        Assert.Equal("Active", template!.Status);
    }

    [Fact]
    public async Task TerminateAsync_does_not_close_previously_generated_transactions()
    {
        var (service, db) = await CreateServiceAsync(nameof(TerminateAsync_does_not_close_previously_generated_transactions));
        var created = await service.CreateAsync(ValidMonthlyRequest(), userId: 1);

        var result = await service.GenerateAsync(created.Id, new GenerateRecurringTransactionRequest
        {
            PeriodKey = "2026-01",
            IncomingDate = new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc),
            ReferralDate = new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc)
        }, userId: 1);

        await service.TerminateAsync(created.Id, new TerminateRecurringTemplateRequest { Reason = "سبب" }, userId: 1);

        var tx = await db.Transactions.FindAsync(result!.TransactionId);
        Assert.NotEqual(TransactionStatus.Closed, tx!.Status);
    }

    [Fact]
    public async Task GetTransactionsAsync_lists_transactions_linked_to_template()
    {
        var (service, _) = await CreateServiceAsync(nameof(GetTransactionsAsync_lists_transactions_linked_to_template));
        var created = await service.CreateAsync(ValidMonthlyRequest(), userId: 1);

        await service.GenerateAsync(created.Id, new GenerateRecurringTransactionRequest
        {
            PeriodKey = "2026-01",
            IncomingDate = new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc),
            ReferralDate = new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc)
        }, userId: 1);

        var transactions = await service.GetTransactionsAsync(created.Id);

        Assert.NotNull(transactions);
        Assert.Single(transactions!);
        Assert.Equal("2026-01", transactions![0].PeriodKey);
    }

    [Fact]
    public async Task GetAllAsync_returns_GeneratedTransactionsCount()
    {
        var (service, _) = await CreateServiceAsync(nameof(GetAllAsync_returns_GeneratedTransactionsCount));
        var created = await service.CreateAsync(ValidMonthlyRequest(), userId: 1);

        await service.GenerateAsync(created.Id, new GenerateRecurringTransactionRequest
        {
            PeriodKey = "2026-01",
            IncomingDate = new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc),
            ReferralDate = new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc)
        }, userId: 1);

        var list = await service.GetAllAsync();
        var item = list.Single(t => t.Id == created.Id);
        Assert.Equal(1, item.GeneratedTransactionsCount);
    }
}
