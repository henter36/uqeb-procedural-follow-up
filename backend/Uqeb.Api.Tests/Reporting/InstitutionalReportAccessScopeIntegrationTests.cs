using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
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

public class InstitutionalReportAccessScopeIntegrationTests : IClassFixture<InstitutionalReportAccessScopeWebApplicationFactory>
{
    private readonly InstitutionalReportAccessScopeWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public InstitutionalReportAccessScopeIntegrationTests(InstitutionalReportAccessScopeWebApplicationFactory factory)
    {
        _factory = factory;
        _factory.EnsureSeeded();
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Preview_AdminWithoutDepartmentId_ReturnsAllMatchingTransactions()
    {
        _client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TestJwtHelper.CreateToken("Admin"));

        var response = await _client.PostAsJsonAsync(
            "/api/institutional-reports/preview",
            BuildPreviewRequest());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var manifest = await response.Content.ReadFromJsonAsync<RenderedReportManifestDto>();
        Assert.NotNull(manifest);
        Assert.Equal(2, manifest!.TotalMatchedRows);
    }

    [Fact]
    public async Task Preview_SupervisorWithoutDepartmentId_ReturnsForbidden()
    {
        _client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TestJwtHelper.CreateToken("Supervisor"));

        var response = await _client.PostAsJsonAsync(
            "/api/institutional-reports/preview",
            BuildPreviewRequest());

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Preview_SupervisorWithDepartment_ReturnsForbidden()
    {
        _client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TestJwtHelper.CreateToken("Supervisor", _factory.DeptAId));

        var response = await _client.PostAsJsonAsync(
            "/api/institutional-reports/preview",
            BuildPreviewRequest());

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Preview_SupervisorFromOtherDepartment_ReturnsForbidden()
    {
        _client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TestJwtHelper.CreateToken("Supervisor", _factory.DeptBId));

        var response = await _client.PostAsJsonAsync(
            "/api/institutional-reports/preview",
            BuildPreviewRequest());

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private static ReportBuildRequestDto BuildPreviewRequest() => new()
    {
        ReportType = InstitutionalReportType.ExecutiveComprehensive,
        SectionIds = [ReportSectionId.ExecutiveSummary],
    };
}

public sealed class InstitutionalReportAccessScopeWebApplicationFactory : WebApplicationFactory<Program>
{
    private const string DatabaseName = "institutional-access-scope-tests";
    private readonly object _seedLock = new();
    private bool _seeded;

    public int DeptAId { get; private set; }
    public int DeptBId { get; private set; }

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
                Username = "access-scope-admin",
                PasswordHash = "hash",
                FullName = "Access Scope Admin",
                Role = UserRole.Admin,
                IsActive = true,
            };
            db.Users.Add(user);

            var deptA = new Department { Name = "إدارة أ", IsActive = true };
            var deptB = new Department { Name = "إدارة ب", IsActive = true };
            db.Departments.AddRange(deptA, deptB);
            db.SaveChanges();

            DeptAId = deptA.Id;
            DeptBId = deptB.Id;

            var txA = new Transaction
            {
                InternalTrackingNumber = "INT-A",
                IncomingNumber = "IN-A",
                IncomingDate = DateTime.UtcNow.Date,
                Subject = "معاملة إدارة أ",
                Status = TransactionStatus.New,
                CreatedById = user.Id,
            };
            var txB = new Transaction
            {
                InternalTrackingNumber = "INT-B",
                IncomingNumber = "IN-B",
                IncomingDate = DateTime.UtcNow.Date,
                Subject = "معاملة إدارة ب",
                Status = TransactionStatus.New,
                CreatedById = user.Id,
            };
            db.Transactions.AddRange(txA, txB);
            db.SaveChanges();

            db.Assignments.AddRange(
                new Assignment
                {
                    TransactionId = txA.Id,
                    DepartmentId = deptA.Id,
                    Status = AssignmentStatus.Active,
                    AssignedDate = DateTime.UtcNow.Date,
                    CreatedById = user.Id,
                },
                new Assignment
                {
                    TransactionId = txB.Id,
                    DepartmentId = deptB.Id,
                    Status = AssignmentStatus.Active,
                    AssignedDate = DateTime.UtcNow.Date,
                    CreatedById = user.Id,
                });
            db.SaveChanges();

            _seeded = true;
        }
    }
}
