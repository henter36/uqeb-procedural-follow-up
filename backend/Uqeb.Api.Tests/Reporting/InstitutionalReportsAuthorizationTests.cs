using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Uqeb.Api.Reporting.DTOs;
using Uqeb.Api.Reporting.Enums;
using Xunit;

namespace Uqeb.Api.Tests.Reporting;

public class InstitutionalReportsAuthorizationTests : IClassFixture<InstitutionalReportsEnabledWebApplicationFactory>
{
    private readonly HttpClient _client;

    public InstitutionalReportsAuthorizationTests(InstitutionalReportsEnabledWebApplicationFactory factory) =>
        _client = factory.CreateClient();

    [Fact]
    public async Task Preview_Unauthenticated_ReturnsUnauthorized()
    {
        var response = await _client.PostAsJsonAsync(
            "/api/institutional-reports/preview",
            BuildRequest());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Preview_Admin_ReturnsOk()
    {
        using var request = CreateAuthorizedRequest(HttpMethod.Post, "/api/institutional-reports/preview", "Admin", BuildRequest());
        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Preview_Supervisor_ReturnsForbidden()
    {
        using var request = CreateAuthorizedRequest(HttpMethod.Post, "/api/institutional-reports/preview", "Supervisor", BuildRequest());
        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Preview_DataEntry_ReturnsForbidden()
    {
        using var request = CreateAuthorizedRequest(HttpMethod.Post, "/api/institutional-reports/preview", "DataEntry", BuildRequest());
        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Export_Admin_IsAccepted()
    {
        using var request = CreateAuthorizedRequest(
            HttpMethod.Post,
            "/api/institutional-reports/export",
            "Admin",
            new ReportExportRequestDto
            {
                ExportFormat = ExportFormat.Html,
                BuildRequest = BuildRequest(),
            });

        var response = await _client.SendAsync(request);

        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Export_Supervisor_ReturnsForbidden()
    {
        using var request = CreateAuthorizedRequest(
            HttpMethod.Post,
            "/api/institutional-reports/export",
            "Supervisor",
            new ReportExportRequestDto
            {
                ExportFormat = ExportFormat.Html,
                BuildRequest = BuildRequest(),
            });

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Export_Unauthenticated_ReturnsUnauthorized()
    {
        var response = await _client.PostAsJsonAsync(
            "/api/institutional-reports/export",
            new ReportExportRequestDto
            {
                ExportFormat = ExportFormat.Html,
                BuildRequest = BuildRequest(),
            });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Readiness_Admin_ReturnsOk()
    {
        using var request = CreateAuthorizedRequest(HttpMethod.Get, "/api/institutional-reports/readiness", "Admin");
        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Readiness_Supervisor_ReturnsForbidden()
    {
        using var request = CreateAuthorizedRequest(HttpMethod.Get, "/api/institutional-reports/readiness", "Supervisor");
        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private static ReportBuildRequestDto BuildRequest() => new()
    {
        ReportType = InstitutionalReportType.ExecutiveComprehensive,
        SectionIds = [ReportSectionId.Cover],
    };

    private static HttpRequestMessage CreateAuthorizedRequest(
        HttpMethod method,
        string path,
        string role,
        object? body = null)
    {
        var request = new HttpRequestMessage(method, path)
        {
            Headers =
            {
                Authorization = new AuthenticationHeaderValue(
                    "Bearer",
                    TestJwtHelper.CreateToken(role)),
            },
        };

        if (body is not null)
            request.Content = JsonContent.Create(body);

        return request;
    }
}
