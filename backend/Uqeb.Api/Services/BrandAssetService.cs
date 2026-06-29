using Microsoft.Extensions.Logging;

namespace Uqeb.Api.Services;

public interface IBrandAssetService
{
    string? GetLogoSvgDataUri();
    string? GetLogoPngDataUri();
    string? GetPreferredLogoDataUri();
}

public sealed class BrandAssetService : IBrandAssetService
{
    private readonly IOrganizationBrandLogoProvider _logoProvider;
    private readonly ILogger<BrandAssetService> _logger;

    public BrandAssetService(
        IOrganizationBrandLogoProvider logoProvider,
        ILogger<BrandAssetService> logger)
    {
        _logoProvider = logoProvider;
        _logger = logger;
    }

    public string? GetLogoSvgDataUri()
    {
        var bytes = _logoProvider.TryGetExactLogoBytes("organization-logo.svg");
        return bytes is null ? null : $"data:image/svg+xml;base64,{Convert.ToBase64String(bytes)}";
    }

    public string? GetLogoPngDataUri()
    {
        var bytes = _logoProvider.TryGetExactLogoBytes("organization-logo.png");
        return bytes is null ? null : $"data:image/png;base64,{Convert.ToBase64String(bytes)}";
    }

    public string? GetPreferredLogoDataUri()
    {
        var dataUri = GetLogoSvgDataUri() ?? GetLogoPngDataUri();
        if (dataUri is null)
            _logger.LogWarning("لم يتم العثور على شعار المؤسسة (SVG أو PNG) لتضمينه في الخطابات.");
        return dataUri;
    }
}
