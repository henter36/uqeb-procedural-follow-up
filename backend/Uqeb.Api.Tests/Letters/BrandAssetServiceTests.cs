using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Uqeb.Api.Configuration;
using Uqeb.Api.Helpers;
using Uqeb.Api.Services;
using Xunit;

namespace Uqeb.Api.Tests.Letters;

public class BrandAssetServiceTests : IDisposable
{
    private readonly string _root;
    private readonly string _brandDir;
    private readonly OrganizationBrandLogoProvider _logoProvider;
    private readonly BrandAssetService _service;

    public BrandAssetServiceTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "uqeb-brand-asset-tests", Guid.NewGuid().ToString("N"));
        _brandDir = Path.Combine(_root, "Assets", "Brand");
        Directory.CreateDirectory(_brandDir);

        var environment = new TestWebHostEnvironment(_root);
        var options = Options.Create(new OrganizationBrandingOptions
        {
            LogoPath = "Assets/Brand/organization-logo.png",
        });

        _logoProvider = new OrganizationBrandLogoProvider(
            environment, options, NullLogger<OrganizationBrandLogoProvider>.Instance);
        _service = new BrandAssetService(
            _logoProvider, NullLogger<BrandAssetService>.Instance);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public void GetLogoSvgDataUri_ReturnsSvgDataUri_WhenSvgFileExists()
    {
        var svgContent = "<svg xmlns=\"http://www.w3.org/2000/svg\"><circle r=\"10\"/></svg>"u8.ToArray();
        File.WriteAllBytes(Path.Combine(_brandDir, "organization-logo.svg"), svgContent);

        var uri = _service.GetLogoSvgDataUri();

        Assert.NotNull(uri);
        Assert.StartsWith("data:image/svg+xml;base64,", uri);
        var encoded = uri["data:image/svg+xml;base64,".Length..];
        Assert.Equal(svgContent, Convert.FromBase64String(encoded));
    }

    [Fact]
    public void GetLogoSvgDataUri_ReturnsNull_WhenNoSvgFile()
    {
        var uri = _service.GetLogoSvgDataUri();

        Assert.Null(uri);
    }

    [Fact]
    public void GetLogoPngDataUri_ReturnsPngDataUri_WhenPngFileExists()
    {
        var pngHeader = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
        File.WriteAllBytes(Path.Combine(_brandDir, "organization-logo.png"), pngHeader);

        var uri = _service.GetLogoPngDataUri();

        Assert.NotNull(uri);
        Assert.StartsWith("data:image/png;base64,", uri);
        var encoded = uri["data:image/png;base64,".Length..];
        Assert.Equal(pngHeader, Convert.FromBase64String(encoded));
    }

    [Fact]
    public void GetLogoPngDataUri_ReturnsNull_WhenNoPngFile()
    {
        var uri = _service.GetLogoPngDataUri();

        Assert.Null(uri);
    }

    [Fact]
    public void GetPreferredLogoDataUri_PrefersSvgOverPng_WhenBothExist()
    {
        var svgContent = "<svg xmlns=\"http://www.w3.org/2000/svg\"/>"u8.ToArray();
        File.WriteAllBytes(Path.Combine(_brandDir, "organization-logo.svg"), svgContent);
        File.WriteAllBytes(Path.Combine(_brandDir, "organization-logo.png"), [0x89, 0x50, 0x4E, 0x47]);

        var uri = _service.GetPreferredLogoDataUri();

        Assert.NotNull(uri);
        Assert.StartsWith("data:image/svg+xml;base64,", uri);
    }

    [Fact]
    public void GetPreferredLogoDataUri_FallsBackToPng_WhenNoSvg()
    {
        File.WriteAllBytes(Path.Combine(_brandDir, "organization-logo.png"), [0x89, 0x50, 0x4E, 0x47]);

        var uri = _service.GetPreferredLogoDataUri();

        Assert.NotNull(uri);
        Assert.StartsWith("data:image/png;base64,", uri);
    }

    [Fact]
    public void GetPreferredLogoDataUri_ReturnsNull_WhenNoLogoFiles()
    {
        var uri = _service.GetPreferredLogoDataUri();

        Assert.Null(uri);
    }

    [Fact]
    public void HtmlRenderer_ContainsDataUri_WhenLogoDataUriSet()
    {
        var pngBytes = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
        File.WriteAllBytes(Path.Combine(_brandDir, "organization-logo.png"), pngBytes);

        var logoDataUri = _service.GetPreferredLogoDataUri()!;
        Assert.NotNull(logoDataUri);

        var doc = new Uqeb.Api.Models.Letters.FollowUpLetterDocumentModel
        {
            LogoPath = logoDataUri,
            Title = "خطاب تعقيب",
            Body = "نص الخطاب",
        };

        var html = FollowUpLetterPrintViewRenderer.Render([doc]);

        Assert.Contains("data:image/png;base64,", html);
        Assert.Contains("letter-logo", html);
    }

    [Fact]
    public void HtmlRenderer_FallsBackToApiUrl_WhenNoLogoDataUri()
    {
        var doc = new Uqeb.Api.Models.Letters.FollowUpLetterDocumentModel
        {
            LogoPath = null,
            Title = "خطاب تعقيب",
            Body = "نص الخطاب",
        };

        var html = FollowUpLetterPrintViewRenderer.Render([doc]);

        Assert.Contains("/api/branding/organization-logo", html);
        Assert.Contains("letter-logo", html);
    }

    private sealed class TestWebHostEnvironment(string contentRoot) : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "Uqeb.Api.Tests";
        public IFileProvider WebRootFileProvider { get; set; } = null!;
        public string WebRootPath { get; set; } = contentRoot;
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ContentRootPath { get; set; } = contentRoot;
        public IFileProvider ContentRootFileProvider { get; set; } = null!;
    }
}
