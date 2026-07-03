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

public class TransactionRecurringWorkflowTests
{
    private sealed class StubTrackingNumberService : ITrackingNumberService
    {
        private int _counter;

        public Task<string> GenerateNextAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult($"UQEB-2026-{++_counter:D5}");
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
        public TestCurrentUser(UserRole role = UserRole.Admin, int userId = 1, int? departmentId = null)
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
        db.Categories.Add(new Category { Id = 1, Name = "عام", NameNormalized = "عام", IsActive = true });
        db.Departments.Add(new Department { Id = 10, Name = "التشغيل", NameNormalized = "التشغيل", IsActive = true });
        db.Departments.Add(new Department { Id = 20, Name = "المالية", NameNormalized = "المالية", IsActive = true });
        await db.SaveChangesAsync();

        var service = new TransactionService(
            db,
            new AuditService(db),
            new StubTrackingNumberService(),
            new TestCacheInvalidation(),
            new RecurringTransactionTemplateService(db, new AuditService(db), new StubTrackingNumberService()));

        return (service, db);
    }

    private static DateTime SaudiToday() => DateTime.UtcNow.AddHours(3).Date;

    private static CreateTransactionRequest BuildBaseRequest(params int[] outgoingDepartmentIds) => new()
    {
        IncomingNumber = $"IN-{Guid.NewGuid():N}",
        IncomingDate = SaudiToday(),
        Subject = "تقرير شهري من إدارة التشغيل",
        IncomingSourceType = IncomingSourceType.Internal.ToString(),
        IncomingFromDepartmentId = 10,
        OutgoingNumber = outgoingDepartmentIds.Length > 0 ? $"OUT-{Guid.NewGuid():N}" : null,
        OutgoingDate = outgoingDepartmentIds.Length > 0 ? SaudiToday() : null,
        OutgoingDepartmentIds = outgoingDepartmentIds.ToList(),
        ResponseType = ResponseType.Internal.ToString(),
        ResponseDueDays = 7,
        Priority = Priority.Normal.ToString(),
        CategoryId = 1
    };

    [Fact]
    public async Task CreateTransaction_DefaultsToNonRecurring()
    {
        var (service, _) = await CreateServiceAsync(nameof(CreateTransaction_DefaultsToNonRecurring));
        var request = BuildBaseRequest(10);

        var result = await service.CreateAsync(request, userId: 1);

        Assert.False(request.EnableRecurringFollowUp);
        Assert.Null(result.RecurringTemplateId);
        Assert.Null(result.RecurringPeriodLabel);
    }

    [Fact]
    public async Task CreateTransaction_DoesNotCreateRecurringTemplateWhenOptionIsUnchecked()
    {
        var (service, db) = await CreateServiceAsync(nameof(CreateTransaction_DoesNotCreateRecurringTemplateWhenOptionIsUnchecked));
        var request = BuildBaseRequest(10);
        request.EnableRecurringFollowUp = false;
        // Even if leftover recurring-shaped values are present, they must be ignored while unchecked.
        request.RecurringRecurrenceType = "Monthly";
        request.RecurringStartDate = SaudiToday();
        request.RecurringDueDaysAfterPeriodEnd = 10;

        var result = await service.CreateAsync(request, userId: 1);

        Assert.Null(result.RecurringTemplateId);
        Assert.Equal(0, await db.RecurringTransactionTemplates.CountAsync());
    }

