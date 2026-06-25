using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Uqeb.Api.Configuration;
using Uqeb.Api.Services;
using Xunit;

namespace Uqeb.Api.Tests.Letters;

public class OrganizationBrandLogoProviderTests : IDisposable
{
    private readonly string _root;
    private readonly string _brandDir;
    private readonly OrganizationBrandLogoProvider _provider;

    public OrganizationBrandLogoProviderTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "uqeb-logo-tests", Guid.NewGuid().ToString("N"));
        _brandDir = Path.Combine(_root, "Assets", "Brand");
        Directory.CreateDirectory(_brandDir);

        var environment = new TestWebHostEnvironment(_root);
        var options = Options.Create(new OrganizationBrandingOptions
        {
            LogoPath = "Assets/Brand/organization-logo.png",
        });

        _provider = new OrganizationBrandLogoProvider(
            environment,
            options,
            NullLogger<OrganizationBrandLogoProvider>.Instance);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public void TryGetLogoBytes_ReturnsBytes_ForExistingLogo()
    {
        var logoPath = Path.Combine(_brandDir, "organization-logo.png");
        var expected = new byte[] { 0x89, 0x50, 0x4E, 0x47 };
        File.WriteAllBytes(logoPath, expected);

        var bytes = _provider.TryGetLogoBytes(null);

        Assert.NotNull(bytes);
        Assert.Equal(expected, bytes);
    }

    [Fact]
    public void TryGetLogoBytes_ReturnsNull_ForMissingFile()
    {
        var bytes = _provider.TryGetLogoBytes("missing-logo.png");

        Assert.Null(bytes);
    }

    [Fact]
    public void TryGetLogoBytes_RejectsPathTraversal()
    {
        var outside = Path.Combine(_root, "outside.png");
        File.WriteAllBytes(outside, [1, 2, 3]);

        var bytes = _provider.TryGetLogoBytes("../../outside.png");

        Assert.Null(bytes);
        Assert.False(File.Exists(Path.Combine(_brandDir, "outside.png")));
    }

    [Fact]
    public void TryGetLogoBytes_RejectsAbsolutePaths()
    {
        var outside = Path.Combine(_brandDir, "safe.png");
        File.WriteAllBytes(outside, [1, 2, 3]);

        var bytes = _provider.TryGetLogoBytes(outside);

        Assert.Null(bytes);
    }

    [Fact]
    public void TryGetLogoBytes_UsesCache_WhenFileUnchanged()
    {
        var logoPath = Path.Combine(_brandDir, "cached.png");
        File.WriteAllBytes(logoPath, [9, 8, 7]);

        var first = _provider.TryGetLogoBytes("cached.png");
        var second = _provider.TryGetLogoBytes("cached.png");

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.Equal(first, second);
    }

    [Fact]
    public void TryGetLogoBytes_RefreshesCache_WhenFileChanges()
    {
        var logoPath = Path.Combine(_brandDir, "refresh.png");
        File.WriteAllBytes(logoPath, [1]);
        _ = _provider.TryGetLogoBytes("refresh.png");

        Thread.Sleep(1100);
        File.WriteAllBytes(logoPath, [2, 3]);
        var refreshed = _provider.TryGetLogoBytes("refresh.png");

        Assert.NotNull(refreshed);
        Assert.Equal(new byte[] { 2, 3 }, refreshed);
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
