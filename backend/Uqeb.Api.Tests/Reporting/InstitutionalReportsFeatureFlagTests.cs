using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Uqeb.Api.Reporting.DTOs;
using Uqeb.Api.Reporting.Enums;
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
        bool institutionalReportsEnabled,
        bool useRealInstitutionalReportService = false,
        Action<IServiceCollection>? configureServices = null)
    {
        HealthTestHostBuilder.Configure(
            builder,
            services =>
            {
                if (!useRealInstitutionalReportService)
                {
                    services.RemoveAll(typeof(IInstitutionalReportService));
                    services.AddSingleton<IInstitutionalReportService, StubInstitutionalReportService>();
                }

                configureServices?.Invoke(services);
            },
            extraConfig: new Dictionary<string, string?>
            {
                ["FeatureFlags:InstitutionalReports"] = institutionalReportsEnabled ? "true" : "false",
            });
    }
}

internal sealed class StubInstitutionalReportService : IInstitutionalReportService
{
    public Task<InstitutionalReportModel> BuildReportModelAsync(ReportBuildRequestDto request, CancellationToken ct = default) =>
        throw new NotSupportedException();

    public Task<RenderedReportManifestDto> RenderPreviewAsync(ReportBuildRequestDto request, CancellationToken ct = default) =>
        throw new NotSupportedException();

    public Task<ReportExportResultDto> ExportAsync(ReportExportRequestDto request, CancellationToken ct = default) =>
        throw new NotSupportedException();

    public Task<List<ReportTemplateDto>> GetTemplatesAsync(CancellationToken ct = default) =>
        Task.FromResult(new List<ReportTemplateDto>());

    public Task<ReportTemplateDto> SaveTemplateAsync(SaveReportTemplateRequestDto request, CancellationToken ct = default) =>
        throw new NotSupportedException();

    public Task DeleteTemplateAsync(int id, CancellationToken ct = default) =>
        Task.CompletedTask;
}
