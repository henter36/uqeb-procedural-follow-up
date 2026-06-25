using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Uqeb.Api.Authorization;
using Uqeb.Api.DTOs.LetterTemplates;
using Uqeb.Api.Helpers;
using Uqeb.Api.Services;

namespace Uqeb.Api.Controllers;

[ApiController]
[Route("api/letter-templates")]
[Authorize]
public class FollowUpLetterTemplateController : ControllerBase
{
    private readonly ILetterTemplateService _templates;
    private readonly ILetterTemplateAdminService _admin;
    private readonly ICurrentUserService _currentUser;

    public FollowUpLetterTemplateController(
        ILetterTemplateService templates,
        ILetterTemplateAdminService admin,
        ICurrentUserService currentUser)
    {
        _templates = templates;
        _admin = admin;
        _currentUser = currentUser;
    }

    [HttpGet("follow-up")]
    public async Task<IActionResult> GetFollowUp()
    {
        var result = await _templates.GetFollowUpTemplateAsync();
        return result == null ? NotFound() : Ok(result);
    }

    [HttpPut("follow-up")]
    [Authorize(Policy = Policies.ManageLetterTemplates)]
    public async Task<IActionResult> UpdateFollowUp([FromBody] UpdateLetterTemplateRequest request)
    {
        try
        {
            var result = await _admin.UpdateDefaultFollowUpTemplateAsync(request.Content, _currentUser.UserId);
            return result == null ? NotFound() : Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpGet("variables")]
    public IActionResult GetVariables()
    {
        var items = FollowUpLetterVariableRegistry.All.Select(v => new
        {
            v.Name,
            v.ArabicDescription,
            v.Example,
            v.MayBeEmpty,
        });
        return Ok(items);
    }
}
