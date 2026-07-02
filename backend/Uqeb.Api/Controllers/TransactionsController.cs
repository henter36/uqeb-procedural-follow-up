using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Uqeb.Api.DTOs.LetterTemplates;
using Uqeb.Api.DTOs.Transactions;
using Uqeb.Api.Authorization;
using Uqeb.Api.Helpers;
using Uqeb.Api.Models.Enums;
using Uqeb.Api.Services;

namespace Uqeb.Api.Controllers;

[ApiController]
[Route("api/transactions")]
[Authorize]
public class TransactionsController : ControllerBase
{
    // Admin-only xlsx import; caps upload size to reduce DoS risk from large multipart payloads.
    private const long MaxExcelImportFileBytes = 5 * 1024 * 1024;

    private readonly ITransactionService _transactions;
    private readonly IAttachmentService _attachments;
    private readonly ILetterTemplateService _letterTemplates;
    private readonly IExcelTransactionImportService _excelImport;
    private readonly ICurrentUserService _currentUser;

    public TransactionsController(
        ITransactionService transactions,
        IAttachmentService attachments,
        ILetterTemplateService letterTemplates,
        IExcelTransactionImportService excelImport,
        ICurrentUserService currentUser)
    {
        _transactions = transactions;
        _attachments = attachments;
        _letterTemplates = letterTemplates;
        _excelImport = excelImport;
        _currentUser = currentUser;
    }

