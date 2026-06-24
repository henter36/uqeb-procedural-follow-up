using Microsoft.Extensions.Options;
using Uqeb.Api.Reporting.Configuration;

namespace Uqeb.Api.Reporting.Operations;

public interface IReportingTempFileManager
{
    string RootDirectory { get; }
    string CreateSessionDirectory();
    string CreateTrackedFile(string sessionDirectory, string extension);
    void TrackFile(string sessionDirectory, string fullPath);
    void EnsurePathInsideRoot(string fullPath);
    void EnsureDiskSpaceForExport(long requiredBytes);
    void CleanupSession(string sessionDirectory);
    void CleanupOrphanedSessions();
}

public sealed class ReportingTempFileManager : IReportingTempFileManager
{
    private readonly ReportingOptions _options;
    private readonly ILogger<ReportingTempFileManager> _logger;
    private readonly IReportingMetrics _metrics;
    private readonly object _lock = new();
    private readonly HashSet<string> _activeSessions = new(StringComparer.OrdinalIgnoreCase);

    public ReportingTempFileManager(
        IOptions<ReportingOptions> options,
        ILogger<ReportingTempFileManager> logger,
        IReportingMetrics metrics)
    {
        _options = options.Value;
        _logger = logger;
        _metrics = metrics;
        RootDirectory = ResolveRootDirectory(_options);
        Directory.CreateDirectory(RootDirectory);
    }

    public string RootDirectory { get; }

    public string CreateSessionDirectory()
    {
        var session = Path.Combine(RootDirectory, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(session);
        EnsurePathInsideRoot(session);

        lock (_lock)
        {
            _activeSessions.Add(session);
        }

        return session;
    }

    public string CreateTrackedFile(string sessionDirectory, string extension)
    {
        var safeExtension = SanitizeExtension(extension);
        var fileName = $"{Guid.NewGuid():N}{safeExtension}";
        var fullPath = Path.GetFullPath(Path.Combine(sessionDirectory, fileName));
        EnsurePathInsideRoot(fullPath);
        TrackFile(sessionDirectory, fullPath);
        return fullPath;
    }

    public void TrackFile(string sessionDirectory, string fullPath)
    {
        EnsurePathInsideRoot(fullPath);
        if (!fullPath.StartsWith(Path.GetFullPath(sessionDirectory), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Tracked file must remain inside the session directory.");
    }

    public void EnsurePathInsideRoot(string fullPath)
    {
        var normalized = Path.GetFullPath(fullPath);
        var root = Path.GetFullPath(RootDirectory);
        if (!IsSubPathOf(normalized, root))
            throw new InvalidOperationException("Reporting temp path escapes the configured root.");
    }

    internal static bool IsSubPathOf(string fullPath, string rootDirectory)
    {
        var relative = Path.GetRelativePath(rootDirectory, fullPath);
        return !relative.StartsWith("..", StringComparison.Ordinal)
               && !Path.IsPathRooted(relative);
    }

    public void EnsureDiskSpaceForExport(long requiredBytes)
    {
        var drive = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(RootDirectory))!);
        var freeBytes = drive.AvailableFreeSpace;
        var minFreeBytes = (long)_options.MinFreeTempSpaceMb * 1024L * 1024L;
        var projected = freeBytes - Math.Max(requiredBytes, _options.MaxTempBytesPerExport);

        if (projected < minFreeBytes)
        {
            throw new ReportingExportRejectedException(
                ReportingErrorCodes.TempSpaceInsufficient,
                "مساحة القرص المؤقتة غير كافية لتصدير التقرير.",
                StatusCodes.Status503ServiceUnavailable);
        }
    }

    public void CleanupSession(string sessionDirectory)
    {
        if (string.IsNullOrWhiteSpace(sessionDirectory))
            return;

        try
        {
            EnsurePathInsideRoot(sessionDirectory);
            if (Directory.Exists(sessionDirectory))
                Directory.Delete(sessionDirectory, recursive: true);
        }
        catch (Exception ex)
        {
            _metrics.RecordTempCleanupFailure();
            _logger.LogWarning(
                ReportingEventIds.TempCleanupFailed,
                ex,
                "Failed to cleanup reporting temp session.");
        }
        finally
        {
            lock (_lock)
            {
                _activeSessions.Remove(sessionDirectory);
            }
        }
    }

    public void CleanupOrphanedSessions()
    {
        if (_options.TempFileMaxAgeMinutes <= 0)
            return;

        var cutoff = DateTime.UtcNow.AddMinutes(-_options.TempFileMaxAgeMinutes);
        if (!Directory.Exists(RootDirectory))
            return;

        foreach (var directory in Directory.EnumerateDirectories(RootDirectory))
        {
            try
            {
                EnsurePathInsideRoot(directory);
                var info = new DirectoryInfo(directory);
                if (info.LastWriteTimeUtc > cutoff)
                    continue;

                lock (_lock)
                {
                    if (_activeSessions.Contains(directory))
                        continue;
                }

                Directory.Delete(directory, recursive: true);
            }
            catch (Exception ex)
            {
                _metrics.RecordTempCleanupFailure();
                _logger.LogWarning(
                    ReportingEventIds.TempCleanupFailed,
                    ex,
                    "Failed to cleanup orphaned reporting temp directory.");
            }
        }
    }

    internal static string ResolveRootDirectory(ReportingOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.TempFileRoot))
            return Path.GetFullPath(options.TempFileRoot);

        return Path.GetFullPath(Path.Combine(Path.GetTempPath(), "uqeb-reporting"));
    }

    private static string SanitizeExtension(string extension)
    {
        var trimmed = (extension ?? string.Empty).Trim();
        if (trimmed.Length == 0)
            return ".tmp";

        if (!trimmed.StartsWith('.'))
            trimmed = "." + trimmed;

        return trimmed.All(c => char.IsLetterOrDigit(c) || c == '.') ? trimmed : ".tmp";
    }
}
