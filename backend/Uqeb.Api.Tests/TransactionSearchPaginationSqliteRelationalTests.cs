using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Uqeb.Api.Data;
using Uqeb.Api.DTOs.Transactions;
using Uqeb.Api.Helpers;
using Uqeb.Api.Models.Entities;
using Uqeb.Api.Models.Enums;
using Uqeb.Api.Services;
using Xunit;

namespace Uqeb.Api.Tests;

// Relational execution validation for issue #142 / PR #152 review follow-up. Runs the same three
// provider-sensitive scenarios as TransactionSearchPaginationSqlServerIntegrationTests, but against
// SQLite in-memory, which needs no external service and therefore always runs (unlike the SQL Server
// tests, which self-skip without UQEB_TRANSACTION_TEST_CONNECTION). This is deliberately NOT a
// substitute for SQL Server equivalence: SQLite's default collation/NULL-ordering rules are not
// guaranteed identical to SQL Server's. Its purpose is to prove the refactored expressions execute
// correctly through a *real* relational query pipeline (unlike EF Core InMemory, which never reaches
// SQL translation) whenever this repo's test run has no SQL Server available - complementing, not
// replacing, TransactionSearchPaginationSqlServerTranslationTests (translation-only, any environment)
// and TransactionSearchPaginationSqlServerIntegrationTests (full behavioral proof, when SQL Server is
// reachable).
public class TransactionSearchPaginationSqliteRelationalTests
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

    private static TransactionService CreateService(AppDbContext db) =>
        new(
            db,
            new AuditService(db),
            new StubTrackingNumberService(),
            new TestCacheInvalidation(),
            new RecurringTransactionTemplateService(db, new AuditService(db), new StubTrackingNumberService()));

    private static async Task<(SqliteConnection Connection, AppDbContext Db)> CreateDbAsync()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connection)
            .Options;

        var db = new AppDbContext(options);
        await db.Database.EnsureCreatedAsync();

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

        return (connection, db);
    }

    private static Transaction BuildTransaction(int id, DateTime incomingDate) => new()
    {
        Id = id,
        InternalTrackingNumber = $"UQEB-2026-{id:00000}",
        IncomingNumber = $"IN-{id:000}",
        IncomingDate = incomingDate,
        Subject = $"Subject {id:000}",
        IncomingSourceType = IncomingSourceType.External,
        RequiresResponse = false,
        ResponseType = ResponseType.None,
        Priority = Priority.Normal,
        Status = TransactionStatus.New,
        CreatedById = 1,
        CreatedAt = incomingDate
    };

    private static async Task<List<int>> WalkCursorAsync(
        TransactionService service,
        TestCurrentUser user,
        Func<string?, TransactionSearchRequest> requestFactory,
        int maxPages = 20)
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

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SearchAsync_IncomingFrom_CoalescesPartyDepartmentAndRawText_OnSqlite(bool sortDesc)
    {
        var (connection, db) = await CreateDbAsync();
        await using (connection)
        await using (db)
        {
            var party = new ExternalParty { Name = "Z-Party", NameNormalized = ReferenceNameNormalizer.NormalizeKey("Z-Party"), IsActive = true };
            var department = new Department { Name = "M-Dept", NameNormalized = ReferenceNameNormalizer.NormalizeKey("M-Dept"), IsActive = true };
            db.ExternalParties.Add(party);
            db.Departments.Add(department);
            await db.SaveChangesAsync();

            var t1 = BuildTransaction(1, new DateTime(2026, 1, 1));
            t1.IncomingFromPartyId = party.Id;

            var t2 = BuildTransaction(2, new DateTime(2026, 1, 2));
            t2.IncomingFromPartyId = party.Id;

            var t3 = BuildTransaction(3, new DateTime(2026, 1, 3));
            t3.IncomingSourceType = IncomingSourceType.Internal;
            t3.IncomingFromDepartmentId = department.Id;

            var t4 = BuildTransaction(4, new DateTime(2026, 1, 4));
            t4.IncomingFrom = "A-Raw";

            var t5 = BuildTransaction(5, new DateTime(2026, 1, 5));
            t5.IncomingFrom = null;

            db.Transactions.AddRange(t1, t2, t3, t4, t5);
            await db.SaveChangesAsync();

            var service = CreateService(db);
            var user = new TestCurrentUser();

            var cursorIds = await WalkCursorAsync(service, user, cursor => new TransactionSearchRequest
            {
                PaginationMode = TransactionSearchPagination.CursorMode,
                PageSize = 1,
                SortBy = "IncomingFrom",
                SortDesc = sortDesc,
                Cursor = cursor
            });

            var expected = sortDesc ? new[] { 2, 1, 3, 4, 5 } : new[] { 5, 4, 3, 1, 2 };
            Assert.Equal(expected, cursorIds);
            Assert.Equal(cursorIds.Count, cursorIds.Distinct().Count());
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SearchAsync_Category_CoalescesLinkedNameAndFreeText_OnSqlite(bool sortDesc)
    {
        var (connection, db) = await CreateDbAsync();
        await using (connection)
        await using (db)
        {
            var category = new Category { Name = "Z-Category", NameNormalized = ReferenceNameNormalizer.NormalizeKey("Z-Category"), IsActive = true };
            db.Categories.Add(category);
            await db.SaveChangesAsync();

            var t1 = BuildTransaction(1, new DateTime(2026, 1, 1));
            t1.CategoryId = category.Id;

            var t2 = BuildTransaction(2, new DateTime(2026, 1, 2));
            t2.CategoryId = category.Id;

            var t3 = BuildTransaction(3, new DateTime(2026, 1, 3));
            t3.Category = "A-FreeText";

            var t4 = BuildTransaction(4, new DateTime(2026, 1, 4));
            t4.Category = null;

            db.Transactions.AddRange(t1, t2, t3, t4);
            await db.SaveChangesAsync();

            var service = CreateService(db);
            var user = new TestCurrentUser();

            var cursorIds = await WalkCursorAsync(service, user, cursor => new TransactionSearchRequest
            {
                PaginationMode = TransactionSearchPagination.CursorMode,
                PageSize = 1,
                SortBy = "Category",
                SortDesc = sortDesc,
                Cursor = cursor
            });

            var expected = sortDesc ? new[] { 2, 1, 3, 4 } : new[] { 4, 3, 1, 2 };
            Assert.Equal(expected, cursorIds);
            Assert.Equal(cursorIds.Count, cursorIds.Distinct().Count());
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SearchAsync_ResponseDueDate_NullOrderingAndDuplicateDates_OnSqlite(bool sortDesc)
    {
        var (connection, db) = await CreateDbAsync();
        await using (connection)
        await using (db)
        {
            var t1 = BuildTransaction(1, new DateTime(2026, 1, 1));
            t1.ResponseDueDate = null;

            var t2 = BuildTransaction(2, new DateTime(2026, 1, 2));
            t2.ResponseDueDate = null;

            var sharedDueDate = new DateTime(2026, 2, 1);
            var t3 = BuildTransaction(3, new DateTime(2026, 1, 3));
            t3.ResponseDueDate = sharedDueDate;

            var t4 = BuildTransaction(4, new DateTime(2026, 1, 4));
            t4.ResponseDueDate = sharedDueDate;

            var t5 = BuildTransaction(5, new DateTime(2026, 1, 5));
            t5.ResponseDueDate = new DateTime(2026, 2, 5);

            db.Transactions.AddRange(t1, t2, t3, t4, t5);
            await db.SaveChangesAsync();

            var service = CreateService(db);
            var user = new TestCurrentUser();

            var cursorIds = await WalkCursorAsync(service, user, cursor => new TransactionSearchRequest
            {
                PaginationMode = TransactionSearchPagination.CursorMode,
                PageSize = 1,
                SortBy = "ResponseDueDate",
                SortDesc = sortDesc,
                Cursor = cursor
            });

            var expected = sortDesc ? new[] { 5, 4, 3, 2, 1 } : new[] { 1, 2, 3, 4, 5 };
            Assert.Equal(expected, cursorIds);
            Assert.Equal(cursorIds.Count, cursorIds.Distinct().Count());
        }
    }
}