    [Fact]
    public async Task CreateAsync_with_recurring_enabled_creates_template_and_links_transaction()
    {
        var (service, db) = await CreateServiceAsync(nameof(CreateAsync_with_recurring_enabled_creates_template_and_links_transaction));
        var request = BuildBaseRequest(10);
        request.EnableRecurringFollowUp = true;
        request.RecurringRecurrenceType = "Monthly";
        request.RecurringStartDate = SaudiToday();
        request.RecurringDueDaysAfterPeriodEnd = 10;

        var result = await service.CreateAsync(request, userId: 1);

        Assert.NotNull(result.RecurringTemplateId);
        Assert.NotNull(result.RecurringPeriodLabel);
        Assert.Equal(1, await db.RecurringTransactionTemplates.CountAsync());

        var template = await db.RecurringTransactionTemplates.FindAsync(result.RecurringTemplateId!.Value);
        Assert.NotNull(template);
        Assert.Equal(RecurringTemplateStatus.Active, template!.Status);
        Assert.Equal(request.Subject, template.SubjectTemplate);
        Assert.Equal(RecurringNextTransactionCreationMethod.Manual, template.NextTransactionCreationMethod);
    }

    [Fact]
    public async Task CreateAsync_recurring_derives_departments_from_outgoing_departments()
    {
        var (service, db) = await CreateServiceAsync(nameof(CreateAsync_recurring_derives_departments_from_outgoing_departments));
        var request = BuildBaseRequest(10, 20);
        request.EnableRecurringFollowUp = true;
        request.RecurringRecurrenceType = "Quarterly";
        request.RecurringStartDate = SaudiToday();
        request.RecurringDueDaysAfterPeriodEnd = 15;

        var result = await service.CreateAsync(request, userId: 1);

        var deptIds = await db.RecurringTransactionTemplateDepartments
            .Where(d => d.TemplateId == result.RecurringTemplateId)
            .Select(d => d.DepartmentId)
            .ToListAsync();
        Assert.Equal(new[] { 10, 20 }, deptIds.OrderBy(x => x));
    }

    [Fact]
    public async Task CreateAsync_recurring_enabled_without_outgoing_departments_fails_validation()
    {
        var (service, db) = await CreateServiceAsync(nameof(CreateAsync_recurring_enabled_without_outgoing_departments_fails_validation));
        var request = BuildBaseRequest();
        request.EnableRecurringFollowUp = true;
        request.RecurringRecurrenceType = "Monthly";
        request.RecurringStartDate = SaudiToday();
        request.RecurringDueDaysAfterPeriodEnd = 10;

        var ex = await Assert.ThrowsAsync<FieldValidationException>(() => service.CreateAsync(request, userId: 1));

        Assert.True(ex.FieldErrors.ContainsKey(nameof(CreateTransactionRequest.OutgoingDepartmentIds)));
        Assert.Equal(0, await db.Transactions.CountAsync());
        Assert.Equal(0, await db.RecurringTransactionTemplates.CountAsync());
    }

    [Fact]
    public async Task CreateAsync_recurring_enabled_missing_RecurrenceType_fails_validation_with_remapped_key()
    {
        var (service, db) = await CreateServiceAsync(nameof(CreateAsync_recurring_enabled_missing_RecurrenceType_fails_validation_with_remapped_key));
        var request = BuildBaseRequest(10);
        request.EnableRecurringFollowUp = true;
        request.RecurringRecurrenceType = null;
        request.RecurringStartDate = SaudiToday();
        request.RecurringDueDaysAfterPeriodEnd = 10;

        var ex = await Assert.ThrowsAsync<FieldValidationException>(() => service.CreateAsync(request, userId: 1));

        Assert.True(ex.FieldErrors.ContainsKey(nameof(CreateTransactionRequest.RecurringRecurrenceType)));
        Assert.Equal(0, await db.Transactions.CountAsync());
    }

    [Fact]
    public async Task CreateAsync_recurring_enabled_missing_StartDate_fails_validation_with_remapped_key()
    {
        var (service, _) = await CreateServiceAsync(nameof(CreateAsync_recurring_enabled_missing_StartDate_fails_validation_with_remapped_key));
        var request = BuildBaseRequest(10);
        request.EnableRecurringFollowUp = true;
        request.RecurringRecurrenceType = "Monthly";
        request.RecurringStartDate = null;
        request.RecurringDueDaysAfterPeriodEnd = 10;

        var ex = await Assert.ThrowsAsync<FieldValidationException>(() => service.CreateAsync(request, userId: 1));

        Assert.True(ex.FieldErrors.ContainsKey(nameof(CreateTransactionRequest.RecurringStartDate)));
    }

