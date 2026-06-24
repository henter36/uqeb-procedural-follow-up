using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Uqeb.Api.Controllers;
using Uqeb.Api.Helpers;
using Uqeb.Api.Reporting.DTOs;
using Uqeb.Api.Reporting.Enums;
using Uqeb.Api.Reporting.Services;
using Xunit;

namespace Uqeb.Api.Tests.Reporting;

public class InstitutionalReportsControllerExportTests
{
    [Fact]
    public async Task Export_ReturnsValidationProblem_WhenServiceThrowsFieldValidation()
    {
        var service = new ThrowingInstitutionalReportService();
        var controller = new InstitutionalReportsController(
            service,
            NullLogger<InstitutionalReportsController>.Instance)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext(),
            },
        };

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

    private sealed class ThrowingInstitutionalReportService : IInstitutionalReportService
    {
        public Task<InstitutionalReportModel> BuildReportModelAsync(ReportBuildRequestDto request, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<RenderedReportManifestDto> RenderPreviewAsync(ReportBuildRequestDto request, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<ReportExportResultDto> ExportAsync(ReportExportRequestDto request, CancellationToken ct = default) =>
            throw new FieldValidationException(new Dictionary<string, string>
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
