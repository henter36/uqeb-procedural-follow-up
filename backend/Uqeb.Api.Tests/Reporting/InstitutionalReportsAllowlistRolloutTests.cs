using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Uqeb.Api.Reporting.Configuration;
using Uqeb.Api.Reporting.DTOs;
using Uqeb.Api.Reporting.Enums;
using Xunit;

namespace Uqeb.Api.Tests.Reporting;

public class InstitutionalReportsAllowlistRolloutTests : IClassFixture<InstitutionalReportsPhase1EnforcedAllowlistWebApplicationFactory>
{
    private const int PilotUserId = 1;
    private const int OtherAdminUserId = 2;
    private const int NormalUserId = 3;

    private readonly HttpClient _client;

    public InstitutionalReportsAllowlistRolloutTests(InstitutionalReportsPhase1EnforcedAllowlistWebApplicationFactory factory) =>
        _client = factory.CreateClient();

    [Fact]
    public async Task AllowlistedAdmin_CanAccessConfigurationAndReadiness()
    {
        using var request = CreateAuthorizedRequest(HttpMethod.Get, "/api/institutional-reports/configuration", PilotUserId);
        using var readinessRequest = CreateAuthorizedRequest(HttpMethod.Get, "/api/institutional-reports/readiness", PilotUserId);

        var configuration = await _client.SendAsync(request);
        var readiness = await _client.SendAsync(readinessRequest);

        Assert.Equal(HttpStatusCode.OK, configuration.StatusCode);
        Assert.Equal(HttpStatusCode.OK, readiness.StatusCode);
    }

    [Fact]
    public async Task NonAllowlistedAdmin_ReturnsNotFound_InEnforcedMode()
    {
        using var configurationRequest = CreateAuthorizedRequest(HttpMethod.Get, "/api/institutional-reports/configuration", OtherAdminUserId);
        using var readinessRequest = CreateAuthorizedRequest(HttpMethod.Get, "/api/institutional-reports/readiness", OtherAdminUserId);

        var configuration = await _client.SendAsync(configurationRequest);
        var readiness = await _client.SendAsync(readinessRequest);

        Assert.Equal(HttpStatusCode.NotFound, configuration.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, readiness.StatusCode);
    }

    [Fact]
    public async Task AllowlistedAdmin_CanExportPdfAndXlsx()
    {
        using var pdfRequest = CreateAuthorizedRequest(
            HttpMethod.Post,
            "/api/institutional-reports/export",
            PilotUserId,
            new ReportExportRequestDto { ExportFormat = ExportFormat.Pdf });

        using var xlsxRequest = CreateAuthorizedRequest(
            HttpMethod.Post,
            "/api/institutional-reports/export",
            PilotUserId,
            new ReportExportRequestDto { ExportFormat = ExportFormat.Xlsx });

        var pdf = await _client.SendAsync(pdfRequest);
        var xlsx = await _client.SendAsync(xlsxRequest);

        Assert.Equal(HttpStatusCode.OK, pdf.StatusCode);
        Assert.Equal(HttpStatusCode.OK, xlsx.StatusCode);
    }

    [Fact]
    public async Task NormalUser_IsDeniedByNormalRbac()
    {
        using var request = CreateAuthorizedRequest(HttpMethod.Get, "/api/institutional-reports/configuration", NormalUserId, role: "DataEntry");
        var response = await _client.SendAsync(request);

        Assert.True(
            response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Forbidden,
            $"Expected 404 or 403, got {response.StatusCode}");
    }

    private static HttpRequestMessage CreateAuthorizedRequest(
        HttpMethod method,
        string path,
        int userId,
        object? body = null,
        string role = "Admin")
    {
        var request = new HttpRequestMessage(method, path)
        {
            Headers =
            {
                Authorization = new AuthenticationHeaderValue(
                    "Bearer",
                    TestJwtHelper.CreateToken(role, userId: userId)),
            },
        };

        if (body is not null)
            request.Content = JsonContent.Create(body);

        return request;
    }
}

