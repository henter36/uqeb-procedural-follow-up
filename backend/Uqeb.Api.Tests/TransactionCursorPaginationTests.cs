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

public class TransactionCursorPaginationTests
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
            new TestCacheInvalidation());

        return (service, db);
    }

    private static async Task SeedTransactionsAsync(AppDbContext db, int count)
    {
        for (var i = 1; i <= count; i++)
        {
            db.Transactions.Add(new Transaction
            {
                Id = i,
                InternalTrackingNumber = $"UQEB-2026-{i:00000}",
                IncomingNumber = $"IN-{i:000}",
                IncomingDate = new DateTime(2026, 1, 1).AddDays(i),
                Subject = $"Subject {i:000}",
                IncomingSourceType = IncomingSourceType.External,
                IncomingFrom = "جهة",
                RequiresResponse = false,
                ResponseType = ResponseType.None,
                Priority = Priority.Normal,
                Status = TransactionStatus.New,
                CreatedById = 1,
                CreatedAt = new DateTime(2026, 1, 1).AddDays(i)
            });
        }

        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task SearchAsync_offset_mode_preserves_existing_paging_metadata()
    {
        var (service, db) = await CreateServiceAsync(nameof(SearchAsync_offset_mode_preserves_existing_paging_metadata));
        await SeedTransactionsAsync(db, 25);

        var result = await service.SearchAsync(new TransactionSearchRequest
        {
            Page = 2,
            PageSize = 10,
            SortBy = "IncomingDate",
            SortDesc = false
        }, new TestCurrentUser());

        Assert.Equal("offset", result.PaginationMode);
        Assert.True(result.TotalCountIncluded);
        Assert.Equal(25, result.TotalCount);
        Assert.Equal(2, result.Page);
        Assert.Equal(10, result.PageSize);
        Assert.Equal(3, result.TotalPages);
        Assert.True(result.HasNextPage);
        Assert.True(result.HasPreviousPage);
        Assert.Null(result.NextCursor);
        Assert.Equal(10, result.Items.Count);
        Assert.Equal("IN-011", result.Items[0].IncomingNumber);
    }

    [Fact]
    public async Task SearchAsync_cursor_mode_walks_all_items_without_duplicates_or_gaps()
    {
        var (service, db) = await CreateServiceAsync(nameof(SearchAsync_cursor_mode_walks_all_items_without_duplicates_or_gaps));
        await SeedTransactionsAsync(db, 23);

        var offsetIds = new List<int>();
        for (var page = 1; page <= 3; page++)
        {
            var offsetPage = await service.SearchAsync(new TransactionSearchRequest
            {
                Page = page,
                PageSize = 10,
                SortBy = "IncomingDate",
                SortDesc = false
            }, new TestCurrentUser());
            offsetIds.AddRange(offsetPage.Items.Select(i => i.Id));
        }

        var cursorIds = new List<int>();
        string? cursor = null;
        for (var i = 0; i < 10; i++)
        {
            var cursorPage = await service.SearchAsync(new TransactionSearchRequest
            {
                PaginationMode = "cursor",
                PageSize = 10,
                SortBy = "IncomingDate",
                SortDesc = false,
                Cursor = cursor
            }, new TestCurrentUser());

            Assert.Equal("cursor", cursorPage.PaginationMode);
            cursorIds.AddRange(cursorPage.Items.Select(item => item.Id));
            cursor = cursorPage.NextCursor;
            if (string.IsNullOrEmpty(cursor))
                break;
        }

        Assert.Equal(offsetIds, cursorIds);
        Assert.Equal(Enumerable.Range(1, 23), cursorIds);
    }

    [Fact]
    public async Task SearchAsync_cursor_mode_includeTotalCount_is_optional()
    {
        var (service, db) = await CreateServiceAsync(nameof(SearchAsync_cursor_mode_includeTotalCount_is_optional));
        await SeedTransactionsAsync(db, 5);

        var withoutTotal = await service.SearchAsync(new TransactionSearchRequest
        {
            PaginationMode = "cursor",
            PageSize = 10,
            SortBy = "IncomingDate",
            SortDesc = false,
            IncludeTotalCount = false
        }, new TestCurrentUser());

        Assert.False(withoutTotal.TotalCountIncluded);
        Assert.Equal(0, withoutTotal.TotalCount);
        Assert.Equal(0, withoutTotal.TotalPages);

        var withTotal = await service.SearchAsync(new TransactionSearchRequest
        {
            PaginationMode = "cursor",
            PageSize = 10,
            SortBy = "IncomingDate",
            SortDesc = false,
            IncludeTotalCount = true
        }, new TestCurrentUser());

        Assert.True(withTotal.TotalCountIncluded);
        Assert.Equal(5, withTotal.TotalCount);
        Assert.Equal(1, withTotal.TotalPages);
    }

    [Fact]
    public void CursorCodec_invalid_cursor_throws()
    {
        Assert.Throws<InvalidTransactionSearchCursorException>(() => TransactionSearchCursorCodec.Decode("not-a-cursor"));
        Assert.Throws<InvalidTransactionSearchCursorException>(() => TransactionSearchCursorCodec.Decode("v2.dGVzdA"));
    }

    [Fact]
    public async Task SearchAsync_cursor_mode_rejects_sort_mismatch()
    {
        var (service, db) = await CreateServiceAsync(nameof(SearchAsync_cursor_mode_rejects_sort_mismatch));
        await SeedTransactionsAsync(db, 3);

        var firstPage = await service.SearchAsync(new TransactionSearchRequest
        {
            PaginationMode = "cursor",
            PageSize = 2,
            SortBy = "IncomingDate",
            SortDesc = false
        }, new TestCurrentUser());

        Assert.NotNull(firstPage.NextCursor);

        await Assert.ThrowsAsync<InvalidTransactionSearchCursorException>(() => service.SearchAsync(
            new TransactionSearchRequest
            {
                PaginationMode = "cursor",
                PageSize = 2,
                SortBy = "Subject",
                SortDesc = false,
                Cursor = firstPage.NextCursor
            },
            new TestCurrentUser()));
    }

    [Fact]
    public void CursorCodec_roundtrip_preserves_payload()
    {
        var payload = new TransactionSearchCursorPayload
        {
            SortBy = "incomingdate",
            SortDesc = true,
            Primary = "2026-01-02T00:00:00.0000000",
            Id = 42
        };

        var encoded = TransactionSearchCursorCodec.Encode(payload);
        var decoded = TransactionSearchCursorCodec.Decode(encoded);

        Assert.StartsWith("v1.", encoded);
        Assert.Equal(payload.SortBy, decoded.SortBy);
        Assert.Equal(payload.SortDesc, decoded.SortDesc);
        Assert.Equal(payload.Primary, decoded.Primary);
        Assert.Equal(payload.Id, decoded.Id);
    }
}