    [Fact]
    public async Task CreateAsync_recurring_enabled_invalid_DueDays_fails_validation_with_remapped_key()
    {
        var (service, _) = await CreateServiceAsync(nameof(CreateAsync_recurring_enabled_invalid_DueDays_fails_validation_with_remapped_key));
        var request = BuildBaseRequest(10);
        request.EnableRecurringFollowUp = true;
        request.RecurringRecurrenceType = "Monthly";
        request.RecurringStartDate = SaudiToday();
        request.RecurringDueDaysAfterPeriodEnd = -1;

        var ex = await Assert.ThrowsAsync<FieldValidationException>(() => service.CreateAsync(request, userId: 1));

        Assert.True(ex.FieldErrors.ContainsKey(nameof(CreateTransactionRequest.RecurringDueDaysAfterPeriodEnd)));
    }

    [Fact]
    public async Task CreateAsync_transaction_recurring_supports_SemiAnnual_recurrence()
    {
        var (service, db) = await CreateServiceAsync(nameof(CreateAsync_transaction_recurring_supports_SemiAnnual_recurrence));
        var request = BuildBaseRequest(10);
        request.EnableRecurringFollowUp = true;
        request.RecurringRecurrenceType = "SemiAnnual";
        request.RecurringStartDate = SaudiToday();
        request.RecurringDueDaysAfterPeriodEnd = 15;

        var result = await service.CreateAsync(request, userId: 1);

        var template = await db.RecurringTransactionTemplates.FindAsync(result.RecurringTemplateId!.Value);
        Assert.Equal(RecurrenceType.SemiAnnual, template!.RecurrenceType);
    }

    [Fact]
    public async Task CreateAsync_transaction_recurring_supports_Annual_recurrence()
    {
        var (service, db) = await CreateServiceAsync(nameof(CreateAsync_transaction_recurring_supports_Annual_recurrence));
        var request = BuildBaseRequest(10);
        request.EnableRecurringFollowUp = true;
        request.RecurringRecurrenceType = "Annual";
        request.RecurringStartDate = SaudiToday();
        request.RecurringDueDaysAfterPeriodEnd = 30;

        var result = await service.CreateAsync(request, userId: 1);

        var template = await db.RecurringTransactionTemplates.FindAsync(result.RecurringTemplateId!.Value);
        Assert.Equal(RecurrenceType.Annual, template!.RecurrenceType);
    }

    [Fact]
    public async Task CreateAsync_recurring_accepts_AutomaticOnClose_NextTransactionCreationMethod()
    {
        var (service, db) = await CreateServiceAsync(nameof(CreateAsync_recurring_accepts_AutomaticOnClose_NextTransactionCreationMethod));
        var request = BuildBaseRequest(10);
        request.EnableRecurringFollowUp = true;
        request.RecurringRecurrenceType = "Monthly";
        request.RecurringStartDate = SaudiToday();
        request.RecurringDueDaysAfterPeriodEnd = 10;
        request.RecurringNextTransactionCreationMethod = "AutomaticOnClose";

        var result = await service.CreateAsync(request, userId: 1);

        var template = await db.RecurringTransactionTemplates.FindAsync(result.RecurringTemplateId!.Value);
        Assert.Equal(RecurringNextTransactionCreationMethod.AutomaticOnClose, template!.NextTransactionCreationMethod);
    }

    [Fact]
    public async Task EnableRecurringAsync_links_existing_transaction_to_new_template()
    {
        var (service, db) = await CreateServiceAsync(nameof(EnableRecurringAsync_links_existing_transaction_to_new_template));
        var created = await service.CreateAsync(BuildBaseRequest(10), userId: 1);
        Assert.Null(created.RecurringTemplateId);

        var result = await service.EnableRecurringAsync(created.Id, new EnableRecurringForTransactionRequest
        {
            RecurrenceType = "Monthly",
            StartDate = SaudiToday(),
            DueDaysAfterPeriodEnd = 10
        }, userId: 1);

        Assert.NotNull(result);
        Assert.NotNull(result!.RecurringTemplateId);
        Assert.Equal(1, await db.RecurringTransactionTemplates.CountAsync());
    }

