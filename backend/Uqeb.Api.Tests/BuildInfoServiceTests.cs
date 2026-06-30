using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Uqeb.Api.Configuration;
using Uqeb.Api.Controllers;
using Uqeb.Api.Services;
using Xunit;

namespace Uqeb.Api.Tests;

public class BuildInfoServiceTests
{
    [Fact]
    public void GetVersion_ReturnsConfiguredBuildMetadata()
    {
        var service = CreateService(
            new BuildInfoOptions
            {
                Version = "20260630-103000",
                CommitSha = "ABCDEF1234567890",
                BuildTimeUtc = "2026-06-30T10:30:00Z"
            },
            "Production");

        var result = service.GetVersion();

        Assert.Equal("20260630-103000", result.BackendVersion);
        Assert.Equal("abcdef1", result.BackendCommitSha);
        Assert.Equal(DateTimeOffset.Parse("2026-06-30T10:30:00Z"), result.BackendBuildTimeUtc);
        Assert.Equal("Production", result.Environment);
    }

    [Fact]
    public void GetVersion_UsesSafeFallbacksWhenOptionalMetadataIsMissing()
    {
        var service = CreateService(new BuildInfoOptions(), "Development");

        var result = service.GetVersion();

        Assert.False(string.IsNullOrWhiteSpace(result.BackendVersion));
        Assert.Equal("local", result.BackendCommitSha);
        Assert.Null(result.BackendBuildTimeUtc);
        Assert.Equal("Development", result.Environment);
    }

    [Fact]
    public void GetVersion_DoesNotReturnSensitiveOperationalFields()
    {
        var service = CreateService(
            new BuildInfoOptions
            {
                Version = "Server=prod-db;Password=secret",
                CommitSha = "not-a-sha-secret",
                BuildTimeUtc = "not-a-date"
            },
            "CustomEnvironmentName");

        var result = service.GetVersion();
        var json = JsonSerializer.Serialize(result);

        Assert.NotEqual("Server=prod-db;Password=secret", result.BackendVersion);
        Assert.Equal("local", result.BackendCommitSha);
        Assert.Null(result.BackendBuildTimeUtc);
        Assert.Equal("Unknown", result.Environment);
        Assert.DoesNotContain("ConnectionStrings", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DefaultConnection", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Password", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Server=prod-db", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SystemVersionEndpoint_RequiresAuthenticatedUser()
    {
        var authorizeAttribute = Assert.Single(
            typeof(SystemController).GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true));

        Assert.IsType<AuthorizeAttribute>(authorizeAttribute);
    }

    [Theory]
    [InlineData("production", "Production")]
    [InlineData("PROD", "Production")]
    [InlineData("staging-blue", "Staging")]
    [InlineData("dev-local", "Development")]
    [InlineData("secret-internal-name", "Unknown")]
    [InlineData(null, "Unknown")]
    [InlineData("", "Unknown")]
    [InlineData("   ", "Unknown")]
    public void GetVersion_NormalizesEnvironmentWithoutExposingUnknownNames(string? environmentName, string expected)
    {
        var service = CreateService(new BuildInfoOptions(), environmentName);

        var result = service.GetVersion();

        Assert.Equal(expected, result.Environment);
    }

    [Theory]
    [InlineData("abcdef1234567890abcdef1234567890abcdef12", "abcdef1")]
    [InlineData("abc1234", "abc1234")]
    [InlineData("ABCDEF1234567890", "abcdef1")]
    [InlineData(" local ", "local")]
    [InlineData("not-a-sha", "local")]
    [InlineData("", "local")]
    [InlineData(null, "local")]
    public void GetVersion_NormalizesCommitSha(string? commitSha, string expected)
    {
        var service = CreateService(new BuildInfoOptions { CommitSha = commitSha }, "Production");

        var result = service.GetVersion();

        Assert.Equal(expected, result.BackendCommitSha);
    }

    private static BuildInfoService CreateService(BuildInfoOptions options, string? environmentName) =>
        new(Options.Create(options), new TestHostEnvironment(environmentName));

    private sealed class TestHostEnvironment(string? environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName!;

        public string ApplicationName { get; set; } = "Uqeb.Api.Tests";

        public string ContentRootPath { get; set; } = string.Empty;

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
