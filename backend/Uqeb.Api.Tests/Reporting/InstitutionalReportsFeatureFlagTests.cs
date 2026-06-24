using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Uqeb.Api.Reporting.Configuration;
using Uqeb.Api.Reporting.DTOs;
using Uqeb.Api.Reporting.Enums;
using Uqeb.Api.Reporting.Exporters;
using Uqeb.Api.Reporting.Services;
using Xunit;

namespace Uqeb.Api.Tests.Reporting;

public class InstitutionalReportsFeatureFlagDisabledTests : IClassFixture<InstitutionalReportsDisabledWebApplicationFactory>
{
    private readonly HttpClient _client;

    public InstitutionalReportsFeatureFlagDisabledTests(InstitutionalReportsDisabledWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
        _client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TestJwtHelper.CreateToken("Admin"));
    }

    [Theory]
    [InlineData("/api/institutional-reports/templates")]
    public async Task GetEndpoints_ReturnNotFound_WhenFeatureDisabled(string path)
    {
        var response = await _client.GetAsync(path);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Preview_ReturnsNotFound_WhenFeatureDisabled()
    {
        var response = await _client.PostAsJsonAsync(
            "/api/institutional-reports/preview",
            new ReportBuildRequestDto());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Export_ReturnsNotFound_WhenFeatureDisabled()
    {
        var response = await _client.PostAsJsonAsync(
            "/api/institutional-reports/export",
            new ReportExportRequestDto());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task SaveTemplate_ReturnsNotFound_WhenFeatureDisabled()
    {
        var response = await _client.PostAsJsonAsync(
            "/api/institutional-reports/templates",
            new SaveReportTemplateRequestDto
            {
                Name = "test",
                ReportType = InstitutionalReportType.ExecutiveComprehensive,
                SectionIds = [ReportSectionId.Cover],
                DefaultFormat = ExportFormat.Pdf,
            });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task DeleteTemplate_ReturnsNotFound_WhenFeatureDisabled()
    {
        var response = await _client.DeleteAsync("/api/institutional-reports/templates/1");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}

public class InstitutionalReportsFeatureFlagEnabledTests : IClassFixture<InstitutionalReportsEnabledWebApplicationFactory>
{
    private readonly HttpClient _client;

    public InstitutionalReportsFeatureFlagEnabledTests(InstitutionalReportsEnabledWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task GetTemplates_ReturnsForbidden_WhenUserIsNotSupervisorOrAdmin()
    {
        _client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TestJwtHelper.CreateToken("DataEntry"));

        var response = await _client.GetAsync("/api/institutional-reports/templates");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetTemplates_ReturnsOk_WhenFeatureEnabledAndUserIsSupervisor()
    {
        _client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TestJwtHelper.CreateToken("Supervisor"));

        var response = await _client.GetAsync("/api/institutional-reports/templates");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task GetTemplates_ReturnsOk_WhenFeatureEnabledAndUserIsAdmin()
    {
        _client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TestJwtHelper.CreateToken("Admin"));

        var response = await _client.GetAsync("/api/institutional-reports/templates");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}

public class InstitutionalReportsMissingFeatureFlagTests : IClassFixture<InstitutionalReportsMissingFeatureFlagWebApplicationFactory>
{
    private readonly HttpClient _client;

    public InstitutionalReportsMissingFeatureFlagTests(InstitutionalReportsMissingFeatureFlagWebApplicationFactory factory) =>
        _client = factory.CreateClient();

    [Fact]
    public async Task GetTemplates_ReturnsOk_WhenFeatureFlagMissingAndUserIsAdmin()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/institutional-reports/templates")
        {
            Headers =
            {
                Authorization = new AuthenticationHeaderValue(
                    "Bearer",
                    TestJwtHelper.CreateToken("Admin")),
            },
        };

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task GetTemplates_ReturnsOk_WhenFeatureFlagMissingAndUserIsSupervisor()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/institutional-reports/templates")
        {
            Headers =
            {
                Authorization = new AuthenticationHeaderValue(
                    "Bearer",
                    TestJwtHelper.CreateToken("Supervisor")),
            },
        };

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}

public sealed class InstitutionalReportsMissingFeatureFlagWebApplicationFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder) =>
        InstitutionalReportsTestHostBuilder.Configure(
            builder,
            institutionalReportsEnabled: null,
            useDefaultRoleRollout: false);
}

public sealed class InstitutionalReportsDisabledWebApplicationFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder) =>
        InstitutionalReportsTestHostBuilder.Configure(builder, institutionalReportsEnabled: false);
}

public sealed class InstitutionalReportsEnabledWebApplicationFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder) =>
        InstitutionalReportsTestHostBuilder.Configure(builder, institutionalReportsEnabled: true);
}

