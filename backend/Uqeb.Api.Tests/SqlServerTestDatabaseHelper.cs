using System.Data;
using Microsoft.Data.SqlClient;

namespace Uqeb.Api.Tests;

internal static class SqlServerTestDatabaseHelper
{
    internal static async Task DropDatabaseAsync(string connectionString, string databaseName)
    {
        var builder = new SqlConnectionStringBuilder(connectionString)
        {
            InitialCatalog = "master",
        };

        await using var connection = new SqlConnection(builder.ConnectionString);
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

            IF DB_ID(@databaseName) IS NOT NULL
            BEGIN
                EXEC(
                    N'ALTER DATABASE ' +
                    @quotedDatabaseName +
                    N' SET SINGLE_USER WITH ROLLBACK IMMEDIATE;'
                );

                EXEC(N'DROP DATABASE ' + @quotedDatabaseName);
            END;
            """;

        command.Parameters.Add(
            new SqlParameter("@databaseName", SqlDbType.NVarChar, 128)
            {
                Value = databaseName,
            });

        await command.ExecuteNonQueryAsync();

        SqlConnection.ClearAllPools();
    }
}
