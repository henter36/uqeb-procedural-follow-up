using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Uqeb.Api.Data;
using Uqeb.Api.DTOs.Transactions;
using Uqeb.Api.Helpers;
using Uqeb.Api.Models.Entities;
using Uqeb.Api.Models.Enums;
using Uqeb.Api.Services;
using Xunit;

namespace Uqeb.Api.Tests;

// Behavioral relational coverage for issue #142 / PR #152 review follow-up: runs the cursor walk
// against a real SQL Server database (same provider as production) for the pagination expressions the
// refactor touched - IncomingFrom/Category navigation coalescing, string keyset CompareTo comparisons,
// and nullable ResponseDueDate keyset comparisons. TransactionSearchPaginationRegressionTests already
// covers every sort mode and edge case against EF Core InMemory; this file intentionally stays small
// and only re-runs the provider-sensitive cases, on real SQL Server collation/NULL-ordering semantics.
//
// Self-skips (like the existing TransactionSearchSqlServerIntegrationTests) unless
// UQEB_TRANSACTION_TEST_CONNECTION is set; CI's "transaction-sqlserver-integration" job sets it against
// a real SQL Server 2022 service container and also sets REQUIRE_TRANSACTION_SQLSERVER_TESTS=1, so a
// silent skip there would fail the assertion below instead of passing vacuously.
[Trait("Category", "SqlServer")]
public class TransactionSearchPaginationSqlServerIntegrationTests
{
    private static string? ConnectionString =>
        Environment.GetEnvironmentVariable("UQEB_TRANSACTION_TEST_CONNECTION")
        ?? Environment.GetEnvironmentVariable("UQEB_REFERENCE_DATA_TEST_CONNECTION");

    private static bool IsRequired =>
        string.Equals(
            Environment.GetEnvironmentVariable("REQUIRE_TRANSACTION_SQLSERVER_TESTS"),
            "1",
            StringComparison.Ordinal);

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