    [Fact]
    public async Task EnableRecurringAsync_returns_null_for_missing_transaction()
    {
        var (service, _) = await CreateServiceAsync(nameof(EnableRecurringAsync_returns_null_for_missing_transaction));

        var result = await service.EnableRecurringAsync(999, new EnableRecurringForTransactionRequest
        {
            RecurrenceType = "Monthly",
            StartDate = SaudiToday(),
            DueDaysAfterPeriodEnd = 10
        }, userId: 1);

        Assert.Null(result);
    }

    [Fact]
    public async Task EnableRecurringAsync_rejects_transaction_already_linked_to_a_template()
    {
        var (service, _) = await CreateServiceAsync(nameof(EnableRecurringAsync_rejects_transaction_already_linked_to_a_template));
        var request = BuildBaseRequest(10);
        request.EnableRecurringFollowUp = true;
        request.RecurringRecurrenceType = "Monthly";
        request.RecurringStartDate = SaudiToday();
        request.RecurringDueDaysAfterPeriodEnd = 10;
        var created = await service.CreateAsync(request, userId: 1);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.EnableRecurringAsync(created.Id, new EnableRecurringForTransactionRequest
            {
                RecurrenceType = "Quarterly",
                StartDate = SaudiToday(),
                DueDaysAfterPeriodEnd = 20
            }, userId: 1));
    }

    [Fact]
    public async Task EnableRecurringAsync_returns_field_errors_with_enable_form_keys()
    {
        var (service, _) = await CreateServiceAsync(nameof(EnableRecurringAsync_returns_field_errors_with_enable_form_keys));
        var created = await service.CreateAsync(BuildBaseRequest(10), userId: 1);

        var ex = await Assert.ThrowsAsync<FieldValidationException>(() =>
            service.EnableRecurringAsync(created.Id, new EnableRecurringForTransactionRequest
            {
                RecurrenceType = null,
                StartDate = null,
                DueDaysAfterPeriodEnd = -1
            }, userId: 1));

        Assert.True(ex.FieldErrors.ContainsKey(nameof(EnableRecurringForTransactionRequest.RecurrenceType)));
        Assert.True(ex.FieldErrors.ContainsKey(nameof(EnableRecurringForTransactionRequest.StartDate)));
        Assert.True(ex.FieldErrors.ContainsKey(nameof(EnableRecurringForTransactionRequest.DueDaysAfterPeriodEnd)));
        Assert.False(ex.FieldErrors.ContainsKey(nameof(CreateTransactionRequest.RecurringRecurrenceType)));
        Assert.False(ex.FieldErrors.ContainsKey(nameof(CreateTransactionRequest.RecurringStartDate)));
        Assert.False(ex.FieldErrors.ContainsKey(nameof(CreateTransactionRequest.RecurringDueDaysAfterPeriodEnd)));
    }

    [Fact]
    public async Task SearchAsync_filters_by_IsRecurring()
    {
        var (service, _) = await CreateServiceAsync(nameof(SearchAsync_filters_by_IsRecurring));
        var recurringRequest = BuildBaseRequest(10);
        recurringRequest.EnableRecurringFollowUp = true;
        recurringRequest.RecurringRecurrenceType = "Monthly";
        recurringRequest.RecurringStartDate = SaudiToday();
        recurringRequest.RecurringDueDaysAfterPeriodEnd = 10;
        await service.CreateAsync(recurringRequest, userId: 1);

        var plainRequest = BuildBaseRequest(10);
        await service.CreateAsync(plainRequest, userId: 1);

        var recurringResults = await service.SearchAsync(
            new TransactionSearchRequest { IsRecurring = true, StatusScope = "all" }, new TestCurrentUser());
        var nonRecurringResults = await service.SearchAsync(
            new TransactionSearchRequest { IsRecurring = false, StatusScope = "all" }, new TestCurrentUser());

        Assert.Single(recurringResults.Items);
        Assert.Single(nonRecurringResults.Items);
        Assert.NotNull(recurringResults.Items[0].RecurringTemplateId);
        Assert.Null(nonRecurringResults.Items[0].RecurringTemplateId);
    }

    [Fact]
    public async Task SearchAsync_filters_by_RecurringRecurrenceType()
    {
        var (service, _) = await CreateServiceAsync(nameof(SearchAsync_filters_by_RecurringRecurrenceType));
        var monthly = BuildBaseRequest(10);
        monthly.EnableRecurringFollowUp = true;
        monthly.RecurringRecurrenceType = "Monthly";
        monthly.RecurringStartDate = SaudiToday();
        monthly.RecurringDueDaysAfterPeriodEnd = 10;
        await service.CreateAsync(monthly, userId: 1);

        var quarterly = BuildBaseRequest(10);
        quarterly.EnableRecurringFollowUp = true;
        quarterly.RecurringRecurrenceType = "Quarterly";
        quarterly.RecurringStartDate = SaudiToday();
        quarterly.RecurringDueDaysAfterPeriodEnd = 10;
        await service.CreateAsync(quarterly, userId: 1);

        var results = await service.SearchAsync(
            new TransactionSearchRequest { RecurringRecurrenceType = "Quarterly", StatusScope = "all" }, new TestCurrentUser());

        Assert.Single(results.Items);
        Assert.Equal("Quarterly", results.Items[0].RecurringRecurrenceType);
    }

    [Fact]
    public async Task SearchAsync_filters_by_RecurringTemplateStatus()
    {
        var (service, db) = await CreateServiceAsync(nameof(SearchAsync_filters_by_RecurringTemplateStatus));
        var request = BuildBaseRequest(10);
        request.EnableRecurringFollowUp = true;
        request.RecurringRecurrenceType = "Monthly";
        request.RecurringStartDate = SaudiToday();
        request.RecurringDueDaysAfterPeriodEnd = 10;
        var created = await service.CreateAsync(request, userId: 1);

        var activeResults = await service.SearchAsync(
            new TransactionSearchRequest { RecurringTemplateStatus = "Active", StatusScope = "all" }, new TestCurrentUser());
        Assert.Single(activeResults.Items);

        var template = await db.RecurringTransactionTemplates.FindAsync(created.RecurringTemplateId!.Value);
        template!.Status = RecurringTemplateStatus.Paused;
        await db.SaveChangesAsync();

        var pausedResults = await service.SearchAsync(
            new TransactionSearchRequest { RecurringTemplateStatus = "Paused", StatusScope = "all" }, new TestCurrentUser());
        var activeResultsAfter = await service.SearchAsync(
            new TransactionSearchRequest { RecurringTemplateStatus = "Active", StatusScope = "all" }, new TestCurrentUser());

        Assert.Single(pausedResults.Items);
        Assert.Empty(activeResultsAfter.Items);
    }

    [Fact]
    public async Task TransactionDetail_shows_recurring_link_after_enabling()
    {
        var (service, _) = await CreateServiceAsync(nameof(TransactionDetail_shows_recurring_link_after_enabling));
        var created = await service.CreateAsync(BuildBaseRequest(10), userId: 1);

        await service.EnableRecurringAsync(created.Id, new EnableRecurringForTransactionRequest
        {
            RecurrenceType = "Monthly",
            StartDate = SaudiToday(),
            DueDaysAfterPeriodEnd = 10
        }, userId: 1);

        var detail = await service.GetByIdAsync(created.Id, new TestCurrentUser());

        Assert.NotNull(detail!.RecurringTemplateId);
        Assert.NotNull(detail.RecurringTemplateTitle);
        Assert.NotNull(detail.RecurringPeriodKey);
        Assert.NotNull(detail.RecurringPeriodLabel);
    }
}
