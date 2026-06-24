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

public class InstitutionalReportReadOnlySqlServerIntegrationTests
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
    public async Task BuildPreviewAndExport_DoNotMutateBusinessData_OnSqlServer()
    {
        if (!IsSqlServerAvailable())
            return;

        var databaseName = $"Uqeb_ReadOnly_{Guid.NewGuid():N}";
        var builder = new SqlConnectionStringBuilder(ConnectionString!) { InitialCatalog = databaseName };
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(builder.ConnectionString)
            .Options;
        IDbContextFactory<AppDbContext> dbFactory = new TestDbContextFactory(options);

        await using (var db = dbFactory.CreateDbContext())
        {
            await db.Database.EnsureCreatedAsync();
            await SeedScenarioAsync(db);
        }

        var service = CreateReportingService(dbFactory);
        var buildRequest = BuildAnalyticalRequest();

        BusinessSnapshot before;
        await using (var db = dbFactory.CreateDbContext())
        {
            before = await CaptureBusinessSnapshotAsync(db);
        }

        await service.BuildReportModelAsync(buildRequest);
        await service.RenderPreviewAsync(buildRequest);
        await service.ExportAsync(new ReportExportRequestDto
        {
            ExportFormat = ExportFormat.Html,
            BuildRequest = buildRequest,
        });

        BusinessSnapshot after;
        await using (var db = dbFactory.CreateDbContext())
        {
            after = await CaptureBusinessSnapshotAsync(db);
        }

        Assert.Equal(before, after);

        try
        {
            await SqlServerTestDatabaseHelper.DropDatabaseAsync(ConnectionString!, databaseName);
        }
        catch
        {
            // Best-effort cleanup for local runs.
        }
    }

    private sealed record BusinessSnapshot(
        TransactionStatus Status,
        ReplyStatus ReplyStatus,
        DateTime? TransactionUpdatedAt,
        DateTime AssignmentCreatedAt,
        int FollowUpCount)
    {
        public static async Task<BusinessSnapshot> CaptureAsync(AppDbContext db)
        {
            var transaction = await db.Transactions
                .Include(t => t.Assignments)
                .Include(t => t.FollowUps)
                .SingleAsync();

            var assignment = Assert.Single(transaction.Assignments);
            return new BusinessSnapshot(
                transaction.Status,
                assignment.ReplyStatus,
                transaction.UpdatedAt,
                assignment.CreatedAt,
                transaction.FollowUps.Count);
        }
    }

    private static async Task<BusinessSnapshot> CaptureBusinessSnapshotAsync(AppDbContext db) =>
        await BusinessSnapshot.CaptureAsync(db);

    private static async Task SeedScenarioAsync(AppDbContext db)
    {
        var updatedAt = new DateTime(2026, 6, 1, 10, 0, 0, DateTimeKind.Utc);
        var user = new User
        {
            Username = "readonly-sql",
            PasswordHash = "hash",
            FullName = "Readonly SQL",
            Role = UserRole.Admin,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
        };
        var department = new Department
        {
            Name = "الشؤون الإدارية",
            NameNormalized = "الشؤون الإدارية",
            Code = "ADMIN",
            IsActive = true,
        };
        db.Users.Add(user);
        db.Departments.Add(department);
        await db.SaveChangesAsync();

        var transaction = new Transaction
        {
            InternalTrackingNumber = "INT-RO-001",
            IncomingNumber = "IN-RO-001",
            IncomingDate = DateTime.UtcNow.Date.AddDays(-15),
            Subject = "معاملة read-only",
            IncomingFrom = "جهة اختبار",
            Status = TransactionStatus.New,
            Priority = Priority.Urgent,
            RequiresResponse = true,
            ResponseDueDate = DateTime.UtcNow.Date.AddDays(-2),
            CreatedById = user.Id,
            CreatedAt = DateTime.UtcNow.AddDays(-15),
            UpdatedAt = updatedAt,
            Assignments =
            [
                new Assignment
                {
                    DepartmentId = department.Id,
                    AssignedDate = DateTime.UtcNow.Date.AddDays(-10),
                    RequiresReply = true,
                    DueDate = DateTime.UtcNow.Date.AddDays(-2),
                    ReplyStatus = ReplyStatus.Pending,
                    Status = AssignmentStatus.Active,
                    CreatedById = user.Id,
                    CreatedAt = DateTime.UtcNow.AddDays(-10),
                }
            ],
            FollowUps =
            [
                new FollowUp
                {
                    FollowUpDate = DateTime.UtcNow.Date.AddDays(-5),
                    Notes = "متابعة اختبار",
                    CreatedById = user.Id,
                    CreatedAt = DateTime.UtcNow.AddDays(-5),
                }
            ],
        };
        db.Transactions.Add(transaction);
        await db.SaveChangesAsync();
    }

    private static ReportBuildRequestDto BuildAnalyticalRequest() => new()
    {
        ReportType = InstitutionalReportType.ExecutiveComprehensive,
        SectionIds =
        [
            ReportSectionId.ExecutiveSummary,
            ReportSectionId.KeyPerformanceIndicators,
            ReportSectionId.SignificantFindings,
            ReportSectionId.CriticalCases,
            ReportSectionId.TransactionDetails,
        ],
        IncludeComparison = true,
        ComparisonMode = ReportComparisonMode.PreviousEquivalentPeriod,
        Filters = new ReportFiltersDto
        {
            DateFrom = DateTime.UtcNow.Date.AddDays(-30),
            DateTo = DateTime.UtcNow.Date,
            IncludeDetails = true,
        },
    };

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
        var pdfExporter = new StubPdfExporter();
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

    private sealed class TestCurrentUserService : ICurrentUserService
    {
        public bool IsAuthenticated => true;
        public int UserId => 1;
        public string Username => "readonly-sql";
        public UserRole Role => UserRole.Admin;
        public int? DepartmentId => null;
    }

    private sealed class NoOpAuditService : IAuditService
    {
        public Task LogAsync(int userId, AuditAction action, string? entityName, int? entityId, int? transactionId, string? oldValue, string? newValue) =>
            Task.CompletedTask;
    }

    private sealed class StubPdfExporter : IInstitutionalReportPdfExporter
    {
        public Task<byte[]> ExportAsync(RenderedReportManifestDto manifest, string htmlDocument, CancellationToken ct = default) =>
            Task.FromResult("%PDF-1.4"u8.ToArray());
    }
}
