using Microsoft.EntityFrameworkCore;
using Uqeb.Api.Data;
using Uqeb.Api.Helpers;
using Uqeb.Api.Models.Entities;
using Uqeb.Api.Models.Enums;
using Uqeb.Api.Reporting.DTOs;
using Uqeb.Api.Reporting.Enums;
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

internal static class InstitutionalReportServiceTestHelpers
{
    internal static InstitutionalReportService CreateService(IDbContextFactory<AppDbContext>? dbFactory = null)
    {
        dbFactory ??= new TestDbContextFactory(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"export-validation-{Guid.NewGuid():N}")
            .Options);

        return new InstitutionalReportService(
            dbFactory,
            new TestCurrentUserService(),
            new NoOpAuditService(),
            new FixedReportNumberAllocator());
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
}
