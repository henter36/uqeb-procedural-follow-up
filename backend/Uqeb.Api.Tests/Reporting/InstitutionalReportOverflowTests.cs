using Microsoft.EntityFrameworkCore;
using Uqeb.Api.Data;
using Uqeb.Api.Helpers;
using Uqeb.Api.Models.Entities;
using Uqeb.Api.Models.Enums;
using Uqeb.Api.Reporting.Configuration;
using Uqeb.Api.Reporting.DTOs;
using Uqeb.Api.Reporting.Enums;
using Uqeb.Api.Reporting.Services;
using Uqeb.Api.Services;
using Xunit;

namespace Uqeb.Api.Tests.Reporting;

public class InstitutionalReportOverflowTests
{
    private const int DetailLimit = 2;

    private static ReportingOptions SmallDetailLimits => new()
    {
        MaxPreviewDetailRows = DetailLimit,
        MaxPdfDetailRows = DetailLimit,
        MaxPdfDetailRowsPerPart = DetailLimit,
    };

    [Fact]
    public async Task ExportAsync_ThrowsValidationProblem_WhenOverflowWithoutAction()
    {
        var dbFactory = await CreateSeededFactoryAsync(transactionCount: 5);
        var service = InstitutionalReportServiceTestHelpers.CreateService(
            dbFactory,
            new ReportingOptions { MaxPreviewDetailRows = DetailLimit, MaxPdfDetailRows = DetailLimit, MaxPdfDetailRowsPerPart = DetailLimit });

        var request = CreateExportRequest(DetailOverflowAction.None);

        var ex = await Assert.ThrowsAsync<FieldValidationException>(() => service.ExportAsync(request));
        Assert.Contains("detailOverflowAction", ex.FieldErrors.Keys);
        Assert.Equal("5", ex.FieldErrors["totalMatchingTransactions"]);
        Assert.Equal("2", ex.FieldErrors["detailRowLimit"]);
    }

    [Fact]
    public async Task RenderPreviewAsync_FlagsOverflowRequirement()
    {
        var dbFactory = await CreateSeededFactoryAsync(transactionCount: 5);
        var service = InstitutionalReportServiceTestHelpers.CreateService(
            dbFactory,
            new ReportingOptions { MaxPreviewDetailRows = DetailLimit, MaxPdfDetailRows = DetailLimit, MaxPdfDetailRowsPerPart = DetailLimit });

        var manifest = await service.RenderPreviewAsync(new ReportBuildRequestDto
        {
            ReportType = InstitutionalReportType.ExecutiveComprehensive,
            SectionIds = [ReportSectionId.Cover, ReportSectionId.TransactionDetails],
        });

        Assert.True(manifest.RequiresDetailOverflowAction);
        Assert.Equal(5, manifest.TotalMatchingTransactions);
        Assert.Equal(DetailLimit, manifest.DetailRowLimit);
        Assert.Equal(DetailLimit, manifest.IncludedTransactionCount);
    }

    [Fact]
    public async Task RenderPreviewAsync_IncludeOverdue_UsesFilteredCountForOverflow()
    {
        var dbFactory = await CreateSeededFactoryAsync(transactionCount: 5, overdueCount: 1);
        var service = InstitutionalReportServiceTestHelpers.CreateService(
            dbFactory,
            new ReportingOptions { MaxPreviewDetailRows = DetailLimit, MaxPdfDetailRows = DetailLimit, MaxPdfDetailRowsPerPart = DetailLimit });

        var manifest = await service.RenderPreviewAsync(new ReportBuildRequestDto
        {
            ReportType = InstitutionalReportType.ExecutiveComprehensive,
            SectionIds = [ReportSectionId.Cover, ReportSectionId.TransactionDetails],
            Filters = new ReportFiltersDto
            {
                IncludeOverdue = true,
            },
        });

        Assert.False(manifest.RequiresDetailOverflowAction);
        Assert.Equal(1, manifest.TotalMatchingTransactions);
        Assert.Equal(1, manifest.IncludedTransactionCount);
        Assert.Equal(1, manifest.LoadedDetailRows);
    }

    [Fact]
    public async Task ExportAsync_IncludeOverdue_DoesNotRequireOverflowActionForFilteredRowsWithinLimit()
    {
        var dbFactory = await CreateSeededFactoryAsync(transactionCount: 5, overdueCount: 1);
        var service = InstitutionalReportServiceTestHelpers.CreateService(
            dbFactory,
            SmallDetailLimits);

        var request = CreateExportRequest(DetailOverflowAction.None);
        request.ExportFormat = ExportFormat.Html;
        request.BuildRequest.Filters.IncludeOverdue = true;

        var result = await service.ExportAsync(request);

        Assert.Equal("text/html; charset=utf-8", result.ContentType);
        Assert.Equal(1, result.Manifest.TotalMatchedRows);
        Assert.Equal(1, result.Manifest.ExportedDetailRows);
        Assert.False(result.Manifest.DetailRowsTruncated);
    }

    [Fact]
    public async Task ExportAsync_SummaryOnly_ProducesPdfWithoutSilentTruncation()
    {
        var dbFactory = await CreateSeededFactoryAsync(transactionCount: 5);
        var audit = new CapturingAuditService();
        var service = InstitutionalReportServiceTestHelpers.CreateService(
            dbFactory,
            SmallDetailLimits,
            audit);

        var result = await service.ExportAsync(CreateExportRequest(DetailOverflowAction.SummaryOnly));

        Assert.Equal("application/pdf", result.ContentType);
        Assert.Contains("institutional_report.export_completed", audit.LastNewValue);
        Assert.Contains("rows=0", audit.LastNewValue);
        Assert.DoesNotContain(ReportSectionId.TransactionDetails, result.Manifest.Pages.Select(p => p.SectionId));
    }