internal static class InstitutionalReportsTestHostBuilder
{
    internal static void Configure(
        IWebHostBuilder builder,
        bool? institutionalReportsEnabled = true,
        bool useRealInstitutionalReportService = false,
        bool useDefaultRoleRollout = true,
        Action<IServiceCollection>? configureServices = null,
        Dictionary<string, string?>? extraConfig = null,
        string? inMemoryDatabaseName = null)
    {
        var config = new Dictionary<string, string?>();

        if (institutionalReportsEnabled.HasValue)
            config["FeatureFlags:InstitutionalReports"] = institutionalReportsEnabled.Value ? "true" : "false";

        if (institutionalReportsEnabled is not false)
        {
            config["ReportingRollout:EnforcementMode"] = ReportingRolloutEnforcementMode.ObserveOnly.ToString();
            config["ReportingRollout:EmergencyDisable"] = "false";

            if (useDefaultRoleRollout)
            {
                config["ReportingRollout:EnabledForRoles:0"] = "Admin";
                config["ReportingRollout:EnabledForRoles:1"] = "Supervisor";
            }
        }

        if (extraConfig is not null)
        {
            foreach (var entry in extraConfig)
                config[entry.Key] = entry.Value;
        }

        HealthTestHostBuilder.Configure(
            builder,
            services =>
            {
                if (!useRealInstitutionalReportService)
                {
                    services.RemoveAll(typeof(IInstitutionalReportService));
                    services.AddSingleton<IInstitutionalReportService, StubInstitutionalReportService>();
                }
                else
                {
                    services.AddSingleton<TrackingInstitutionalReportNumberAllocator>();
                    services.AddSingleton<IInstitutionalReportNumberAllocator>(sp =>
                        sp.GetRequiredService<TrackingInstitutionalReportNumberAllocator>());
                    services.RemoveAll<IInstitutionalReportPdfExporter>();
                    services.AddSingleton<IInstitutionalReportPdfExporter, TestInstitutionalReportPdfExporter>();
                }

                configureServices?.Invoke(services);
            },
            extraConfig: config,
            inMemoryDatabaseName: inMemoryDatabaseName);
    }
}

internal sealed class StubInstitutionalReportService : IInstitutionalReportService
{
    public Task<InstitutionalReportModel> BuildReportModelAsync(ReportBuildRequestDto request, CancellationToken ct = default) =>
        throw new NotSupportedException();

    public Task<RenderedReportManifestDto> RenderPreviewAsync(ReportBuildRequestDto request, CancellationToken ct = default) =>
        Task.FromResult(new RenderedReportManifestDto());

    public Task<ReportExportResultDto> ExportAsync(ReportExportRequestDto request, CancellationToken ct = default) =>
        Task.FromResult(new ReportExportResultDto
        {
            Content = [0x25, 0x50, 0x44, 0x46],
            ContentType = "application/pdf",
            FileName = "stub.pdf",
        });

    public Task<List<ReportTemplateDto>> GetTemplatesAsync(CancellationToken ct = default) =>
        Task.FromResult(new List<ReportTemplateDto>());

    public Task<ReportTemplateDto> SaveTemplateAsync(SaveReportTemplateRequestDto request, CancellationToken ct = default) =>
        throw new NotSupportedException();

    public Task DeleteTemplateAsync(int id, CancellationToken ct = default) =>
        Task.CompletedTask;
}
