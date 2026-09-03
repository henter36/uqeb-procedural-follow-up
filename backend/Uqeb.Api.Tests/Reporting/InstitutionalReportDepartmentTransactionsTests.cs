using ClosedXML.Excel;
using DocumentFormat.OpenXml.Packaging;
using OpenXmlText = DocumentFormat.OpenXml.Wordprocessing.Text;
using Microsoft.EntityFrameworkCore;
using Uqeb.Api.Data;
using Uqeb.Api.Helpers;
using Uqeb.Api.Models.Entities;
using Uqeb.Api.Models.Enums;
using Uqeb.Api.Reporting.DTOs;
using Uqeb.Api.Reporting.Enums;
using Uqeb.Api.Reporting.Exporters;
using Uqeb.Api.Reporting.Services;
using Xunit;

namespace Uqeb.Api.Tests.Reporting;

/// <summary>
/// Seeded scenario:
/// - Department 10 (A), Department 20 (B), Department 30 (C).
/// - T1: Assignment for B only (Active). -> B: "إحالة" only.
/// - T2: OutgoingDepartment to B only, no assignment at all. -> B: "صادر لها" only.
/// - T3: Assignment for B AND OutgoingDepartment to B. -> B: "إحالة وصادر لها".
/// - T4: Assignment for C only - excluded when only department 20 (B) is selected.
/// - T5: Assignment for BOTH B and C - the only transaction shared across departments, used for
///   multi-department/grouping tests. Also matches department 20 (B) on its own via "إحالة".
/// Each transaction has a distinct Transaction.ResponseDueDate for deterministic DueDate-sort tests.
/// </summary>
public class InstitutionalReportDepartmentTransactionsTests
{
    private static DbContextOptions<AppDbContext> CreateOptions(string dbName) =>
        new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(dbName).Options;

    private static async Task SeedAsync(DbContextOptions<AppDbContext> options)
    {
        using var db = new AppDbContext(options);
        var now = DateTime.UtcNow;

        db.Users.Add(new User { Id = 1, Username = "admin", PasswordHash = "h", FullName = "Admin", Role = UserRole.Admin, IsActive = true });
        db.Departments.Add(new Department { Id = 10, Name = "الإدارة أ", NameNormalized = "الإدارة أ", IsActive = true });
        db.Departments.Add(new Department { Id = 20, Name = "الإدارة ب", NameNormalized = "الإدارة ب", IsActive = true });
        db.Departments.Add(new Department { Id = 30, Name = "الإدارة ج", NameNormalized = "الإدارة ج", IsActive = true });
        db.Categories.Add(new Category { Id = 1, Name = "معاملات", NameNormalized = "معاملات", IsActive = true });
        await db.SaveChangesAsync();

        Transaction NewTransaction(int id, DateTime incomingDate, DateTime responseDueDate) => new()
        {
            Id = id,
            InternalTrackingNumber = $"UQEB-2026-{id:00000}",
            IncomingNumber = $"IN-{id:0000}",
            IncomingDate = incomingDate,
            Subject = $"معاملة {id}",
            IncomingSourceType = IncomingSourceType.External,
            IncomingFrom = "جهة خارجية",
            CategoryId = 1,
            Priority = Priority.Normal,
            ResponseType = ResponseType.None,
            Status = TransactionStatus.InProgress,
            RequiresResponse = true,
            ResponseDueDate = responseDueDate,
            CreatedById = 1,
            CreatedAt = incomingDate,
        };

        // DueDate order (ascending): T2 (Jan 10) < T3 (Jan 15) < T1 (Jan 20) < T5 (Jan 30).
        var t1 = NewTransaction(1, new DateTime(2026, 1, 5, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 1, 20));
        var t2 = NewTransaction(2, new DateTime(2026, 1, 6, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 1, 10));
        var t3 = NewTransaction(3, new DateTime(2026, 1, 7, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 1, 15));
        var t4 = NewTransaction(4, new DateTime(2026, 1, 8, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 1, 25));
        var t5 = NewTransaction(5, new DateTime(2026, 1, 9, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 1, 30));
        db.Transactions.AddRange(t1, t2, t3, t4, t5);
        await db.SaveChangesAsync();

        // T1: assignment for B only -> "إحالة"
        db.Assignments.Add(new Assignment
        {
            TransactionId = 1, DepartmentId = 20, Status = AssignmentStatus.Active, RequiresReply = true,
            ReplyStatus = ReplyStatus.Pending, AssignedDate = now.AddDays(-5), DueDate = now.AddDays(5), CreatedById = 1,
        });

        // T2: outgoing to B only -> "صادر لها"
        db.TransactionOutgoingDepartments.Add(new TransactionOutgoingDepartment { TransactionId = 2, DepartmentId = 20, CreatedById = 1 });

        // T3: assignment AND outgoing for B -> "إحالة وصادر لها"
        db.Assignments.Add(new Assignment
        {
            TransactionId = 3, DepartmentId = 20, Status = AssignmentStatus.Active, RequiresReply = true,
            ReplyStatus = ReplyStatus.Pending, AssignedDate = now.AddDays(-2), DueDate = now.AddDays(2), CreatedById = 1,
        });
        db.TransactionOutgoingDepartments.Add(new TransactionOutgoingDepartment { TransactionId = 3, DepartmentId = 20, CreatedById = 1 });

        // T4: assignment for C only - excluded when only B is selected.
        db.Assignments.Add(new Assignment
        {
            TransactionId = 4, DepartmentId = 30, Status = AssignmentStatus.Active, RequiresReply = true,
            ReplyStatus = ReplyStatus.Pending, AssignedDate = now.AddDays(-1), DueDate = now.AddDays(1), CreatedById = 1,
        });

        // T5: assignment for both B and C - used for multi-department grouping tests.
        db.Assignments.Add(new Assignment
        {
            TransactionId = 5, DepartmentId = 20, Status = AssignmentStatus.Active, RequiresReply = true,
            ReplyStatus = ReplyStatus.Pending, AssignedDate = now.AddDays(-3), DueDate = now.AddDays(3), CreatedById = 1,
        });
        db.Assignments.Add(new Assignment
        {
            TransactionId = 5, DepartmentId = 30, Status = AssignmentStatus.Active, RequiresReply = true,
            ReplyStatus = ReplyStatus.Pending, AssignedDate = now.AddDays(-3), DueDate = now.AddDays(3), CreatedById = 1,
        });

        await db.SaveChangesAsync();
    }

    private static ReportBuildRequestDto DepartmentTransactionsRequest(List<int> departmentIds, DateTime from, DateTime to) => new()
    {
        ReportType = InstitutionalReportType.DepartmentTransactions,
        SectionIds = [ReportSectionId.TransactionDetails],
        Filters = new ReportFiltersDto
        {
            DateFrom = from,
            DateTo = to,
            DepartmentIds = departmentIds,
        },
    };

