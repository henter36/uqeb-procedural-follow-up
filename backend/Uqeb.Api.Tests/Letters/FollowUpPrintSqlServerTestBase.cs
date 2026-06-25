using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Uqeb.Api.Configuration;
using Uqeb.Api.Data;
using Uqeb.Api.DTOs.FollowUpPrint;
using Uqeb.Api.HostedServices;
using Uqeb.Api.Services;
using Xunit;

namespace Uqeb.Api.Tests.Letters;

public abstract class FollowUpPrintSqlServerTestBase
{
    protected const string RequireEnvVar = "REQUIRE_FOLLOWUP_PRINT_SQLSERVER_TESTS";

    protected static string? ConnectionString =>
        Environment.GetEnvironmentVariable("UQEB_TRANSACTION_TEST_CONNECTION")
        ?? Environment.GetEnvironmentVariable("UQEB_REFERENCE_DATA_TEST_CONNECTION");

    protected static bool IsRequired =>
        string.Equals(Environment.GetEnvironmentVariable(RequireEnvVar), "1", StringComparison.Ordinal);

    protected static bool IsSqlServerAvailable()
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

    protected static bool ShouldRunSqlServerTest()
    {
        if (IsSqlServerAvailable())
            return true;

        Assert.False(
            IsRequired,
            "Follow-up print SQL Server integration tests are required but the database is unavailable.");
        return false;
    }

    protected static async Task<FollowUpPrintSqlServerContext> CreateMigratedContextAsync(string? databaseName = null)
    {
        databaseName ??= $"UqebFollowUpPrint_{Guid.NewGuid():N}";
        var testConnectionString = await CreateDatabaseAsync(databaseName);

        var services = new ServiceCollection();
        services.AddLogging(b => b.SetMinimumLevel(LogLevel.Warning));
        services.AddDbContext<AppDbContext>(o => o.UseSqlServer(testConnectionString));
        services.Configure<FollowUpLettersOptions>(o =>
        {
            o.DefaultBatchPrintSize = 25;
            o.AbsoluteMaxBatchPrintSize = 100;
            o.JobLeaseSeconds = 30;
            o.JobRetryCount = 3;
            o.MaxConcurrentPrintJobs = 10;
            o.MaxConcurrentJobsPerUser = 3;
            o.MaxOutstandingUnprintedJobs = 5;
        });
        services.AddScoped<IAuditService, AuditService>();
        services.AddScoped<IUserNotificationService, NoOpFollowUpPrintNotificationService>();
        services.AddScoped<FollowUpPrintJobProcessorHostedService>();

        var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.MigrateAsync();

        return new FollowUpPrintSqlServerContext(provider, testConnectionString, databaseName);
    }

    protected static async Task<string> CreateDatabaseAsync(string databaseName)
    {
        var masterBuilder = new SqlConnectionStringBuilder(ConnectionString!)
        {
            InitialCatalog = "master",
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
                    Value = databaseName,
                });

            await command.ExecuteNonQueryAsync();
        }

        return new SqlConnectionStringBuilder(ConnectionString!)
        {
            InitialCatalog = databaseName,
        }.ConnectionString;
    }

    protected static async Task CleanupAsync(FollowUpPrintSqlServerContext context)
    {
        await context.DisposeAsync();
        await SqlServerTestDatabaseHelper.DropDatabaseAsync(ConnectionString!, context.DatabaseName);
    }

    protected static async Task<bool> TableExistsAsync(AppDbContext db, string tableName)
    {
        var connection = db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
            await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT COUNT(1)
            FROM INFORMATION_SCHEMA.TABLES
            WHERE TABLE_NAME = @tableName
            """;
        var parameter = command.CreateParameter();
        parameter.ParameterName = "@tableName";
        parameter.Value = tableName;
        command.Parameters.Add(parameter);

        return Convert.ToInt32(await command.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture) == 1;
    }

    protected static async Task<bool> IndexExistsAsync(AppDbContext db, string tableName, string indexName)
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
            WHERE t.name = @tableName AND i.name = @indexName
            """;
        var tableParameter = command.CreateParameter();
        tableParameter.ParameterName = "@tableName";
        tableParameter.Value = tableName;
        command.Parameters.Add(tableParameter);

        var indexParameter = command.CreateParameter();
        indexParameter.ParameterName = "@indexName";
        indexParameter.Value = indexName;
        command.Parameters.Add(indexParameter);

        return Convert.ToInt32(await command.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture) == 1;
    }

    protected sealed class FollowUpPrintSqlServerContext : IAsyncDisposable
    {
        public FollowUpPrintSqlServerContext(ServiceProvider provider, string connectionString, string databaseName)
        {
            Provider = provider;
            ConnectionString = connectionString;
            DatabaseName = databaseName;
        }

        public ServiceProvider Provider { get; }
        public string ConnectionString { get; }
        public string DatabaseName { get; }

        public AppDbContext CreateDbContext() =>
            Provider.GetRequiredService<AppDbContext>();

        public async ValueTask DisposeAsync()
        {
            await Provider.DisposeAsync();
        }
    }

    protected sealed class NoOpFollowUpPrintNotificationService : IUserNotificationService
    {
        public Task<UserNotificationDto> CreateAsync(int userId, string type, string title, string body, string? link = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(new UserNotificationDto { Type = type, Title = title, Body = body, Link = link });

        public Task<List<UserNotificationDto>> ListForUserAsync(int userId, bool unreadOnly = false, DateTime? since = null, int limit = 50, CancellationToken cancellationToken = default) =>
            Task.FromResult(new List<UserNotificationDto>());

        public Task<UserNotificationDto?> MarkReadAsync(int notificationId, int userId, CancellationToken cancellationToken = default) =>
            Task.FromResult<UserNotificationDto?>(null);
    }
}
