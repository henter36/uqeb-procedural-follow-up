using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Uqeb.Api.Authorization;
using Uqeb.Api.DTOs.LetterTemplates;
using Uqeb.Api.Services;

namespace Uqeb.Api.Controllers;

[ApiController]
[Route("api/letter-templates")]
[Authorize]
public class FollowUpLetterTemplateAdminController : ControllerBase
{
    private readonly ILetterTemplateAdminService _admin;
    private readonly ICurrentUserService _currentUser;

    public FollowUpLetterTemplateAdminController(
        ILetterTemplateAdminService admin,
        ICurrentUserService currentUser)
    {
        _admin = admin;
        _currentUser = currentUser;
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
}