    private static async Task ConfigureSharedTransactionWithIndependentDepartmentStatesAsync(
        DbContextOptions<AppDbContext> options)
    {
        await using var db = new AppDbContext(options);
        var departmentB = await db.Assignments.SingleAsync(a => a.TransactionId == 5 && a.DepartmentId == 20);
        departmentB.Status = AssignmentStatus.Completed;
        departmentB.ReplyStatus = ReplyStatus.Replied;
        departmentB.DueDate = new DateTime(2026, 1, 20);
        departmentB.ReplyDate = new DateTime(2026, 1, 15);

        var departmentC = await db.Assignments.SingleAsync(a => a.TransactionId == 5 && a.DepartmentId == 30);
        departmentC.Status = AssignmentStatus.Active;
        departmentC.ReplyStatus = ReplyStatus.Pending;
        departmentC.DueDate = new DateTime(2026, 1, 10);
        departmentC.ReplyDate = null;

        var transaction = await db.Transactions.SingleAsync(t => t.Id == 5);
        transaction.Status = TransactionStatus.InProgress;
        transaction.ResponseCompleted = false;
        transaction.ResponseCompletedDate = null;
        transaction.ClosedAt = null;
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task BuildReportModelAsync_DepartmentTransactionsWithoutDepartments_ThrowsValidation()
    {
        var options = CreateOptions($"deptx-{Guid.NewGuid():N}");
        await SeedAsync(options);
        var service = InstitutionalReportServiceTestHelpers.CreateService(new TestDbContextFactory(options));

        var request = DepartmentTransactionsRequest([], new DateTime(2026, 1, 1), new DateTime(2026, 1, 31));

        var ex = await Assert.ThrowsAsync<FieldValidationException>(() => service.BuildReportModelAsync(request));
        Assert.Contains("filters.departmentIds", ex.FieldErrors.Keys);
    }

    [Fact]
    public async Task BuildReportModelAsync_UndefinedReportType_ThrowsValidation()
    {
        var service = InstitutionalReportServiceTestHelpers.CreateService();
        var request = new ReportBuildRequestDto
        {
            ReportType = (InstitutionalReportType)999,
            SectionIds = [ReportSectionId.Cover],
        };

        var ex = await Assert.ThrowsAsync<FieldValidationException>(() => service.BuildReportModelAsync(request));
        Assert.Contains("reportType", ex.FieldErrors.Keys);
    }

    [Theory]
    [InlineData("NotAPriority")]
    [InlineData("999")] // numeric but undefined - Enum.TryParse alone would accept this silently
    public async Task BuildReportModelAsync_InvalidPriorityValue_ThrowsValidation(string invalidValue)
    {
        var service = InstitutionalReportServiceTestHelpers.CreateService();
        var request = new ReportBuildRequestDto
        {
            ReportType = InstitutionalReportType.ExecutiveComprehensive,
            SectionIds = [ReportSectionId.Cover],
            Filters = new ReportFiltersDto { Priorities = [invalidValue] },
        };

        var ex = await Assert.ThrowsAsync<FieldValidationException>(() => service.BuildReportModelAsync(request));
        Assert.Contains("filters.priorities", ex.FieldErrors.Keys);
    }

    [Theory]
    [InlineData("NotAStatus")]
    [InlineData("999")] // numeric but undefined - Enum.TryParse alone would accept this silently
    public async Task BuildReportModelAsync_InvalidStatusValue_ThrowsValidation(string invalidValue)
    {
        var service = InstitutionalReportServiceTestHelpers.CreateService();
        var request = new ReportBuildRequestDto
        {
            ReportType = InstitutionalReportType.ExecutiveComprehensive,
            SectionIds = [ReportSectionId.Cover],
            Filters = new ReportFiltersDto { Statuses = [invalidValue] },
        };

        var ex = await Assert.ThrowsAsync<FieldValidationException>(() => service.BuildReportModelAsync(request));
        Assert.Contains("filters.statuses", ex.FieldErrors.Keys);
    }

    [Fact]
    public async Task BuildReportModelAsync_UndefinedDetailSortBy_ThrowsValidation()
    {
        var service = InstitutionalReportServiceTestHelpers.CreateService();
        var request = new ReportBuildRequestDto
        {
            ReportType = InstitutionalReportType.ExecutiveComprehensive,
            SectionIds = [ReportSectionId.Cover],
            DetailSortBy = (ReportDetailSortBy)999,
        };

        var ex = await Assert.ThrowsAsync<FieldValidationException>(() => service.BuildReportModelAsync(request));
        Assert.Contains("detailSortBy", ex.FieldErrors.Keys);
    }

    [Fact]
    public async Task SaveTemplateAsync_UndefinedDetailSortBy_ThrowsValidation()
    {
        var service = InstitutionalReportServiceTestHelpers.CreateService();
        var request = new SaveReportTemplateRequestDto
        {
            Name = "قالب تجريبي",
            ReportType = InstitutionalReportType.ExecutiveComprehensive,
            SectionIds = [ReportSectionId.Cover],
            DefaultFilters = new ReportFiltersDto(),
            DetailSortBy = (ReportDetailSortBy)999,
        };

        var ex = await Assert.ThrowsAsync<FieldValidationException>(() => service.SaveTemplateAsync(request));
        Assert.Contains("detailSortBy", ex.FieldErrors.Keys);
    }

    [Fact]
    public async Task BuildReportModelAsync_DepartmentTransactionsWithNullFilters_ThrowsValidationNotNullReferenceException()
    {
        var service = InstitutionalReportServiceTestHelpers.CreateService();
        var request = new ReportBuildRequestDto
        {
            ReportType = InstitutionalReportType.DepartmentTransactions,
            SectionIds = [ReportSectionId.Cover],
            Filters = null!,
        };

        var ex = await Assert.ThrowsAsync<FieldValidationException>(() => service.BuildReportModelAsync(request));
        // The generic "filters is required" check fires first for a fully-null Filters object -
        // proving no NullReferenceException leaks out for DepartmentTransactions either.
        Assert.Contains("filters", ex.FieldErrors.Keys);
    }

    [Fact]
    public async Task BuildReportModelAsync_DepartmentTransactionsWithNullDepartmentIds_ThrowsFieldValidationException()
    {
        var service = InstitutionalReportServiceTestHelpers.CreateService();
        var request = new ReportBuildRequestDto
        {
            ReportType = InstitutionalReportType.DepartmentTransactions,
            SectionIds = [ReportSectionId.Cover],
            Filters = new ReportFiltersDto { DepartmentIds = null! },
        };

        var ex = await Assert.ThrowsAsync<FieldValidationException>(() => service.BuildReportModelAsync(request));
        Assert.Contains("filters.departmentIds", ex.FieldErrors.Keys);
        Assert.Equal("يجب تحديد إدارة واحدة على الأقل لتقرير معاملات إدارة.", ex.FieldErrors["filters.departmentIds"]);
    }

    [Fact]
    public void ResolveDepartmentNameById_MatchingIndexWithinBounds_ReturnsName()
    {
        var name = InstitutionalReportService.ResolveDepartmentNameById(
            departmentId: 20, ids: [10, 20, 30], names: ["A", "B", "C"], fallback: "—");

        Assert.Equal("B", name);
    }

    [Fact]
    public void ResolveDepartmentNameById_NamesListShorterThanIdsList_ReturnsFallbackInsteadOfThrowing()
    {
        // ids has 3 entries but names only has 1 - the id at index 2 (departmentId 30) has no
        // corresponding name. Must return the fallback, not throw ArgumentOutOfRangeException.
        var name = InstitutionalReportService.ResolveDepartmentNameById(
            departmentId: 30, ids: [10, 20, 30], names: ["A"], fallback: "—");

        Assert.Equal("—", name);
    }

    [Fact]
    public void ResolveDepartmentNameById_DepartmentIdNotInList_ReturnsFallback()
    {
        var name = InstitutionalReportService.ResolveDepartmentNameById(
            departmentId: 99, ids: [10, 20, 30], names: ["A", "B", "C"], fallback: "—");

        Assert.Equal("—", name);
    }

    [Fact]
    public void ResolveDepartmentNameById_EmptyIdsList_ReturnsFallback()
    {
        var name = InstitutionalReportService.ResolveDepartmentNameById(
            departmentId: 20, ids: [], names: [], fallback: "—");

        Assert.Equal("—", name);
    }

    [Fact]
    public async Task BuildReportModelAsync_ComputesMatchedDepartmentsAndRelationLabels()
    {
        var options = CreateOptions($"deptx-{Guid.NewGuid():N}");
        await SeedAsync(options);
        var service = InstitutionalReportServiceTestHelpers.CreateService(new TestDbContextFactory(options));

        var request = DepartmentTransactionsRequest([20], new DateTime(2026, 1, 1), new DateTime(2026, 1, 31));
        var model = await service.BuildReportModelAsync(request);

        // T1, T2, T3, T5 all touch department 20 (B) (T5 via assignment, alongside its department-30
        // assignment); T4 touches only C and is excluded when only B is selected.
        Assert.Equal(4, model.Transactions.Count);

        var row1 = model.Transactions.Single(r => r.TransactionId == 1);
        Assert.Equal("إحالة", Assert.Single(row1.MatchedDepartments).Relation);

        var row2 = model.Transactions.Single(r => r.TransactionId == 2);
        Assert.Equal("صادر لها", Assert.Single(row2.MatchedDepartments).Relation);

        var row3 = model.Transactions.Single(r => r.TransactionId == 3);
        Assert.Equal("إحالة وصادر لها", Assert.Single(row3.MatchedDepartments).Relation);

        var row5 = model.Transactions.Single(r => r.TransactionId == 5);
        Assert.Equal("إحالة", Assert.Single(row5.MatchedDepartments).Relation);
    }

    [Fact]
    public async Task BuildReportModelAsync_ExcludedDepartments_RemovesTransactionsFromDetailsAndTotals()
    {
        var options = CreateOptions($"deptx-{Guid.NewGuid():N}");
        await SeedAsync(options);
        var service = InstitutionalReportServiceTestHelpers.CreateService(new TestDbContextFactory(options));

        var request = DepartmentTransactionsRequest([20], new DateTime(2026, 1, 1), new DateTime(2026, 1, 31));
        request.Filters.ExcludedDepartmentIds = [30];

        var model = await service.BuildReportModelAsync(request);

        Assert.Equal(3, model.TotalMatchedRows);
        Assert.Equal(3, model.Transactions.Count);
        Assert.DoesNotContain(model.Transactions, row => row.TransactionId == 5);
        Assert.DoesNotContain(model.Transactions.SelectMany(row => row.MatchedDepartments), dept => dept.DepartmentId == 30);
    }

    [Fact]
    public async Task BuildReportModelAsync_ExcludedDepartmentWithActiveAssignment_RemovesTransaction()
    {
        var options = CreateOptions($"deptx-{Guid.NewGuid():N}");
        await SeedAsync(options);
        var service = InstitutionalReportServiceTestHelpers.CreateService(new TestDbContextFactory(options));

        var request = new ReportBuildRequestDto
        {
            ReportType = InstitutionalReportType.ExecutiveComprehensive,
            SectionIds = [ReportSectionId.TransactionDetails],
            Filters = new ReportFiltersDto
            {
                DateFrom = new DateTime(2026, 1, 1),
                DateTo = new DateTime(2026, 1, 31),
                ExcludedDepartmentIds = [30],
            },
        };

        var model = await service.BuildReportModelAsync(request);

        Assert.DoesNotContain(model.Transactions, row => row.TransactionId == 4);
    }

    [Fact]
    public async Task BuildReportModelAsync_ExcludedDepartmentWithCompletedAssignment_RemovesTransaction()
    {
        var options = CreateOptions($"deptx-{Guid.NewGuid():N}");
        await SeedAsync(options);
        await using (var db = new AppDbContext(options))
        {
            var assignment = await db.Assignments.SingleAsync(a => a.TransactionId == 4 && a.DepartmentId == 30);
            assignment.Status = AssignmentStatus.Completed;
            await db.SaveChangesAsync();
        }
        var service = InstitutionalReportServiceTestHelpers.CreateService(new TestDbContextFactory(options));

        var request = new ReportBuildRequestDto
        {
            ReportType = InstitutionalReportType.ExecutiveComprehensive,
            SectionIds = [ReportSectionId.TransactionDetails],
            Filters = new ReportFiltersDto
            {
                DateFrom = new DateTime(2026, 1, 1),
                DateTo = new DateTime(2026, 1, 31),
                ExcludedDepartmentIds = [30],
            },
        };

        var model = await service.BuildReportModelAsync(request);

        Assert.DoesNotContain(model.Transactions, row => row.TransactionId == 4);
    }

    [Fact]
    public async Task BuildReportModelAsync_ExcludedDepartmentWithCancelledAssignmentOnly_DoesNotRemoveTransaction()
    {
        var options = CreateOptions($"deptx-{Guid.NewGuid():N}");
        await SeedAsync(options);
        await using (var db = new AppDbContext(options))
        {
            var assignment = await db.Assignments.SingleAsync(a => a.TransactionId == 4 && a.DepartmentId == 30);
            assignment.Status = AssignmentStatus.Cancelled;
            await db.SaveChangesAsync();
        }
        var service = InstitutionalReportServiceTestHelpers.CreateService(new TestDbContextFactory(options));

        var request = new ReportBuildRequestDto
        {
            ReportType = InstitutionalReportType.ExecutiveComprehensive,
            SectionIds = [ReportSectionId.TransactionDetails],
            Filters = new ReportFiltersDto
            {
                DateFrom = new DateTime(2026, 1, 1),
                DateTo = new DateTime(2026, 1, 31),
                ExcludedDepartmentIds = [30],
            },
        };

        var model = await service.BuildReportModelAsync(request);

        Assert.Contains(model.Transactions, row => row.TransactionId == 4);
    }

    [Fact]
    public async Task BuildReportModelAsync_ActiveIncludedAssignmentWithCancelledExcludedAssignment_KeepsTransaction()
    {
        var options = CreateOptions($"deptx-{Guid.NewGuid():N}");
        await SeedAsync(options);
        await using (var db = new AppDbContext(options))
        {
            var excludedAssignment = await db.Assignments.SingleAsync(a => a.TransactionId == 5 && a.DepartmentId == 30);
            excludedAssignment.Status = AssignmentStatus.Cancelled;
            await db.SaveChangesAsync();
        }
        var service = InstitutionalReportServiceTestHelpers.CreateService(new TestDbContextFactory(options));

        var request = DepartmentTransactionsRequest([20], new DateTime(2026, 1, 1), new DateTime(2026, 1, 31));
        request.Filters.ExcludedDepartmentIds = [30];

        var model = await service.BuildReportModelAsync(request);

        // Multi-department exclusion follows report attribution: active/completed assignments exclude;
        // cancelled historical assignments do not.
        Assert.Contains(model.Transactions, row => row.TransactionId == 5);
    }

    [Fact]
    public async Task BuildReportModelAsync_SharedIncludedAndEligibleExcludedAssignment_RemovesTransaction()
    {
        var options = CreateOptions($"deptx-{Guid.NewGuid():N}");
        await SeedAsync(options);
        var service = InstitutionalReportServiceTestHelpers.CreateService(new TestDbContextFactory(options));

        var request = DepartmentTransactionsRequest([20], new DateTime(2026, 1, 1), new DateTime(2026, 1, 31));
        request.Filters.ExcludedDepartmentIds = [30];

        var model = await service.BuildReportModelAsync(request);

        Assert.DoesNotContain(model.Transactions, row => row.TransactionId == 5);
    }

    [Fact]
    public async Task BuildReportModelAsync_ExcludedOutgoingDepartment_RemovesTransaction()
    {
        var options = CreateOptions($"deptx-{Guid.NewGuid():N}");
        await SeedAsync(options);
        await using (var db = new AppDbContext(options))
        {
            db.TransactionOutgoingDepartments.Add(new TransactionOutgoingDepartment
            {
                TransactionId = 2,
                DepartmentId = 30,
                CreatedById = 1,
            });
            await db.SaveChangesAsync();
        }
        var service = InstitutionalReportServiceTestHelpers.CreateService(new TestDbContextFactory(options));

        var request = DepartmentTransactionsRequest([20], new DateTime(2026, 1, 1), new DateTime(2026, 1, 31));
        request.Filters.ExcludedDepartmentIds = [30];

        var model = await service.BuildReportModelAsync(request);

        Assert.DoesNotContain(model.Transactions, row => row.TransactionId == 2);
    }

    [Fact]
    public async Task ExportAsync_DepartmentTransactionsXlsx_MatchesPreviewCountAfterExclusions()
    {
        var options = CreateOptions($"deptx-{Guid.NewGuid():N}");
        await SeedAsync(options);
        var service = InstitutionalReportServiceTestHelpers.CreateService(new TestDbContextFactory(options));

        var request = DepartmentTransactionsRequest([20], new DateTime(2026, 1, 1), new DateTime(2026, 1, 31));
        request.Filters.ExcludedDepartmentIds = [30];
        var preview = await service.BuildReportModelAsync(request);

        var export = await service.ExportAsync(new ReportExportRequestDto
        {
            ExportFormat = ExportFormat.Xlsx,
            ExportMode = ExportMode.FullReport,
            BuildRequest = request,
        });

        using var workbook = new XLWorkbook(new MemoryStream(export.Content));
        var ws = workbook.Worksheet("المعاملات التفصيلية");
        var exportedRows = ws.LastRowUsed()!.RowNumber() - 1;
        Assert.Equal(preview.TotalMatchedRows, exportedRows);
    }

    [Fact]
    public async Task BuildReportModelAsync_DepartmentTransactionsOpenOnly_UsesOpenScopeAfterPeriodScope()
    {
        var options = CreateOptions($"deptx-{Guid.NewGuid():N}");
        await SeedAsync(options);
        await using (var db = new AppDbContext(options))
        {
            var closed = await db.Transactions.SingleAsync(t => t.Id == 3);
            closed.Status = TransactionStatus.Closed;
            closed.ClosedAt = new DateTime(2026, 1, 20, 0, 0, 0, DateTimeKind.Utc);
            await db.SaveChangesAsync();
        }
        var service = InstitutionalReportServiceTestHelpers.CreateService(new TestDbContextFactory(options));

        var request = DepartmentTransactionsRequest([20], new DateTime(2026, 1, 8), new DateTime(2026, 1, 31));
        request.Filters.DepartmentTransactionScope = DepartmentTransactionScope.OpenOnly;

        var model = await service.BuildReportModelAsync(request);

        // T2 is OutgoingDepartment-only: without assignment evidence it is intentionally not
        // classified as open merely because the transaction as a whole is open.
        Assert.Equal([1, 5], model.Transactions.Select(row => row.TransactionId).OrderBy(id => id).ToArray());
        Assert.Equal(2, model.TotalMatchedRows);
    }

    [Fact]
    public async Task BuildReportModelAsync_SharedTransaction_OpenOnlyUsesOnlySelectedDepartmentState()
    {
        var options = CreateOptions($"deptx-{Guid.NewGuid():N}");
        await SeedAsync(options);
        await ConfigureSharedTransactionWithIndependentDepartmentStatesAsync(options);
        var service = InstitutionalReportServiceTestHelpers.CreateService(new TestDbContextFactory(options));

        var completedDepartmentRequest = DepartmentTransactionsRequest([20], new DateTime(2026, 1, 1), new DateTime(2026, 1, 31));
        completedDepartmentRequest.Filters.DepartmentTransactionScope = DepartmentTransactionScope.OpenOnly;
        var completedDepartmentModel = await service.BuildReportModelAsync(completedDepartmentRequest);

        var pendingDepartmentRequest = DepartmentTransactionsRequest([30], new DateTime(2026, 1, 1), new DateTime(2026, 1, 31));
        pendingDepartmentRequest.Filters.DepartmentTransactionScope = DepartmentTransactionScope.OpenOnly;
        var pendingDepartmentModel = await service.BuildReportModelAsync(pendingDepartmentRequest);

        var bothDepartmentsRequest = DepartmentTransactionsRequest([20, 30], new DateTime(2026, 1, 1), new DateTime(2026, 1, 31));
        bothDepartmentsRequest.Filters.DepartmentTransactionScope = DepartmentTransactionScope.OpenOnly;
        var bothDepartmentsModel = await service.BuildReportModelAsync(bothDepartmentsRequest);

        Assert.DoesNotContain(completedDepartmentModel.Transactions, row => row.TransactionId == 5);
        Assert.Contains(pendingDepartmentModel.Transactions, row => row.TransactionId == 5);
        Assert.Contains(bothDepartmentsModel.Transactions, row => row.TransactionId == 5);
    }

    [Fact]
    public async Task BuildReportModelAsync_SharedTransaction_OverdueFilterUsesOnlySelectedDepartmentState()
    {
        var options = CreateOptions($"deptx-{Guid.NewGuid():N}");
        await SeedAsync(options);
        await ConfigureSharedTransactionWithIndependentDepartmentStatesAsync(options);
        var service = InstitutionalReportServiceTestHelpers.CreateService(new TestDbContextFactory(options));

        var completedDepartmentRequest = DepartmentTransactionsRequest([20], new DateTime(2026, 1, 1), new DateTime(2026, 1, 31));
        completedDepartmentRequest.Filters.IncludeOverdue = true;
        var completedDepartmentModel = await service.BuildReportModelAsync(completedDepartmentRequest);

        var pendingDepartmentRequest = DepartmentTransactionsRequest([30], new DateTime(2026, 1, 1), new DateTime(2026, 1, 31));
        pendingDepartmentRequest.Filters.IncludeOverdue = true;
        var pendingDepartmentModel = await service.BuildReportModelAsync(pendingDepartmentRequest);

        Assert.DoesNotContain(completedDepartmentModel.Transactions, row => row.TransactionId == 5);
        Assert.Contains(pendingDepartmentModel.Transactions, row => row.TransactionId == 5);
    }

    [Fact]
    public async Task BuildReportModelAsync_SharedTransaction_AllShowsDepartmentStateAndKeepsOverallTransactionOpen()
    {
        var options = CreateOptions($"deptx-{Guid.NewGuid():N}");
        await SeedAsync(options);
        await ConfigureSharedTransactionWithIndependentDepartmentStatesAsync(options);
        var service = InstitutionalReportServiceTestHelpers.CreateService(new TestDbContextFactory(options));

        var completedModel = await service.BuildReportModelAsync(
            DepartmentTransactionsRequest([20], new DateTime(2026, 1, 1), new DateTime(2026, 1, 31)));
        var pendingModel = await service.BuildReportModelAsync(
            DepartmentTransactionsRequest([30], new DateTime(2026, 1, 1), new DateTime(2026, 1, 31)));

        var completedRow = completedModel.Transactions.Single(row => row.TransactionId == 5);
        Assert.Equal("منجزة ضمن المهلة", completedRow.DepartmentStatus);
        Assert.Equal("2026-01-20", completedRow.DepartmentDueDate);
        Assert.Equal("2026-01-15", completedRow.DepartmentCompletionDate);
        Assert.Equal("مكتمل", completedRow.DepartmentResponseState);
        Assert.Equal("مفتوحة", completedRow.Status);
        var completedMatch = Assert.Single(completedRow.MatchedDepartments);
        Assert.True(completedMatch.IsCompletedForDepartment);
        Assert.True(completedMatch.IsOnTimeForDepartment);
        Assert.False(completedMatch.IsOpenForDepartment);

        var pendingRow = pendingModel.Transactions.Single(row => row.TransactionId == 5);
        Assert.Equal("مفتوحة متأخرة", pendingRow.DepartmentStatus);
        Assert.Equal("2026-01-10", pendingRow.DepartmentDueDate);
        Assert.Null(pendingRow.DepartmentCompletionDate);
        Assert.Equal("بانتظار", pendingRow.DepartmentResponseState);
        Assert.Equal("مفتوحة", pendingRow.Status);
        var pendingMatch = Assert.Single(pendingRow.MatchedDepartments);
        Assert.True(pendingMatch.IsOpenForDepartment);
        Assert.True(pendingMatch.IsOverdueForDepartment);
        Assert.False(pendingMatch.IsCompletedForDepartment);

        Assert.Contains(completedModel.Summary.KpiCards, card => card.Key == "open" && card.Value == "4");
    }

    [Fact]
    public async Task BuildReportModelAsync_DepartmentStatusAndDueDateSortUseSelectedDepartmentStates()
    {
        var options = CreateOptions($"deptx-{Guid.NewGuid():N}");
        await SeedAsync(options);
        await ConfigureSharedTransactionWithIndependentDepartmentStatesAsync(options);
        await using (var db = new AppDbContext(options))
        {
            var t1 = await db.Assignments.SingleAsync(a => a.TransactionId == 1 && a.DepartmentId == 20);
            t1.DueDate = new DateTime(2026, 1, 25); // open overdue
            var t3 = await db.Assignments.SingleAsync(a => a.TransactionId == 3 && a.DepartmentId == 20);
            t3.DueDate = new DateTime(2026, 2, 5); // open, not overdue
            await db.SaveChangesAsync();
        }
        var service = InstitutionalReportServiceTestHelpers.CreateService(new TestDbContextFactory(options));

        var statusRequest = DepartmentTransactionsRequest([20], new DateTime(2026, 1, 1), new DateTime(2026, 1, 31));
        statusRequest.DetailSortBy = ReportDetailSortBy.Status;
        var statusModel = await service.BuildReportModelAsync(statusRequest);

        var dueRequest = DepartmentTransactionsRequest([20], new DateTime(2026, 1, 1), new DateTime(2026, 1, 31));
        dueRequest.DetailSortBy = ReportDetailSortBy.DueDate;
        var dueModel = await service.BuildReportModelAsync(dueRequest);

        Assert.Equal([1, 3, 5, 2], statusModel.Transactions.Select(row => row.TransactionId).ToArray());
        Assert.Equal([5, 1, 3, 2], dueModel.Transactions.Select(row => row.TransactionId).ToArray());
    }

    [Fact]
    public async Task BuildReportModelAsync_GroupedRowsCarryTheirExactDepartmentState()
    {
        var options = CreateOptions($"deptx-{Guid.NewGuid():N}");
        await SeedAsync(options);
        await ConfigureSharedTransactionWithIndependentDepartmentStatesAsync(options);
        var service = InstitutionalReportServiceTestHelpers.CreateService(new TestDbContextFactory(options));
        var request = DepartmentTransactionsRequest([20, 30], new DateTime(2026, 1, 1), new DateTime(2026, 1, 31));
        request.GroupDetailsByDepartment = true;

        var model = await service.BuildReportModelAsync(request);

        var sharedRows = model.Transactions
            .Where(row => row.TransactionId == 5)
            .ToDictionary(row => row.DepartmentGroupDepartmentId!.Value);
        Assert.Equal("منجزة ضمن المهلة", sharedRows[20].DepartmentStatus);
        Assert.Equal("2026-01-20", sharedRows[20].DepartmentDueDate);
        Assert.Equal("مفتوحة متأخرة", sharedRows[30].DepartmentStatus);
        Assert.Equal("2026-01-10", sharedRows[30].DepartmentDueDate);
    }

    [Fact]
    public async Task BuildReportModelAsync_OutgoingOnlyRelationIsExplicitlyUnmeasurable()
    {
        var options = CreateOptions($"deptx-{Guid.NewGuid():N}");
        await SeedAsync(options);
        var service = InstitutionalReportServiceTestHelpers.CreateService(new TestDbContextFactory(options));

        var model = await service.BuildReportModelAsync(
            DepartmentTransactionsRequest([20], new DateTime(2026, 1, 1), new DateTime(2026, 1, 31)));

        var row = model.Transactions.Single(item => item.TransactionId == 2);
        var match = Assert.Single(row.MatchedDepartments);
        Assert.Equal("صادر لها", match.Relation);
        Assert.False(match.HasPerformanceState);
        Assert.Null(match.IsOpenForDepartment);
        Assert.Null(match.IsCompletedForDepartment);
        Assert.Null(row.DepartmentDueDate);
        Assert.Contains("غير قابلة للقياس", row.DepartmentStatus);
    }

    [Fact]
    public async Task DepartmentTransactionExportsUseTheSameComputedDepartmentState()
    {
        var options = CreateOptions($"deptx-{Guid.NewGuid():N}");
        await SeedAsync(options);
        await ConfigureSharedTransactionWithIndependentDepartmentStatesAsync(options);
        var service = InstitutionalReportServiceTestHelpers.CreateService(new TestDbContextFactory(options));
        var request = DepartmentTransactionsRequest([20], new DateTime(2026, 1, 1), new DateTime(2026, 1, 31));
        var model = await service.BuildReportModelAsync(request);
        model.Transactions = model.Transactions.Where(row => row.TransactionId == 5).ToList();

        var renderer = new InstitutionalReportRenderer();
        var manifest = renderer.RenderManifest(model, [ReportSectionId.TransactionDetails]);
        var html = InstitutionalReportRenderer.RenderHtmlDocument(manifest);
        // PDF uses this exact rendered HTML as its source; no exporter recomputes department state.
        Assert.Contains("منجزة ضمن المهلة", html);
        Assert.Contains("2026-01-20", html);
        Assert.Contains("2026-01-15", html);

        var xlsx = InstitutionalReportXlsxExporter.Export(model, manifest, new ReportExportRequestDto());
        using (var workbook = new XLWorkbook(new MemoryStream(xlsx)))
        {
            var sheet = workbook.Worksheet("المعاملات التفصيلية");
            Assert.Equal("منجزة ضمن المهلة", sheet.Cell(2, 12).GetString());
            Assert.Equal("2026-01-20", sheet.Cell(2, 15).GetString());
            Assert.Equal("2026-01-15", sheet.Cell(2, 16).GetString());
        }

        var docx = InstitutionalReportDocxExporter.Export(model, manifest, new ReportExportRequestDto());
        using var stream = new MemoryStream(docx);
        using var document = WordprocessingDocument.Open(stream, false);
        var text = string.Concat(document.MainDocumentPart!.Document.Body!.Descendants<OpenXmlText>().Select(node => node.Text));
        Assert.Contains("منجزة ضمن المهلة", text);
        Assert.Contains("2026-01-20", text);
        Assert.Contains("2026-01-15", text);
    }

    [Fact]
    public async Task BuildReportModelAsync_DepartmentTransactionsSelectedDepartmentCannotBeExcluded()
    {
        var service = InstitutionalReportServiceTestHelpers.CreateService();
        var request = DepartmentTransactionsRequest([20], new DateTime(2026, 1, 1), new DateTime(2026, 1, 31));
        request.Filters.ExcludedDepartmentIds = [20];

        var ex = await Assert.ThrowsAsync<FieldValidationException>(() => service.BuildReportModelAsync(request));

        Assert.Contains("filters.excludedDepartmentIds", ex.FieldErrors.Keys);
    }

    [Fact]
    public async Task BuildReportModelAsync_MultipleDepartmentsWithoutGrouping_ShowsOneRowPerTransaction()
    {
        var options = CreateOptions($"deptx-{Guid.NewGuid():N}");
        await SeedAsync(options);
        var service = InstitutionalReportServiceTestHelpers.CreateService(new TestDbContextFactory(options));

        var request = DepartmentTransactionsRequest([20, 30], new DateTime(2026, 1, 1), new DateTime(2026, 1, 31));
        request.GroupDetailsByDepartment = false;
        var model = await service.BuildReportModelAsync(request);

        // T1..T5 all touch either B or C (or both, T5) - one row each, never duplicated.
        Assert.Equal(5, model.Transactions.Count);
        Assert.Equal(5, model.Transactions.Select(r => r.TransactionId).Distinct().Count());
        Assert.False(model.GroupDetailsByDepartmentEffective);
        Assert.True(model.DetailRowsAreAdditive);

        var row5 = model.Transactions.Single(r => r.TransactionId == 5);
        Assert.Equal(2, row5.MatchedDepartments.Count);
    }

    [Fact]
    public async Task BuildReportModelAsync_GroupByDepartment_DuplicatesSharedTransactionButKeepsTotalMatchedRowsDistinct()
    {
        var options = CreateOptions($"deptx-{Guid.NewGuid():N}");
        await SeedAsync(options);
        var service = InstitutionalReportServiceTestHelpers.CreateService(new TestDbContextFactory(options));

        var request = DepartmentTransactionsRequest([20, 30], new DateTime(2026, 1, 1), new DateTime(2026, 1, 31));
        request.GroupDetailsByDepartment = true;
        var model = await service.BuildReportModelAsync(request);

        // T5 touches both B and C -> duplicated once per department -> 6 rows total (T1,T2,T3,T4 once + T5 twice).
        Assert.Equal(6, model.Transactions.Count);
        Assert.True(model.GroupDetailsByDepartmentEffective);
        Assert.False(model.DetailRowsAreAdditive);

        // But the distinct-transaction counts must never inflate due to the duplication.
        Assert.Equal(5, model.TotalMatchedRows);
        Assert.Equal(5, model.Metadata.IncludedTransactionCount);

        var t5Rows = model.Transactions.Where(r => r.TransactionId == 5).ToList();
        Assert.Equal(2, t5Rows.Count);
        var t5GroupNames = t5Rows.Select(r =>
        {
            Assert.NotNull(r.DepartmentGroupDepartmentName);
            return r.DepartmentGroupDepartmentName;
        }).OrderBy(n => n).ToArray();
        Assert.Equal(["الإدارة ب", "الإدارة ج"], t5GroupNames);
    }

    [Fact]
    public async Task BuildReportModelAsync_GroupByDepartment_MakesDetailSortByEffectiveReflectFinalDepartmentOrder()
    {
        var options = CreateOptions($"deptx-{Guid.NewGuid():N}");
        await SeedAsync(options);
        var service = InstitutionalReportServiceTestHelpers.CreateService(new TestDbContextFactory(options));

        var request = DepartmentTransactionsRequest([20, 30], new DateTime(2026, 1, 1), new DateTime(2026, 1, 31));
        request.DetailSortBy = ReportDetailSortBy.DueDate;
        request.GroupDetailsByDepartment = true;
        var model = await service.BuildReportModelAsync(request);

        // Grouping re-sorts rows by department regardless of the requested DueDate sort, so the
        // reported effective sort must reflect that final order, not the originally requested one.
        Assert.True(model.GroupDetailsByDepartmentEffective);
        Assert.Equal(ReportDetailSortBy.Department, model.DetailSortByEffective);
    }

    [Fact]
    public async Task RenderTransactionDetailsManifest_DepartmentTransactions_PreservesGroupingSortAndComparisonMetadata()
    {
        var options = CreateOptions($"deptx-{Guid.NewGuid():N}");
        await SeedAsync(options);
        var service = InstitutionalReportServiceTestHelpers.CreateService(new TestDbContextFactory(options));

        var request = DepartmentTransactionsRequest([20, 30], new DateTime(2026, 1, 1), new DateTime(2026, 1, 31));
        request.GroupDetailsByDepartment = true;
        var model = await service.BuildReportModelAsync(request);

        var renderer = new InstitutionalReportRenderer();
        var manifest = renderer.RenderTransactionDetailsManifest(model, model.Transactions, "part1");

        // A SplitPdf ZIP part must not silently drop the parent report's grouping/sort/comparison
        // metadata - otherwise the part's own "بيانات التقرير والفلاتر"/methodology section would
        // wrongly claim e.g. "التجميع = لا" while the rows are actually grouped by department.
        var page = Assert.Single(manifest.Pages);
        Assert.Contains("التفاصيل مجمّعة حسب الإدارة", page.HtmlContent);
    }

    [Fact]
    public async Task BuildReportModelAsync_NonDepartmentTransactions_LeavesAuditDepartmentListsEmpty()
    {
        var options = CreateOptions($"deptx-{Guid.NewGuid():N}");
        await SeedAsync(options);
        var service = InstitutionalReportServiceTestHelpers.CreateService(new TestDbContextFactory(options));

        var request = new ReportBuildRequestDto
        {
            ReportType = InstitutionalReportType.ExecutiveComprehensive,
            SectionIds = [ReportSectionId.TransactionDetails],
            Filters = new ReportFiltersDto { DateFrom = new DateTime(2026, 1, 1), DateTo = new DateTime(2026, 1, 31) },
        };
        var model = await service.BuildReportModelAsync(request);

        Assert.NotEmpty(model.Transactions);
        Assert.All(model.Transactions, r =>
        {
            Assert.Empty(r.AllAssignmentDepartments);
            Assert.Empty(r.AllOutgoingDepartments);
        });
    }

    [Fact]
    public async Task BuildReportModelAsync_DepartmentTransactions_StillPopulatesAuditDepartmentLists()
    {
        var options = CreateOptions($"deptx-{Guid.NewGuid():N}");
        await SeedAsync(options);
        var service = InstitutionalReportServiceTestHelpers.CreateService(new TestDbContextFactory(options));

        var request = DepartmentTransactionsRequest([20], new DateTime(2026, 1, 1), new DateTime(2026, 1, 31));
        var model = await service.BuildReportModelAsync(request);

        var row3 = model.Transactions.Single(r => r.TransactionId == 3);
        Assert.NotEmpty(row3.AllAssignmentDepartments);
        Assert.NotEmpty(row3.AllOutgoingDepartments);
    }

    [Fact]
    public async Task BuildReportModelAsync_SingleDepartmentNeverDuplicatesEvenWithGroupingRequested()
    {
        var options = CreateOptions($"deptx-{Guid.NewGuid():N}");
        await SeedAsync(options);
        var service = InstitutionalReportServiceTestHelpers.CreateService(new TestDbContextFactory(options));

        var request = DepartmentTransactionsRequest([20], new DateTime(2026, 1, 1), new DateTime(2026, 1, 31));
        request.GroupDetailsByDepartment = true;
        var model = await service.BuildReportModelAsync(request);

        // Only one department selected -> grouping never activates, regardless of the request flag.
        Assert.Equal(4, model.Transactions.Count);
        Assert.False(model.GroupDetailsByDepartmentEffective);
        Assert.True(model.DetailRowsAreAdditive);
    }

    [Fact]
    public async Task BuildReportModelAsync_DetailSortByDefault_DepartmentTransactions_SortsByDepartmentThenIncomingDateDesc()
    {
        var options = CreateOptions($"deptx-{Guid.NewGuid():N}");
        await SeedAsync(options);
        var service = InstitutionalReportServiceTestHelpers.CreateService(new TestDbContextFactory(options));

        var request = DepartmentTransactionsRequest([20], new DateTime(2026, 1, 1), new DateTime(2026, 1, 31));
        var model = await service.BuildReportModelAsync(request);

        Assert.Equal(ReportDetailSortBy.Department, model.DetailSortByEffective);
        // Only department 20 is selected, so every row's department-sort-key is identical ("الإدارة ب"),
        // leaving IncomingDate desc as the effective tiebreaker: T5(9), T3(7), T2(6), T1(5).
        Assert.Equal([5, 3, 2, 1], model.Transactions.Select(r => r.TransactionId).ToArray());
    }

    [Theory]
    [InlineData(ReportDetailSortBy.IncomingDateDesc, new[] { 5, 3, 2, 1 })]
    [InlineData(ReportDetailSortBy.DueDate, new[] { 3, 5, 1, 2 })]
    public async Task BuildReportModelAsync_ExplicitDetailSortBy_ReordersRows(ReportDetailSortBy sortBy, int[] expectedOrder)
    {
        var options = CreateOptions($"deptx-{Guid.NewGuid():N}");
        await SeedAsync(options);
        var service = InstitutionalReportServiceTestHelpers.CreateService(new TestDbContextFactory(options));

        var request = DepartmentTransactionsRequest([20], new DateTime(2026, 1, 1), new DateTime(2026, 1, 31));
        request.DetailSortBy = sortBy;
        var model = await service.BuildReportModelAsync(request);

        Assert.Equal(sortBy, model.DetailSortByEffective);
        Assert.Equal(expectedOrder, model.Transactions.Select(r => r.TransactionId).ToArray());
    }

    [Fact]
    public async Task BuildReportModelAsync_DepartmentSort_WithMultipleDepartments_GroupsRowsByResolvedDepartmentName()
    {
        var options = CreateOptions($"deptx-{Guid.NewGuid():N}");
        await SeedAsync(options);
        var service = InstitutionalReportServiceTestHelpers.CreateService(new TestDbContextFactory(options));

        // Departments 20 then 30: T1/T2/T3/T5 resolve to "الإدارة ب" (dept 20 checked first for T5,
        // which matches it), T4 resolves to "الإدارة ج" - proving the sort key is genuinely
        // discriminating across departments, not just a coincidental IncomingDate tiebreak.
        var request = DepartmentTransactionsRequest([20, 30], new DateTime(2026, 1, 1), new DateTime(2026, 1, 31));
        request.DetailSortBy = ReportDetailSortBy.Department;
        var model = await service.BuildReportModelAsync(request);

        Assert.Equal([5, 3, 2, 1, 4], model.Transactions.Select(r => r.TransactionId).ToArray());
    }

    [Fact]
    public async Task BuildReportModelAsync_DetailSortByDefault_OtherReportTypes_UnchangedIncomingDateDescOrder()
    {
        var options = CreateOptions($"deptx-{Guid.NewGuid():N}");
        await SeedAsync(options);
        var service = InstitutionalReportServiceTestHelpers.CreateService(new TestDbContextFactory(options));

        var request = new ReportBuildRequestDto
        {
            ReportType = InstitutionalReportType.ExecutiveComprehensive,
            SectionIds = [ReportSectionId.TransactionDetails],
            Filters = new ReportFiltersDto { DateFrom = new DateTime(2026, 1, 1), DateTo = new DateTime(2026, 1, 31) },
        };
        var model = await service.BuildReportModelAsync(request);

        Assert.Equal(ReportDetailSortBy.IncomingDateDesc, model.DetailSortByEffective);
        Assert.Equal([5, 4, 3, 2, 1], model.Transactions.Select(r => r.TransactionId).ToArray());
        Assert.All(model.Transactions, r => Assert.Empty(r.MatchedDepartments));
    }

    [Fact]
    public async Task BuildReportModelAsync_Comparison_DepartmentTransactions_ReusesSameDepartmentIds()
    {
        var options = CreateOptions($"deptx-{Guid.NewGuid():N}");
        await SeedAsync(options);
        var service = InstitutionalReportServiceTestHelpers.CreateService(new TestDbContextFactory(options));

        var request = DepartmentTransactionsRequest([20], new DateTime(2026, 1, 6), new DateTime(2026, 1, 31));
        request.IncludeComparison = true;
        request.ComparisonMode = ReportComparisonMode.PreviousEquivalentPeriod;
        var model = await service.BuildReportModelAsync(request);

        // Comparison period shifts back 26 days (Jan 6-31 = 26 days) to Dec 11-Jan 5, which still
        // only matches department 20's transactions (T1 on Jan 5) - proving DepartmentIds carried over.
        Assert.Null(model.ComparisonUnavailableReason);
        Assert.NotEqual("غير مطبقة", model.Analysis.Methodology.ComparisonPeriod);
    }

    [Fact]
    public async Task BuildReportModelAsync_IncludeComparisonFalse_NeverBuildsComparisonAndNoReason()
    {
        var options = CreateOptions($"deptx-{Guid.NewGuid():N}");
        await SeedAsync(options);
        var service = InstitutionalReportServiceTestHelpers.CreateService(new TestDbContextFactory(options));

        var request = DepartmentTransactionsRequest([20], new DateTime(2026, 1, 1), new DateTime(2026, 1, 31));
        request.IncludeComparison = false;
        var model = await service.BuildReportModelAsync(request);

        Assert.Null(model.ComparisonUnavailableReason);
    }

    [Fact]
    public async Task BuildReportModelAsync_ComparisonRequestedButPeriodIncomplete_SetsUnavailableReason()
    {
        var options = CreateOptions($"deptx-{Guid.NewGuid():N}");
        await SeedAsync(options);
        var service = InstitutionalReportServiceTestHelpers.CreateService(new TestDbContextFactory(options));

        var request = DepartmentTransactionsRequest([20], new DateTime(2026, 1, 1), new DateTime(2026, 1, 31));
        request.Filters.DateTo = null;
        request.IncludeComparison = true;
        request.ComparisonMode = ReportComparisonMode.PreviousEquivalentPeriod;
        var model = await service.BuildReportModelAsync(request);

        Assert.NotNull(model.ComparisonUnavailableReason);
    }

    [Fact]
    public async Task SaveTemplateAsync_DepartmentTransactionsWithoutDepartments_Throws()
    {
        var service = InstitutionalReportServiceTestHelpers.CreateService();
        var request = new SaveReportTemplateRequestDto
        {
            Name = "قالب تجريبي",
            ReportType = InstitutionalReportType.DepartmentTransactions,
            SectionIds = [ReportSectionId.TransactionDetails],
            DefaultFilters = new ReportFiltersDto(),
        };

        var ex = await Assert.ThrowsAsync<FieldValidationException>(() => service.SaveTemplateAsync(request));
        Assert.Contains("defaultFilters.departmentIds", ex.FieldErrors.Keys);
    }

    [Fact]
    public async Task SaveTemplateAsync_DepartmentTransactionsSelectedDepartmentCannotBeExcluded()
    {
        var service = InstitutionalReportServiceTestHelpers.CreateService();
        var request = new SaveReportTemplateRequestDto
        {
            Name = "قالب معاملات إدارة",
            ReportType = InstitutionalReportType.DepartmentTransactions,
            SectionIds = [ReportSectionId.TransactionDetails],
            DefaultFilters = new ReportFiltersDto
            {
                DepartmentIds = [20],
                ExcludedDepartmentIds = [20],
            },
        };

        var ex = await Assert.ThrowsAsync<FieldValidationException>(() => service.SaveTemplateAsync(request));

        Assert.Contains("defaultFilters.excludedDepartmentIds", ex.FieldErrors.Keys);
    }
}
