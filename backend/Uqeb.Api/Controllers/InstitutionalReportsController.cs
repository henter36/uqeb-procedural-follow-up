using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Uqeb.Api.Authorization;
using Uqeb.Api.Helpers;
using Uqeb.Api.Middleware;
using Uqeb.Api.Reporting.DTOs;
using Uqeb.Api.Reporting.Operations;
using Uqeb.Api.Reporting.Services;

namespace Uqeb.Api.Controllers;

[ApiController]
[Route("api/institutional-reports")]
[Authorize(Policy = Policies.SupervisorOrAdmin)]
public class InstitutionalReportsController : ControllerBase
{
    private readonly IInstitutionalReportService _service;
    private readonly ILogger<InstitutionalReportsController> _logger;

    public InstitutionalReportsController(
        IInstitutionalReportService service,
        ILogger<InstitutionalReportsController> logger)
    {
        _service = service;
        _logger = logger;
    }

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
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            var correlationId = HttpContext.Items[CorrelationIdMiddleware.ItemKey] as string;

            _logger.LogError(
                ex,
                "Institutional report preview failed. PreviewStage=unknown ExceptionType={ExceptionType} CorrelationId={CorrelationId}",
                ex.GetType().Name,
                correlationId);

            return StatusCode(
                StatusCodes.Status500InternalServerError,
                new
                {
                    errorCode = "institutional_report_preview_failed",
                    message = "تعذر إنشاء معاينة التقرير.",
                    correlationId,
                });
        }
    }

    [HttpPost("export")]
    public async Task<IActionResult> Export([FromBody] ReportExportRequestDto request, CancellationToken ct)
    {
        var correlationId = HttpContext.Items[CorrelationIdMiddleware.ItemKey] as string;

        try
        {
            var result = await _service.ExportAsync(request, ct);
            return File(result.Content, result.ContentType, result.FileName);
        }
        catch (FieldValidationException ex)
        {
            return ToValidationProblem(ex);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (ReportingExportRejectedException ex)
        {
            if (ex.RetryAfterSeconds is int retryAfter)
                Response.Headers.RetryAfter = retryAfter.ToString();

            return StatusCode(ex.StatusCode, new
            {
                errorCode = ex.ErrorCode,
                message = ex.Message,
                correlationId,
            });
        }
        catch (ReportingConfigurationException ex)
        {
            return StatusCode(ex.StatusCode, new
            {
                errorCode = ex.ErrorCode,
                message = ex.Message,
                correlationId,
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Institutional report export failed. ExportFormat={ExportFormat} ReportType={ReportType} ExceptionType={ExceptionType} CorrelationId={CorrelationId}",
                request.ExportFormat,
                request.BuildRequest.ReportType,
                ex.GetType().Name,
                correlationId);

            return StatusCode(
                StatusCodes.Status500InternalServerError,
                new
                {
                    errorCode = "institutional_report_export_failed",
                    message = "تعذر تصدير التقرير.",
                    correlationId,
                });
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
