using System.Diagnostics;
using Xunit;

namespace Uqeb.Api.Tests.Letters;

public class OrganizationBrandLogoPublishTests
{
    [Fact]
    public void SourceLogoFile_ExistsInRepository()
    {
        var repoRoot = FindRepoRoot();
        var logoPath = Path.Combine(repoRoot, "backend", "Uqeb.Api", "Assets", "Brand", "organization-logo.png");

        Assert.True(File.Exists(logoPath), $"Expected official logo at {logoPath}");
        Assert.True(new FileInfo(logoPath).Length > 0);
    }

    [Fact]
    public async Task PublishOutput_IncludesOrganizationLogo()
    {
        var repoRoot = FindRepoRoot();
        var csproj = Path.Combine(repoRoot, "backend", "Uqeb.Api", "Uqeb.Api.csproj");
        var outputDir = Path.Combine(Path.GetTempPath(), "uqeb-publish-logo", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(outputDir);

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = $"publish \"{csproj}\" -c Release -o \"{outputDir}\" /p:UseAppHost=false",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };

            using var process = Process.Start(startInfo)
                ?? throw new InvalidOperationException("Failed to start dotnet publish.");

            var stdout = await process.StandardOutput.ReadToEndAsync();
            var stderr = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();

            Assert.True(process.ExitCode == 0, $"dotnet publish failed:\n{stdout}\n{stderr}");

            var publishedLogo = Path.Combine(outputDir, "Assets", "Brand", "organization-logo.png");
            Assert.True(File.Exists(publishedLogo), $"Logo missing from publish output: {publishedLogo}");
        }
        finally
        {
            if (Directory.Exists(outputDir))
                Directory.Delete(outputDir, recursive: true);
        }
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "backend", "Uqeb.Api", "Uqeb.Api.csproj")))
                return dir.FullName;

            dir = dir.Parent;
        }

        throw new InvalidOperationException("Could not locate repository root.");
    }
}
