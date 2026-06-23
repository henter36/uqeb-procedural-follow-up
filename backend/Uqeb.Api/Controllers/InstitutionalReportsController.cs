using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Uqeb.Api.Authorization;
using Uqeb.Api.Reporting.DTOs;
using Uqeb.Api.Reporting.Services;

namespace Uqeb.Api.Controllers;

[ApiController]
[Route("api/institutional-reports")]
[Authorize(Policy = Policies.SupervisorOrAdmin)]
public class InstitutionalReportsController : ControllerBase
{
    private readonly IInstitutionalReportService _service;

    public InstitutionalReportsController(IInstitutionalReportService service)
    {
        _service = service;
    }

    [HttpPost("preview")]
    public Task<RenderedReportManifestDto> Preview([FromBody] ReportBuildRequestDto request, CancellationToken ct) =>
        _service.RenderPreviewAsync(request, ct);

    [HttpPost("export")]
    public async Task<IActionResult> Export([FromBody] ReportExportRequestDto request, CancellationToken ct)
    {
        var result = await _service.ExportAsync(request, ct);
        return File(result.Content, result.ContentType, result.FileName);
    }

    [HttpGet("templates")]
    public Task<List<ReportTemplateDto>> GetTemplates(CancellationToken ct) =>
        _service.GetTemplatesAsync(ct);

    [HttpPost("templates")]
    public Task<ReportTemplateDto> SaveTemplate([FromBody] SaveReportTemplateRequestDto request, CancellationToken ct) =>
        _service.SaveTemplateAsync(request, ct);

    [HttpDelete("templates/{id:int}")]
    public async Task<IActionResult> DeleteTemplate(int id, CancellationToken ct)
    {
        await _service.DeleteTemplateAsync(id, ct);
        return NoContent();
    }
}
