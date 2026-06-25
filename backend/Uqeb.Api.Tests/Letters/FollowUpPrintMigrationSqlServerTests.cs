using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Uqeb.Api.Data;
using Xunit;

namespace Uqeb.Api.Tests.Letters;

public class FollowUpPrintMigrationSqlServerTests : FollowUpPrintSqlServerTestBase
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
        }
        finally
        {
            await CleanupAsync(context);
        }
    }

    [Fact]
    public async Task DownAdditivePayloadMigration_RemovesPayloadTable_ThenReUpRestoresIt()
    {
        if (!ShouldRunSqlServerTest())
            return;

        var databaseName = $"UqebFollowUpPrint_Migration_{Guid.NewGuid():N}";
        var connectionString = await CreateDatabaseAsync(databaseName);

        var services = new ServiceCollection();
        services.AddDbContext<AppDbContext>(o => o.UseSqlServer(connectionString));
        await using var provider = services.BuildServiceProvider();

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

        await provider.DisposeAsync();
        await SqlServerTestDatabaseHelper.DropDatabaseAsync(ConnectionString!, databaseName);
    }
}
