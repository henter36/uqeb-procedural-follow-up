using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Uqeb.Api.Controllers;
using Uqeb.Api.Helpers;
using Uqeb.Api.Middleware;
using Uqeb.Api.Reporting.DTOs;
using Uqeb.Api.Reporting.Enums;
using Uqeb.Api.Reporting.Exporters;
using Uqeb.Api.Reporting.Operations;
using Uqeb.Api.Reporting.Services;
using Xunit;

namespace Uqeb.Api.Tests.Reporting;

public class InstitutionalReportsControllerExportTests
{
    [Fact]
    public async Task Export_ReturnsValidationProblem_WhenServiceThrowsFieldValidation()
    {
        var service = new ThrowingInstitutionalReportService();
        var controller = CreateController(service);

        var result = await controller.Export(new ReportExportRequestDto
        {
            ExportMode = ExportMode.SelectedPages,
            PageRangeExpression = "abc",
            BuildRequest = new ReportBuildRequestDto
            {
                ReportType = InstitutionalReportType.ExecutiveComprehensive,
                SectionIds = [ReportSectionId.Cover],
            },
        }, CancellationToken.None);

        var objectResult = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal(StatusCodes.Status400BadRequest, objectResult.StatusCode);
    }

    [Fact]
    public async Task Export_ReturnsStructuredJson_WhenUnexpectedFailureOccurs()
    {
        var service = new ThrowingInstitutionalReportService
        {
            ExceptionToThrow = new InvalidOperationException("boom"),
        };
        var controller = CreateController(service, "corr-export-500");

        var result = await controller.Export(new ReportExportRequestDto
        {
            ExportFormat = ExportFormat.Pdf,
            BuildRequest = new ReportBuildRequestDto
            {
                ReportType = InstitutionalReportType.ExecutiveComprehensive,
                SectionIds = [ReportSectionId.Cover],
            },
        }, CancellationToken.None);

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status500InternalServerError, objectResult.StatusCode);
        using var doc = System.Text.Json.JsonDocument.Parse(System.Text.Json.JsonSerializer.Serialize(objectResult.Value));
        Assert.Equal("institutional_report_export_failed", doc.RootElement.GetProperty("errorCode").GetString());
        Assert.Equal("تعذر تصدير التقرير.", doc.RootElement.GetProperty("message").GetString());
        Assert.Equal("corr-export-500", doc.RootElement.GetProperty("correlationId").GetString());
    }

    [Fact]
    public async Task Export_ReturnsConfigurationJson_WhenChromiumUnavailable()
    {
        var service = new ThrowingInstitutionalReportService
        {
            ExceptionToThrow = new ReportingConfigurationException(
                ReportingErrorCodes.ChromiumUnavailable,
                "متصفح Chromium غير متاح لتصدير PDF."),
        };
        var controller = CreateController(service, "corr-chromium");

        var result = await controller.Export(new ReportExportRequestDto
        {
            ExportFormat = ExportFormat.Pdf,
            BuildRequest = new ReportBuildRequestDto
            {
                ReportType = InstitutionalReportType.ExecutiveComprehensive,
                SectionIds = [ReportSectionId.Cover],
            },
        }, CancellationToken.None);

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, objectResult.StatusCode);
        using var doc = System.Text.Json.JsonDocument.Parse(System.Text.Json.JsonSerializer.Serialize(objectResult.Value));
        Assert.Equal(ReportingErrorCodes.ChromiumUnavailable, doc.RootElement.GetProperty("errorCode").GetString());
        Assert.Contains("Chromium", doc.RootElement.GetProperty("message").GetString());
        Assert.Equal("corr-chromium", doc.RootElement.GetProperty("correlationId").GetString());
    }

    private static InstitutionalReportsController CreateController(
        IInstitutionalReportService service,
        string? correlationId = null)
    {
        var httpContext = new DefaultHttpContext();
        if (correlationId is not null)
            httpContext.Items[CorrelationIdMiddleware.ItemKey] = correlationId;

        return new InstitutionalReportsController(
            service,
            NullLogger<InstitutionalReportsController>.Instance)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = httpContext,
            },
        };
    }

    private sealed class ThrowingInstitutionalReportService : IInstitutionalReportService
    {
        public Exception? ExceptionToThrow { get; init; }

        public Task<InstitutionalReportModel> BuildReportModelAsync(ReportBuildRequestDto request, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<RenderedReportManifestDto> RenderPreviewAsync(ReportBuildRequestDto request, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<ReportExportResultDto> ExportAsync(ReportExportRequestDto request, CancellationToken ct = default) =>
            throw ExceptionToThrow ?? new FieldValidationException(new Dictionary<string, string>
            {
                ["pageRangeExpression"] = "صيغة الصفحات غير صالحة.",
            });

        public Task<List<ReportTemplateDto>> GetTemplatesAsync(CancellationToken ct = default) =>
            Task.FromResult(new List<ReportTemplateDto>());

        public Task<ReportTemplateDto> SaveTemplateAsync(SaveReportTemplateRequestDto request, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task DeleteTemplateAsync(int id, CancellationToken ct = default) =>
            Task.CompletedTask;
    }
}

public class InstitutionalReportExportFormatTests
{
  [Theory]
  [InlineData(ExportFormat.Html, "text/html")]
  [InlineData(ExportFormat.Xlsx, "spreadsheetml")]
  [InlineData(ExportFormat.Docx, "wordprocessingml")]
  public async Task ExportAsync_SucceedsWithoutChromium_ForNonPdfFormats(ExportFormat format, string contentTypeFragment)
  {
      var service = InstitutionalReportServiceTestHelpers.CreateService();
      var result = await service.ExportAsync(new ReportExportRequestDto
      {
          ExportFormat = format,
          BuildRequest = new ReportBuildRequestDto
          {
              ReportType = InstitutionalReportType.ExecutiveComprehensive,
              SectionIds = [ReportSectionId.Cover, ReportSectionId.ExecutiveSummary],
          },
      });

      Assert.NotEmpty(result.Content);
      Assert.Contains(contentTypeFragment, result.ContentType, StringComparison.OrdinalIgnoreCase);
  }

  [Fact]
  public async Task ExportAsync_Pdf_ThrowsConfigurationError_WhenChromiumUnavailable()
  {
      var unavailableProbe = new UnavailableChromiumProbe();
      var pdfExporter = new InstitutionalReportPlaywrightPdfExporter(
          unavailableProbe,
          NullLogger<InstitutionalReportPlaywrightPdfExporter>.Instance);
      var service = InstitutionalReportServiceTestHelpers.CreateService(pdfExporter: pdfExporter);

      var ex = await Assert.ThrowsAsync<ReportingConfigurationException>(() => service.ExportAsync(new ReportExportRequestDto
      {
          ExportFormat = ExportFormat.Pdf,
          BuildRequest = new ReportBuildRequestDto
          {
              ReportType = InstitutionalReportType.ExecutiveComprehensive,
              SectionIds = [ReportSectionId.Cover],
          },
      }));

      Assert.Equal(ReportingErrorCodes.ChromiumUnavailable, ex.ErrorCode);
  }

  private sealed class UnavailableChromiumProbe : IReportingChromiumProbe
  {
      public Task<ReportingChromiumProbeResult> ProbeAsync(CancellationToken cancellationToken = default) =>
          Task.FromResult(new ReportingChromiumProbeResult
          {
              State = ReportingChromiumProbeState.ExecutableMissing,
              ExecutableAvailable = false,
              LaunchSuccessful = false,
              Summary = "Chromium executable is not installed.",
          });

      public Task<ReportingChromiumProbeResult> ProbeLaunchOnlyAsync(CancellationToken cancellationToken = default) =>
          ProbeAsync(cancellationToken);
  }
}
