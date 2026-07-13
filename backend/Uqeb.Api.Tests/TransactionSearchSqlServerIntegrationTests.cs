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

[Trait("Category", "SqlServer")]
public class TransactionSearchSqlServerIntegrationTests
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

    private static async Task<(AppDbContext Db, string TestConnectionString, string DatabaseName)> CreateSqlDbAsync()
    {
        var databaseName = $"Uqeb_TransactionRetry_{Guid.NewGuid():N}";
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

    [Fact]
    public async Task SearchAsync_GlobalSearchPredicate_TranslatesOnSqlServerForAssignmentRelation()
    {
        if (!IsSqlServerAvailable())
        {
            Assert.False(
                IsRequired,
                "SQL Server transaction search integration tests are required but the database is unavailable.");

            return;
        }

        var (db, testConnectionString, databaseName) = await CreateSqlDbAsync();
        try
        {
            var user = new User
            {
                Username = "admin",
                PasswordHash = "hash",
                FullName = "Admin",
                Role = UserRole.Admin,
                IsActive = true
            };
            var department = new Department
            {
                Name = "إدارة البحث",
                NameNormalized = ReferenceNameNormalizer.NormalizeKey("إدارة البحث"),
                IsActive = true
            };
            db.Users.Add(user);
            db.Departments.Add(department);
            await db.SaveChangesAsync();

            var transaction = new Transaction
            {
                InternalTrackingNumber = "UQEB-SQL-SEARCH",
                IncomingNumber = "IN-SQL-SEARCH",
                IncomingDate = new DateTime(2026, 1, 2, 9, 30, 0, DateTimeKind.Utc),
                Subject = "معاملة ترجمة البحث",
                RequiresResponse = false,
                Status = TransactionStatus.InProgress,
                Priority = Priority.Normal,
                CreatedById = user.Id
            };
            db.Transactions.Add(transaction);
            await db.SaveChangesAsync();

            db.Assignments.Add(new Assignment
            {
                TransactionId = transaction.Id,
                DepartmentId = department.Id,
                AssignedDate = DateTime.UtcNow.Date,
                RequiredAction = "تحقق SQL شامل",
                RequiresReply = true,
                ReplyStatus = ReplyStatus.Pending,
                Status = AssignmentStatus.Active,
                CreatedById = user.Id
            });
            await db.SaveChangesAsync();

            var result = await CreateService(db).SearchAsync(
                new TransactionSearchRequest
                {
                    SearchText = "تحقق SQL",
                    StatusScope = "all",
                    Page = 1,
                    PageSize = 20
                },
                new TestCurrentUser());

            var item = Assert.Single(result.Items);
            Assert.Equal(transaction.Id, item.Id);
        }
        finally
        {
            await db.DisposeAsync();
            await SqlServerTestDatabaseHelper.DropDatabaseAsync(testConnectionString, databaseName);
        }
    }
}
