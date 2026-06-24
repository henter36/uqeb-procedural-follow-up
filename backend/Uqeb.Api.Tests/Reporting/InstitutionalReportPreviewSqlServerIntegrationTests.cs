using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
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
using Uqeb.Api.Reporting.Exporters;
using Uqeb.Api.Reporting.Operations;
using Uqeb.Api.Reporting.Services;
using Uqeb.Api.Services;
using Xunit;

namespace Uqeb.Api.Tests.Reporting;

public class InstitutionalReportPreviewSqlServerIntegrationTests
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

    [Fact]
    public async Task Preview_NoSequenceTable_Returns200()
    {
        if (!IsSqlServerAvailable())
            return;

        var context = await CreateServiceWithoutSequenceTableAsync();
        try
        {
            var manifest = await context.Service.RenderPreviewAsync(BuildRegressionRequest());

            Assert.True(manifest.Pages.Count > 0);
            Assert.StartsWith("PREVIEW-", manifest.ReportId);
        }
        finally
        {
            await SqlServerTestDatabaseHelper.DropDatabaseAsync(ConnectionString!, context.DatabaseName);
        }
    }

    [Fact]
    public async Task Preview_DoesNotAllocateOfficialNumber_OnSqlServer()
    {
        if (!IsSqlServerAvailable())
            return;

        var context = await CreateServiceWithoutSequenceTableAsync();
        try
        {
            await context.Service.RenderPreviewAsync(BuildRegressionRequest());

            await using var db = context.DbFactory.CreateDbContext();
            Assert.False(await TableExistsAsync(db));
        }
        finally
        {
            await SqlServerTestDatabaseHelper.DropDatabaseAsync(ConnectionString!, context.DatabaseName);
        }
    }

    [Fact]
    public async Task Preview_DoesNotCreateSequenceRow_OnSqlServer()
    {
        if (!IsSqlServerAvailable())
            return;

        var context = await CreateServiceWithSequenceTableAsync();
        try
        {
            await context.Service.RenderPreviewAsync(BuildRegressionRequest());

            await using var db = context.DbFactory.CreateDbContext();
            Assert.Empty(db.ReportNumberSequences);
        }
        finally
        {
            await SqlServerTestDatabaseHelper.DropDatabaseAsync(ConnectionString!, context.DatabaseName);
        }
    }

    [Fact]
    public async Task Preview_RegressionPayload_Returns200_OnSqlServer()
    {
        if (!IsSqlServerAvailable())
            return;

        var context = await CreateServiceWithSequenceTableAsync();
        try
        {
            var manifest = await context.Service.RenderPreviewAsync(BuildRegressionRequest());

            Assert.True(manifest.Pages.Count > 0);
            Assert.False(string.IsNullOrWhiteSpace(manifest.Stylesheet));
            Assert.StartsWith("PREVIEW-", manifest.ReportId);
        }
        finally
        {
            await SqlServerTestDatabaseHelper.DropDatabaseAsync(ConnectionString!, context.DatabaseName);
        }
    }

    [Fact]
    public async Task Export_MissingSequenceTable_ReturnsConfigurationError()
    {
        if (!IsSqlServerAvailable())
            return;

        var context = await CreateServiceWithoutSequenceTableAsync();
        try
        {
            var ex = await Assert.ThrowsAsync<ReportingConfigurationException>(() =>
                context.Service.ExportAsync(new ReportExportRequestDto
                {
                    ExportFormat = ExportFormat.Html,
                    BuildRequest = BuildRegressionRequest(),
                }));

            Assert.Equal(ReportingErrorCodes.ReportNumberSequenceSchemaMissing, ex.ErrorCode);
        }
        finally
        {
            await SqlServerTestDatabaseHelper.DropDatabaseAsync(ConnectionString!, context.DatabaseName);
        }
    }

    [Fact]
    public async Task Export_WithMigration_ArrangesOfficialNumber()
    {
        if (!IsSqlServerAvailable())
            return;

        var context = await CreateServiceWithSequenceTableAsync();
        try
        {
            var expectedYear = DateTime.UtcNow.Year;
            var result = await context.Service.ExportAsync(new ReportExportRequestDto
            {
                ExportFormat = ExportFormat.Html,
                BuildRequest = BuildRegressionRequest(),
            });

            Assert.StartsWith($"REP-{expectedYear}-", result.Manifest!.ReportId);

            await using var db = context.DbFactory.CreateDbContext();
            var sequence = Assert.Single(db.ReportNumberSequences);
            Assert.Equal(1, sequence.LastNumber);
        }
        finally
        {
            await SqlServerTestDatabaseHelper.DropDatabaseAsync(ConnectionString!, context.DatabaseName);
        }
    }

    [Fact]
    public async Task AllocateAsync_MissingSequenceTable_ThrowsConfigurationError()
    {
        if (!IsSqlServerAvailable())
            return;

        var context = await CreateServiceWithoutSequenceTableAsync();
        try
        {
            var allocator = new InstitutionalReportNumberAllocator(context.DbFactory);

            var ex = await Assert.ThrowsAsync<ReportingConfigurationException>(() => allocator.AllocateAsync());
            Assert.Equal(ReportingErrorCodes.ReportNumberSequenceSchemaMissing, ex.ErrorCode);
        }
        finally
        {
            await SqlServerTestDatabaseHelper.DropDatabaseAsync(ConnectionString!, context.DatabaseName);
        }
    }

    private sealed record SqlServerPreviewTestContext(
        string DatabaseName,
        IDbContextFactory<AppDbContext> DbFactory,
        InstitutionalReportService Service);

    private static async Task<SqlServerPreviewTestContext> CreateServiceWithoutSequenceTableAsync()
    {
        var databaseName = $"Uqeb_Preview_NoSeq_{Guid.NewGuid():N}";
        var builder = new SqlConnectionStringBuilder(ConnectionString!) { InitialCatalog = databaseName };
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(builder.ConnectionString)
            .Options;
        IDbContextFactory<AppDbContext> dbFactory = new TestDbContextFactory(options);

        await using (var db = dbFactory.CreateDbContext())
        {
            await db.Database.EnsureCreatedAsync();
            await db.Database.ExecuteSqlRawAsync("DROP TABLE IF EXISTS ReportNumberSequences");
            await SeedMinimalDataAsync(db);
        }

        return new SqlServerPreviewTestContext(databaseName, dbFactory, CreateReportingService(dbFactory));
    }

    private static async Task<SqlServerPreviewTestContext> CreateServiceWithSequenceTableAsync()
    {
        var databaseName = $"Uqeb_Preview_WithSeq_{Guid.NewGuid():N}";
        var builder = new SqlConnectionStringBuilder(ConnectionString!) { InitialCatalog = databaseName };
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(builder.ConnectionString)
            .Options;
        IDbContextFactory<AppDbContext> dbFactory = new TestDbContextFactory(options);

        await using (var db = dbFactory.CreateDbContext())
        {
            await db.Database.EnsureCreatedAsync();
            await SeedMinimalDataAsync(db);
        }

        return new SqlServerPreviewTestContext(databaseName, dbFactory, CreateReportingService(dbFactory));
    }

    private static InstitutionalReportService CreateReportingService(IDbContextFactory<AppDbContext> dbFactory)
    {
        var reportingOptions = Options.Create(new ReportingOptions
        {
            MaxPreviewDetailRows = 10_000,
            MaxPdfDetailRows = 10_000,
            MaxHtmlDetailRows = 10_000,
        });
        var metrics = new ReportingMetrics();
        var tempFileManager = new ReportingTempFileManager(
            reportingOptions,
            NullLogger<ReportingTempFileManager>.Instance,
            metrics);
        var concurrencyGate = new ReportingExportConcurrencyGate(reportingOptions);
        var resourceGuard = new ReportingExportResourceGuard(reportingOptions, tempFileManager);
        var audit = new NoOpAuditService();
        var currentUser = new TestCurrentUserService();
        var admission = new ReportingExportAdmissionService(
            concurrencyGate,
            resourceGuard,
            audit,
            currentUser,
            metrics);
        var lifecycle = new ReportingExportLifecycleObserver(
            audit,
            currentUser,
            metrics,
            NullLogger<ReportingExportLifecycleObserver>.Instance);
        var scopeFactory = new ReportingExportScopeFactory(
            concurrencyGate,
            tempFileManager,
            lifecycle,
            metrics);
        var correlationIdProvider = new ReportingCorrelationIdProvider(new Microsoft.AspNetCore.Http.HttpContextAccessor());
        var exportGuard = new ReportingExportGuard(
            admission,
            resourceGuard,
            scopeFactory,
            correlationIdProvider,
            concurrencyGate);
        var pdfExporter = new TestInstitutionalReportPdfExporter();
        InstitutionalReportService? serviceRef = null;
        serviceRef = new InstitutionalReportService(
            dbFactory,
            currentUser,
            new InstitutionalReportNumberAllocator(dbFactory),
            reportingOptions,
            NullLogger<InstitutionalReportService>.Instance,
            correlationIdProvider,
            () => new InstitutionalReportExportService(
                serviceRef!,
                pdfExporter,
                reportingOptions,
                exportGuard,
                metrics,
                NullLogger<InstitutionalReportExportService>.Instance,
                correlationIdProvider));

        return serviceRef;
    }

    private static async Task SeedMinimalDataAsync(AppDbContext db)
    {
        if (await db.Users.AnyAsync())
            return;

        db.Users.Add(new User
        {
            Username = "preview-sql",
            PasswordHash = "hash",
            FullName = "Preview SQL",
            Role = UserRole.Admin,
            IsActive = true,
        });
        db.Departments.Add(new Department { Name = "إدارة", IsActive = true });
        await db.SaveChangesAsync();
    }

    private static async Task<bool> TableExistsAsync(AppDbContext db) =>
        await db.Database.SqlQueryRaw<int>(
            "SELECT CASE WHEN OBJECT_ID(N'dbo.ReportNumberSequences', N'U') IS NOT NULL THEN 1 ELSE 0 END")
            .SingleAsync() == 1;

    private static ReportBuildRequestDto BuildRegressionRequest() => new()
    {
        ReportType = InstitutionalReportType.ExecutiveComprehensive,
        Title = "تقرير المتابعة الإجرائية للمعاملات",
        SectionIds =
        [
            ReportSectionId.Cover,
            ReportSectionId.ExecutiveSummary,
            ReportSectionId.IndicatorsDashboard,
            ReportSectionId.DepartmentPerformance,
            ReportSectionId.RisksAndAlerts,
            ReportSectionId.ExecutiveRecommendations,
            ReportSectionId.TransactionDetails,
            ReportSectionId.Appendices,
        ],
        Filters = new ReportFiltersDto
        {
            DateFrom = DateTime.UtcNow.Date.AddDays(-30),
            DateTo = DateTime.UtcNow.Date,
            IncludeJointDepartmentTransactions = true,
            IncludeOverdue = true,
            IncludeDetails = true,
            IncludeRisks = true,
            IncludeRecommendations = true,
        },
    };

    private sealed class TestCurrentUserService : ICurrentUserService
    {
        public bool IsAuthenticated => true;
        public int UserId => 1;
        public string Username => "preview-sql";
        public UserRole Role => UserRole.Admin;
        public int? DepartmentId => null;
    }

    private sealed class NoOpAuditService : IAuditService
    {
        public Task LogAsync(int userId, AuditAction action, string? entityName, int? entityId, int? transactionId, string? oldValue, string? newValue) =>
            Task.CompletedTask;
    }
}