    private static bool IsSqlServerAvailable()
    {
        if (string.IsNullOrWhiteSpace(ConnectionString))
            return false;

        try
        {
            using var connection = new SqlConnection(ConnectionString);
            connection.Open();
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static TransactionService CreateService(AppDbContext db) =>
        new(
            db,
            new AuditService(db),
            new StubTrackingNumberService(),
            new TestCacheInvalidation(),
            new RecurringTransactionTemplateService(db, new AuditService(db), new StubTrackingNumberService()));

    private static async Task<(AppDbContext Db, string TestConnectionString, string DatabaseName)> CreateSqlDbAsync(
        string databaseNamePrefix)
    {
        var databaseName = $"{databaseNamePrefix}_{Guid.NewGuid():N}";
        var masterBuilder = new SqlConnectionStringBuilder(ConnectionString!)
        {
            InitialCatalog = "master"
        };

        await using (var connection = new SqlConnection(masterBuilder.ConnectionString))
        {
            await connection.OpenAsync();
            _ = SqlTestDatabaseNameHelper.ValidateAndQuoteDatabaseName(connection, databaseName);

            await using var command = connection.CreateCommand();
            command.CommandText =
                """
                DECLARE @quotedDatabaseName sysname = QUOTENAME(@databaseName);

                IF @quotedDatabaseName IS NULL
                BEGIN
                    THROW 51020, N'Invalid SQL Server test database name.', 1;
                END;

                EXEC(N'CREATE DATABASE ' + @quotedDatabaseName);
                """;

            command.Parameters.Add(
                new SqlParameter("@databaseName", SqlDbType.NVarChar, 128)
                {
                    Value = databaseName
                });

            await command.ExecuteNonQueryAsync();
        }

        var testConnectionString = new SqlConnectionStringBuilder(ConnectionString!)
        {
            InitialCatalog = databaseName
        }.ConnectionString;

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(testConnectionString)
            .Options;

        var db = new AppDbContext(options);
        await db.Database.EnsureCreatedAsync();
        return (db, testConnectionString, databaseName);
    }

    // Transaction.Id is a SQL Server IDENTITY column (no ValueGeneratedNever override), so unlike the
    // EF Core InMemory/SQLite fixtures elsewhere in this suite, these rows must never set Id explicitly
    // - SQL Server rejects an explicit insert into an identity column unless IDENTITY_INSERT is turned
    // on, which the project's existing SQL Server test fixtures never do. `label` only makes
    // IncomingNumber/InternalTrackingNumber/Subject unique per row; the real Id is read back from the
    // entity after SaveChangesAsync and used for every ordering assertion below.
    private static Transaction BuildTransaction(int label, DateTime incomingDate, int createdById) => new()
    {
        InternalTrackingNumber = $"UQEB-2026-{label:00000}",
        IncomingNumber = $"IN-{label:000}",
        IncomingDate = incomingDate,
        Subject = $"Subject {label:000}",
        IncomingSourceType = IncomingSourceType.External,
        RequiresResponse = false,
        ResponseType = ResponseType.None,
        Priority = Priority.Normal,
        Status = TransactionStatus.New,
        CreatedById = createdById,
        CreatedAt = incomingDate
    };

    private static async Task<int> SeedAdminUserAsync(AppDbContext db)
    {
        var user = new User
        {
            Username = "admin",
            PasswordHash = "hash",
            FullName = "Admin",
            Role = UserRole.Admin,
            IsActive = true
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user.Id;
    }

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
    public async Task SearchAsync_IncomingFrom_CoalescesPartyDepartmentAndRawText_OnSqlServer(bool sortDesc)
    {
        if (!IsSqlServerAvailable())
        {
            Assert.False(IsRequired, "SQL Server transaction pagination integration tests are required but the database is unavailable.");
            return;
        }

        var (db, testConnectionString, databaseName) = await CreateSqlDbAsync("Uqeb_Pagination_IncomingFrom");
        try
        {
            var createdById = await SeedAdminUserAsync(db);

            var party = new ExternalParty { Name = "Z-Party", NameNormalized = ReferenceNameNormalizer.NormalizeKey("Z-Party"), IsActive = true };
            var department = new Department { Name = "M-Dept", NameNormalized = ReferenceNameNormalizer.NormalizeKey("M-Dept"), IsActive = true };
            db.ExternalParties.Add(party);
            db.Departments.Add(department);
            await db.SaveChangesAsync();

            // Two rows share the same party name so a page boundary can land on a tied primary value.
            var t1 = BuildTransaction(1, new DateTime(2026, 1, 1), createdById);
            t1.IncomingFromPartyId = party.Id;

            var t2 = BuildTransaction(2, new DateTime(2026, 1, 2), createdById);
            t2.IncomingFromPartyId = party.Id;

            var t3 = BuildTransaction(3, new DateTime(2026, 1, 3), createdById);
            t3.IncomingSourceType = IncomingSourceType.Internal;
            t3.IncomingFromDepartmentId = department.Id;

            var t4 = BuildTransaction(4, new DateTime(2026, 1, 4), createdById);
            t4.IncomingFrom = "A-Raw";

            var t5 = BuildTransaction(5, new DateTime(2026, 1, 5), createdById);
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

            var expected = sortDesc
                ? new[] { t2.Id, t1.Id, t3.Id, t4.Id, t5.Id }
                : new[] { t5.Id, t4.Id, t3.Id, t1.Id, t2.Id };
            Assert.Equal(expected, cursorIds);
            Assert.Equal(cursorIds.Count, cursorIds.Distinct().Count());
        }
        finally
        {
            await db.DisposeAsync();
            await SqlServerTestDatabaseHelper.DropDatabaseAsync(testConnectionString, databaseName);
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SearchAsync_Category_CoalescesLinkedNameAndFreeText_OnSqlServer(bool sortDesc)
    {
        if (!IsSqlServerAvailable())
        {
            Assert.False(IsRequired, "SQL Server transaction pagination integration tests are required but the database is unavailable.");
            return;
        }

        var (db, testConnectionString, databaseName) = await CreateSqlDbAsync("Uqeb_Pagination_Category");
        try
        {
            var createdById = await SeedAdminUserAsync(db);

            var category = new Category { Name = "Z-Category", NameNormalized = ReferenceNameNormalizer.NormalizeKey("Z-Category"), IsActive = true };
            db.Categories.Add(category);
            await db.SaveChangesAsync();

            // Two rows share the same linked category so a page boundary can land on a tied primary value.
            var t1 = BuildTransaction(1, new DateTime(2026, 1, 1), createdById);
            t1.CategoryId = category.Id;

            var t2 = BuildTransaction(2, new DateTime(2026, 1, 2), createdById);
            t2.CategoryId = category.Id;

            var t3 = BuildTransaction(3, new DateTime(2026, 1, 3), createdById);
            t3.Category = "A-FreeText";

            var t4 = BuildTransaction(4, new DateTime(2026, 1, 4), createdById);
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

            var expected = sortDesc
                ? new[] { t2.Id, t1.Id, t3.Id, t4.Id }
                : new[] { t4.Id, t3.Id, t1.Id, t2.Id };
            Assert.Equal(expected, cursorIds);
            Assert.Equal(cursorIds.Count, cursorIds.Distinct().Count());
        }
        finally
        {
            await db.DisposeAsync();
            await SqlServerTestDatabaseHelper.DropDatabaseAsync(testConnectionString, databaseName);
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SearchAsync_ResponseDueDate_NullOrderingAndDuplicateDates_OnSqlServer(bool sortDesc)
    {
        if (!IsSqlServerAvailable())
        {
            Assert.False(IsRequired, "SQL Server transaction pagination integration tests are required but the database is unavailable.");
            return;
        }

        var (db, testConnectionString, databaseName) = await CreateSqlDbAsync("Uqeb_Pagination_ResponseDueDate");
        try
        {
            var createdById = await SeedAdminUserAsync(db);

            var t1 = BuildTransaction(1, new DateTime(2026, 1, 1), createdById);
            t1.ResponseDueDate = null;

            var t2 = BuildTransaction(2, new DateTime(2026, 1, 2), createdById);
            t2.ResponseDueDate = null;

            var sharedDueDate = new DateTime(2026, 2, 1);
            var t3 = BuildTransaction(3, new DateTime(2026, 1, 3), createdById);
            t3.ResponseDueDate = sharedDueDate;

            var t4 = BuildTransaction(4, new DateTime(2026, 1, 4), createdById);
            t4.ResponseDueDate = sharedDueDate;

            var t5 = BuildTransaction(5, new DateTime(2026, 1, 5), createdById);
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

            // Nulls sort first ascending / last descending, matching SQL Server's default NULL ordering
            // and ApplyNullableDateTimeKeyset's null-bucket-then-value walk in both directions.
            var expected = sortDesc
                ? new[] { t5.Id, t4.Id, t3.Id, t2.Id, t1.Id }
                : new[] { t1.Id, t2.Id, t3.Id, t4.Id, t5.Id };
            Assert.Equal(expected, cursorIds);
            Assert.Equal(cursorIds.Count, cursorIds.Distinct().Count());
        }
        finally
        {
            await db.DisposeAsync();
            await SqlServerTestDatabaseHelper.DropDatabaseAsync(testConnectionString, databaseName);
        }
    }
}