    [Fact]
    public async Task ExportAsync_SplitPdf_ProducesZipWithSummaryAndDetailParts()
    {
        var dbFactory = await CreateSeededFactoryAsync(transactionCount: 5);
        var audit = new CapturingAuditService();
        var service = InstitutionalReportServiceTestHelpers.CreateService(
            dbFactory,
            SmallDetailLimits,
            audit);

        var result = await service.ExportAsync(CreateExportRequest(DetailOverflowAction.SplitPdf));

        Assert.Equal("application/zip", result.ContentType);
        Assert.EndsWith("-SPLIT.zip", result.FileName);
        Assert.Contains("institutional_report.export_completed", audit.LastNewValue);
        Assert.Contains("rows=5", audit.LastNewValue);
        Assert.Equal(DetailOverflowAction.SplitPdf, result.Manifest.OverflowAction);
    }

    [Fact]
    public async Task ExportAsync_FullDetailsXlsx_ExportsAllRows()
    {
        var dbFactory = await CreateSeededFactoryAsync(transactionCount: 5);
        var audit = new CapturingAuditService();
        var service = InstitutionalReportServiceTestHelpers.CreateService(
            dbFactory,
            SmallDetailLimits,
            audit);

        var result = await service.ExportAsync(CreateExportRequest(DetailOverflowAction.FullDetailsXlsx));

        Assert.Equal("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", result.ContentType);
        Assert.Contains("institutional_report.export_completed", audit.LastNewValue);
        Assert.Equal(5, result.Manifest.TotalMatchedRows);
        Assert.Equal(5, result.Manifest.ExportedDetailRows);
        Assert.False(result.Manifest.DetailRowsTruncated);
    }

    [Fact]
    public async Task ExportAsync_FullDetailsXlsx_ExportsAllRows_WhenCountExceedsPdfLimit()
    {
        const int transactionCount = 501;
        var dbFactory = await CreateSeededFactoryAsync(transactionCount);
        var service = InstitutionalReportServiceTestHelpers.CreateService(
            dbFactory,
            new ReportingOptions { MaxPreviewDetailRows = DetailLimit, MaxPdfDetailRows = DetailLimit, MaxPdfDetailRowsPerPart = DetailLimit });

        var result = await service.ExportAsync(CreateExportRequest(DetailOverflowAction.FullDetailsXlsx));

        Assert.Equal(transactionCount, result.Manifest.TotalMatchedRows);
        Assert.Equal(transactionCount, result.Manifest.ExportedDetailRows);
        Assert.False(result.Manifest.DetailRowsTruncated);
    }

    private static ReportExportRequestDto CreateExportRequest(DetailOverflowAction overflowAction) => new()
    {
        ExportFormat = overflowAction == DetailOverflowAction.FullDetailsXlsx
            ? ExportFormat.Xlsx
            : ExportFormat.Pdf,
        ExportMode = ExportMode.FullReport,
        DetailOverflowAction = overflowAction,
        BuildRequest = new ReportBuildRequestDto
        {
            ReportType = InstitutionalReportType.ExecutiveComprehensive,
            SectionIds =
            [
                ReportSectionId.Cover,
                ReportSectionId.ExecutiveSummary,
                ReportSectionId.TransactionDetails,
                ReportSectionId.ReportMetadata,
            ],
        },
    };

    private static async Task<IDbContextFactory<AppDbContext>> CreateSeededFactoryAsync(
        int transactionCount,
        int overdueCount = 0)
    {
        var dbName = $"overflow-{Guid.NewGuid():N}";
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;
        var dbFactory = new TestDbContextFactory(options);

        await using var db = dbFactory.CreateDbContext();
        var user = new User
        {
            Username = "overflow-test",
            PasswordHash = "hash",
            FullName = "Overflow Test",
            Role = UserRole.Admin,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var today = DateTime.UtcNow.Date;
        for (var i = 1; i <= transactionCount; i++)
        {
            var isOverdue = i <= overdueCount;
            db.Transactions.Add(new Transaction
            {
                InternalTrackingNumber = $"INT-{i:D4}",
                IncomingNumber = $"IN-{i:D4}",
                IncomingDate = today.AddDays(-i),
                Subject = $"معاملة {i}",
                IncomingFrom = "جهة",
                Status = isOverdue ? TransactionStatus.Overdue : TransactionStatus.New,
                Priority = Priority.Normal,
                RequiresResponse = isOverdue,
                ResponseCompleted = false,
                ResponseDueDate = isOverdue ? today.AddDays(-1) : null,
                CreatedById = user.Id,
                CreatedAt = DateTime.UtcNow,
            });
        }

        await db.SaveChangesAsync();
        return dbFactory;
    }

    private sealed class CapturingAuditService : IAuditService
    {
        public string? LastNewValue { get; private set; }

        public AuditLog TrackLog(
            int userId,
            AuditAction action,
            string? entityName,
            int? entityId,
            int? transactionId,
            string? oldValue,
            string? newValue)
        {
            LastNewValue = newValue;
            return new AuditLog();
        }

        public Task LogAsync(
            int userId,
            AuditAction action,
            string? entityName,
            int? entityId,
            int? transactionId,
            string? oldValue,
            string? newValue)
        {
            LastNewValue = newValue;
            return Task.CompletedTask;
        }
    }
}
