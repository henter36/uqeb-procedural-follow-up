using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Uqeb.Api.Data;
using Uqeb.Api.Helpers;
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

public class InstitutionalReportServiceTemplatesTests
{
    [Fact]
    public async Task GetTemplatesAsync_MapsSavedTemplateJsonInMemory()
    {
        var dbName = $"templates-{Guid.NewGuid():N}";
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;
        IDbContextFactory<AppDbContext> dbFactory = new TestDbContextFactory(options);

        await using (var db = dbFactory.CreateDbContext())
        {
            db.ReportExportTemplates.Add(new ReportExportTemplate
            {
                Name = "قالب اختبار",
                ReportType = InstitutionalReportType.ExecutiveComprehensive,
                SectionIdsJson = System.Text.Json.JsonSerializer.Serialize(new[] { ReportSectionId.Cover, ReportSectionId.ExecutiveSummary }),
                DefaultFiltersJson = System.Text.Json.JsonSerializer.Serialize(new ReportFiltersDto { DepartmentIds = [10, 20] }),
                DefaultFormat = ExportFormat.Pdf,
                PageNumberingMode = PageNumberingMode.Restart,
                IncludePartialCover = true,
                IncludePartialManifest = false,
                CreatedAt = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        var service = InstitutionalReportServiceTestHelpers.CreateService(dbFactory);
        var templates = await service.GetTemplatesAsync();

        var template = Assert.Single(templates);
        Assert.Equal("قالب اختبار", template.Name);
        Assert.Equal([ReportSectionId.Cover, ReportSectionId.ExecutiveSummary], template.SectionIds);
        Assert.Equal([10, 20], template.DefaultFilters.DepartmentIds);
    }
}

public class InstitutionalReportServiceExportValidationTests
{
    [Fact]
    public async Task ExportAsync_ThrowsValidationProblem_ForInvalidPageRange()
    {
        var service = InstitutionalReportServiceTestHelpers.CreateService();
        var request = new ReportExportRequestDto
        {
            ExportMode = ExportMode.SelectedPages,
            PageRangeExpression = "abc",
            BuildRequest = new ReportBuildRequestDto
            {
                ReportType = InstitutionalReportType.ExecutiveComprehensive,
                SectionIds = [ReportSectionId.Cover],
            },
        };

        var ex = await Assert.ThrowsAsync<FieldValidationException>(() => service.ExportAsync(request));
        Assert.Contains("pageRangeExpression", ex.FieldErrors.Keys);
    }

    [Fact]
    public async Task ExportAsync_ThrowsValidationProblem_WhenSelectedPagesResolveEmpty()
    {
        var service = InstitutionalReportServiceTestHelpers.CreateService();
        var request = new ReportExportRequestDto
        {
            ExportMode = ExportMode.SelectedPages,
            SelectedPageNumbers = [],
            BuildRequest = new ReportBuildRequestDto
            {
                ReportType = InstitutionalReportType.ExecutiveComprehensive,
                SectionIds = [ReportSectionId.Cover],
            },
        };

        var ex = await Assert.ThrowsAsync<FieldValidationException>(() => service.ExportAsync(request));
        Assert.Contains("selectedPages", ex.FieldErrors.Keys);
    }
}

public class InstitutionalReportServiceBuildValidationTests
{
    [Fact]
    public async Task RenderPreviewAsync_ThrowsValidationProblem_WhenSectionIdsEmpty()
    {
        var service = InstitutionalReportServiceTestHelpers.CreateService();

        var ex = await Assert.ThrowsAsync<FieldValidationException>(() => service.RenderPreviewAsync(new ReportBuildRequestDto
        {
            ReportType = InstitutionalReportType.ExecutiveComprehensive,
            SectionIds = [],
        }));

        Assert.Contains("sectionIds", ex.FieldErrors.Keys);
    }

    [Fact]
    public async Task BuildReportModelAsync_ThrowsValidationProblem_WhenSectionIdsEmpty()
    {
        var service = InstitutionalReportServiceTestHelpers.CreateService();

        var ex = await Assert.ThrowsAsync<FieldValidationException>(() => service.BuildReportModelAsync(new ReportBuildRequestDto
        {
            ReportType = InstitutionalReportType.ExecutiveComprehensive,
            SectionIds = [],
        }));

        Assert.Contains("sectionIds", ex.FieldErrors.Keys);
    }

    [Fact]
    public async Task ExportAsync_ThrowsValidationProblem_WhenSectionIdsEmpty()
    {
        var service = InstitutionalReportServiceTestHelpers.CreateService();

        var ex = await Assert.ThrowsAsync<FieldValidationException>(() => service.ExportAsync(new ReportExportRequestDto
        {
            ExportFormat = ExportFormat.Html,
            BuildRequest = new ReportBuildRequestDto
            {
                ReportType = InstitutionalReportType.ExecutiveComprehensive,
                SectionIds = [],
            },
        }));

        Assert.Contains("sectionIds", ex.FieldErrors.Keys);
    }

    [Fact]
    public async Task ExportAsync_CurrentPageWithoutNumber_ThrowsValidationProblem()
    {
        var service = InstitutionalReportServiceTestHelpers.CreateService();

        var ex = await Assert.ThrowsAsync<FieldValidationException>(() => service.ExportAsync(new ReportExportRequestDto
        {
            ExportFormat = ExportFormat.Html,
            ExportMode = ExportMode.CurrentPage,
            BuildRequest = new ReportBuildRequestDto
            {
                ReportType = InstitutionalReportType.ExecutiveComprehensive,
                SectionIds = [ReportSectionId.Cover],
            },
        }));

        Assert.Equal("يجب تحديد الصفحة الحالية للتصدير.", ex.FieldErrors["selectedPages"]);
    }

    [Fact]
    public async Task RenderPreviewAsync_ThrowsValidationProblem_WhenDateRangeInvalid()
    {
        var service = InstitutionalReportServiceTestHelpers.CreateService();
        var today = DateTime.UtcNow.Date;

        var ex = await Assert.ThrowsAsync<FieldValidationException>(() => service.RenderPreviewAsync(new ReportBuildRequestDto
        {
            ReportType = InstitutionalReportType.ExecutiveComprehensive,
            SectionIds = [ReportSectionId.Cover],
            Filters = new ReportFiltersDto
            {
                DateFrom = today,
                DateTo = today.AddDays(-1),
            },
        }));

        Assert.Contains("filters.dateFrom", ex.FieldErrors.Keys);
        Assert.Contains("filters.dateTo", ex.FieldErrors.Keys);
    }

    [Fact]
    public async Task BuildReportModelAsync_ThrowsValidationProblem_WhenFiltersNull()
    {
        var service = InstitutionalReportServiceTestHelpers.CreateService();

        var ex = await Assert.ThrowsAsync<FieldValidationException>(() => service.BuildReportModelAsync(new ReportBuildRequestDto
        {
            ReportType = InstitutionalReportType.ExecutiveComprehensive,
            SectionIds = [ReportSectionId.Cover],
            Filters = null!,
        }));

        Assert.Contains("filters", ex.FieldErrors.Keys);
    }
}

internal static class InstitutionalReportServiceTestHelpers
{
    internal static InstitutionalReportService CreateService(
        IDbContextFactory<AppDbContext>? dbFactory = null,
        ReportingOptions? reportingOptions = null,
        IAuditService? audit = null,
        IInstitutionalReportNumberAllocator? reportNumberAllocator = null,
        IInstitutionalReportPdfExporter? pdfExporter = null)
    {
        dbFactory ??= new TestDbContextFactory(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"export-validation-{Guid.NewGuid():N}")
            .Options);

        var options = Options.Create(reportingOptions ?? new ReportingOptions { MaxPdfDetailRows = 10_000 });
        var exportGuard = CreateExportGuard(reportingOptions, audit);
        var metrics = new ReportingMetrics();
        var correlationIdProvider = new ReportingCorrelationIdProvider(new HttpContextAccessor());
        var pdf = pdfExporter ?? new StubPdfExporter();
        InstitutionalReportService? serviceRef = null;
        serviceRef = new InstitutionalReportService(
            dbFactory,
            new TestCurrentUserService(),
            reportNumberAllocator ?? new FixedReportNumberAllocator(),
            options,
            NullLogger<InstitutionalReportService>.Instance,
            correlationIdProvider,
            () => new InstitutionalReportExportService(
                serviceRef!,
                pdf,
                options,
                exportGuard,
                metrics,
                NullLogger<InstitutionalReportExportService>.Instance,
                correlationIdProvider));

        return serviceRef;
    }

    private static IReportingExportGuard CreateExportGuard(ReportingOptions? reportingOptions, IAuditService? audit = null)
    {
        var options = Options.Create(reportingOptions ?? new ReportingOptions { MaxPdfDetailRows = 10_000 });
        var metrics = new ReportingMetrics();
        var tempFileManager = new ReportingTempFileManager(
            options,
            NullLogger<ReportingTempFileManager>.Instance,
            metrics);
        var concurrencyGate = new ReportingExportConcurrencyGate(options);
        var resourceGuard = new ReportingExportResourceGuard(options, tempFileManager);
        var auditService = audit ?? new NoOpAuditService();
        var currentUser = new TestCurrentUserService();
        var admission = new ReportingExportAdmissionService(
            concurrencyGate,
            resourceGuard,
            auditService,
            currentUser,
            metrics);
        var lifecycle = new ReportingExportLifecycleObserver(
            auditService,
            currentUser,
            metrics,
            NullLogger<ReportingExportLifecycleObserver>.Instance);
        var scopeFactory = new ReportingExportScopeFactory(
            concurrencyGate,
            tempFileManager,
            lifecycle,
            metrics);
        var correlationIdProvider = new ReportingCorrelationIdProvider(new HttpContextAccessor());
        return new ReportingExportGuard(admission, resourceGuard, scopeFactory, correlationIdProvider, concurrencyGate);
    }

    private sealed class TestCurrentUserService : ICurrentUserService
    {
        public bool IsAuthenticated => true;
        public int UserId => 1;
        public string Username => "test";
        public UserRole Role => UserRole.Admin;
        public int? DepartmentId => null;
    }

    private sealed class NoOpAuditService : IAuditService
    {
        public Task LogAsync(int userId, AuditAction action, string? entityName, int? entityId, int? transactionId, string? oldValue, string? newValue) =>
            Task.CompletedTask;
    }

    private sealed class FixedReportNumberAllocator : IInstitutionalReportNumberAllocator
    {
        public Task<string> AllocateAsync(CancellationToken ct = default) =>
            Task.FromResult("REP-2026-000001");
    }

    private sealed class StubPdfExporter : IInstitutionalReportPdfExporter
    {
        public Task<byte[]> ExportAsync(RenderedReportManifestDto manifest, string htmlDocument, CancellationToken ct = default) =>
            Task.FromResult("%PDF-1.4"u8.ToArray());
    }
}
