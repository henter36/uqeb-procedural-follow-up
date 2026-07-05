using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Uqeb.Api.Data;
using Uqeb.Api.DTOs.Reports;
using Uqeb.Api.Helpers;
using Uqeb.Api.Models.Entities;
using Uqeb.Api.Models.Enums;
using Uqeb.Api.Services;
using Xunit;

namespace Uqeb.Api.Tests;

public class RecurringObligationsReportServiceTests
{
    // A start date far enough in the past that, with no generated periods yet, the
    // computed next-period due date is always in the past (Feb 2020) regardless of
    // when this test suite runs. This is the only way to assert "Overdue" without
    // depending on the wall clock.
    private static readonly DateTime FarPastStartDate = new(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private sealed class TestDbContextFactory : IDbContextFactory<AppDbContext>
    {
        private readonly DbContextOptions<AppDbContext> _options;
        public TestDbContextFactory(DbContextOptions<AppDbContext> options) => _options = options;
        public AppDbContext CreateDbContext() => new(_options);
    }

    private static DbContextOptions<AppDbContext> CreateOptions(string dbName) =>
        new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(dbName)
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

    private static ReportService CreateService(DbContextOptions<AppDbContext> options) =>
        new(new AppDbContext(options), new TestDbContextFactory(options));

    // Day-1-of-the-current-month anchor: AddMonths on a day-1 date is always lossless
    // (day 1 exists in every month), so the resulting "upcoming" due date is robust
    // against whatever calendar day the test suite actually runs on.
    private static DateTime CurrentMonthAnchor() =>
        new(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1, 0, 0, 0, DateTimeKind.Utc);

    private static async Task SeedAsync(DbContextOptions<AppDbContext> options)
    {
        using var db = new AppDbContext(options);

        db.Users.Add(new User { Id = 1, Username = "admin", PasswordHash = "h", FullName = "Admin", Role = UserRole.Admin, IsActive = true });
        db.Departments.Add(new Department { Id = 10, Name = "الإدارة أ", NameNormalized = "الإدارة أ", IsActive = true });
        db.Departments.Add(new Department { Id = 20, Name = "الإدارة ب", NameNormalized = "الإدارة ب", IsActive = true });
        db.Departments.Add(new Department { Id = 30, Name = "إدارة مسؤولة", NameNormalized = "إدارة مسؤولة", IsActive = true });
        db.Categories.Add(new Category { Id = 1, Name = "تقارير دورية", NameNormalized = "تقارير دورية", IsActive = true });
        await db.SaveChangesAsync();

        var anchor = CurrentMonthAnchor();
        var thisMonthKey = RecurringPeriodCalculator.GetPeriodKeyForDate(RecurrenceType.Monthly, anchor);

        // T1: Active, Monthly, deeply overdue (never generated since 2020).
        db.RecurringTransactionTemplates.Add(new RecurringTransactionTemplate
        {
            Id = 1,
            Title = "التزام أ متأخر",
            SubjectTemplate = "تقرير شهري",
            RecurrenceType = RecurrenceType.Monthly,
            Status = RecurringTemplateStatus.Active,
            StartDate = FarPastStartDate,
            IncomingSourceType = IncomingSourceType.Internal,
            IncomingFromDepartmentId = 10,
            CategoryId = 1,
            Priority = Priority.Normal,
            ResponseType = ResponseType.None,
            DefaultRequiredAction = "تحديث",
            CreatedById = 1,
        });

        // T2: Active, Quarterly, deeply overdue, Urgent priority.
        db.RecurringTransactionTemplates.Add(new RecurringTransactionTemplate
        {
            Id = 2,
            Title = "التزام أ ربع سنوي متأخر",
            SubjectTemplate = "تقرير ربع سنوي",
            RecurrenceType = RecurrenceType.Quarterly,
            Status = RecurringTemplateStatus.Active,
            StartDate = FarPastStartDate,
            IncomingSourceType = IncomingSourceType.Internal,
            IncomingFromDepartmentId = 10,
            CategoryId = 1,
            Priority = Priority.Urgent,
            ResponseType = ResponseType.None,
            DefaultRequiredAction = "تحديث",
            CreatedById = 1,
        });

        // T3: Active, Monthly, robustly upcoming (next due ~2 months from "now"),
        // owning department 20, plus a separate responsible department 30.
        var t3 = new RecurringTransactionTemplate
        {
            Id = 3,
            Title = "التزام ب قادم",
            SubjectTemplate = "تقرير شهري",
            RecurrenceType = RecurrenceType.Monthly,
            Status = RecurringTemplateStatus.Active,
            StartDate = anchor,
            LastGeneratedPeriodKey = thisMonthKey,
            IncomingSourceType = IncomingSourceType.Internal,
            IncomingFromDepartmentId = 20,
            CategoryId = 1,
            Priority = Priority.Normal,
            ResponseType = ResponseType.None,
            DefaultRequiredAction = "تحديث",
            CreatedById = 1,
        };
        t3.Departments.Add(new RecurringTransactionTemplateDepartment { DepartmentId = 30, SortOrder = 0 });
        db.RecurringTransactionTemplates.Add(t3);

        // T4: Paused, owning department 10.
        db.RecurringTransactionTemplates.Add(new RecurringTransactionTemplate
        {
            Id = 4,
            Title = "التزام أ موقوف",
            SubjectTemplate = "تقرير شهري",
            RecurrenceType = RecurrenceType.Monthly,
            Status = RecurringTemplateStatus.Paused,
            StartDate = FarPastStartDate,
            IncomingSourceType = IncomingSourceType.Internal,
            IncomingFromDepartmentId = 10,
            CategoryId = 1,
            Priority = Priority.Normal,
            ResponseType = ResponseType.None,
            DefaultRequiredAction = "تحديث",
            CreatedById = 1,
        });

        // T5: Terminated, owning department 20.
        db.RecurringTransactionTemplates.Add(new RecurringTransactionTemplate
        {
            Id = 5,
            Title = "التزام ب منتهٍ",
            SubjectTemplate = "تقرير شهري",
            RecurrenceType = RecurrenceType.Monthly,
            Status = RecurringTemplateStatus.Terminated,
            StartDate = FarPastStartDate,
            IncomingSourceType = IncomingSourceType.Internal,
            IncomingFromDepartmentId = 20,
            CategoryId = 1,
            Priority = Priority.Normal,
            ResponseType = ResponseType.None,
            DefaultRequiredAction = "تحديث",
            CreatedById = 1,
        });

        await db.SaveChangesAsync();

        // A completed generated transaction for T1, to exercise LastCompletionDate/GeneratedTransactionsCount.
        db.Transactions.Add(new Transaction
        {
            Id = 100,
            InternalTrackingNumber = "UQEB-2020-00001",
            IncomingNumber = "IN-100",
            IncomingDate = new DateTime(2020, 2, 1, 0, 0, 0, DateTimeKind.Utc),
            Subject = "فترة سابقة",
            IncomingSourceType = IncomingSourceType.Internal,
            IncomingFromDepartmentId = 10,
            CategoryId = 1,
            Priority = Priority.Normal,
            ResponseType = ResponseType.None,
            RequiresResponse = false,
            ResponseCompleted = true,
            ResponseCompletedDate = new DateTime(2020, 2, 5, 0, 0, 0, DateTimeKind.Utc),
            ClosedAt = new DateTime(2020, 2, 6, 0, 0, 0, DateTimeKind.Utc),
            Status = TransactionStatus.Closed,
            RecurringTemplateId = 1,
            RecurringPeriodKey = "2020-01",
            CreatedById = 1,
            CreatedAt = new DateTime(2020, 2, 1, 0, 0, 0, DateTimeKind.Utc),
        });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task GetRecurringObligationsSummaryAsync_computes_expected_bucket_counts()
    {
        var options = CreateOptions(nameof(GetRecurringObligationsSummaryAsync_computes_expected_bucket_counts));
        await SeedAsync(options);
        var service = CreateService(options);

        var summary = await service.GetRecurringObligationsSummaryAsync();

        Assert.Equal(5, summary.Total);
        Assert.Equal(3, summary.Active);
        Assert.Equal(2, summary.Overdue);
        Assert.Equal(1, summary.Upcoming);
        Assert.Equal(0, summary.DueSoon);
        Assert.Equal(1, summary.Suspended);
        Assert.Equal(1, summary.Terminated);

        // Active must always equal the sum of its schedule sub-buckets.
        Assert.Equal(summary.Active, summary.Upcoming + summary.DueSoon + summary.Overdue);
    }

    [Fact]
    public async Task GetRecurringObligationsDetailsAsync_maps_last_completion_and_generated_count()
    {
        var options = CreateOptions(nameof(GetRecurringObligationsDetailsAsync_maps_last_completion_and_generated_count));
        await SeedAsync(options);
        var service = CreateService(options);

        var result = await service.GetRecurringObligationsDetailsAsync(new RecurringObligationsReportPagedFilterRequest { PageSize = 50 });
        var row = result.Items.Single(r => r.TemplateId == 1);

        Assert.Equal(new DateTime(2020, 2, 5, 0, 0, 0, DateTimeKind.Utc), row.LastCompletionDate);
        Assert.Equal(1, row.GeneratedTransactionsCount);
        Assert.Equal(RecurringObligationScheduleStatus.Overdue, row.ScheduleStatus);
        Assert.True(row.DaysRemaining < 0);
        Assert.Equal("الإدارة أ", row.OwningDepartmentName);
    }

    [Theory]
    [InlineData(10, new[] { 1, 2, 4 })]
    [InlineData(20, new[] { 3, 5 })]
    [InlineData(30, new[] { 3 })]
    public async Task GetRecurringObligationsDetailsAsync_department_filter_matches_owning_or_responsible_department(
        int departmentId, int[] expectedTemplateIds)
    {
        var options = CreateOptions($"{nameof(GetRecurringObligationsDetailsAsync_department_filter_matches_owning_or_responsible_department)}-{departmentId}");
        await SeedAsync(options);
        var service = CreateService(options);

        var result = await service.GetRecurringObligationsDetailsAsync(new RecurringObligationsReportPagedFilterRequest
        {
            DepartmentId = departmentId,
            PageSize = 50,
        });

        Assert.Equal(expectedTemplateIds.OrderBy(x => x), result.Items.Select(r => r.TemplateId).OrderBy(x => x));
    }

    [Fact]
    public async Task GetRecurringObligationsDetailsAsync_recurrence_type_filter_returns_only_matching_templates()
    {
        var options = CreateOptions(nameof(GetRecurringObligationsDetailsAsync_recurrence_type_filter_returns_only_matching_templates));
        await SeedAsync(options);
        var service = CreateService(options);

        var result = await service.GetRecurringObligationsDetailsAsync(new RecurringObligationsReportPagedFilterRequest
        {
            RecurrenceType = "Quarterly",
            PageSize = 50,
        });

        Assert.Equal(new[] { 2 }, result.Items.Select(r => r.TemplateId));
    }

    [Fact]
    public async Task GetRecurringObligationsDetailsAsync_priority_filter_returns_only_matching_templates()
    {
        var options = CreateOptions(nameof(GetRecurringObligationsDetailsAsync_priority_filter_returns_only_matching_templates));
        await SeedAsync(options);
        var service = CreateService(options);

        var result = await service.GetRecurringObligationsDetailsAsync(new RecurringObligationsReportPagedFilterRequest
        {
            Priority = "Urgent",
            PageSize = 50,
        });

        Assert.Equal(new[] { 2 }, result.Items.Select(r => r.TemplateId));
    }

    [Fact]
    public async Task GetRecurringObligationsDetailsAsync_date_range_filter_matches_only_the_upcoming_template()
    {
        var options = CreateOptions(nameof(GetRecurringObligationsDetailsAsync_date_range_filter_matches_only_the_upcoming_template));
        await SeedAsync(options);
        var service = CreateService(options);

        var anchor = CurrentMonthAnchor();
        var expectedT3DueDate = anchor.AddMonths(2);

        var result = await service.GetRecurringObligationsDetailsAsync(new RecurringObligationsReportPagedFilterRequest
        {
            DateFrom = expectedT3DueDate,
            DateTo = expectedT3DueDate,
            PageSize = 50,
        });

        Assert.Equal(new[] { 3 }, result.Items.Select(r => r.TemplateId));
    }

    [Fact]
    public async Task GetRecurringObligationsSummaryAsync_groups_by_status()
    {
        var options = CreateOptions(nameof(GetRecurringObligationsSummaryAsync_groups_by_status));
        await SeedAsync(options);
        var service = CreateService(options);

        var summary = await service.GetRecurringObligationsSummaryAsync(new RecurringObligationsReportFilterRequest { GroupBy = "status" });

        Assert.Equal(3, summary.Groups.Count);
        Assert.Equal(3, summary.Groups.Single(g => g.GroupKey == "Active").Count);
        Assert.Equal(1, summary.Groups.Single(g => g.GroupKey == "Paused").Count);
        Assert.Equal(1, summary.Groups.Single(g => g.GroupKey == "Terminated").Count);
        Assert.Equal(summary.Total, summary.Groups.Sum(g => g.Count));
    }

    [Fact]
    public async Task GetRecurringObligationsSummaryAsync_groups_by_department()
    {
        var options = CreateOptions(nameof(GetRecurringObligationsSummaryAsync_groups_by_department));
        await SeedAsync(options);
        var service = CreateService(options);

        var summary = await service.GetRecurringObligationsSummaryAsync(new RecurringObligationsReportFilterRequest { GroupBy = "department" });

        Assert.Equal(3, summary.Groups.Single(g => g.GroupKey == "الإدارة أ").Count);
        Assert.Equal(2, summary.Groups.Single(g => g.GroupKey == "الإدارة ب").Count);
        Assert.Equal(summary.Total, summary.Groups.Sum(g => g.Count));
    }

    [Fact]
    public async Task ExportRecurringObligationsExcelAsync_produces_a_row_per_template_with_expected_headers()
    {
        var options = CreateOptions(nameof(ExportRecurringObligationsExcelAsync_produces_a_row_per_template_with_expected_headers));
        await SeedAsync(options);
        var service = CreateService(options);

        var bytes = await service.ExportRecurringObligationsExcelAsync();

        using var workbook = new XLWorkbook(new MemoryStream(bytes));
        var ws = workbook.Worksheets.Single();

        Assert.Equal("المعرف", ws.Cell(1, 1).GetString());
        Assert.Equal("العنوان", ws.Cell(1, 2).GetString());
        Assert.Equal("تاريخ الاستحقاق القادم", ws.Cell(1, 7).GetString());

        var lastRow = ws.LastRowUsed();
        Assert.NotNull(lastRow);
        var lastUsedRow = lastRow.RowNumber();
        Assert.Equal(6, lastUsedRow); // header + 5 templates
    }

    [Fact]
    public async Task MapToRecurringObligationRow_preserves_month_end_clamped_due_date()
    {
        var options = CreateOptions(nameof(MapToRecurringObligationRow_preserves_month_end_clamped_due_date));
        using (var db = new AppDbContext(options))
        {
            db.Users.Add(new User { Id = 1, Username = "admin", PasswordHash = "h", FullName = "Admin", Role = UserRole.Admin, IsActive = true });
            db.Departments.Add(new Department { Id = 10, Name = "الإدارة أ", NameNormalized = "الإدارة أ", IsActive = true });
            db.Categories.Add(new Category { Id = 1, Name = "تقارير دورية", NameNormalized = "تقارير دورية", IsActive = true });

            // Anchored on Jan 31, 2028 (a leap year): the same month-end clamping case
            // proven directly against RecurringPeriodCalculator in RecurringPeriodCalculatorTests.
            // With LastGeneratedPeriodKey = "2028-01", the next period is "2028-02", whose
            // due date is the anchor plus two months: Jan 31 + 2 months = Mar 31 (exact,
            // since March has 31 days) even though the intermediate month (Feb) does not.
            db.RecurringTransactionTemplates.Add(new RecurringTransactionTemplate
            {
                Id = 1,
                Title = "التزام نهاية الشهر",
                SubjectTemplate = "تقرير شهري",
                RecurrenceType = RecurrenceType.Monthly,
                Status = RecurringTemplateStatus.Active,
                StartDate = new DateTime(2028, 1, 31, 0, 0, 0, DateTimeKind.Utc),
                LastGeneratedPeriodKey = "2028-01",
                IncomingSourceType = IncomingSourceType.Internal,
                IncomingFromDepartmentId = 10,
                CategoryId = 1,
                Priority = Priority.Normal,
                ResponseType = ResponseType.None,
                DefaultRequiredAction = "تحديث",
                CreatedById = 1,
            });
            await db.SaveChangesAsync();
        }

        var service = CreateService(options);
        var result = await service.GetRecurringObligationsDetailsAsync(new RecurringObligationsReportPagedFilterRequest { PageSize = 10 });
        var row = result.Items.Single();

        Assert.Equal("2028-02", row.NextPeriodKey);
        Assert.Equal(new DateTime(2028, 3, 31, 0, 0, 0, DateTimeKind.Utc), row.NextDueDate);
    }
}
