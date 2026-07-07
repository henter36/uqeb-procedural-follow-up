using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Uqeb.Api.Data;
using Uqeb.Api.Models.Entities;
using Uqeb.Api.Models.Enums;
using Uqeb.Api.Reporting.Configuration;
using Uqeb.Api.Reporting.DTOs;
using Uqeb.Api.Reporting.Enums;
using Uqeb.Api.Reporting.Operations;
using Uqeb.Api.Reporting.Services;
using Uqeb.Api.Services;
using Xunit;

namespace Uqeb.Api.Tests.Reporting;

// GET /api/institutional-reports/templates previously threw against a real SQL
// Server database whose schema predated the AddDepartmentTransactionsReportTemplateOptions
// migration (missing DetailSortBy/GroupDetailsByDepartment columns on
// ReportExportTemplates). The EF InMemory-backed tests for this service never
// caught it because InMemory ignores relational schema/migrations entirely. These
// tests apply the full migration history (Database.MigrateAsync, not
// EnsureCreatedAsync) to a fresh database so a future model/migration drift on
// ReportExportTemplate would fail here instead of only in production.
[Trait("Category", "SqlServer")]
public class InstitutionalReportTemplatesSqlServerIntegrationTests
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

    private static async Task<(string DatabaseName, DbContextOptions<AppDbContext> Options)> CreateMigratedSqlDbAsync()
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

    private static InstitutionalReportService CreateService(IDbContextFactory<AppDbContext> dbFactory)
    {
        var reportingOptions = Options.Create(new ReportingOptions
        {
            MaxPreviewDetailRows = 10_000,
            MaxPdfDetailRows = 10_000,
            MaxHtmlDetailRows = 10_000,
        });
        var correlationIdProvider = new ReportingCorrelationIdProvider(new Microsoft.AspNetCore.Http.HttpContextAccessor());
        var analysisInstrumentation = new ReportingAnalysisInstrumentation(
            new ReportingMetrics(),
            NullLogger<ReportingAnalysisInstrumentation>.Instance);

        return new InstitutionalReportService(
            dbFactory,
            new TestCurrentUserService(),
            new InstitutionalReportNumberAllocator(dbFactory),
            reportingOptions,
            NullLogger<InstitutionalReportService>.Instance,
            correlationIdProvider,
            analysisInstrumentation,
            () => throw new NotSupportedException("Export is not exercised by these tests."));
    }

    [Fact]
    public async Task GetTemplatesAsync_ReturnsEmptyList_OnFreshlyMigratedDatabaseWithNoTemplates()
    {
        if (!IsSqlServerAvailable())
        {
            Assert.False(IsRequired, "SQL Server institutional report template tests are required but the database is unavailable.");
            return;
        }

        var (databaseName, options) = await CreateMigratedSqlDbAsync();
        try
        {
            var service = CreateService(new TestDbContextFactory(options));

            var templates = await service.GetTemplatesAsync();

            Assert.Empty(templates);
        }
        finally
        {
            await SqlServerTestDatabaseHelper.DropDatabaseAsync(ConnectionString!, databaseName);
        }
    }

    [Fact]
    public async Task GetTemplatesAsync_ReturnsSeededTemplate_OnFreshlyMigratedDatabase()
    {
        if (!IsSqlServerAvailable())
        {
            Assert.False(IsRequired, "SQL Server institutional report template tests are required but the database is unavailable.");
            return;
        }

        var (databaseName, options) = await CreateMigratedSqlDbAsync();
        try
        {
            await using (var db = new AppDbContext(options))
            {
                var user = new User
                {
                    Username = "admin",
                    PasswordHash = "hash",
                    FullName = "Admin",
                    Role = UserRole.Admin,
                    IsActive = true,
                };
                db.Users.Add(user);
                await db.SaveChangesAsync();

                db.ReportExportTemplates.Add(new ReportExportTemplate
                {
                    Name = "قالب اختبار",
                    ReportType = InstitutionalReportType.ExecutiveComprehensive,
                    SectionIdsJson = System.Text.Json.JsonSerializer.Serialize(new[] { ReportSectionId.Cover }),
                    DefaultFiltersJson = System.Text.Json.JsonSerializer.Serialize(new ReportFiltersDto()),
                    DefaultFormat = ExportFormat.Pdf,
                    PageNumberingMode = PageNumberingMode.Restart,
                    DetailSortBy = ReportDetailSortBy.Default,
                    GroupDetailsByDepartment = true,
                    CreatedById = user.Id,
                    CreatedAt = DateTime.UtcNow,
                });
                await db.SaveChangesAsync();
            }

            var service = CreateService(new TestDbContextFactory(options));

            var templates = await service.GetTemplatesAsync();

            var template = Assert.Single(templates);
            Assert.Equal("قالب اختبار", template.Name);
            Assert.True(template.GroupDetailsByDepartment);
        }
        finally
        {
            await SqlServerTestDatabaseHelper.DropDatabaseAsync(ConnectionString!, databaseName);
        }
    }

    private sealed class TestCurrentUserService : ICurrentUserService
    {
        public bool IsAuthenticated => true;
        public int UserId => 1;
        public string Username => "tester";
        public UserRole Role => UserRole.Admin;
        public int? DepartmentId => null;
    }
}