    [HttpGet]
    public async Task<IActionResult> Search([FromQuery] TransactionSearchRequest request)
    {
        try
        {
            return Ok(await _transactions.SearchAsync(request, _currentUser));
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { message = ex.Message });
        }
        catch (InvalidTransactionSearchCursorException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpGet("{id}/basic")]
    public async Task<IActionResult> GetBasic(int id)
    {
        try
        {
            var result = await _transactions.GetBasicByIdAsync(id, _currentUser);
            return result == null ? NotFound() : Ok(result);
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { message = ex.Message });
        }
    }

    [HttpGet("{id}/workspace")]
    public async Task<IActionResult> GetWorkspace(int id)
    {
        try
        {
            var result = await _transactions.GetWorkspaceAsync(id, _currentUser);
            return result == null ? NotFound() : Ok(result);
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { message = ex.Message });
        }
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(int id)
    {
        try
        {
            var result = await _transactions.GetByIdAsync(id, _currentUser);
            return result == null ? NotFound() : Ok(result);
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { message = ex.Message });
        }
    }

    [HttpPost]
    [Authorize(Policy = Policies.CanEditTransactions)]
    public async Task<IActionResult> Create([FromBody] CreateTransactionRequest request)
    {
        try
        {
            var result = await _transactions.CreateAsync(request, _currentUser.UserId);
            return CreatedAtAction(nameof(GetById), new { id = result.Id }, result);
        }
        catch (FieldValidationException ex)
        {
            return BadRequest(new { message = ex.Message, errors = ex.FieldErrors });
        }
        catch (DuplicateIncomingNumberException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (DuplicateTrackingNumberException ex)
        {
            return Conflict(new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPut("{id}")]
    [Authorize(Policy = Policies.CanEditTransactions)]
    public async Task<IActionResult> Update(int id, [FromBody] UpdateTransactionRequest request)
    {
        try
        {
            var result = await _transactions.UpdateAsync(id, request, _currentUser.UserId, _currentUser.Role);
            return result == null ? NotFound() : Ok(result);
        }
        catch (FieldValidationException ex)
        {
            return BadRequest(new { message = ex.Message, errors = ex.FieldErrors });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (UnauthorizedAccessException ex)
        {
            return Forbid(ex.Message);
        }
    }

    [HttpPost("{id}/cancel")]
    [Authorize(Policy = "SupervisorOrAdmin")]
    public async Task<IActionResult> Cancel(int id)
    {
        try
        {
            return await _transactions.CancelAsync(id, _currentUser.UserId, _currentUser.Role) ? Ok() : NotFound();
        }
        catch (UnauthorizedAccessException ex)
        {
            return Forbid(ex.Message);
        }
    }

    [HttpPost("{id}/archive")]
    [Authorize(Policy = "SupervisorOrAdmin")]
    public async Task<IActionResult> Archive(int id)
    {
        try
        {
            return await _transactions.ArchiveAsync(id, _currentUser.UserId, _currentUser.Role) ? Ok() : NotFound();
        }
        catch (UnauthorizedAccessException ex)
        {
            return Forbid(ex.Message);
        }
    }

    [HttpPost("{id:int}/complete-response")]
    [Authorize(Policy = "SupervisorOrAdmin")]
    public async Task<IActionResult> CompleteResponse(int id, [FromBody] CompleteResponseRequest request)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        try
        {
            var result = await _transactions.CompleteResponseAsync(id, request, _currentUser);
            return result == null
                ? NotFound(new { message = "المعاملة غير موجودة" })
                : Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (UnauthorizedAccessException ex)
        {
            return Forbid(ex.Message);
        }
    }

    [HttpPost("{id}/close")]
    [Authorize(Policy = "CanCloseTransactions")]
    public async Task<IActionResult> Close(int id)
    {
        try
        {
            return await _transactions.CloseAsync(id, _currentUser.UserId, _currentUser.Role) ? Ok() : NotFound();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (UnauthorizedAccessException ex)
        {
            return Forbid(ex.Message);
        }
    }

    [HttpGet("{id}/followups")]
    public async Task<IActionResult> GetFollowUps(int id)
    {
        try
        {
            var result = await _transactions.GetFollowUpsAsync(id, _currentUser);
            return result == null ? NotFound() : Ok(result);
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { message = ex.Message });
        }
    }

    [HttpGet("{id}/followup-departments")]
    public async Task<IActionResult> GetFollowUpDepartments(int id)
    {
        try
        {
            var result = await _transactions.GetFollowUpDepartmentsAsync(id, _currentUser);
            return result == null ? NotFound() : Ok(result);
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { message = ex.Message });
        }
    }

    [HttpPost("{id}/followups")]
    [Authorize(Policy = Policies.CanEditTransactions)]
    public async Task<IActionResult> AddFollowUp(int id, [FromBody] CreateFollowUpRequest request)
    {
        try
        {
            var result = await _transactions.AddFollowUpAsync(id, request, _currentUser.UserId);
            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost("{id}/followups/{followUpId}/reply")]
    [Authorize(Policy = Policies.CanEditTransactions)]
    public async Task<IActionResult> ReplyFollowUp(int id, int followUpId, [FromBody] ReplyFollowUpRequest request)
    {
        var result = await _transactions.ReplyFollowUpAsync(id, followUpId, request, _currentUser.UserId);
        return result == null ? NotFound() : Ok(result);
    }

    [HttpGet("{id}/assignments")]
    public async Task<IActionResult> GetAssignments(int id)
    {
        try
        {
            var result = await _transactions.GetAssignmentsAsync(id, _currentUser);
            return result == null ? NotFound() : Ok(result);
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { message = ex.Message });
        }
    }

    [HttpPost("{id}/assignments")]
    [Authorize(Policy = Policies.CanEditTransactions)]
    public async Task<IActionResult> AddAssignment(int id, [FromBody] CreateAssignmentRequest request)
    {
        try
        {
            var result = await _transactions.AddAssignmentAsync(id, request, _currentUser.UserId);
            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost("{id}/assignments/{assignmentId}/reply")]
    [Authorize(Policy = Policies.CanEditTransactions)]
    public async Task<IActionResult> ReplyAssignment(int id, int assignmentId, [FromBody] ReplyAssignmentRequest request)
    {
        try
        {
            var result = await _transactions.ReplyAssignmentAsync(id, assignmentId, request, _currentUser);
            return result == null ? NotFound() : Ok(result);
        }
        catch (UnauthorizedAccessException ex)
        {
            return Forbid(ex.Message);
        }
    }

    [HttpPatch("{id}/assignments/{assignmentId}")]
    [Authorize(Policy = Policies.AdminOnly)]
    public async Task<IActionResult> AdminEditAssignment(int id, int assignmentId, [FromBody] AdminEditAssignmentRequest request)
    {
        var result = await _transactions.AdminEditAssignmentAsync(id, assignmentId, request, _currentUser.UserId);
        return result == null ? NotFound() : Ok(result);
    }

    [HttpGet("{id}/attachments")]
    public async Task<IActionResult> GetAttachments(int id)
    {
        return Ok(await _attachments.GetByTransactionAsync(id));
    }

    [HttpPost("{id}/attachments")]
    [Authorize(Policy = Policies.CanEditTransactions)]
    [RequestSizeLimit(50_000_000)]
    public async Task<IActionResult> UploadAttachment(int id, IFormFile file, [FromForm] string? attachmentType)
    {
        if (file == null || file.Length == 0)
            return BadRequest(new { message = "يجب اختيار ملف" });
        try
        {
            var result = await _attachments.UploadAsync(id, file, attachmentType, _currentUser.UserId);
            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpGet("{id}/attachments/{attachmentId}/download")]
    public async Task<IActionResult> DownloadAttachment(int id, int attachmentId)
    {
        var result = await _attachments.DownloadAsync(id, attachmentId);
        if (result == null) return NotFound();
        return File(result.Value.Content, result.Value.ContentType, result.Value.FileName);
    }

    [HttpGet("{id}/audit-log")]
    public async Task<IActionResult> GetAuditLog(int id, [FromQuery] int page = 1, [FromQuery] int pageSize = 50)
    {
        try
        {
            return Ok(await _transactions.GetAuditLogAsync(id, page, pageSize, _currentUser));
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { message = ex.Message });
        }
    }

    [HttpPost("{id}/follow-up-letter/preview")]
    [Authorize(Policy = Policies.CanEditTransactions)]
    public async Task<IActionResult> PreviewFollowUpLetter(int id, [FromBody] FollowUpLetterRequest? request)
    {
        var result = await _letterTemplates.RenderFollowUpLetterAsync(
            id,
            request?.TargetEntity,
            request?.Content,
            _currentUser);
        return result == null ? NotFound() : Ok(result);
    }

    [HttpPost("{id}/follow-up-letter/pdf")]
    [Authorize(Policy = Policies.CanEditTransactions)]
    public async Task<IActionResult> DownloadFollowUpLetterPdf(int id, [FromBody] FollowUpLetterRequest? request)
    {
        var bytes = await _letterTemplates.GenerateFollowUpLetterPdfAsync(
            id,
            request?.TargetEntity,
            request?.Content,
            _currentUser);
        if (bytes == null) return NotFound();
        return File(bytes, "application/pdf", $"follow-up-letter-{id}.pdf");
    }

    [HttpPost("import/excel/preview")]
    [Authorize(Policy = Policies.AdminOnly)]
    [RequestSizeLimit(MaxExcelImportFileBytes)]
    public async Task<IActionResult> PreviewExcelImport(IFormFile file, CancellationToken cancellationToken)
    {
        var validationError = ValidateExcelFile(file);
        if (validationError != null)
            return BadRequest(new { message = validationError });

        try
        {
            await using var stream = file.OpenReadStream();
            return Ok(await _excelImport.PreviewAsync(stream, cancellationToken));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost("import/excel/commit")]
    [Authorize(Policy = Policies.AdminOnly)]
    [RequestSizeLimit(MaxExcelImportFileBytes)]
    public async Task<IActionResult> CommitExcelImport(IFormFile file, CancellationToken cancellationToken)
    {
        var validationError = ValidateExcelFile(file);
        if (validationError != null)
            return BadRequest(new { message = validationError });

        try
        {
            await using var stream = file.OpenReadStream();
            return Ok(await _excelImport.CommitAsync(stream, _currentUser, cancellationToken));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    private static string? ValidateExcelFile(IFormFile? file)
    {
        if (file == null || file.Length == 0)
            return "يجب اختيار ملف Excel";

        if (file.Length > MaxExcelImportFileBytes)
            return "حجم الملف يتجاوز الحد المسموح للاستيراد (5 ميجابايت)";

        var extension = Path.GetExtension(file.FileName);
        if (!string.Equals(extension, ".xlsx", StringComparison.OrdinalIgnoreCase))
            return "يُقبل ملفات xlsx فقط";

        return null;
    }
}
