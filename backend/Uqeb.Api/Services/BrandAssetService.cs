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
    private readonly Lazy<string?> _svgDataUri;
    private readonly Lazy<string?> _pngDataUri;
    private readonly Lazy<string?> _preferredDataUri;

    public BrandAssetService(
        IOrganizationBrandLogoProvider logoProvider,
        ILogger<BrandAssetService> logger)
    {
        _svgDataUri = new Lazy<string?>(() =>
        {
            var bytes = logoProvider.TryGetExactLogoBytes("organization-logo.svg");
            return bytes is null || bytes.Length == 0
                ? null
                : $"data:image/svg+xml;base64,{Convert.ToBase64String(bytes)}";
        });

        _pngDataUri = new Lazy<string?>(() =>
        {
            var bytes = logoProvider.TryGetExactLogoBytes("organization-logo.png");
            return bytes is null || bytes.Length == 0
                ? null
                : $"data:image/png;base64,{Convert.ToBase64String(bytes)}";
        });

        _preferredDataUri = new Lazy<string?>(() =>
        {
            var dataUri = GetLogoSvgDataUri() ?? GetLogoPngDataUri();
            if (dataUri is null)
                logger.LogWarning("لم يتم العثور على شعار المؤسسة (SVG أو PNG) لتضمينه في الخطابات.");
            return dataUri;
        });
    }

    public string? GetLogoSvgDataUri() => _svgDataUri.Value;

    public string? GetLogoPngDataUri() => _pngDataUri.Value;

    public string? GetPreferredLogoDataUri() => _preferredDataUri.Value;
}
