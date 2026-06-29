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
        var svgBytes = _logoProvider.TryGetExactLogoBytes("organization-logo.svg");
        if (svgBytes != null && svgBytes.Length > 0)
            return File(svgBytes, "image/svg+xml");

        var pngBytes = _logoProvider.TryGetLogoBytes();
        if (pngBytes == null || pngBytes.Length == 0)
            return NotFound();

        return File(pngBytes, "image/png");
    }
}
