using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Uqeb.Api.Data;
using Uqeb.Api.Models.Entities;
using Uqeb.Api.Models.Enums;
using Uqeb.Api.Reporting.DTOs;
using Uqeb.Api.Reporting.Enums;
using Xunit;

namespace Uqeb.Api.Tests.Reporting;

public class InstitutionalReportPreviewIntegrationTests : IClassFixture<InstitutionalReportPreviewWebApplicationFactory>
{
    private readonly InstitutionalReportPreviewWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public InstitutionalReportPreviewIntegrationTests(InstitutionalReportPreviewWebApplicationFactory factory)
    {
        _factory = factory;
        _factory.EnsureSeeded();
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Preview_Unauthenticated_ReturnsUnauthorized()
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/institutional-reports/preview")
        {
            Content = JsonContent.Create(BuildRegressionRequest()),
        };

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Preview_UnauthorizedRole_ReturnsForbidden()
    {
        using var request = CreateAuthorizedRequest("DataEntry", BuildRegressionRequest());
        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Preview_AuthorizedAdmin_ReturnsOk()
    {
        using var request = CreateAuthorizedRequest("Admin", BuildRegressionRequest());
        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Preview_AuthorizedSupervisor_ReturnsForbidden()
    {
        using var request = CreateAuthorizedRequest("Supervisor", BuildRegressionRequest(), _factory.DeptAId);
        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Preview_WithZeroMatchingTransactions_ReturnsOk()
    {
        var requestBody = BuildRegressionRequest();
        requestBody.Filters.DateFrom = DateTime.UtcNow.Date.AddYears(5);
        requestBody.Filters.DateTo = DateTime.UtcNow.Date.AddYears(6);

        using var request = CreateAuthorizedRequest("Admin", requestBody);
        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var manifest = await response.Content.ReadFromJsonAsync<RenderedReportManifestDto>();
        Assert.NotNull(manifest);
        Assert.Equal(0, manifest!.TotalMatchedRows);
        Assert.True(manifest.TotalPages > 0);
    }

    [Fact]
    public async Task Preview_WithOneTransaction_ReturnsOk()
    {
        var requestBody = BuildRegressionRequest();
        requestBody.Filters.DateFrom = _factory.SingleTransactionDate;
        requestBody.Filters.DateTo = _factory.SingleTransactionDate;

        using var request = CreateAuthorizedRequest("Admin", requestBody);
        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var manifest = await response.Content.ReadFromJsonAsync<RenderedReportManifestDto>();
        Assert.Equal(1, manifest!.TotalMatchedRows);
    }

    [Fact]
    public async Task Preview_WithNullOptionalRelations_ReturnsOk()
    {
        var requestBody = BuildRegressionRequest();
        requestBody.Filters.DateFrom = _factory.NullRelationTransactionDate;
        requestBody.Filters.DateTo = _factory.NullRelationTransactionDate;

        using var request = CreateAuthorizedRequest("Admin", requestBody);
        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Preview_WithDateRange_ReturnsOk()
    {
        using var request = CreateAuthorizedRequest("Admin", BuildRegressionRequest());
        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var manifest = await response.Content.ReadFromJsonAsync<RenderedReportManifestDto>();
        Assert.True(manifest!.TotalMatchedRows >= 2);
    }

    [Fact]
    public async Task Preview_WithAllSections_ReturnsOk()
    {
        using var request = CreateAuthorizedRequest("Admin", BuildRegressionRequest());
        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var manifest = await response.Content.ReadFromJsonAsync<RenderedReportManifestDto>();
        Assert.True(manifest!.Pages.Count >= 6);
        Assert.Contains(manifest.Pages, page => page.SectionId == ReportSectionId.ExecutiveSummary);
        Assert.Contains(manifest.Pages, page => page.SectionId == ReportSectionId.TransactionDetails);
    }

    [Fact]
    public async Task Preview_WithEmptySectionIds_ReturnsBadRequest()
    {
        var requestBody = BuildRegressionRequest();
        requestBody.SectionIds = [];

        using var request = CreateAuthorizedRequest("Admin", requestBody);
        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Preview_WithInvalidDateRange_ReturnsBadRequest()
    {
        var requestBody = BuildRegressionRequest();
        var today = DateTime.UtcNow.Date;
        requestBody.Filters.DateFrom = today;
        requestBody.Filters.DateTo = today.AddDays(-10);

        using var request = CreateAuthorizedRequest("Admin", requestBody);
        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Preview_RegressionPayload_ReturnsOk()
    {
        using var request = CreateAuthorizedRequest("Admin", BuildRegressionRequest());
        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var manifest = await response.Content.ReadFromJsonAsync<RenderedReportManifestDto>();
        Assert.NotNull(manifest);
        Assert.False(string.IsNullOrWhiteSpace(manifest!.Stylesheet));
        Assert.True(manifest.Pages.Count > 0);
        Assert.StartsWith("PREVIEW-", manifest.ReportId);
    }

    [Fact]
    public async Task Preview_DoesNotAllocateOfficialNumber()
    {
        var allocator = _factory.Services.GetRequiredService<TrackingInstitutionalReportNumberAllocator>();
        var callsBefore = allocator.AllocateCallCount;

        using var request = CreateAuthorizedRequest("Admin", BuildRegressionRequest());
        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(callsBefore, allocator.AllocateCallCount);
    }

    [Fact]
    public async Task Preview_DoesNotCreateSequenceRow()
    {
        using var request = CreateAuthorizedRequest("Admin", BuildRegressionRequest());
        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await using var scope = _factory.Services.CreateAsyncScope();
        var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using var db = await dbFactory.CreateDbContextAsync();
        Assert.Empty(db.ReportNumberSequences);
    }

    private static HttpRequestMessage CreateAuthorizedRequest(
        string role,
        ReportBuildRequestDto body,
        int? departmentId = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/institutional-reports/preview")
        {
            Content = JsonContent.Create(body),
            Headers =
            {
                Authorization = new AuthenticationHeaderValue(
                    "Bearer",
                    TestJwtHelper.CreateToken(role, departmentId: departmentId)),
            },
        };

        return request;
    }

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
            IncludeOverdue = false,
        },
    };
}

public sealed class InstitutionalReportPreviewWebApplicationFactory : WebApplicationFactory<Program>
{
    private const string DatabaseName = "institutional-preview-integration-tests";
    private readonly object _seedLock = new();
    private bool _seeded;

    public int DeptAId { get; private set; }
    public DateTime SingleTransactionDate { get; private set; }
    public DateTime NullRelationTransactionDate { get; private set; }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        InstitutionalReportsTestHostBuilder.Configure(
            builder,
            institutionalReportsEnabled: true,
            useRealInstitutionalReportService: true,
            inMemoryDatabaseName: DatabaseName);
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
                Username = "preview-admin",
                PasswordHash = "hash",
                FullName = "Preview Admin",
                Role = UserRole.Admin,
                IsActive = true,
            };
            db.Users.Add(user);

            var deptA = new Department { Name = "إدارة المعاينة", IsActive = true };
            db.Departments.Add(deptA);
            db.SaveChanges();
            DeptAId = deptA.Id;

            var today = DateTime.UtcNow.Date;
            SingleTransactionDate = today.AddDays(-3);
            NullRelationTransactionDate = today.AddDays(-7);

            var transactions = new[]
            {
                new Transaction
                {
                    InternalTrackingNumber = "INT-001",
                    IncomingNumber = "IN-001",
                    IncomingDate = today.AddDays(-5),
                    Subject = "معاملة ضمن النطاق",
                    Status = TransactionStatus.New,
                    CreatedById = user.Id,
                },
                new Transaction
                {
                    InternalTrackingNumber = "INT-002",
                    IncomingNumber = "IN-002",
                    IncomingDate = today.AddDays(-10),
                    Subject = "معاملة ثانية",
                    Status = TransactionStatus.Closed,
                    ClosedAt = today.AddDays(-2),
                    CreatedById = user.Id,
                },
                new Transaction
                {
                    InternalTrackingNumber = "INT-SINGLE",
                    IncomingNumber = "IN-SINGLE",
                    IncomingDate = SingleTransactionDate,
                    Subject = "معاملة واحدة",
                    Status = TransactionStatus.New,
                    CreatedById = user.Id,
                },
                new Transaction
                {
                    InternalTrackingNumber = "INT-NULL",
                    IncomingNumber = "IN-NULL",
                    IncomingDate = NullRelationTransactionDate,
                    Subject = "معاملة بدون علاقات اختيارية",
                    IncomingFrom = null,
                    Category = null,
                    Status = TransactionStatus.New,
                    CreatedById = user.Id,
                },
            };

            db.Transactions.AddRange(transactions);
            db.SaveChanges();

            db.Assignments.Add(new Assignment
            {
                TransactionId = transactions[0].Id,
                DepartmentId = deptA.Id,
                Status = AssignmentStatus.Active,
                AssignedDate = today,
                CreatedById = user.Id,
            });
            db.SaveChanges();

            _seeded = true;
        }
    }
}
