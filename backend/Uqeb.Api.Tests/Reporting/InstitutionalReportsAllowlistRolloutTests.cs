using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Uqeb.Api.Reporting.DTOs;
using Xunit;

namespace Uqeb.Api.Tests.Reporting;

public class InstitutionalReportsAllowlistRolloutTests : IClassFixture<InstitutionalReportsPhase1AllowlistWebApplicationFactory>
{
    private const int PilotUserId = 1;
    private const int OtherAdminUserId = 2;
    private const int NormalUserId = 3;

    private readonly HttpClient _client;

    public InstitutionalReportsAllowlistRolloutTests(InstitutionalReportsPhase1AllowlistWebApplicationFactory factory) =>
        _client = factory.CreateClient();

    [Fact]
    public async Task AllowlistedAdmin_CanAccessConfigurationAndReadiness()
    {
        SetBearer("Admin", PilotUserId);

        var configuration = await _client.GetAsync("/api/institutional-reports/configuration");
        var readiness = await _client.GetAsync("/api/institutional-reports/readiness");

        Assert.Equal(HttpStatusCode.OK, configuration.StatusCode);
        Assert.Equal(HttpStatusCode.OK, readiness.StatusCode);
    }

    [Fact]
    public async Task AllowlistedAdmin_CanPreview()
    {
        SetBearer("Admin", PilotUserId);

        var response = await _client.PostAsJsonAsync(
            "/api/institutional-reports/preview",
            new ReportBuildRequestDto());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task NonAllowlistedAdmin_ReturnsNotFound()
    {
        SetBearer("Admin", OtherAdminUserId);

        var configuration = await _client.GetAsync("/api/institutional-reports/configuration");
        var readiness = await _client.GetAsync("/api/institutional-reports/readiness");

        Assert.Equal(HttpStatusCode.NotFound, configuration.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, readiness.StatusCode);
    }

    [Fact]
    public async Task NormalUserNotInAllowlist_IsDenied()
    {
        SetBearer("DataEntry", NormalUserId);

        var configuration = await _client.GetAsync("/api/institutional-reports/configuration");

        Assert.True(
            configuration.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Forbidden,
            $"Expected 404 or 403, got {configuration.StatusCode}");
    }

    private void SetBearer(string role, int userId) =>
        _client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TestJwtHelper.CreateToken(role, userId: userId));
}

public class InstitutionalReportsEmergencyDisableRolloutTests : IClassFixture<InstitutionalReportsPhase1EmergencyDisableWebApplicationFactory>
{
    [Fact]
    public async Task EmergencyDisable_DeniesAllowlistedAdmin()
    {
        using var factory = new InstitutionalReportsPhase1EmergencyDisableWebApplicationFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TestJwtHelper.CreateToken("Admin", userId: 1));

        var configuration = await client.GetAsync("/api/institutional-reports/configuration");

        Assert.Equal(HttpStatusCode.NotFound, configuration.StatusCode);
    }
}

public sealed class InstitutionalReportsPhase1AllowlistWebApplicationFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder) =>
        InstitutionalReportsTestHostBuilder.Configure(
            builder,
            institutionalReportsEnabled: true,
            useDefaultRoleRollout: false,
            extraConfig: new Dictionary<string, string?>
            {
                ["ReportingRollout:EmergencyDisable"] = "false",
                ["ReportingRollout:EnabledForUserIds:0"] = "1",
                ["ReportingRollout:Percentage"] = "0",
            });
}

public sealed class InstitutionalReportsPhase1EmergencyDisableWebApplicationFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder) =>
        InstitutionalReportsTestHostBuilder.Configure(
            builder,
            institutionalReportsEnabled: true,
            useDefaultRoleRollout: false,
            extraConfig: new Dictionary<string, string?>
            {
                ["ReportingRollout:EmergencyDisable"] = "true",
                ["ReportingRollout:EnabledForUserIds:0"] = "1",
                ["ReportingRollout:Percentage"] = "0",
            });
}
