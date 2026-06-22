using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Uqeb.Api.Data;
using Uqeb.Api.DTOs.Departments;
using Uqeb.Api.DTOs.Users;
using Uqeb.Api.Helpers;
using Uqeb.Api.Models.Entities;
using Uqeb.Api.Models.Enums;
using Uqeb.Api.Services;
using Xunit;

namespace Uqeb.Api.Tests;

public class ReferenceDataSqlServerIntegrationTests
{
    private static string? ConnectionString =>
        Environment.GetEnvironmentVariable("UQEB_REFERENCE_DATA_TEST_CONNECTION");

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

    private static async Task<(AppDbContext Db, string ConnectionString, string DatabaseName)> CreateSqlDbAsync(string databaseSuffix)
    {
        var databaseName = $"Uqeb_RefDataTest_{databaseSuffix}_{Guid.NewGuid():N}";
        var masterBuilder = new SqlConnectionStringBuilder(ConnectionString!)
        {
            InitialCatalog = "master"
        };

        await using (var connection = new SqlConnection(masterBuilder.ConnectionString))
        {
            await connection.OpenAsync();
            var quotedDatabaseName = SqlTestDatabaseNameHelper.ValidateAndQuoteDatabaseName(connection, databaseName);
            await using var command = connection.CreateCommand();
            command.CommandText = $"CREATE DATABASE {quotedDatabaseName}";
            await command.ExecuteNonQueryAsync();
        }

        var connectionString = new SqlConnectionStringBuilder(ConnectionString!)
        {
            InitialCatalog = databaseName
        }.ConnectionString;

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(connectionString)
            .Options;

        var db = new AppDbContext(options);
        await db.Database.EnsureCreatedAsync();
        return (db, connectionString, databaseName);
    }

    [Fact]
    public async Task ConcurrentDepartmentCreate_OnlyOneSucceeds()
    {
        if (!IsSqlServerAvailable())
            return;

        var (db, connectionString, databaseName) = await CreateSqlDbAsync(nameof(ConcurrentDepartmentCreate_OnlyOneSucceeds));
        await using (db)
        {
            db.Users.Add(new User
            {
                Username = "actor",
                PasswordHash = "x",
                FullName = "Actor",
                Role = UserRole.Admin,
                IsActive = true
            });
            await db.SaveChangesAsync();

            async Task<Exception?> TryCreate()
            {
                await using var scopedDb = new AppDbContext(
                    new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(connectionString).Options);
                var service = new DepartmentService(scopedDb, new AuditService(scopedDb));
                try
                {
                    await service.CreateAsync(new CreateDepartmentRequest { Name = "Duplicate Dept" }, 1);
                    return null;
                }
                catch (Exception ex)
                {
                    return ex;
                }
            }

            var results = await Task.WhenAll(TryCreate(), TryCreate());
            Assert.Equal(1, results.Count(r => r == null));
            Assert.Equal(1, results.Count(r => r is DuplicateReferenceException));
            Assert.Equal(1, await db.Departments.CountAsync());
        }

        await DropDatabase(databaseName);
    }

    [Fact]
    public async Task ConcurrentAdminDeactivate_OnlyOneSucceedsAndOneAdminRemains()
    {
        if (!IsSqlServerAvailable())
            return;

        var (db, connectionString, databaseName) = await CreateSqlDbAsync(nameof(ConcurrentAdminDeactivate_OnlyOneSucceedsAndOneAdminRemains));
        await using (db)
        {
            db.Users.AddRange(
                new User
                {
                    Username = "admin1",
                    PasswordHash = "x",
                    FullName = "Admin One",
                    Role = UserRole.Admin,
                    IsActive = true
                },
                new User
                {
                    Username = "admin2",
                    PasswordHash = "x",
                    FullName = "Admin Two",
                    Role = UserRole.Admin,
                    IsActive = true
                });
            await db.SaveChangesAsync();

            async Task<Exception?> TryDeactivate(int userId)
            {
                await using var scopedDb = new AppDbContext(
                    new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(connectionString).Options);
                var service = new UserService(scopedDb, new AuditService(scopedDb));
                try
                {
                    await service.UpdateAsync(userId, new UpdateUserRequest { IsActive = false }, 1);
                    return null;
                }
                catch (Exception ex)
                {
                    return ex;
                }
            }

            var outcomes = await Task.WhenAll(TryDeactivate(1), TryDeactivate(2));
            Assert.Equal(1, outcomes.Count(r => r == null));
            Assert.Equal(1, outcomes.Count(r => r is LastActiveAdminException or InvalidOperationException));

            var activeAdmins = await db.Users.CountAsync(u => u.IsActive && u.Role == UserRole.Admin);
            Assert.Equal(1, activeAdmins);
        }

        await DropDatabase(databaseName);
    }

    private static async Task DropDatabase(string databaseName)
    {
        var builder = new SqlConnectionStringBuilder(ConnectionString!)
        {
            InitialCatalog = "master"
        };

        await using var connection = new SqlConnection(builder.ConnectionString);
        await connection.OpenAsync();
        var quotedDatabaseName = SqlTestDatabaseNameHelper.ValidateAndQuoteDatabaseName(connection, databaseName);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            IF DB_ID(@databaseName) IS NOT NULL
            BEGIN
                ALTER DATABASE {quotedDatabaseName} SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
                DROP DATABASE {quotedDatabaseName};
            END
            """;
        command.Parameters.Add(new SqlParameter("@databaseName", SqlDbType.NVarChar, 128)
        {
            Value = databaseName
        });
        await command.ExecuteNonQueryAsync();
    }
}
