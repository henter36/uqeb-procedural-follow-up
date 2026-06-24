using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Uqeb.Api.Authorization;
using Uqeb.Api.Helpers;
using Uqeb.Api.Reporting.DTOs;
using Uqeb.Api.Reporting.Services;

namespace Uqeb.Api.Controllers;

[ApiController]
[Route("api/institutional-reports")]
[Authorize(Policy = Policies.SupervisorOrAdmin)]
public class InstitutionalReportsController : ControllerBase
{
    private readonly IInstitutionalReportService _service;
    private readonly IReportingReadinessService _readiness;

    public InstitutionalReportsController(
        IInstitutionalReportService service,
        IReportingReadinessService readiness)
    {
        _service = service;
        _readiness = readiness;
    }

    [HttpGet("configuration")]
    public ActionResult<ReportingConfigurationDto> GetConfiguration() =>
        Ok(_readiness.GetConfiguration());

    [HttpGet("readiness")]
    public ActionResult<ReportingReadinessDto> GetReadiness() =>
        Ok(_readiness.GetReadiness());

    [HttpPost("preview")]
    public async Task<IActionResult> Preview([FromBody] ReportBuildRequestDto request, CancellationToken ct)
    {
        try
        {
            return Ok(await _service.RenderPreviewAsync(request, ct));
        }
        catch (FieldValidationException ex)
        {
            return ToValidationProblem(ex);
        }
    }

    [HttpPost("export")]
    public async Task<IActionResult> Export([FromBody] ReportExportRequestDto request, CancellationToken ct)
    {
        try
        {
            var result = await _service.ExportAsync(request, ct);
            return File(result.Content, result.ContentType, result.FileName);
        }
        catch (FieldValidationException ex)
        {
            return ToValidationProblem(ex);
        }
    }

    [HttpGet("templates")]
    public Task<List<ReportTemplateDto>> GetTemplates(CancellationToken ct) =>
        _service.GetTemplatesAsync(ct);

    [HttpPost("templates")]
    public async Task<IActionResult> SaveTemplate([FromBody] SaveReportTemplateRequestDto request, CancellationToken ct)
    {
        try
        {
            return Ok(await _service.SaveTemplateAsync(request, ct));
        }
        catch (FieldValidationException ex)
        {
            return ToValidationProblem(ex);
        }
    }

    [HttpDelete("templates/{id:int}")]
    public async Task<IActionResult> DeleteTemplate(int id, CancellationToken ct)
    {
        await _service.DeleteTemplateAsync(id, ct);
        return NoContent();
    }

    private static BadRequestObjectResult ToValidationProblem(FieldValidationException ex) =>
        new(new ValidationProblemDetails(
            ex.FieldErrors.ToDictionary(k => k.Key, v => new[] { v.Value }))
        {
            Title = ex.Message,
            Status = StatusCodes.Status400BadRequest
        });
}
