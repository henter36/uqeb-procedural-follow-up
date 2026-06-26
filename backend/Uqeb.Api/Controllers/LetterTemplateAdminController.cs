using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Uqeb.Api.Authorization;
using Uqeb.Api.DTOs.LetterTemplates;
using Uqeb.Api.Services;

namespace Uqeb.Api.Controllers;

[ApiController]
[Route("api/letter-templates")]
[Authorize]
public class LetterTemplateAdminController : ControllerBase
{
    private readonly ILetterTemplateAdminService _admin;
    private readonly ICurrentUserService _currentUser;

    public LetterTemplateAdminController(
        ILetterTemplateAdminService admin,
        ICurrentUserService currentUser)
    {
        _admin = admin;
        _currentUser = currentUser;
    }

    [HttpGet]
    [Authorize(Policy = Policies.ManageLetterTemplates)]
    public async Task<IActionResult> List([FromQuery] LetterTemplateListRequest request, CancellationToken cancellationToken)
    {
        return Ok(await _admin.ListAsync(request, cancellationToken));
    }

    [HttpGet("{id:int}")]
    [Authorize(Policy = Policies.ManageLetterTemplates)]
    public async Task<IActionResult> GetById(int id, CancellationToken cancellationToken)
    {
        var result = await _admin.GetByIdAsync(id, cancellationToken);
        return result == null ? NotFound() : Ok(result);
    }

    [HttpPost]
    [Authorize(Policy = Policies.ManageLetterTemplates)]
    public async Task<IActionResult> Create([FromBody] CreateLetterTemplateRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var result = await _admin.CreateAsync(request, _currentUser.UserId, cancellationToken);
            return CreatedAtAction(nameof(GetById), new { id = result.Id }, result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPut("{id:int}")]
    [Authorize(Policy = Policies.ManageLetterTemplates)]
    public async Task<IActionResult> Update(int id, [FromBody] UpdateLetterTemplateAdminRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var result = await _admin.UpdateAsync(id, request, _currentUser.UserId, cancellationToken);
            return result == null ? NotFound() : Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost("{id:int}/copy")]
    [Authorize(Policy = Policies.ManageLetterTemplates)]
    public async Task<IActionResult> Copy(int id, CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _admin.CopyAsync(id, _currentUser.UserId, cancellationToken));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost("{id:int}/set-default")]
    [Authorize(Policy = Policies.ManageLetterTemplates)]
    public async Task<IActionResult> SetDefault(int id, CancellationToken cancellationToken)
    {
        try
        {
            var result = await _admin.SetDefaultAsync(id, _currentUser.UserId, cancellationToken);
            return result == null ? NotFound() : Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPatch("{id:int}/activate")]
    [Authorize(Policy = Policies.ManageLetterTemplates)]
    public async Task<IActionResult> Activate(int id, CancellationToken cancellationToken)
    {
        var result = await _admin.SetActiveAsync(id, true, _currentUser.UserId, cancellationToken);
        return result == null ? NotFound() : Ok(result);
    }

    [HttpPatch("{id:int}/deactivate")]
    [Authorize(Policy = Policies.ManageLetterTemplates)]
    public async Task<IActionResult> Deactivate(int id, CancellationToken cancellationToken)
    {
        try
        {
            var result = await _admin.SetActiveAsync(id, false, _currentUser.UserId, cancellationToken);
            return result == null ? NotFound() : Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPatch("reorder")]
    [Authorize(Policy = Policies.ManageLetterTemplates)]
    public async Task<IActionResult> Reorder([FromBody] ReorderLetterTemplatesRequest request, CancellationToken cancellationToken)
    {
        try
        {
            await _admin.ReorderAsync(request, _currentUser.UserId, cancellationToken);
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpDelete("{id:int}")]
    [Authorize(Policy = Policies.ManageLetterTemplates)]
    public async Task<IActionResult> Delete(int id, [FromQuery] int? replacementDefaultId, CancellationToken cancellationToken)
    {
        try
        {
            await _admin.DeleteAsync(id, replacementDefaultId, _currentUser.UserId, cancellationToken);
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }
}
