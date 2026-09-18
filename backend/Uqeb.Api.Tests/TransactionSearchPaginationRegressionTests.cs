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

// Regression coverage for issue #142: TransactionSearchPagination was refactored to reduce cognitive
// complexity (a shared ORDER BY helper, and `??` chains instead of nested ternaries for the computed
// IncomingFrom/Category sort keys). These tests pin the observable API behavior - ordering, cursor
// semantics, tie-breaking, filter/search scoping, page-size handling and validation - so the refactor
// cannot silently change results.
public class TransactionSearchPaginationRegressionTests
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
        public int UserId => 1;
        public string Username => "admin";
        public UserRole Role => UserRole.Admin;
        public int? DepartmentId => null;
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
        await db.SaveChangesAsync();

        var service = new TransactionService(
            db,
            new AuditService(db),
            new StubTrackingNumberService(),
            new TestCacheInvalidation(),
            new RecurringTransactionTemplateService(db, new AuditService(db), new StubTrackingNumberService()));

        return (service, db);
    }

    private static Transaction BuildTransaction(int id, DateTime incomingDate) => new()
    {
        Id = id,
        InternalTrackingNumber = $"UQEB-2026-{id:00000}",
        IncomingNumber = $"IN-{id:000}",
        IncomingDate = incomingDate,
        Subject = $"Subject {id:000}",
        IncomingSourceType = IncomingSourceType.External,
        IncomingFrom = "جهة",
        RequiresResponse = false,
        ResponseType = ResponseType.None,
        Priority = Priority.Normal,
        Status = TransactionStatus.New,
        CreatedById = 1,
        CreatedAt = incomingDate
    };

    private static async Task SeedTransactionsAsync(AppDbContext db, int count, DateTime? baseDate = null)
    {
        var start = baseDate ?? new DateTime(2026, 1, 1);
        for (var i = 1; i <= count; i++)
            db.Transactions.Add(BuildTransaction(i, start.AddDays(i)));

        await db.SaveChangesAsync();
    }

    private static async Task<List<int>> WalkCursorAsync(
        TransactionService service,
        TestCurrentUser user,
        Func<string?, TransactionSearchRequest> requestFactory,
        int maxPages = 50)
    {
        var ids = new List<int>();
        string? cursor = null;
        for (var i = 0; i < maxPages; i++)
        {
            var page = await service.SearchAsync(requestFactory(cursor), user);
            Assert.Equal(TransactionSearchPagination.CursorMode, page.PaginationMode);
            ids.AddRange(page.Items.Select(x => x.Id));
            cursor = page.NextCursor;
            if (string.IsNullOrEmpty(cursor))
            {
                Assert.False(page.HasNextPage);
                return ids;
            }

            Assert.True(page.HasNextPage);
        }

        throw new InvalidOperationException("Cursor walk did not terminate within maxPages.");
    }

    public static IEnumerable<object[]> SupportedSortModes()
    {
        foreach (var sortBy in new[]
                 {
                     "IncomingNumber", "IncomingDate", "Subject", "IncomingFrom",
                     "Category", "Priority", "Status", "ResponseDueDate", "CreatedAt"
                 })
        foreach (var sortDesc in new[] { true, false })
            yield return new object[] { sortBy, sortDesc };
    }

    [Theory]
    [MemberData(nameof(SupportedSortModes))]
    public async Task SearchAsync_cursor_mode_reproduces_offset_order_for_every_supported_sort_field(
        string sortBy, bool sortDesc)
    {
        var (service, db) = await CreateServiceAsync(
            $"{nameof(SearchAsync_cursor_mode_reproduces_offset_order_for_every_supported_sort_field)}_{sortBy}_{sortDesc}");
        var user = new TestCurrentUser();

        for (var i = 1; i <= 17; i++)
        {
            // Mirrors the invariant TransactionService.ApplyIncomingSourceAsync enforces on real data:
            // IncomingFromPartyId is only ever set for External rows, IncomingFromDepartmentId only for
            // Internal rows, and never both - so ApplySort's "party, then department" resolution and
            // ExtractPrimaryValue's source-type-based resolution always agree in practice.
            var isExternal = i % 2 == 0;
            db.Transactions.Add(new Transaction
            {
                Id = i,
                InternalTrackingNumber = $"UQEB-2026-{i:00000}",
                IncomingNumber = $"IN-{17 - i:000}",
                IncomingDate = new DateTime(2026, 1, 1).AddDays(i % 5),
                Subject = $"Subject {(i * 7) % 11:000}",
                IncomingSourceType = isExternal ? IncomingSourceType.External : IncomingSourceType.Internal,
                IncomingFromPartyId = isExternal && i % 3 == 0 ? 1 : null,
                IncomingFromDepartmentId = !isExternal && i % 3 == 1 ? 1 : null,
                IncomingFrom = "نص خام",
                CategoryId = i % 4 == 0 ? 1 : null,
                Category = i % 4 == 1 ? "فئة نصية" : null,
                Priority = (Priority)(i % 3),
                Status = (TransactionStatus)(i % 4 + 1),
                ResponseDueDate = i % 5 == 0 ? null : new DateTime(2026, 2, 1).AddDays(i % 7),
                RequiresResponse = false,
                ResponseType = ResponseType.None,
                CreatedById = 1,
                CreatedAt = new DateTime(2026, 1, 1).AddHours(i)
            });
        }

        db.ExternalParties.Add(new ExternalParty { Id = 1, Name = "جهة خارجية", NameNormalized = "جهة خارجية", IsActive = true });
        db.Departments.Add(new Department { Id = 1, Name = "إدارة", NameNormalized = "إدارة", IsActive = true });
        db.Categories.Add(new Category { Id = 1, Name = "فئة مرتبطة", NameNormalized = "فئة مرتبطة", IsActive = true });
        await db.SaveChangesAsync();

        var offsetIds = new List<int>();
        for (var page = 1; page <= 5; page++)
        {
            var offsetPage = await service.SearchAsync(new TransactionSearchRequest
            {
                StatusScope = "all",
                Page = page,
                PageSize = 4,
                SortBy = sortBy,
                SortDesc = sortDesc
            }, user);
            offsetIds.AddRange(offsetPage.Items.Select(x => x.Id));
        }

        var cursorIds = await WalkCursorAsync(service, user, cursor => new TransactionSearchRequest
        {
            StatusScope = "all",
            PaginationMode = TransactionSearchPagination.CursorMode,
            PageSize = 4,
            SortBy = sortBy,
            SortDesc = sortDesc,
            Cursor = cursor
        });

        Assert.Equal(offsetIds, cursorIds);
        Assert.Equal(cursorIds.Count, cursorIds.Distinct().Count());
        Assert.Equal(17, cursorIds.Count);
    }

    [Fact]
    public async Task SearchAsync_cursor_mode_handles_many_rows_sharing_the_same_primary_sort_value()
    {
        var (service, db) = await CreateServiceAsync(
            nameof(SearchAsync_cursor_mode_handles_many_rows_sharing_the_same_primary_sort_value));
        var user = new TestCurrentUser();
        var sameDate = new DateTime(2026, 3, 1);

        for (var i = 1; i <= 12; i++)
            db.Transactions.Add(BuildTransaction(i, sameDate));
        await db.SaveChangesAsync();

        var cursorIds = await WalkCursorAsync(service, user, cursor => new TransactionSearchRequest
        {
            PaginationMode = TransactionSearchPagination.CursorMode,
            PageSize = 5,
            SortBy = "IncomingDate",
            SortDesc = true,
            Cursor = cursor
        });

        Assert.Equal(12, cursorIds.Count);
        Assert.Equal(cursorIds.Count, cursorIds.Distinct().Count());
        Assert.Equal(Enumerable.Range(1, 12).Reverse(), cursorIds);
    }

    [Fact]
    public async Task SearchAsync_cursor_mode_does_not_duplicate_or_drop_the_boundary_item()
    {
        var (service, db) = await CreateServiceAsync(
            nameof(SearchAsync_cursor_mode_does_not_duplicate_or_drop_the_boundary_item));
        var user = new TestCurrentUser();
        await SeedTransactionsAsync(db, 10);

        var firstPage = await service.SearchAsync(new TransactionSearchRequest
        {
            PaginationMode = TransactionSearchPagination.CursorMode,
            PageSize = 5,
            SortBy = "IncomingDate",
            SortDesc = false
        }, user);

        Assert.Equal(5, firstPage.Items.Count);
        var boundaryId = firstPage.Items[^1].Id;

        var secondPage = await service.SearchAsync(new TransactionSearchRequest
        {
            PaginationMode = TransactionSearchPagination.CursorMode,
            PageSize = 5,
            SortBy = "IncomingDate",
            SortDesc = false,
            Cursor = firstPage.NextCursor
        }, user);

        Assert.DoesNotContain(secondPage.Items, x => x.Id == boundaryId);
        Assert.Equal(Enumerable.Range(1, 5), firstPage.Items.Select(x => x.Id));
        Assert.Equal(Enumerable.Range(6, 5), secondPage.Items.Select(x => x.Id));
        Assert.Null(secondPage.NextCursor);
        Assert.False(secondPage.HasNextPage);
    }

    [Fact]
    public async Task SearchAsync_cursor_mode_keeps_status_filter_scope_across_pages()
    {
        var (service, db) = await CreateServiceAsync(nameof(SearchAsync_cursor_mode_keeps_status_filter_scope_across_pages));
        var user = new TestCurrentUser();

        for (var i = 1; i <= 20; i++)
        {
            var t = BuildTransaction(i, new DateTime(2026, 1, 1).AddDays(i));
            t.Status = i % 2 == 0 ? TransactionStatus.InProgress : TransactionStatus.New;
            db.Transactions.Add(t);
        }
        await db.SaveChangesAsync();

        var expectedIds = Enumerable.Range(1, 20).Where(i => i % 2 == 0).ToList();

        var cursorIds = await WalkCursorAsync(service, user, cursor => new TransactionSearchRequest
        {
            StatusScope = "all",
            Status = "InProgress",
            PaginationMode = TransactionSearchPagination.CursorMode,
            PageSize = 3,
            SortBy = "IncomingDate",
            SortDesc = false,
            Cursor = cursor
        });

        Assert.Equal(expectedIds, cursorIds);
    }

    [Fact]
    public async Task SearchAsync_cursor_mode_keeps_search_text_scope_across_pages()
    {
        var (service, db) = await CreateServiceAsync(nameof(SearchAsync_cursor_mode_keeps_search_text_scope_across_pages));
        var user = new TestCurrentUser();

        for (var i = 1; i <= 20; i++)
        {
            var t = BuildTransaction(i, new DateTime(2026, 1, 1).AddDays(i));
            t.Subject = i % 2 == 0 ? $"MARKER-{i:000}" : $"Other {i:000}";
            db.Transactions.Add(t);
        }
        await db.SaveChangesAsync();

        var expectedIds = Enumerable.Range(1, 20).Where(i => i % 2 == 0).ToList();

        var cursorIds = await WalkCursorAsync(service, user, cursor => new TransactionSearchRequest
        {
            SearchText = "MARKER",
            PaginationMode = TransactionSearchPagination.CursorMode,
            PageSize = 3,
            SortBy = "IncomingDate",
            SortDesc = false,
            Cursor = cursor
        });

        Assert.Equal(expectedIds, cursorIds);
    }

    [Fact]
    public async Task SearchAsync_cursor_mode_invalid_cursor_throws_validation_error_not_a_500()
    {
        var (service, db) = await CreateServiceAsync(nameof(SearchAsync_cursor_mode_invalid_cursor_throws_validation_error_not_a_500));
        var user = new TestCurrentUser();
        await SeedTransactionsAsync(db, 3);

        await Assert.ThrowsAsync<InvalidTransactionSearchCursorException>(() => service.SearchAsync(
            new TransactionSearchRequest
            {
                PaginationMode = TransactionSearchPagination.CursorMode,
                PageSize = 2,
                SortBy = "IncomingDate",
                SortDesc = false,
                Cursor = "not-a-valid-cursor"
            },
            user));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-100)]
    public async Task SearchAsync_cursor_mode_non_positive_page_size_falls_back_to_default(int pageSize)
    {
        var (service, db) = await CreateServiceAsync(
            $"{nameof(SearchAsync_cursor_mode_non_positive_page_size_falls_back_to_default)}_{pageSize}");
        var user = new TestCurrentUser();
        await SeedTransactionsAsync(db, 25);

        var page = await service.SearchAsync(new TransactionSearchRequest
        {
            PaginationMode = TransactionSearchPagination.CursorMode,
            PageSize = pageSize,
            SortBy = "IncomingDate",
            SortDesc = false
        }, user);

        Assert.Equal(20, page.PageSize);
        Assert.Equal(20, page.Items.Count);
        Assert.True(page.HasNextPage);
    }

    [Fact]
    public async Task SearchAsync_cursor_mode_page_size_larger_than_dataset_returns_everything_without_a_cap()
    {
        var (service, db) = await CreateServiceAsync(
            nameof(SearchAsync_cursor_mode_page_size_larger_than_dataset_returns_everything_without_a_cap));
        var user = new TestCurrentUser();
        await SeedTransactionsAsync(db, 5);

        var page = await service.SearchAsync(new TransactionSearchRequest
        {
            PaginationMode = TransactionSearchPagination.CursorMode,
            PageSize = 1000,
            SortBy = "IncomingDate",
            SortDesc = false
        }, user);

        Assert.Equal(1000, page.PageSize);
        Assert.Equal(5, page.Items.Count);
        Assert.False(page.HasNextPage);
        Assert.Null(page.NextCursor);
    }

    [Fact]
    public async Task SearchAsync_cursor_mode_empty_result_returns_no_items_and_no_cursor()
    {
        var (service, db) = await CreateServiceAsync(nameof(SearchAsync_cursor_mode_empty_result_returns_no_items_and_no_cursor));
        var user = new TestCurrentUser();
        await SeedTransactionsAsync(db, 5);

        var page = await service.SearchAsync(new TransactionSearchRequest
        {
            PaginationMode = TransactionSearchPagination.CursorMode,
            PageSize = 10,
            SortBy = "IncomingDate",
            SortDesc = false,
            IncomingNumber = "NO-SUCH-NUMBER"
        }, user);

        Assert.Empty(page.Items);
        Assert.Null(page.NextCursor);
        Assert.False(page.HasNextPage);
    }

    [Fact]
    public async Task SearchAsync_incomingfrom_sort_and_keyset_use_party_then_department_then_raw_text_fallback()
    {
        var (service, db) = await CreateServiceAsync(
            nameof(SearchAsync_incomingfrom_sort_and_keyset_use_party_then_department_then_raw_text_fallback));
        var user = new TestCurrentUser();

        db.ExternalParties.Add(new ExternalParty { Id = 1, Name = "Z-Party", NameNormalized = "z-party", IsActive = true });
        db.Departments.Add(new Department { Id = 1, Name = "M-Dept", NameNormalized = "m-dept", IsActive = true });

        // IncomingSourceType is kept consistent with which link is set (External+Party,
        // Internal+Department), matching the invariant TransactionService.ApplyIncomingSourceAsync
        // enforces on real data.
        var withParty = BuildTransaction(1, new DateTime(2026, 1, 1));
        withParty.IncomingFromPartyId = 1;
        withParty.IncomingFrom = null;

        var withDepartment = BuildTransaction(2, new DateTime(2026, 1, 2));
        withDepartment.IncomingSourceType = IncomingSourceType.Internal;
        withDepartment.IncomingFromDepartmentId = 1;
        withDepartment.IncomingFrom = null;

        var withRawTextOnly = BuildTransaction(3, new DateTime(2026, 1, 3));
        withRawTextOnly.IncomingFrom = "A-Raw";

        var withNothing = BuildTransaction(4, new DateTime(2026, 1, 4));
        withNothing.IncomingFrom = null;

        db.Transactions.AddRange(withParty, withDepartment, withRawTextOnly, withNothing);
        await db.SaveChangesAsync();

        var offsetPage = await service.SearchAsync(new TransactionSearchRequest
        {
            SortBy = "IncomingFrom",
            SortDesc = false,
            PageSize = 10
        }, user);

        // "" (withNothing) < "A-Raw" (withRawTextOnly) < "M-Dept" (withDepartment) < "Z-Party" (withParty)
        Assert.Equal(new[] { 4, 3, 2, 1 }, offsetPage.Items.Select(x => x.Id));

        var cursorIds = await WalkCursorAsync(service, user, cursor => new TransactionSearchRequest
        {
            PaginationMode = TransactionSearchPagination.CursorMode,
            PageSize = 1,
            SortBy = "IncomingFrom",
            SortDesc = false,
            Cursor = cursor
        });

        Assert.Equal(new[] { 4, 3, 2, 1 }, cursorIds);
    }

    [Fact]
    public async Task SearchAsync_category_sort_and_keyset_uses_linked_category_then_free_text_fallback()
    {
        var (service, db) = await CreateServiceAsync(
            nameof(SearchAsync_category_sort_and_keyset_uses_linked_category_then_free_text_fallback));
        var user = new TestCurrentUser();

        db.Categories.Add(new Category { Id = 1, Name = "Z-Category", NameNormalized = "z-category", IsActive = true });

        var withLinkedCategory = BuildTransaction(1, new DateTime(2026, 1, 1));
        withLinkedCategory.CategoryId = 1;
        withLinkedCategory.Category = null;

        var withFreeTextOnly = BuildTransaction(2, new DateTime(2026, 1, 2));
        withFreeTextOnly.Category = "A-FreeText";

        var withNoCategory = BuildTransaction(3, new DateTime(2026, 1, 3));
        withNoCategory.Category = null;

        db.Transactions.AddRange(withLinkedCategory, withFreeTextOnly, withNoCategory);
        await db.SaveChangesAsync();

        var offsetPage = await service.SearchAsync(new TransactionSearchRequest
        {
            SortBy = "Category",
            SortDesc = false,
            PageSize = 10
        }, user);

        Assert.Equal(new[] { 3, 2, 1 }, offsetPage.Items.Select(x => x.Id));

        var cursorIds = await WalkCursorAsync(service, user, cursor => new TransactionSearchRequest
        {
            PaginationMode = TransactionSearchPagination.CursorMode,
            PageSize = 1,
            SortBy = "Category",
            SortDesc = false,
            Cursor = cursor
        });

        Assert.Equal(new[] { 3, 2, 1 }, cursorIds);
    }
}
