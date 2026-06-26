using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Uqeb.Api.Helpers;
using Uqeb.Api.Services;

namespace Uqeb.Api.Controllers;

[ApiController]
[Route("api/letter-templates")]
[Authorize]
public class FollowUpLetterTemplateController : ControllerBase
{
    private readonly ILetterTemplateService _templates;

    public FollowUpLetterTemplateController(ILetterTemplateService templates)
    {
        _templates = templates;
    }

    [HttpGet("follow-up")]
    public async Task<IActionResult> GetFollowUp()
    {
        var result = await _templates.GetFollowUpTemplateAsync();
        return result == null ? NotFound() : Ok(result);
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
