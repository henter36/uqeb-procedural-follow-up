using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Uqeb.Api.Data;
using Uqeb.Api.Models.Entities;
using Uqeb.Api.Models.Enums;
using Uqeb.Api.Services;
using Xunit;

namespace Uqeb.Api.Tests;

// GetDashboardAsync / GetPageSummaryAsync previously threw
// "could not be translated" against a real relational provider because the
// aggregate Count() predicates read a computed property off a record that was
// constructed inside the predicate. EF Core InMemory silently allowed this via
// client evaluation, which is why the bug shipped undetected — these tests must
// run against SQL Server, not InMemory, to guard against a regression.
[Trait("Category", "SqlServer")]
public class ReportServiceDashboardSqlServerIntegrationTests
{
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

    private sealed class SimpleDbContextFactory(DbContextOptions<AppDbContext> options) : IDbContextFactory<AppDbContext>
    {
        public AppDbContext CreateDbContext() => new(options);

        public Task<AppDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new AppDbContext(options));
    }

    private sealed record SeededReferenceData(int AdminUserId, int PartyId);

    private static async Task<(string DatabaseName, DbContextOptions<AppDbContext> Options)> CreateSqlDbAsync()
    {
        var databaseName = $"Uqeb_ReportDashboard_{Guid.NewGuid():N}";
        var masterBuilder = new SqlConnectionStringBuilder(ConnectionString!) { InitialCatalog = "master" };

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

                EXEC('CREATE DATABASE ' + @quotedDatabaseName);
                """;
            var parameter = command.CreateParameter();
            parameter.ParameterName = "@databaseName";
            parameter.Value = databaseName;
            command.Parameters.Add(parameter);
            await command.ExecuteNonQueryAsync();
        }

        var dbBuilder = new SqlConnectionStringBuilder(ConnectionString!) { InitialCatalog = databaseName };
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(dbBuilder.ConnectionString).Options;

        await using (var db = new AppDbContext(options))
        {
            await db.Database.MigrateAsync();
        }

        return (databaseName, options);
    }

    private static async Task DropSqlDbAsync(string databaseName) =>
        await SqlServerTestDatabaseHelper.DropDatabaseAsync(ConnectionString!, databaseName);

    private static async Task<SeededReferenceData> SeedReferenceDataAsync(AppDbContext db)
    {
        var user = new User { Username = "admin", PasswordHash = "hash", FullName = "Admin", Role = UserRole.Admin, IsActive = true };
        var party = new ExternalParty { Name = "جهة", NameNormalized = "جهة", IsActive = true };
        var department = new Department { Name = "المالية", NameNormalized = "المالية", IsActive = true };
        db.Users.Add(user);
        db.ExternalParties.Add(party);
        db.Departments.Add(department);
        await db.SaveChangesAsync();

        return new SeededReferenceData(user.Id, party.Id);
    }

    private static Transaction BuildTransaction(SeededReferenceData seed, string incomingNumber, string trackingNumber) => new()
    {
        IncomingNumber = incomingNumber,
        InternalTrackingNumber = trackingNumber,
        IncomingDate = DateTime.UtcNow.Date.AddDays(-10),
        Subject = "معاملة اختبار",
        IncomingSourceType = IncomingSourceType.External,
        IncomingFromPartyId = seed.PartyId,
        IncomingFrom = "جهة",
        Status = TransactionStatus.New,
        Priority = Priority.Normal,
        CreatedById = seed.AdminUserId,
        CreatedAt = DateTime.UtcNow
    };

    [Fact]
    public async Task GetDashboardAsync_and_GetPageSummaryAsync_execute_against_sql_server_without_translation_error()
    {
        if (!IsSqlServerAvailable())
        {
            Assert.False(IsRequired, "SQL Server report dashboard integration tests are required but the database is unavailable.");
            return;
        }

        var (databaseName, options) = await CreateSqlDbAsync();
        try
        {
            var factory = new SimpleDbContextFactory(options);
            await using var seedDb = await factory.CreateDbContextAsync();
            var seed = await SeedReferenceDataAsync(seedDb);
            seedDb.Transactions.Add(BuildTransaction(seed, "IN-DASH-1", "TRK-DASH-1"));
            await seedDb.SaveChangesAsync();

            await using var db = await factory.CreateDbContextAsync();
            var service = new ReportService(db, factory);

            var dashboard = await service.GetDashboardAsync();
            var pageSummary = await service.GetPageSummaryAsync();

            Assert.NotNull(dashboard);
            Assert.NotNull(pageSummary);
        }
        finally
        {
            await DropSqlDbAsync(databaseName);
        }
    }

    [Fact]
    public async Task GetDashboardAsync_and_GetPageSummaryAsync_count_overdue_assignment_response_required_and_closed_this_month()
    {
        if (!IsSqlServerAvailable())
        {
            Assert.False(IsRequired, "SQL Server report dashboard integration tests are required but the database is unavailable.");
            return;
        }

        var (databaseName, options) = await CreateSqlDbAsync();
        try
        {
            var factory = new SimpleDbContextFactory(options);
            await using var seedDb = await factory.CreateDbContextAsync();
            var seed = await SeedReferenceDataAsync(seedDb);

            // Transaction with an overdue open assignment (no response requirement of its own).
            var overdueAssignmentTx = BuildTransaction(seed, "IN-OVERDUE-1", "TRK-OVERDUE-1");
            overdueAssignmentTx.Status = TransactionStatus.InProgress;
            seedDb.Transactions.Add(overdueAssignmentTx);
            await seedDb.SaveChangesAsync();
            seedDb.Assignments.Add(new Assignment
            {
                TransactionId = overdueAssignmentTx.Id,
                DepartmentId = (await seedDb.Departments.FirstAsync()).Id,
                AssignedDate = DateTime.UtcNow.Date.AddDays(-10),
                RequiresReply = true,
                ReplyStatus = ReplyStatus.Pending,
                Status = AssignmentStatus.Active,
                DueDate = DateTime.UtcNow.Date.AddDays(-1),
                CreatedById = seed.AdminUserId,
                CreatedAt = DateTime.UtcNow
            });

            // Response-required transaction with a pending (not yet completed) response.
            var responseRequiredTx = BuildTransaction(seed, "IN-RESPREQ-1", "TRK-RESPREQ-1");
            responseRequiredTx.RequiresResponse = true;
            responseRequiredTx.ResponseCompleted = false;
            responseRequiredTx.ResponseDueDate = DateTime.UtcNow.Date.AddDays(5);
            seedDb.Transactions.Add(responseRequiredTx);

            // Transaction closed earlier this month.
            var closedThisMonthTx = BuildTransaction(seed, "IN-CLOSED-1", "TRK-CLOSED-1");
            closedThisMonthTx.Status = TransactionStatus.Closed;
            closedThisMonthTx.ClosedAt = DateTime.UtcNow;
            seedDb.Transactions.Add(closedThisMonthTx);

            await seedDb.SaveChangesAsync();

            await using var db = await factory.CreateDbContextAsync();
            var service = new ReportService(db, factory);

            var dashboard = await service.GetDashboardAsync();
            var pageSummary = await service.GetPageSummaryAsync();

            Assert.True(dashboard.OverdueCount >= 1);
            Assert.True(pageSummary.Overdue >= 1);
            Assert.True(pageSummary.OpenAssignments >= 1);

            Assert.True(dashboard.RequiresResponse >= 1);
            Assert.True(pageSummary.ResponseRequired >= 1);

            Assert.True(dashboard.ClosedThisMonth >= 1);
        }
        finally
        {
            await DropSqlDbAsync(databaseName);
        }
    }

    [Fact]
    public async Task GetDashboardAsync_and_GetPageSummaryAsync_count_response_overdue_partial_replies_and_average_completion_days()
    {
        if (!IsSqlServerAvailable())
        {
            Assert.False(IsRequired, "SQL Server report dashboard integration tests are required but the database is unavailable.");
            return;
        }

        var (databaseName, options) = await CreateSqlDbAsync();
        try
        {
            var factory = new SimpleDbContextFactory(options);
            await using var seedDb = await factory.CreateDbContextAsync();
            var seed = await SeedReferenceDataAsync(seedDb);
            var departmentId = (await seedDb.Departments.FirstAsync()).Id;

            // Response-required transaction whose due date is already in the past and whose
            // response was never completed: must contribute to the response-overdue counters,
            // not merely to "response required".
            var responseOverdueTx = BuildTransaction(seed, "IN-RESP-OVERDUE-1", "TRK-RESP-OVERDUE-1");
            responseOverdueTx.RequiresResponse = true;
            responseOverdueTx.ResponseCompleted = false;
            responseOverdueTx.ResponseDueDate = DateTime.UtcNow.Date.AddDays(-3);
            seedDb.Transactions.Add(responseOverdueTx);

            // Transaction with one department that already replied and another still pending:
            // must contribute to PartialReplies but not to OpenAssignments alone semantics change.
            var partiallyRepliedTx = BuildTransaction(seed, "IN-PARTIAL-1", "TRK-PARTIAL-1");
            seedDb.Transactions.Add(partiallyRepliedTx);
            await seedDb.SaveChangesAsync();
            seedDb.Assignments.AddRange(
                new Assignment
                {
                    TransactionId = partiallyRepliedTx.Id,
                    DepartmentId = departmentId,
                    AssignedDate = DateTime.UtcNow.Date.AddDays(-10),
                    RequiresReply = true,
                    ReplyStatus = ReplyStatus.Replied,
                    Status = AssignmentStatus.Active,
                    CreatedById = seed.AdminUserId,
                    CreatedAt = DateTime.UtcNow
                },
                new Assignment
                {
                    TransactionId = partiallyRepliedTx.Id,
                    DepartmentId = departmentId,
                    AssignedDate = DateTime.UtcNow.Date.AddDays(-10),
                    RequiresReply = true,
                    ReplyStatus = ReplyStatus.Pending,
                    Status = AssignmentStatus.Active,
                    CreatedById = seed.AdminUserId,
                    CreatedAt = DateTime.UtcNow
                });

            // Closed transaction with a known incoming-to-closed gap for AverageCompletionDays.
            var closedWithKnownDurationTx = BuildTransaction(seed, "IN-DURATION-1", "TRK-DURATION-1");
            closedWithKnownDurationTx.IncomingDate = DateTime.UtcNow.Date.AddDays(-7);
            closedWithKnownDurationTx.Status = TransactionStatus.Closed;
            closedWithKnownDurationTx.ClosedAt = DateTime.UtcNow.Date.AddDays(-2);
            seedDb.Transactions.Add(closedWithKnownDurationTx);

            await seedDb.SaveChangesAsync();

            await using var db = await factory.CreateDbContextAsync();
            var service = new ReportService(db, factory);

            var dashboard = await service.GetDashboardAsync();
            var pageSummary = await service.GetPageSummaryAsync();

            Assert.True(dashboard.ResponseOverdueCount >= 1);
            Assert.True(pageSummary.OverdueResponses >= 1);

            Assert.True(pageSummary.PartialReplies >= 1);

            Assert.Equal(5, dashboard.AverageCompletionDays);
        }
        finally
        {
            await DropSqlDbAsync(databaseName);
        }
    }
}
