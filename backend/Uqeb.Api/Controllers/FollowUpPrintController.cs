using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Uqeb.Api.Authorization;
using Uqeb.Api.DTOs.FollowUpPrint;
using Uqeb.Api.Services;

namespace Uqeb.Api.Controllers;

[ApiController]
[Route("api/follow-up-print")]
[Authorize]
public class FollowUpPrintController : ControllerBase
{
    private readonly IFollowUpPrintEligibilityService _eligibility;
    private readonly IFollowUpPrintJobService _jobs;
    private readonly IFollowUpLetterPrintRecordService _records;
    private readonly IFollowUpLetterRenderService _render;
    private readonly ICurrentUserService _currentUser;

    public FollowUpPrintController(
        IFollowUpPrintEligibilityService eligibility,
        IFollowUpPrintJobService jobs,
        IFollowUpLetterPrintRecordService records,
        IFollowUpLetterRenderService render,
        ICurrentUserService currentUser)
    {
        _eligibility = eligibility;
        _jobs = jobs;
        _records = records;
        _render = render;
        _currentUser = currentUser;
    }

    [HttpGet("eligible")]
    [Authorize(Policy = Policies.CreateFollowUpPrintJob)]
    public async Task<IActionResult> GetEligible([FromQuery] FollowUpPrintFilterRequest filter, CancellationToken cancellationToken)
    {
        return Ok(await _eligibility.GetEligibleAsync(filter, _currentUser, cancellationToken));
    }

    [HttpGet("pending-summary")]
    [Authorize(Policy = Policies.ViewFollowUpPrintJobs)]
    public async Task<IActionResult> GetPendingSummary(CancellationToken cancellationToken)
    {
        return Ok(await _records.GetPendingSummaryAsync(cancellationToken));
    }

    [HttpGet("pending")]
    [Authorize(Policy = Policies.ViewFollowUpPrintJobs)]
    public async Task<IActionResult> GetPending([FromQuery] PendingPrintRecordsRequest request, CancellationToken cancellationToken)
    {
        return Ok(await _records.GetPendingListAsync(request.Page, request.PageSize, cancellationToken));
    }

    [HttpPost("jobs/preview")]
    [Authorize(Policy = Policies.CreateFollowUpPrintJob)]
    public async Task<IActionResult> PreviewJob([FromBody] CreateFollowUpPrintJobRequest request, CancellationToken cancellationToken)
    {
        return Ok(await _jobs.PreviewJobAsync(request, _currentUser, cancellationToken));
    }

    [HttpPost("jobs")]
    [Authorize(Policy = Policies.CreateFollowUpPrintJob)]
    public async Task<IActionResult> CreateJob([FromBody] CreateFollowUpPrintJobRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var job = await _jobs.CreateJobAsync(request, _currentUser, cancellationToken);
            return Accepted(job);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpGet("jobs")]
    [Authorize(Policy = Policies.ViewFollowUpPrintJobs)]
    public async Task<IActionResult> ListJobs([FromQuery] int page = 1, [FromQuery] int pageSize = 25, CancellationToken cancellationToken = default)
    {
        return Ok(await _jobs.ListJobsAsync(page, pageSize, _currentUser, cancellationToken));
    }

    [HttpGet("jobs/{id:int}")]
    [Authorize(Policy = Policies.ViewFollowUpPrintJobs)]
    public async Task<IActionResult> GetJob(int id, CancellationToken cancellationToken)
    {
        var job = await _jobs.GetJobAsync(id, cancellationToken);
        return job == null ? NotFound() : Ok(job);
    }

    [HttpPost("jobs/{id:int}/cancel")]
    [Authorize(Policy = Policies.CancelFollowUpPrintJob)]
    public async Task<IActionResult> CancelJob(int id, CancellationToken cancellationToken)
    {
        try
        {
            var job = await _jobs.CancelJobAsync(id, _currentUser, cancellationToken);
            return job == null ? NotFound() : Ok(job);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost("jobs/{id:int}/retry")]
    [Authorize(Policy = Policies.RetryFollowUpPrintJob)]
    public async Task<IActionResult> RetryJob(int id, CancellationToken cancellationToken)
    {
        try
        {
            var job = await _jobs.RetryJobAsync(id, _currentUser, cancellationToken);
            return job == null ? NotFound() : Ok(job);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpGet("jobs/{jobId:int}/parts/{partNumber:int}/print-view")]
    [Authorize(Policy = Policies.PrintFollowUpLetters)]
    public async Task<IActionResult> GetPartPrintView(int jobId, int partNumber, CancellationToken cancellationToken)
    {
        var html = await _jobs.GetPartPrintViewHtmlAsync(jobId, partNumber, _currentUser, cancellationToken);
        return html == null ? NotFound() : Content(html, "text/html; charset=utf-8");
    }

    [HttpPost("jobs/{jobId:int}/parts/{partNumber:int}/mark-print-requested")]
    [Authorize(Policy = Policies.PrintFollowUpLetters)]
    public async Task<IActionResult> MarkPartPrintRequested(int jobId, int partNumber, CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _jobs.MarkPartPrintRequestedAsync(jobId, partNumber, _currentUser, cancellationToken));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost("records/{id:int}/confirm")]
    [Authorize(Policy = Policies.PrintFollowUpLetters)]
    public async Task<IActionResult> ConfirmPrint(int id, CancellationToken cancellationToken)
    {
        var record = await _records.ConfirmPrintAsync(id, _currentUser, cancellationToken);
        return record == null ? NotFound() : Ok(record);
    }

    [HttpPost("records/{id:int}/cancel")]
    [Authorize(Policy = Policies.CancelFollowUpPrintRecord)]
    public async Task<IActionResult> CancelRecord(int id, [FromBody] CancelPrintRecordRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var record = await _records.CancelRecordAsync(id, request.Reason, _currentUser, cancellationToken);
            return record == null ? NotFound() : Ok(record);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost("records/{id:int}/link-follow-up")]
    [Authorize(Policy = Policies.RegisterPrintedFollowUp)]
    public async Task<IActionResult> LinkFollowUp(int id, [FromBody] LinkPrintRecordRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var record = await _records.LinkToFollowUpAsync(id, request.FollowUpId, _currentUser, cancellationToken);
            return record == null ? NotFound() : Ok(record);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost("records/{id:int}/reprint")]
    [Authorize(Policy = Policies.PrintFollowUpLetters)]
    public async Task<IActionResult> Reprint(int id, CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _records.ReprintAsync(id, _currentUser, cancellationToken));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost("transactions/{transactionId:int}/print-view")]
    [Authorize(Policy = Policies.PrintFollowUpLetters)]
    public async Task<IActionResult> GetTransactionPrintView(int transactionId, [FromBody] FollowUpLetterPrintRequest? request, CancellationToken cancellationToken)
    {
        var html = await _render.GeneratePrintViewHtmlAsync(
            transactionId,
            request?.TargetEntity,
            request?.Content,
            _currentUser,
            request?.TemplateId,
            cancellationToken);
        return html == null ? NotFound() : Content(html, "text/html; charset=utf-8");
    }
}

public class FollowUpLetterPrintRequest
{
    public string? TargetEntity { get; set; }
    public string? Content { get; set; }
    public int? TemplateId { get; set; }
}