public class InstitutionalReportsObserveOnlyRolloutTests : IClassFixture<InstitutionalReportsPhase1ObserveOnlyAllowlistWebApplicationFactory>
{
    private readonly HttpClient _client;

    public InstitutionalReportsObserveOnlyRolloutTests(InstitutionalReportsPhase1ObserveOnlyAllowlistWebApplicationFactory factory) =>
        _client = factory.CreateClient();

    [Fact]
    public async Task UnmatchedAdmin_Allowed_InObserveOnlyMode()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/institutional-reports/configuration")
        {
            Headers =
            {
                Authorization = new AuthenticationHeaderValue(
                    "Bearer",
                    TestJwtHelper.CreateToken("Admin", userId: 2)),
            },
        };

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Supervisor_Allowed_InObserveOnlyMode()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/institutional-reports/templates")
        {
            Headers =
            {
                Authorization = new AuthenticationHeaderValue(
                    "Bearer",
                    TestJwtHelper.CreateToken("Supervisor", userId: 99)),
            },
        };

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}

public class InstitutionalReportsEmergencyDisableRolloutTests
    : IClassFixture<InstitutionalReportsPhase1EmergencyDisableWebApplicationFactory>
{
    private readonly HttpClient _client;

    public InstitutionalReportsEmergencyDisableRolloutTests(
        InstitutionalReportsPhase1EmergencyDisableWebApplicationFactory factory) =>
        _client = factory.CreateClient();

    [Fact]
    public async Task EmergencyDisable_DeniesAllowlistedAdmin()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/institutional-reports/configuration")
        {
            Headers =
            {
                Authorization = new AuthenticationHeaderValue(
                    "Bearer",
                    TestJwtHelper.CreateToken("Admin", userId: 1)),
            },
        };

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}

public class ReportingRolloutEnforcementMiddlewareTests
{
    [Theory]
    [InlineData(false, true, ReportingRolloutEnforcementMode.ObserveOnly, 1, HttpStatusCode.NotFound)]
    [InlineData(true, true, ReportingRolloutEnforcementMode.ObserveOnly, 1, HttpStatusCode.NotFound)]
    [InlineData(true, true, ReportingRolloutEnforcementMode.ObserveOnly, 2, HttpStatusCode.NotFound)]
    [InlineData(true, false, ReportingRolloutEnforcementMode.ObserveOnly, 1, HttpStatusCode.OK)]
    [InlineData(true, false, ReportingRolloutEnforcementMode.ObserveOnly, 2, HttpStatusCode.OK)]
    [InlineData(true, false, ReportingRolloutEnforcementMode.Enforced, 1, HttpStatusCode.OK)]
    [InlineData(true, false, ReportingRolloutEnforcementMode.Enforced, 2, HttpStatusCode.NotFound)]
    public async Task ConfigurationEndpoint_FollowsEnforcementMatrix(
        bool featureEnabled,
        bool emergencyDisable,
        ReportingRolloutEnforcementMode enforcementMode,
        int userId,
        HttpStatusCode expectedStatus)
    {
        using var factory = new ReportingRolloutMatrixWebApplicationFactory(
            featureEnabled,
            emergencyDisable,
            enforcementMode,
            enabledUserIds: [1]);
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/institutional-reports/configuration")
        {
            Headers =
            {
                Authorization = new AuthenticationHeaderValue(
                    "Bearer",
                    TestJwtHelper.CreateToken("Admin", userId: userId)),
            },
        };

        var response = await client.SendAsync(request);

        Assert.Equal(expectedStatus, response.StatusCode);
    }

