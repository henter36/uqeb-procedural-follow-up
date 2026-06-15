using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Uqeb.Api.Authorization;
using Uqeb.Api.DTOs.LetterTemplates;
using Uqeb.Api.Services;

namespace Uqeb.Api.Controllers;

[ApiController]
[Route("api/letter-templates")]
[Authorize]
public class LetterTemplatesController : ControllerBase
{
    private readonly ILetterTemplateService _templates;

    public LetterTemplatesController(ILetterTemplateService templates) => _templates = templates;

    [HttpGet("follow-up")]
    public async Task<IActionResult> GetFollowUp()
    {
        var result = await _templates.GetFollowUpTemplateAsync();
        return result == null ? NotFound() : Ok(result);
    }

    [HttpPut("follow-up")]
    [Authorize(Policy = Policies.CanCloseTransactions)]
    public async Task<IActionResult> UpdateFollowUp([FromBody] UpdateLetterTemplateRequest request)
    {
        try
        {
            var result = await _templates.UpdateFollowUpTemplateAsync(request.Content);
            return result == null ? NotFound() : Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }
}
