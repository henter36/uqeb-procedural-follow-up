using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Uqeb.Api.Data;
using Uqeb.Api.DTOs.Reports;
using Uqeb.Api.Models.Entities;
using Uqeb.Api.Models.Enums;
using Uqeb.Api.Services;
using Xunit;

namespace Uqeb.Api.Tests;

public class LegacyReportExportTests
{
    private static DbContextOptions<AppDbContext> CreateOptions(string dbName) =>
        new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(dbName)
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

    private static async Task<(IDbContextFactory<AppDbContext> Factory, string DbName)> CreateFactoryAsync(string dbName)
    {
        var options = CreateOptions(dbName);
        var factory = new TestDbContextFactory(options);
        await using var seedContext = factory.CreateDbContext();
        seedContext.Users.Add(new User
        {
            Id = 1,
            Username = "admin",
            PasswordHash = "hash",
            FullName = "Admin",
            Role = UserRole.Admin,
            IsActive = true
        });
        await seedContext.SaveChangesAsync();
        return (factory, dbName);
    }

    private static ReportService CreateService(IDbContextFactory<AppDbContext> factory)
    {
        var db = factory.CreateDbContext();
        return new ReportService(db, factory);
    }

    private static async Task SeedOpenTransactionsAsync(AppDbContext db, int count)
    {
        for (var i = 1; i <= count; i++)
        {
            db.Transactions.Add(new Transaction
            {
                Id = i,
                InternalTrackingNumber = $"UQEB-2026-{i:00000}",
                IncomingNumber = $"IN-{i:0000}",
                IncomingDate = new DateTime(2026, 1, 1).AddDays(i),
                Subject = $"Subject {i}",
                IncomingSourceType = IncomingSourceType.External,
                IncomingFrom = "جهة",
                RequiresResponse = false,
                ResponseType = ResponseType.None,
                Priority = Priority.Normal,
                Status = TransactionStatus.InProgress,
                CreatedById = 1,
                CreatedAt = new DateTime(2026, 6, 1).AddMinutes(-i)
            });
        }

        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task ExportReportDetailsExcelAsync_full_export_includes_all_rows_across_batches()
    {
        var (factory, _) = await CreateFactoryAsync(nameof(ExportReportDetailsExcelAsync_full_export_includes_all_rows_across_batches));
        await using (var db = factory.CreateDbContext())
        {
            await SeedOpenTransactionsAsync(db, 1005);
        }

        var service = CreateService(factory);
        var bytes = await service.ExportReportDetailsExcelAsync(
            "open",
            new ReportPagedFilterRequest { Page = 1, PageSize = 5 },
            currentPageOnly: false);

        Assert.NotEmpty(bytes);
        using var workbook = new ClosedXML.Excel.XLWorkbook(new MemoryStream(bytes));
        var ws = workbook.Worksheet(1);
        Assert.Equal(1005, ws.LastRowUsed()!.RowNumber() - 1);
    }

    [Fact]
    public async Task ExportReportDetailsExcelAsync_honors_cancellation_before_export_starts()
    {
        var (factory, _) = await CreateFactoryAsync(nameof(ExportReportDetailsExcelAsync_honors_cancellation_before_export_starts));
        await using (var db = factory.CreateDbContext())
        {
            await SeedOpenTransactionsAsync(db, 3);
        }

        var service = CreateService(factory);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            service.ExportReportDetailsExcelAsync(
                "open",
                new ReportPagedFilterRequest { Page = 1, PageSize = 5 },
                currentPageOnly: false,
                cts.Token));
    }

    [Fact]
    public async Task ExportReportDetailsExcelAsync_current_page_only_exports_requested_page()
    {
        var (factory, _) = await CreateFactoryAsync(nameof(ExportReportDetailsExcelAsync_current_page_only_exports_requested_page));
        await using (var db = factory.CreateDbContext())
        {
            await SeedOpenTransactionsAsync(db, 12);
        }

        var service = CreateService(factory);
        var bytes = await service.ExportReportDetailsExcelAsync(
            "open",
            new ReportPagedFilterRequest { Page = 2, PageSize = 5 },
            currentPageOnly: true);

        using var workbook = new ClosedXML.Excel.XLWorkbook(new MemoryStream(bytes));
        var ws = workbook.Worksheet(1);
        Assert.Equal(5, ws.LastRowUsed()!.RowNumber() - 1);
        Assert.Equal("IN-0006", ws.Cell(2, 2).GetString());
    }

    [Fact]
    public async Task ExportReportDetailsExcelAsync_supports_concurrent_exports()
    {
        var (factory, _) = await CreateFactoryAsync(nameof(ExportReportDetailsExcelAsync_supports_concurrent_exports));
        await using (var db = factory.CreateDbContext())
        {
            await SeedOpenTransactionsAsync(db, 20);
        }

        var filter = new ReportPagedFilterRequest { Page = 1, PageSize = 5 };
        var tasks = Enumerable.Range(0, 3)
            .Select(_ => CreateService(factory).ExportReportDetailsExcelAsync("open", filter, currentPageOnly: true))
            .ToArray();

        var results = await Task.WhenAll(tasks);
        Assert.All(results, bytes => Assert.NotEmpty(bytes));
    }

    private sealed class TestDbContextFactory : IDbContextFactory<AppDbContext>
    {
        private readonly DbContextOptions<AppDbContext> _options;

        public TestDbContextFactory(DbContextOptions<AppDbContext> options) => _options = options;

        public AppDbContext CreateDbContext() => new(_options);
    }
}
