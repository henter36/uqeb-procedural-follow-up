using System.Diagnostics;
using Xunit;

namespace Uqeb.Api.Tests.Letters;

public class OrganizationBrandLogoPublishTests
{
    private static readonly TimeSpan PublishTimeout = TimeSpan.FromMinutes(5);

    [Fact]
    public void SourceLogoFile_ExistsInRepository()
    {
        var repoRoot = FindRepoRoot();
        var logoPath = Path.Combine(repoRoot, "backend", "Uqeb.Api", "Assets", "Brand", "organization-logo.png");

        Assert.True(File.Exists(logoPath), $"Expected official logo at {logoPath}");
        Assert.True(new FileInfo(logoPath).Length > 0);
    }

    [Fact]
    [Trait("Category", "BuildIntegration")]
    public async Task PublishOutput_IncludesOrganizationLogo()
    {
        var repoRoot = FindRepoRoot();
        var csproj = Path.Combine(repoRoot, "backend", "Uqeb.Api", "Uqeb.Api.csproj");
        var outputDir = Path.Combine(Path.GetTempPath(), "uqeb-publish-logo", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(outputDir);

        using var cts = new CancellationTokenSource(PublishTimeout);

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

            // Read both streams in parallel to prevent deadlock.
            var stdoutTask = process.StandardOutput.ReadToEndAsync(cts.Token);
            var stderrTask = process.StandardError.ReadToEndAsync(cts.Token);

            try
            {
                await process.WaitForExitAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                try { process.Kill(entireProcessTree: true); } catch { }
                var partialOut = await stdoutTask;
                var partialErr = await stderrTask;
                Assert.Fail($"dotnet publish timed out after {PublishTimeout}.\nstdout: {partialOut}\nstderr: {partialErr}");
            }

            var stdout = await stdoutTask;
            var stderr = await stderrTask;

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
