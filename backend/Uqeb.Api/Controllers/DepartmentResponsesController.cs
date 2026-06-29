using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Uqeb.Api.Authorization;
using Uqeb.Api.DTOs.DepartmentResponses;
using Uqeb.Api.Services;

namespace Uqeb.Api.Controllers;

[ApiController]
[Route("api/department-responses")]
[Authorize]
public class DepartmentResponsesController : ControllerBase
{
    // 10 MB file limit plus a small multipart/form-data overhead allowance.
    // The service enforces the per-file 10 MB cap; this limit covers the full request.
    private const long AttachmentRequestSizeLimitBytes = 10_500_000L;

    private readonly IDepartmentResponseService _service;
    private readonly ICurrentUserService _currentUser;

    public DepartmentResponsesController(IDepartmentResponseService service, ICurrentUserService currentUser)
    {
        _service = service;
        _currentUser = currentUser;
    }

    [HttpGet("department-transactions")]
    [Authorize(Policy = Policies.SubmitDepartmentResponse)]
    public async Task<IActionResult> GetDepartmentTransactions()
        => Ok(await _service.GetDepartmentTransactionsAsync(_currentUser));

    [HttpGet("my")]
    [Authorize(Policy = Policies.SubmitDepartmentResponse)]
    public async Task<IActionResult> GetMyResponses()
        => Ok(await _service.GetMyDepartmentResponsesAsync(_currentUser));

    [HttpGet("my-stats")]
    [Authorize(Policy = Policies.SubmitDepartmentResponse)]
    public async Task<IActionResult> GetMyStats()
        => Ok(await _service.GetMyStatsAsync(_currentUser));

    [HttpGet("pending-review")]
    [Authorize(Policy = Policies.ReviewDepartmentResponse)]
    public async Task<IActionResult> GetPendingReview(CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _service.GetPendingReviewAsync(_currentUser, cancellationToken));
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { message = ex.Message });
        }
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetById(int id)
    {
        var dto = await _service.GetByIdAsync(id, _currentUser);
        return dto == null ? NotFound() : Ok(dto);
    }

    [HttpPost]
    [Authorize(Policy = Policies.SubmitDepartmentResponse)]
    public async Task<IActionResult> Create([FromBody] CreateDepartmentResponseRequest request)
    {
        try
        {
            var dto = await _service.CreateAsync(request, _currentUser);
            return CreatedAtAction(nameof(GetById), new { id = dto.Id }, dto);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { message = ex.Message });
        }
    }

    [HttpPut("{id:int}")]
    [Authorize(Policy = Policies.SubmitDepartmentResponse)]
    public async Task<IActionResult> Update(int id, [FromBody] UpdateDepartmentResponseRequest request)
    {
        try
        {
            return Ok(await _service.UpdateAsync(id, request, _currentUser));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { message = ex.Message });
        }
    }

    [HttpPost("{id:int}/submit")]
    [Authorize(Policy = Policies.SubmitDepartmentResponse)]
    public async Task<IActionResult> Submit(int id)
    {
        try
        {
            return Ok(await _service.SubmitAsync(id, _currentUser));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { message = ex.Message });
        }
    }

    [HttpPost("{id:int}/approve")]
    [Authorize(Policy = Policies.ReviewDepartmentResponse)]
    public async Task<IActionResult> Approve(int id)
    {
        try
        {
            return Ok(await _service.ApproveAsync(id, _currentUser));
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost("{id:int}/return")]
    [Authorize(Policy = Policies.ReviewDepartmentResponse)]
    public async Task<IActionResult> ReturnForCorrection(int id, [FromBody] ReviewDepartmentResponseRequest request)
    {
        try
        {
            return Ok(await _service.ReturnForCorrectionAsync(id, request, _currentUser));
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost("{id:int}/reject")]
    [Authorize(Policy = Policies.ReviewDepartmentResponse)]
    public async Task<IActionResult> Reject(int id, [FromBody] ReviewDepartmentResponseRequest request)
    {
        try
        {
            return Ok(await _service.RejectAsync(id, request, _currentUser));
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost("{id:int}/attachments")]
    [Authorize(Policy = Policies.SubmitDepartmentResponse)]
    [RequestSizeLimit(AttachmentRequestSizeLimitBytes)]
    public async Task<IActionResult> UploadAttachment(int id, IFormFile file)
    {
        if (Request.ContentLength is > AttachmentRequestSizeLimitBytes)
            return StatusCode(StatusCodes.Status413PayloadTooLarge, new { message = "حجم الطلب يتجاوز الحد المسموح." });

        if (file == null || file.Length == 0)
            return BadRequest(new { message = "يجب اختيار ملف" });
        try
        {
            var dto = await _service.UploadAttachmentAsync(id, file, _currentUser);
            return Ok(dto);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { message = ex.Message });
        }
    }

    [HttpDelete("{id:int}/attachments/{attachmentId:int}")]
    [Authorize(Policy = Policies.SubmitDepartmentResponse)]
    public async Task<IActionResult> DeleteAttachment(int id, int attachmentId)
    {
        try
        {
            await _service.DeleteAttachmentAsync(id, attachmentId, _currentUser);
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { message = ex.Message });
        }
    }

    [HttpGet("{id:int}/attachments/{attachmentId:int}/download")]
    public async Task<IActionResult> DownloadAttachment(int id, int attachmentId)
    {
        try
        {
            var result = await _service.DownloadAttachmentAsync(id, attachmentId, _currentUser);
            if (result == null) return NotFound();
            return File(result.Value.Content, result.Value.ContentType, result.Value.FileName);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }
}