    [Fact]
    public async Task UnauthenticatedUser_ReturnsUnauthorized()
    {
        using var factory = new ReportingRolloutMatrixWebApplicationFactory(
            featureEnabled: true,
            emergencyDisable: false,
            enforcementMode: ReportingRolloutEnforcementMode.ObserveOnly,
            enabledUserIds: [1]);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/institutional-reports/configuration");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}

public sealed class InstitutionalReportsPhase1EnforcedAllowlistWebApplicationFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder) =>
        InstitutionalReportsTestHostBuilder.Configure(
            builder,
            institutionalReportsEnabled: true,
            useDefaultRoleRollout: false,
            extraConfig: Phase1RolloutTestConfig.EnforcedAllowlist(1));
}

public sealed class InstitutionalReportsPhase1ObserveOnlyAllowlistWebApplicationFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder) =>
        InstitutionalReportsTestHostBuilder.Configure(
            builder,
            institutionalReportsEnabled: true,
            useDefaultRoleRollout: false,
            extraConfig: Phase1RolloutTestConfig.ObserveOnlyAllowlist(1));
}

public sealed class InstitutionalReportsPhase1EmergencyDisableWebApplicationFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder) =>
        InstitutionalReportsTestHostBuilder.Configure(
            builder,
            institutionalReportsEnabled: true,
            useDefaultRoleRollout: false,
            extraConfig: Phase1RolloutTestConfig.EmergencyDisableAllowlist(1));
}

public sealed class ReportingRolloutMatrixWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly bool _featureEnabled;
    private readonly bool _emergencyDisable;
    private readonly ReportingRolloutEnforcementMode _enforcementMode;
    private readonly int[] _enabledUserIds;

    public ReportingRolloutMatrixWebApplicationFactory(
        bool featureEnabled,
        bool emergencyDisable,
        ReportingRolloutEnforcementMode enforcementMode,
        int[] enabledUserIds)
    {
        _featureEnabled = featureEnabled;
        _emergencyDisable = emergencyDisable;
        _enforcementMode = enforcementMode;
        _enabledUserIds = enabledUserIds;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        var extra = new Dictionary<string, string?>
        {
            ["FeatureFlags:InstitutionalReports"] = _featureEnabled ? "true" : "false",
            ["ReportingRollout:EmergencyDisable"] = _emergencyDisable ? "true" : "false",
            ["ReportingRollout:EnforcementMode"] = _enforcementMode.ToString(),
            ["ReportingRollout:Percentage"] = "0",
        };

        for (var i = 0; i < _enabledUserIds.Length; i++)
            extra[$"ReportingRollout:EnabledForUserIds:{i}"] = _enabledUserIds[i].ToString();

        InstitutionalReportsTestHostBuilder.Configure(
            builder,
            institutionalReportsEnabled: _featureEnabled,
            useDefaultRoleRollout: false,
            extraConfig: extra);
    }
}

internal static class Phase1RolloutTestConfig
{
    internal static Dictionary<string, string?> EnforcedAllowlist(int userId) => new()
    {
        ["ReportingRollout:EnforcementMode"] = ReportingRolloutEnforcementMode.Enforced.ToString(),
        ["ReportingRollout:EmergencyDisable"] = "false",
        ["ReportingRollout:EnabledForUserIds:0"] = userId.ToString(),
        ["ReportingRollout:Percentage"] = "0",
    };

    internal static Dictionary<string, string?> ObserveOnlyAllowlist(int userId) => new()
    {
        ["ReportingRollout:EnforcementMode"] = ReportingRolloutEnforcementMode.ObserveOnly.ToString(),
        ["ReportingRollout:EmergencyDisable"] = "false",
        ["ReportingRollout:EnabledForUserIds:0"] = userId.ToString(),
        ["ReportingRollout:Percentage"] = "0",
    };

    internal static Dictionary<string, string?> EmergencyDisableAllowlist(int userId) => new()
    {
        ["ReportingRollout:EnforcementMode"] = ReportingRolloutEnforcementMode.ObserveOnly.ToString(),
        ["ReportingRollout:EmergencyDisable"] = "true",
        ["ReportingRollout:EnabledForUserIds:0"] = userId.ToString(),
        ["ReportingRollout:Percentage"] = "0",
    };
}
