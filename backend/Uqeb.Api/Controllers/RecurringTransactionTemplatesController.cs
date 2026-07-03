using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Uqeb.Api.Authorization;
using Uqeb.Api.DTOs.RecurringTemplates;
using Uqeb.Api.Helpers;
using Uqeb.Api.Services;

namespace Uqeb.Api.Controllers;

[ApiController]
[Route("api/recurring-transaction-templates")]
[Authorize(Policy = Policies.AdminOnly)]
public class RecurringTransactionTemplatesController : ControllerBase
{
    private readonly IRecurringTransactionTemplateService _templates;
    private readonly ICurrentUserService _currentUser;

    public RecurringTransactionTemplatesController(
        IRecurringTransactionTemplateService templates,
        ICurrentUserService currentUser)
    {
        _templates = templates;
        _currentUser = currentUser;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll() => Ok(await _templates.GetAllAsync());

    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(int id)
    {
        var result = await _templates.GetByIdAsync(id);
        return result == null ? NotFound() : Ok(result);
    }

    [HttpGet("{id}/transactions")]
    public async Task<IActionResult> GetTransactions(int id)
    {
        var result = await _templates.GetTransactionsAsync(id);
        return result == null ? NotFound() : Ok(result);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateRecurringTemplateRequest request)
    {
        try
        {
            var result = await _templates.CreateAsync(request, _currentUser.UserId);
            return CreatedAtAction(nameof(GetById), new { id = result.Id }, result);
        }
        catch (FieldValidationException ex)
        {
            return BadRequest(new { message = ex.Message, errors = ex.FieldErrors });
        }
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(int id, [FromBody] CreateRecurringTemplateRequest request)
    {
        try
        {
            var result = await _templates.UpdateAsync(id, request, _currentUser.UserId);
            return result == null ? NotFound() : Ok(result);
        }
        catch (FieldValidationException ex)
        {
            return BadRequest(new { message = ex.Message, errors = ex.FieldErrors });
        }
    }

    [HttpPost("{id}/pause")]
    public async Task<IActionResult> Pause(int id)
    {
        try
        {
            var result = await _templates.PauseAsync(id, _currentUser.UserId);
            return result == null ? NotFound() : Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost("{id}/resume")]
    public async Task<IActionResult> Resume(int id)
    {
        try
        {
            var result = await _templates.ResumeAsync(id, _currentUser.UserId);
            return result == null ? NotFound() : Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost("{id}/terminate")]
    public async Task<IActionResult> Terminate(int id, [FromBody] TerminateRecurringTemplateRequest request)
    {
        try
        {
            var result = await _templates.TerminateAsync(id, request, _currentUser.UserId);
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
    }

    [HttpPost("{id}/generate")]
    public async Task<IActionResult> Generate(int id, [FromBody] GenerateRecurringTransactionRequest request)
    {
        try
        {
            var result = await _templates.GenerateAsync(id, request, _currentUser.UserId);
            return result == null ? NotFound() : Ok(result);
        }
        catch (FieldValidationException ex)
        {
            return BadRequest(new { message = ex.Message, errors = ex.FieldErrors });
        }
        catch (RecurringTemplatePeriodAlreadyGeneratedException ex)
        {
            return Conflict(new { message = ex.Message, existingTransactionId = ex.ExistingTransactionId });
        }
        catch (RecurringTemplateStateException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }
}
