using System.Data;
using System.Data.Common;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Uqeb.Api.Data;
using Uqeb.Api.DTOs.Transactions;
using Uqeb.Api.Models.Entities;
using Uqeb.Api.Models.Enums;
using Uqeb.Api.Services;
using Xunit;

namespace Uqeb.Api.Tests;

public class TransactionPersistenceSqlServerIntegrationTests
{
    private const string InternalTrackingNumberIndexName = "IX_Transactions_InternalTrackingNumber";

    private static string? ConnectionString =>
        Environment.GetEnvironmentVariable("UQEB_TRANSACTION_TEST_CONNECTION")
        ?? Environment.GetEnvironmentVariable("UQEB_REFERENCE_DATA_TEST_CONNECTION");

    private static bool IsRequired =>
        string.Equals(
            Environment.GetEnvironmentVariable("REQUIRE_TRANSACTION_SQLSERVER_TESTS"),
            "1",
            StringComparison.Ordinal);

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

    private sealed class CollisionThenUniqueTrackingService : ITrackingNumberService
    {
        private int _calls;

        public const string CollisionNumber = "UQEB-COLLISION";

        public const string UniqueNumber = "UQEB-2026-00002";

        public int Calls => _calls;

        public Task<string> GenerateNextAsync(CancellationToken cancellationToken = default)
        {
            _calls++;

            return Task.FromResult(
                _calls == 1
                    ? CollisionNumber
                    : UniqueNumber);
        }
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

    private static CreateTransactionRequest BuildCreateRequest(params int[] outgoingDepartmentIds) =>
        new()
        {
            IncomingNumber = "IN-1001",
            IncomingDate = DateTime.UtcNow.Date,
            Subject = "معاملة اختبار",
            IncomingSourceType = IncomingSourceType.External.ToString(),
            IncomingFromPartyId = 1,
            OutgoingNumber = "OUT-1001",
            OutgoingDate = DateTime.UtcNow.Date,
            OutgoingDepartmentIds = outgoingDepartmentIds.ToList(),
            ResponseType = ResponseType.External.ToString(),
            ResponseDueDays = 7,
            Priority = Priority.Normal.ToString(),
            CategoryId = 1
        };

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

    private static async Task AssertUniqueIndexExistsAsync(AppDbContext db, string indexName)
    {
        var connection = db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
            await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT COUNT(1)
            FROM sys.indexes i
            INNER JOIN sys.tables t ON i.object_id = t.object_id
            WHERE t.name = N'Transactions'
              AND i.name = @indexName
              AND i.is_unique = 1
            """;

        var parameter = command.CreateParameter();
        parameter.ParameterName = "@indexName";
        parameter.Value = indexName;
        command.Parameters.Add(parameter);

        var count = Convert.ToInt32(await command.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture);
        Assert.Equal(1, count);
    }

    private static async Task SeedReferenceDataAsync(AppDbContext db, int adminUserId = 1)
    {
        db.Users.Add(new User
        {
            Id = adminUserId,
            Username = "admin",
            PasswordHash = "hash",
            FullName = "Admin",
            Role = UserRole.Admin,
            IsActive = true
        });
        db.Categories.Add(new Category { Id = 1, Name = "عام", NameNormalized = "عام", IsActive = true });
        db.ExternalParties.Add(new ExternalParty { Id = 1, Name = "جهة", NameNormalized = "جهة", IsActive = true });
        db.Departments.AddRange(
            new Department { Id = 10, Name = "المالية", NameNormalized = "المالية", IsActive = true },
            new Department { Id = 11, Name = "الموارد", NameNormalized = "الموارد", IsActive = true });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task CreateAsync_retries_after_tracking_number_collision_without_duplicates()
    {
        if (!IsSqlServerAvailable())
        {
            Assert.False(
                IsRequired,
                "SQL Server transaction integration tests are required but the database is unavailable.");

            return;
        }

        var (db, testConnectionString, databaseName) = await CreateSqlDbAsync();
        try
        {
            await AssertUniqueIndexExistsAsync(db, InternalTrackingNumberIndexName);
            await SeedReferenceDataAsync(db);

            db.Transactions.Add(new Transaction
            {
                IncomingNumber = "IN-EXISTING",
                InternalTrackingNumber = CollisionThenUniqueTrackingService.CollisionNumber,
                IncomingDate = DateTime.UtcNow.Date,
                Subject = "existing",
                IncomingSourceType = IncomingSourceType.External,
                IncomingFromPartyId = 1,
                IncomingFrom = "جهة",
                Status = TransactionStatus.New,
                CreatedById = 1,
                CreatedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();

            var trackingService = new CollisionThenUniqueTrackingService();
            var service = new TransactionService(
                db,
                new AuditService(db),
                trackingService,
                new TestCacheInvalidation());

            var created = await service.CreateAsync(BuildCreateRequest(10, 11), userId: 1);

            Assert.NotNull(created);
            Assert.Equal(2, trackingService.Calls);
            Assert.Equal(CollisionThenUniqueTrackingService.UniqueNumber, created.InternalTrackingNumber);

            var verificationOptions = new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlServer(testConnectionString)
                .Options;

            await using var verificationDb = new AppDbContext(verificationOptions);

            var transactions = await verificationDb.Transactions
                .Where(t => t.IncomingNumber == "IN-EXISTING" || t.IncomingNumber == "IN-1001")
                .ToListAsync();

            Assert.Equal(2, transactions.Count);
            Assert.Equal(1, transactions.Count(t => t.IncomingNumber == "IN-1001"));

            var persistedCreated = await verificationDb.Transactions.SingleAsync(
                t => t.IncomingNumber == "IN-1001");

            Assert.Equal(CollisionThenUniqueTrackingService.UniqueNumber, persistedCreated.InternalTrackingNumber);
            Assert.NotEqual(CollisionThenUniqueTrackingService.CollisionNumber, persistedCreated.InternalTrackingNumber);

            Assert.Equal(
                2,
                await verificationDb.TransactionOutgoingDepartments.CountAsync(x =>
                    x.TransactionId == persistedCreated.Id));

            Assert.Equal(
                2,
                await verificationDb.Assignments.CountAsync(x =>
                    x.TransactionId == persistedCreated.Id));

            Assert.Equal(
                1,
                await verificationDb.AuditLogs.CountAsync(a =>
                    a.Action == AuditAction.Create &&
                    a.TransactionId == persistedCreated.Id));

            Assert.False(
                await verificationDb.AuditLogs.AnyAsync(a =>
                    a.TransactionId == persistedCreated.Id &&
                    a.EntityName == "Assignment" &&
                    a.EntityId == null));

            Assert.False(
                await verificationDb.AuditLogs.AnyAsync(a =>
                    a.TransactionId == null &&
                    (a.EntityName == "Assignment" ||
                     a.EntityName == "TransactionOutgoingDepartments")));

            var existing = await verificationDb.Transactions.SingleAsync(
                t => t.IncomingNumber == "IN-EXISTING");

            Assert.Equal(
                CollisionThenUniqueTrackingService.CollisionNumber,
                existing.InternalTrackingNumber);
        }
        finally
        {
            await db.DisposeAsync();
            await SqlServerTestDatabaseHelper.DropDatabaseAsync(ConnectionString!, databaseName);
        }
    }
}
