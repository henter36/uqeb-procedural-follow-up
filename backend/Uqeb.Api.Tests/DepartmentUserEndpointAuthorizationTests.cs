using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Uqeb.Api.Tests;

public class DepartmentUserEndpointAuthorizationTests : IClassFixture<DepartmentUserEndpointAuthorizationFactory>
{
    private readonly DepartmentUserEndpointAuthorizationFactory _factory;

    public DepartmentUserEndpointAuthorizationTests(DepartmentUserEndpointAuthorizationFactory factory) =>
        _factory = factory;

    [Theory]
    [InlineData("PUT", "/api/transactions/1", """{"subject":"x"}""")]
    [InlineData("POST", "/api/transactions/1/assignments", """{"departmentId":1,"assignedDate":"2026-01-01"}""")]
    [InlineData("POST", "/api/transactions/1/followups", """{"followUpDate":"2026-01-01","departmentIds":[1]}""")]
    [InlineData("POST", "/api/follow-up-print/jobs", """{"filter":{},"idempotencyKey":"k"}""")]
    [InlineData("POST", "/api/department-responses/1/approve", "{}")]
    [InlineData("POST", "/api/department-responses/1/return", """{"reviewNote":"x"}""")]
    [InlineData("POST", "/api/department-responses/1/reject", """{"reviewNote":"x"}""")]
    [InlineData("POST", "/api/recurring-transaction-templates", """{"title":"x"}""")]
    [InlineData("PUT", "/api/recurring-transaction-templates/1", """{"title":"x"}""")]
    [InlineData("POST", "/api/recurring-transaction-templates/1/pause", "{}")]
    [InlineData("POST", "/api/recurring-transaction-templates/1/resume", "{}")]
    [InlineData("POST", "/api/recurring-transaction-templates/1/terminate", """{"reason":"x"}""")]
    [InlineData("POST", "/api/recurring-transaction-templates/1/generate", """{"periodKey":"2026-01","incomingDate":"2026-01-01","referralDate":"2026-01-01"}""")]
    public async Task DepartmentUser_MutationEndpoints_ReturnForbidden(string method, string url, string body)
    {
        using var client = CreateDepartmentUserClient();
        using var request = new HttpRequestMessage(new HttpMethod(method), url)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [InlineData("/api/reports/dashboard")]
    [InlineData("/api/dashboard/summary")]
    [InlineData("/api/dashboard/action-required")]
    [InlineData("/api/dashboard/top-overdue-departments")]
    [InlineData("/api/dashboard/top-incoming-parties")]
    [InlineData("/api/dashboard/category-distribution")]
    [InlineData("/api/dashboard/status-distribution")]
    [InlineData("/api/follow-up-print/eligible")]
    [InlineData("/api/follow-up-print/jobs")]
    [InlineData("/api/follow-up-print/pending")]
    [InlineData("/api/follow-up-print/pending-summary")]
    [InlineData("/api/department-responses/pending-review")]
    [InlineData("/api/recurring-transaction-templates")]
    [InlineData("/api/recurring-transaction-templates/1")]
    [InlineData("/api/recurring-transaction-templates/1/transactions")]
    [InlineData("/api/reports/recurring-obligations/summary")]
    [InlineData("/api/reports/recurring-obligations/details")]
    [InlineData("/api/reports/recurring-obligations/export-excel")]
    [InlineData("/api/reports/department-obligation-snapshot")]
    [InlineData("/api/reports/department-obligation-snapshot/export-excel")]
    public async Task DepartmentUser_PrivilegedReadEndpoints_ReturnForbidden(string url)
    {
        using var client = CreateDepartmentUserClient();

        var response = await client.GetAsync(url);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [InlineData("Admin")]
    [InlineData("Supervisor")]
    [InlineData("DataEntry")]
    [InlineData("Reader")]
    public async Task NonDepartmentUserRoles_CanStillReachDashboardEndpoints(string role)
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            TestJwtHelper.CreateToken(role, userId: 3));

        var response = await client.GetAsync("/api/dashboard/summary");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // Recurring-obligations report lives under ReportsController. Its default permission
    // set remains Admin/Supervisor/DataEntry only; Reader is deliberately excluded here,
    // unlike the operational dashboard permission.
    [Theory]
    [InlineData("Admin")]
    [InlineData("Supervisor")]
    [InlineData("DataEntry")]
    public async Task NonDepartmentUserRoles_CanReachRecurringObligationsReport(string role)
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            TestJwtHelper.CreateToken(role, userId: 3));

        var summaryResponse = await client.GetAsync("/api/reports/recurring-obligations/summary");
        var detailsResponse = await client.GetAsync("/api/reports/recurring-obligations/details");
        var exportResponse = await client.GetAsync("/api/reports/recurring-obligations/export-excel");

        Assert.Equal(HttpStatusCode.OK, summaryResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, detailsResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, exportResponse.StatusCode);
    }

    [Fact]
    public async Task Reader_CannotReachRecurringObligationsReport()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            TestJwtHelper.CreateToken("Reader", userId: 3));

        var response = await client.GetAsync("/api/reports/recurring-obligations/summary");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // Department obligation snapshot is institution-wide cross-department attribution.
    // DepartmentUser is excluded entirely rather than being given a narrowed/scoped view
    // of this snapshot.
    [Theory]
    [InlineData("Admin")]
    [InlineData("Supervisor")]
    [InlineData("DataEntry")]
    public async Task NonDepartmentUserRoles_CanReachDepartmentObligationSnapshot(string role)
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            TestJwtHelper.CreateToken(role, userId: 3));

        var summaryResponse = await client.GetAsync("/api/reports/department-obligation-snapshot");
        var exportResponse = await client.GetAsync("/api/reports/department-obligation-snapshot/export-excel");

        Assert.Equal(HttpStatusCode.OK, summaryResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, exportResponse.StatusCode);
    }

    [Fact]
    public async Task Reader_CannotReachDepartmentObligationSnapshot()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            TestJwtHelper.CreateToken("Reader", userId: 3));

        var response = await client.GetAsync("/api/reports/department-obligation-snapshot");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private HttpClient CreateDepartmentUserClient()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            TestJwtHelper.CreateToken("DepartmentUser", userId: 2, departmentId: 10));
        return client;
    }
}

public sealed class DepartmentUserEndpointAuthorizationFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder) =>
        HealthTestHostBuilder.Configure(builder, inMemoryDatabaseName: $"department-user-endpoint-auth-{Guid.NewGuid():N}");
}
