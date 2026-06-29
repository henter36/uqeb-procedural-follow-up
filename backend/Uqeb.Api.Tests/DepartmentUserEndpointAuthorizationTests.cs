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
    [InlineData("POST", "/api/transactions/1/assignments/1/reply", """{"replyDate":"2026-01-01","replySummary":"x"}""")]
    [InlineData("POST", "/api/transactions/1/followups/1/reply", """{"replyDate":"2026-01-01","replySummary":"x"}""")]
    [InlineData("POST", "/api/follow-up-print/jobs", """{"filter":{},"idempotencyKey":"k"}""")]
    [InlineData("POST", "/api/department-responses/1/approve", "{}")]
    [InlineData("POST", "/api/department-responses/1/return", """{"reviewNote":"x"}""")]
    [InlineData("POST", "/api/department-responses/1/reject", """{"reviewNote":"x"}""")]
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
    [InlineData("/api/follow-up-print/eligible")]
    [InlineData("/api/follow-up-print/jobs")]
    [InlineData("/api/follow-up-print/pending")]
    [InlineData("/api/follow-up-print/pending-summary")]
    [InlineData("/api/department-responses/pending-review")]
    public async Task DepartmentUser_PrivilegedReadEndpoints_ReturnForbidden(string url)
    {
        using var client = CreateDepartmentUserClient();

        var response = await client.GetAsync(url);

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
