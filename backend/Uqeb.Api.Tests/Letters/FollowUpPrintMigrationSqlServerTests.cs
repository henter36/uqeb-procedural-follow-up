using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Uqeb.Api.Data;
using Xunit;

namespace Uqeb.Api.Tests.Letters;

[Trait("Category", "SqlServer")]
public class FollowUpPrintMigrationSqlServerTests
    : FollowUpPrintSqlServerTestBase
{
    [Fact]
    public async Task MigrateFromEmpty_CreatesFollowUpPrintTablesAndIndexes()
    {
        if (!ShouldRunSqlServerTest())
            return;

        var context = await CreateMigratedContextAsync();
        try
        {
            await using var scope = context.Provider.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            Assert.True(await TableExistsAsync(db, "FollowUpPrintJobs"));
            Assert.True(await TableExistsAsync(db, "FollowUpPrintJobParts"));
            Assert.True(await TableExistsAsync(db, "FollowUpPrintJobPayloads"));
            Assert.True(await TableExistsAsync(db, "FollowUpPrintIdempotencyKeys"));
            Assert.True(await TableExistsAsync(db, "FollowUpLetterPrintRecords"));

            Assert.True(await IndexExistsAsync(db, "FollowUpPrintJobPayloads", "IX_FollowUpPrintJobPayloads_JobId_PayloadOrdinal"));
            Assert.True(await IndexExistsAsync(db, "FollowUpPrintIdempotencyKeys", "IX_FollowUpPrintIdempotencyKeys_UserId_Operation_Key"));
            Assert.True(await IndexExistsAsync(db, "LetterTemplates", "IX_LetterTemplates_TemplateType"));
            Assert.True(await CheckConstraintExistsAsync(db, "LetterTemplates", "CK_LetterTemplates_DefaultRequiresActive"));
            Assert.Equal(("nvarchar", 128, true), await GetColumnShapeAsync(db, "FollowUpPrintIdempotencyKeys", "Key"));
            Assert.Equal(("nvarchar", 64, true), await GetColumnShapeAsync(db, "FollowUpPrintIdempotencyKeys", "Operation"));
            Assert.Equal(("varchar", 64, false), await GetColumnShapeAsync(db, "FollowUpPrintIdempotencyKeys", "RequestHash"));
        }
        finally
        {
            await CleanupAsync(context);
        }
    }

    [Fact]
    public async Task DownAndReUpInvariantMigration_RestoresIndexesConstraintsAndColumnShapes()
    {
        if (!ShouldRunSqlServerTest())
            return;

        var databaseName = $"Uqeb_FollowUpPrint_Invariants_{Guid.NewGuid():N}";
        var connectionString = await CreateDatabaseAsync(databaseName);

        var services = new ServiceCollection();
        services.AddDbContext<AppDbContext>(o => o.UseSqlServer(connectionString));
        ServiceProvider? provider = null;
        try
        {
            provider = services.BuildServiceProvider();

            await using (var scope = provider.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                await db.Database.MigrateAsync();
                Assert.True(await CheckConstraintExistsAsync(db, "LetterTemplates", "CK_LetterTemplates_DefaultRequiresActive"));
            }

            await using (var scope = provider.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var migrator = db.Database.GetService<IMigrator>();
                migrator.Migrate("20260625201021_AddFollowUpPrintJobPayloadsAndBatchSize");
                Assert.False(await CheckConstraintExistsAsync(db, "LetterTemplates", "CK_LetterTemplates_DefaultRequiresActive"));
                Assert.Equal(("nvarchar", -1, true), await GetColumnShapeAsync(db, "FollowUpPrintIdempotencyKeys", "RequestHash"));
            }

            await using (var scope = provider.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                await db.Database.MigrateAsync();
                Assert.True(await IndexExistsAsync(db, "LetterTemplates", "IX_LetterTemplates_TemplateType"));
                Assert.True(await CheckConstraintExistsAsync(db, "LetterTemplates", "CK_LetterTemplates_DefaultRequiresActive"));
                Assert.Equal(("varchar", 64, false), await GetColumnShapeAsync(db, "FollowUpPrintIdempotencyKeys", "RequestHash"));
            }
        }
        finally
        {
            if (provider != null)
                await provider.DisposeAsync();
            await SqlServerTestDatabaseHelper.DropDatabaseAsync(ConnectionString!, databaseName);
        }
    }

    [Fact]
    public async Task DownAdditivePayloadMigration_RemovesPayloadTable_ThenReUpRestoresIt()
    {
        if (!ShouldRunSqlServerTest())
            return;

        var databaseName = $"Uqeb_FollowUpPrint_Migration_{Guid.NewGuid():N}";
        var connectionString = await CreateDatabaseAsync(databaseName);

        var services = new ServiceCollection();
        services.AddDbContext<AppDbContext>(o => o.UseSqlServer(connectionString));
        ServiceProvider? provider = null;
        try
        {
            provider = services.BuildServiceProvider();

            await using (var scope = provider.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                await db.Database.MigrateAsync();
                Assert.True(await TableExistsAsync(db, "FollowUpPrintJobPayloads"));
            }

            await using (var scope = provider.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var migrator = db.Database.GetService<IMigrator>();
                migrator.Migrate("20260625194203_AddFollowUpPrintQueueAndLetterTemplatesV2");
                Assert.False(await TableExistsAsync(db, "FollowUpPrintJobPayloads"));
            }

            await using (var scope = provider.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                await db.Database.MigrateAsync();
                Assert.True(await TableExistsAsync(db, "FollowUpPrintJobPayloads"));
            }
        }
        finally
        {
            if (provider != null)
                await provider.DisposeAsync();
            await SqlServerTestDatabaseHelper.DropDatabaseAsync(ConnectionString!, databaseName);
        }
    }

    [Fact]
    public async Task NewMigration_AddsScopeDepartmentIdColumnAndTargetShapeConstraints()
    {
        if (!ShouldRunSqlServerTest())
            return;

        var context = await CreateMigratedContextAsync();
        try
        {
            await using var scope = context.Provider.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            // ScopeDepartmentId column on FollowUpPrintJobs
            var col = await GetColumnShapeAsync(db, "FollowUpPrintJobs", "ScopeDepartmentId");
            Assert.Equal("int", col.DataType);

            // Filtered unique indexes on FollowUpPrintJobPayloads
            Assert.True(await IndexExistsAsync(db, "FollowUpPrintJobPayloads",
                "IX_FollowUpPrintJobPayloads_JobId_Tx_Dept_Seq"));
            Assert.True(await IndexExistsAsync(db, "FollowUpPrintJobPayloads",
                "IX_FollowUpPrintJobPayloads_JobId_Tx_Entity_Seq"));

            // XOR check constraints
            Assert.True(await CheckConstraintExistsAsync(db, "FollowUpPrintJobPayloads",
                "CK_FollowUpPrintJobPayloads_TargetShape"));
            Assert.True(await CheckConstraintExistsAsync(db, "FollowUpLetterPrintRecords",
                "CK_FollowUpLetterPrintRecords_TargetShape"));
            Assert.True(await FilteredUniqueIndexExistsAsync(
                db,
                "FollowUpLetterPrintRecords",
                "IX_FollowUpLetterPrintRecords_RegisteredFollowUpId_Linked",
                "[RegisteredFollowUpId] IS NOT NULL"));
        }
        finally
        {
            await CleanupAsync(context);
        }
    }

    [Fact]
    public async Task DocumentSnapshotMigration_IsDiscoveredAndRoundTripsColumn()
    {
        if (!ShouldRunSqlServerTest())
            return;

        const string migrationId = "20260626143000_AddDocumentSnapshotToFollowUpPrintRecords";
        const string previousMigrationId = "20260626112704_AddUniqueRegisteredFollowUpPrintRecordLink";
        var databaseName = $"Uqeb_FollowUpPrint_DocumentSnapshot_{Guid.NewGuid():N}";
        var connectionString = await CreateDatabaseAsync(databaseName);

        var services = new ServiceCollection();
        services.AddDbContext<AppDbContext>(o => o.UseSqlServer(connectionString));
        ServiceProvider? provider = null;
        try
        {
            provider = services.BuildServiceProvider();

            await using (var scope = provider.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var migrations = db.GetService<IMigrationsAssembly>().Migrations;
                Assert.Contains(migrationId, migrations.Keys);

                await db.Database.MigrateAsync();

                Assert.False(db.Database.HasPendingModelChanges());
                Assert.Equal(("nvarchar", -1, true), await GetColumnShapeAsync(
                    db,
                    "FollowUpLetterPrintRecords",
                    "DocumentSnapshotJson"));
                Assert.True(await IsColumnNullableAsync(
                    db,
                    "FollowUpLetterPrintRecords",
                    "DocumentSnapshotJson"));
            }

            await using (var scope = provider.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var migrator = db.Database.GetService<IMigrator>();
                migrator.Migrate(previousMigrationId);

                Assert.False(await ColumnExistsAsync(
                    db,
                    "FollowUpLetterPrintRecords",
                    "DocumentSnapshotJson"));
            }

            await using (var scope = provider.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                await db.Database.MigrateAsync();

                Assert.Equal(("nvarchar", -1, true), await GetColumnShapeAsync(
                    db,
                    "FollowUpLetterPrintRecords",
                    "DocumentSnapshotJson"));
                Assert.True(await IsColumnNullableAsync(
                    db,
                    "FollowUpLetterPrintRecords",
                    "DocumentSnapshotJson"));
                Assert.False(db.Database.HasPendingModelChanges());
            }
        }
        finally
        {
            if (provider != null)
                await provider.DisposeAsync();
            await SqlServerTestDatabaseHelper.DropDatabaseAsync(ConnectionString!, databaseName);
        }
    }

    [Fact]
    public async Task DownScopeDepartmentIdMigration_ThenReUp_RestoresConstraints()
    {
        if (!ShouldRunSqlServerTest())
            return;

        var databaseName = $"Uqeb_FollowUpPrint_ScopeConstraints_{Guid.NewGuid():N}";
        var connectionString = await CreateDatabaseAsync(databaseName);

        var services = new ServiceCollection();
        services.AddDbContext<AppDbContext>(o => o.UseSqlServer(connectionString));
        ServiceProvider? provider = null;
        try
        {
            provider = services.BuildServiceProvider();

            await using (var scope = provider.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                await db.Database.MigrateAsync();
                Assert.True(await CheckConstraintExistsAsync(db, "FollowUpPrintJobPayloads",
                    "CK_FollowUpPrintJobPayloads_TargetShape"));
            }

            // Roll back the new migration
            await using (var scope = provider.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var migrator = db.Database.GetService<IMigrator>();
                migrator.Migrate("20260626061519_EnforceFollowUpPrintInvariants");
                Assert.False(await CheckConstraintExistsAsync(db, "FollowUpPrintJobPayloads",
                    "CK_FollowUpPrintJobPayloads_TargetShape"));
            }

            // Re-apply
            await using (var scope = provider.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                await db.Database.MigrateAsync();
                Assert.True(await CheckConstraintExistsAsync(db, "FollowUpPrintJobPayloads",
                    "CK_FollowUpPrintJobPayloads_TargetShape"));
                Assert.True(await IndexExistsAsync(db, "FollowUpPrintJobPayloads",
                    "IX_FollowUpPrintJobPayloads_JobId_Tx_Dept_Seq"));
            }
        }
        finally
        {
            if (provider != null)
                await provider.DisposeAsync();
            await SqlServerTestDatabaseHelper.DropDatabaseAsync(ConnectionString!, databaseName);
        }
    }

    private static async Task<(string DataType, int MaxLength, bool IsUnicode)> GetColumnShapeAsync(
        AppDbContext db,
        string tableName,
        string columnName)
    {
        var connection = db.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
            await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT DATA_TYPE, CHARACTER_MAXIMUM_LENGTH
            FROM INFORMATION_SCHEMA.COLUMNS
            WHERE TABLE_SCHEMA = 'dbo'
              AND TABLE_NAME = @tableName
              AND COLUMN_NAME = @columnName
            """;
        var tableParameter = command.CreateParameter();
        tableParameter.ParameterName = "@tableName";
        tableParameter.Value = tableName;
        command.Parameters.Add(tableParameter);

        var columnParameter = command.CreateParameter();
        columnParameter.ParameterName = "@columnName";
        columnParameter.Value = columnName;
        command.Parameters.Add(columnParameter);

        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync(), $"Column {tableName}.{columnName} was not found.");
        var dataType = reader.GetString(0);
        var maxLength = reader.IsDBNull(1) ? 0 : reader.GetInt32(1);
        return (dataType, maxLength, string.Equals(dataType, "nvarchar", StringComparison.OrdinalIgnoreCase));
    }

    private static async Task<bool> ColumnExistsAsync(AppDbContext db, string tableName, string columnName)
    {
        var connection = db.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
            await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT COUNT(1)
            FROM INFORMATION_SCHEMA.COLUMNS
            WHERE TABLE_SCHEMA = 'dbo'
              AND TABLE_NAME = @tableName
              AND COLUMN_NAME = @columnName
            """;
        var tableParameter = command.CreateParameter();
        tableParameter.ParameterName = "@tableName";
        tableParameter.Value = tableName;
        command.Parameters.Add(tableParameter);

        var columnParameter = command.CreateParameter();
        columnParameter.ParameterName = "@columnName";
        columnParameter.Value = columnName;
        command.Parameters.Add(columnParameter);

        return Convert.ToInt32(await command.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture) == 1;
    }

    private static async Task<bool> IsColumnNullableAsync(AppDbContext db, string tableName, string columnName)
    {
        var connection = db.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
            await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT IS_NULLABLE
            FROM INFORMATION_SCHEMA.COLUMNS
            WHERE TABLE_SCHEMA = 'dbo'
              AND TABLE_NAME = @tableName
              AND COLUMN_NAME = @columnName
            """;
        var tableParameter = command.CreateParameter();
        tableParameter.ParameterName = "@tableName";
        tableParameter.Value = tableName;
        command.Parameters.Add(tableParameter);

        var columnParameter = command.CreateParameter();
        columnParameter.ParameterName = "@columnName";
        columnParameter.Value = columnName;
        command.Parameters.Add(columnParameter);

        var result = await command.ExecuteScalarAsync();
        Assert.NotNull(result);
        return string.Equals(Convert.ToString(result, System.Globalization.CultureInfo.InvariantCulture), "YES", StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<bool> CheckConstraintExistsAsync(AppDbContext db, string tableName, string constraintName)
    {
        var connection = db.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
            await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT COUNT(1)
            FROM sys.check_constraints c
            INNER JOIN sys.tables t ON c.parent_object_id = t.object_id
            WHERE t.name = @tableName AND c.name = @constraintName
            """;
        var tableParameter = command.CreateParameter();
        tableParameter.ParameterName = "@tableName";
        tableParameter.Value = tableName;
        command.Parameters.Add(tableParameter);

        var constraintParameter = command.CreateParameter();
        constraintParameter.ParameterName = "@constraintName";
        constraintParameter.Value = constraintName;
        command.Parameters.Add(constraintParameter);

        return Convert.ToInt32(await command.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture) == 1;
    }

    private static async Task<bool> FilteredUniqueIndexExistsAsync(
        AppDbContext db,
        string tableName,
        string indexName,
        string expectedFilter)
    {
        var connection = db.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
            await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT i.is_unique, i.has_filter, i.filter_definition
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

        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
            return false;

        var isUnique = reader.GetBoolean(0);
        var hasFilter = reader.GetBoolean(1);
        var actualFilter = reader.GetString(2);
        var matches = isUnique
            && hasFilter
            && string.Equals(
                NormalizeFilterDefinition(actualFilter),
                NormalizeFilterDefinition(expectedFilter),
                StringComparison.OrdinalIgnoreCase);

        Assert.True(
            matches,
            $"""
            Filtered unique index assertion failed.
            Index: {indexName}
            Expected filter: {expectedFilter}
            Actual filter_definition: {actualFilter}
            is_unique: {isUnique}
            has_filter: {hasFilter}
            """);

        return true;
    }

    private static string NormalizeFilterDefinition(string definition)
    {
        var normalized = string.Join(
            " ",
            definition.Split(
                [' ', '\r', '\n', '\t'],
                StringSplitOptions.RemoveEmptyEntries));

        while (HasSingleOuterParentheses(normalized))
            normalized = normalized[1..^1].Trim();

        return normalized;
    }

    private static bool HasSingleOuterParentheses(string value)
    {
        if (value.Length < 2 || value[0] != '(' || value[^1] != ')')
            return false;

        var depth = 0;

        for (var index = 0; index < value.Length; index++)
        {
            if (value[index] == '(')
                depth++;
            else if (value[index] == ')')
                depth--;

            if (depth == 0 && index < value.Length - 1)
                return false;

            if (depth < 0)
                return false;
        }

        return depth == 0;
    }
}
