using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Uqeb.Api.Authorization;
using Uqeb.Api.DTOs.LetterTemplates;
using Uqeb.Api.Services;

namespace Uqeb.Api.Controllers;

[ApiController]
[Route("api/letter-templates")]
[Authorize]
public class LetterTemplatePreviewController : ControllerBase
{
    private readonly ILetterTemplateAdminService _admin;
    private readonly IFollowUpLetterRenderService _render;

    public LetterTemplatePreviewController(
        ILetterTemplateAdminService admin,
        IFollowUpLetterRenderService render)
    {
        _admin = admin;
        _render = render;
    }

    [HttpPost("validate")]
    [Authorize(Policy = Policies.ManageLetterTemplates)]
    public IActionResult Validate([FromBody] UpdateLetterTemplateRequest request)
    {
        return Ok(_admin.ValidateContent(request.Content));
    }

    [HttpPost("preview")]
    [Authorize(Policy = Policies.ManageLetterTemplates)]
    public async Task<IActionResult> Preview([FromBody] LetterTemplatePreviewRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var html = await _render.GenerateTemplatePreviewHtmlAsync(request, cancellationToken);
            return Ok(new LetterTemplatePreviewResponse { Html = html });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }
}
