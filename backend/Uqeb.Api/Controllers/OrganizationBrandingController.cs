using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Uqeb.Api.Services;

namespace Uqeb.Api.Controllers;

[ApiController]
[Route("api/branding")]
public class OrganizationBrandingController : ControllerBase
{
    private readonly IOrganizationBrandLogoProvider _logoProvider;

    public OrganizationBrandingController(IOrganizationBrandLogoProvider logoProvider) =>
        _logoProvider = logoProvider;

    [HttpGet("organization-logo")]
    [AllowAnonymous]
    [ResponseCache(Duration = 3600, Location = ResponseCacheLocation.Any)]
    public IActionResult GetOrganizationLogo()
    {
        var bytes = _logoProvider.TryGetLogoBytes();
        if (bytes == null || bytes.Length == 0)
            return NotFound();

        return File(bytes, "image/png");
    }
}
