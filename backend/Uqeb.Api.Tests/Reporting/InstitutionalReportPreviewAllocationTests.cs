using Microsoft.EntityFrameworkCore;
using Uqeb.Api.Data;
using Uqeb.Api.Reporting.DTOs;
using Uqeb.Api.Reporting.Enums;
using Uqeb.Api.Reporting.Services;
using Xunit;

namespace Uqeb.Api.Tests.Reporting;

public class InstitutionalReportPreviewAllocationTests
{
    [Fact]
    public async Task Preview_DoesNotCallAllocator()
    {
        var tracking = new TrackingInstitutionalReportNumberAllocator();
        var service = InstitutionalReportServiceTestHelpers.CreateService(reportNumberAllocator: tracking);

        await service.RenderPreviewAsync(new ReportBuildRequestDto
        {
            ReportType = InstitutionalReportType.ExecutiveComprehensive,
            SectionIds = [ReportSectionId.Cover, ReportSectionId.ExecutiveSummary],
        });

        Assert.Equal(0, tracking.AllocateCallCount);
    }

    [Fact]
    public async Task Preview_UsesTemporaryReportNumber()
    {
        var service = InstitutionalReportServiceTestHelpers.CreateService();

        var manifest = await service.RenderPreviewAsync(new ReportBuildRequestDto
        {
            ReportType = InstitutionalReportType.ExecutiveComprehensive,
            SectionIds = [ReportSectionId.Cover],
        });

        Assert.StartsWith("PREVIEW-", manifest.ReportId);
    }

    [Fact]
    public async Task Preview_DoesNotCreateSequenceRow()
    {
        var dbName = $"preview-sequence-{Guid.NewGuid():N}";
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;
        IDbContextFactory<AppDbContext> dbFactory = new TestDbContextFactory(options);
        var allocator = new InstitutionalReportNumberAllocator(dbFactory);
        var service = InstitutionalReportServiceTestHelpers.CreateService(
            dbFactory,
            reportNumberAllocator: allocator);

        await service.RenderPreviewAsync(new ReportBuildRequestDto
        {
            ReportType = InstitutionalReportType.ExecutiveComprehensive,
            SectionIds = [ReportSectionId.Cover],
        });

        await using var db = dbFactory.CreateDbContext();
        Assert.Empty(db.ReportNumberSequences);
    }

    [Fact]
    public async Task Preview_EmptyDatabase_Returns200()
    {
        var service = InstitutionalReportServiceTestHelpers.CreateService();

        var manifest = await service.RenderPreviewAsync(new ReportBuildRequestDto
        {
            ReportType = InstitutionalReportType.ExecutiveComprehensive,
            SectionIds = [ReportSectionId.Cover, ReportSectionId.ExecutiveSummary],
            Filters = new ReportFiltersDto
            {
                DateFrom = DateTime.UtcNow.Date.AddYears(10),
                DateTo = DateTime.UtcNow.Date.AddYears(11),
            },
        });

        Assert.True(manifest.Pages.Count > 0);
        Assert.Equal(0, manifest.TotalMatchedRows);
    }

    [Fact]
    public async Task Export_CallsAllocator()
    {
        var dbFactory = await InstitutionalReportPreviewAllocationTestData.CreateSeededFactoryAsync();
        var tracking = new TrackingInstitutionalReportNumberAllocator();
        var service = InstitutionalReportServiceTestHelpers.CreateService(
            dbFactory,
            reportNumberAllocator: tracking);

        await service.ExportAsync(new ReportExportRequestDto
        {
            ExportFormat = ExportFormat.Html,
            BuildRequest = new ReportBuildRequestDto
            {
                ReportType = InstitutionalReportType.ExecutiveComprehensive,
                SectionIds = [ReportSectionId.Cover],
            },
        });

        Assert.Equal(1, tracking.AllocateCallCount);
    }

    [Fact]
    public async Task Export_IncrementsSequenceOnce_OnInMemoryProvider()
    {
        var dbName = $"export-sequence-{Guid.NewGuid():N}";
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;
        IDbContextFactory<AppDbContext> dbFactory = new TestDbContextFactory(options);
        var allocator = new InstitutionalReportNumberAllocator(dbFactory);
        var service = InstitutionalReportServiceTestHelpers.CreateService(
            dbFactory,
            reportNumberAllocator: allocator);

        await service.ExportAsync(new ReportExportRequestDto
        {
            ExportFormat = ExportFormat.Html,
            BuildRequest = new ReportBuildRequestDto
            {
                ReportType = InstitutionalReportType.ExecutiveComprehensive,
                SectionIds = [ReportSectionId.Cover],
            },
        });

        await using var db = dbFactory.CreateDbContext();
        var year = DateTime.UtcNow.Year;
        var sequence = Assert.Single(db.ReportNumberSequences);
        Assert.Equal(year, sequence.Year);
        Assert.Equal(1, sequence.LastNumber);
    }
}

internal static class InstitutionalReportPreviewAllocationTestData
{
    internal static async Task<IDbContextFactory<AppDbContext>> CreateSeededFactoryAsync()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"preview-export-{Guid.NewGuid():N}")
            .Options;
        IDbContextFactory<AppDbContext> dbFactory = new TestDbContextFactory(options);

        await using var db = dbFactory.CreateDbContext();
        db.Departments.Add(new Uqeb.Api.Models.Entities.Department { Name = "إدارة", IsActive = true });
        await db.SaveChangesAsync();

        return dbFactory;
    }
}
