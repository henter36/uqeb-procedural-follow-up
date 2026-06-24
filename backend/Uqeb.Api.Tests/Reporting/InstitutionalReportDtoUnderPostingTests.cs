using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using ClosedXML.Excel;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Uqeb.Api.Data;
using Uqeb.Api.Models.Entities;
using Uqeb.Api.Models.Enums;
using Uqeb.Api.Reporting.DTOs;
using Uqeb.Api.Reporting.Enums;
using Xunit;

namespace Uqeb.Api.Tests.Reporting;

public class InstitutionalReportDtoUnderPostingTests : IClassFixture<InstitutionalReportDtoWebApplicationFactory>
{
    private readonly HttpClient _client;

    public InstitutionalReportDtoUnderPostingTests(InstitutionalReportDtoWebApplicationFactory factory)
    {
        factory.EnsureSeeded();
        _client = factory.CreateClient();
        _client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TestJwtHelper.CreateToken("Admin"));
    }

    [Fact]
    public async Task Export_MinimalPayload_UsesDocumentedDefaults()
    {
        const string payload = """
            {
              "buildRequest": {
                "reportType": 1,
                "sectionIds": [1, 2],
                "filters": {
                  "departmentIds": [],
                  "partyIds": [],
                  "categoryIds": [],
                  "priorities": [],
                  "statuses": [],
                  "includeJointDepartmentTransactions": true,
                  "includeOverdue": true,
                  "includeDetails": true,
                  "includeRisks": true,
                  "includeRecommendations": true
                }
              }
            }
            """;

        var response = await _client.PostAsync(
            "/api/institutional-reports/export",
            new StringContent(payload, Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/pdf", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task SaveTemplate_WithoutName_Returns400()
    {
        var response = await _client.PostAsJsonAsync(
            "/api/institutional-reports/templates",
            new SaveReportTemplateRequestDto
            {
                Name = "  ",
                ReportType = InstitutionalReportType.ExecutiveComprehensive,
                SectionIds = [ReportSectionId.Cover],
            });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task SaveTemplate_WithoutValidReportType_Returns400()
    {
        var response = await _client.PostAsJsonAsync(
            "/api/institutional-reports/templates",
            new { name = "قالب", reportType = 0, sectionIds = new[] { 1 } });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task SaveTemplate_WithoutReportType_Returns400()
    {
        var response = await _client.PostAsJsonAsync(
            "/api/institutional-reports/templates",
            new { name = "قالب", sectionIds = new[] { 1 } });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.InternalServerError, response.StatusCode);
    }

    [Fact]
    public async Task SaveTemplate_WithInvalidEnum_Returns400_Not500()
    {
        var response = await _client.PostAsJsonAsync(
            "/api/institutional-reports/templates",
            new { name = "قالب", reportType = 99, sectionIds = new[] { 1 } });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.InternalServerError, response.StatusCode);
    }
}

public class InstitutionalReportFullDetailsXlsxContentTests : IClassFixture<InstitutionalReportXlsxWebApplicationFactory>
{
    private readonly HttpClient _client;

    public InstitutionalReportFullDetailsXlsxContentTests(InstitutionalReportXlsxWebApplicationFactory factory)
    {
        factory.EnsureSeeded();
        _client = factory.CreateClient();
        _client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TestJwtHelper.CreateToken("Admin"));
    }

    [Fact]
    public async Task ExportAsync_FullDetailsXlsx_ExportsAllRowsWithoutTruncation()
    {
        var response = await _client.PostAsJsonAsync(
            "/api/institutional-reports/export",
            new ReportExportRequestDto
            {
                ExportFormat = ExportFormat.Xlsx,
                ExportMode = ExportMode.FullReport,
                DetailOverflowAction = DetailOverflowAction.FullDetailsXlsx,
                BuildRequest = new ReportBuildRequestDto
                {
                    ReportType = InstitutionalReportType.ExecutiveComprehensive,
                    SectionIds = [ReportSectionId.Cover, ReportSectionId.TransactionDetails],
                },
            });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var bytes = await response.Content.ReadAsByteArrayAsync();

        using var stream = new MemoryStream(bytes);
        using var workbook = new XLWorkbook(stream);
        var ws = workbook.Worksheet("المعاملات التفصيلية");

        Assert.True(ws.Row(1).CellsUsed().Any());
        Assert.Equal(5, ws.RowsUsed().Count() - 1);
        Assert.Contains("IN-0005", ws.CellsUsed().Select(c => c.GetString()));
    }
}

public sealed class InstitutionalReportDtoWebApplicationFactory : WebApplicationFactory<Program>
{
    private const string DatabaseName = "institutional-dto-tests";
    private readonly object _seedLock = new();
    private bool _seeded;
    private int _transactionCount = 1;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        InstitutionalReportsTestHostBuilder.Configure(
            builder,
            institutionalReportsEnabled: true,
            useRealInstitutionalReportService: true,
            extraConfig: new Dictionary<string, string?>
            {
                ["Reporting:MaxPdfDetailRows"] = "2",
            },
            inMemoryDatabaseName: DatabaseName);
    }

    internal void EnsureSeeded(int transactionCount = 1)
    {
        _transactionCount = transactionCount;
        lock (_seedLock)
        {
            if (_seeded)
                return;

            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Database.EnsureDeleted();
            db.Database.EnsureCreated();

            var user = new User
            {
                Username = "dto-test-admin",
                PasswordHash = "hash",
                FullName = "DTO Test Admin",
                Role = UserRole.Admin,
                IsActive = true,
            };
            db.Users.Add(user);
            db.SaveChanges();

            var today = DateTime.UtcNow.Date;
            for (var i = 1; i <= _transactionCount; i++)
            {
                db.Transactions.Add(new Transaction
                {
                    InternalTrackingNumber = $"INT-{i:D4}",
                    IncomingNumber = $"IN-{i:D4}",
                    IncomingDate = today.AddDays(-i),
                    Subject = $"معاملة {i}",
                    Status = TransactionStatus.New,
                    CreatedById = user.Id,
                });
            }

            db.SaveChanges();
            _seeded = true;
        }
    }
}

public sealed class InstitutionalReportXlsxWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly object _seedLock = new();
    private bool _seeded;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        InstitutionalReportsTestHostBuilder.Configure(
            builder,
            institutionalReportsEnabled: true,
            useRealInstitutionalReportService: true,
            extraConfig: new Dictionary<string, string?>
            {
                ["Reporting:MaxPdfDetailRows"] = "2",
            },
            inMemoryDatabaseName: "institutional-xlsx-tests");
    }

    internal void EnsureSeeded()
    {
        lock (_seedLock)
        {
            if (_seeded)
                return;

            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Database.EnsureDeleted();
            db.Database.EnsureCreated();

            var user = new User
            {
                Username = "xlsx-test-admin",
                PasswordHash = "hash",
                FullName = "XLSX Test Admin",
                Role = UserRole.Admin,
                IsActive = true,
            };
            db.Users.Add(user);
            db.SaveChanges();

            var today = DateTime.UtcNow.Date;
            for (var i = 1; i <= 5; i++)
            {
                db.Transactions.Add(new Transaction
                {
                    InternalTrackingNumber = $"INT-{i:D4}",
                    IncomingNumber = $"IN-{i:D4}",
                    IncomingDate = today.AddDays(-i),
                    Subject = $"معاملة {i}",
                    Status = TransactionStatus.New,
                    CreatedById = user.Id,
                });
            }

            db.SaveChanges();
            _seeded = true;
        }
    }
}
