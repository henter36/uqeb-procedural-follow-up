using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Uqeb.Api.Reporting.Configuration;
using Uqeb.Api.Reporting.Operations;
using Xunit;

namespace Uqeb.Api.Tests.Reporting;

public class ReportingTempFileManagerTests
{
    [Fact]
    public void EnsurePathInsideRoot_RejectsPathTraversal()
    {
        using var root = new TempDirectory();
        var manager = CreateManager(root.Path);
        var escapeAttempt = Path.GetFullPath(Path.Combine(root.Path, "..", "escape.txt"));
        Assert.Throws<InvalidOperationException>(() => manager.EnsurePathInsideRoot(escapeAttempt));
    }

    [Fact]
    public void CleanupSession_RemovesTrackedFiles_WithoutTouchingExternalFiles()
    {
        using var root = new TempDirectory();
        using var external = new TempDirectory();
        var externalFile = Path.Combine(external.Path, "keep.txt");
        File.WriteAllText(externalFile, "keep");

        var manager = CreateManager(root.Path);
        var session = manager.CreateSessionDirectory();
        var tracked = manager.CreateTrackedFile(session, ".bin");
        File.WriteAllBytes(tracked, [1, 2, 3]);

        manager.CleanupSession(session);

        Assert.False(Directory.Exists(session));
        Assert.True(File.Exists(externalFile));
    }

    [Fact]
    public void EnsureDiskSpaceForExport_Throws_WhenFreeSpaceBelowThreshold()
    {
        var manager = new ReportingTempFileManager(
            Options.Create(new ReportingOptions
            {
                MinFreeTempSpaceMb = int.MaxValue / 1024,
                MaxTempBytesPerExport = 1,
            }),
            NullLogger<ReportingTempFileManager>.Instance,
            new ReportingMetrics());

        Assert.Throws<ReportingExportRejectedException>(() =>
            manager.EnsureDiskSpaceForExport(1));
    }

    [Fact]
    public void EnsurePathInsideRoot_RejectsSiblingDirectoryWithSharedPrefix()
    {
        using var root = new TempDirectory();
        var manager = CreateManager(root.Path);
        var sibling = $"{root.Path}-evil";
        Directory.CreateDirectory(sibling);
        var escapeAttempt = Path.Combine(sibling, "escape.txt");
        File.WriteAllText(escapeAttempt, "x");

        Assert.Throws<InvalidOperationException>(() => manager.EnsurePathInsideRoot(escapeAttempt));
    }

    [Fact]
    public void IsSubPathOf_RejectsPathsOutsideRoot()
    {
        using var root = new TempDirectory();
        var sibling = $"{root.Path}-evil";
        Assert.False(ReportingTempFileManager.IsSubPathOf(sibling, root.Path));
    }

    private static ReportingTempFileManager CreateManager(string root)
    {
        return new ReportingTempFileManager(
            Options.Create(new ReportingOptions { TempFileRoot = root }),
            NullLogger<ReportingTempFileManager>.Instance,
            new ReportingMetrics());
    }

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"uqeb-report-test-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }
        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
