using Uqeb.ScannerBridge.Models;
using Uqeb.ScannerBridge.Options;
using Microsoft.Extensions.Options;

namespace Uqeb.ScannerBridge.Services;

public sealed record StoredScan(
    Guid ScanId,
    string FilePath,
    string ContentType,
    string FileName,
    int Width,
    int Height,
    string PreviewBase64,
    DateTime ExpiresAtUtc);

public sealed class ScanTempStore
{
    private readonly Dictionary<Guid, StoredScan> _scans = new();
    private readonly object _gate = new();
    private readonly ScannerBridgeOptions _options;
    private readonly ILogger<ScanTempStore> _logger;

    public ScanTempStore(IOptions<ScannerBridgeOptions> options, ILogger<ScanTempStore> logger)
    {
        _options = options.Value;
        _logger = logger;
        Directory.CreateDirectory(TempRoot);
    }

    public static string TempRoot =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Uqeb",
            "ScannerBridge",
            "temp");

    public StoredScan SaveScan(ScanOutput output)
    {
        var fileInfo = new FileInfo(output.FilePath);
        if (!fileInfo.Exists)
        {
            throw new InvalidOperationException("Scan output file was not created.");
        }

        if (fileInfo.Length > _options.MaxFileSizeBytes)
        {
            TryDeleteFile(output.FilePath);
            throw new InvalidOperationException($"Scan exceeds max file size of {_options.MaxFileSizeBytes} bytes.");
        }

        var scanId = Guid.NewGuid();
        var destination = Path.Combine(TempRoot, $"{scanId:N}.jpg");

        try
        {
            File.Move(output.FilePath, destination, overwrite: true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to move scan file to temp store.");
            TryDeleteFile(output.FilePath);
            throw;
        }

        var stored = new StoredScan(
            scanId,
            destination,
            output.ContentType,
            output.FileName,
            output.Width,
            output.Height,
            output.PreviewBase64,
            DateTime.UtcNow.AddMinutes(_options.TempTtlMinutes));

        lock (_gate)
        {
            _scans[scanId] = stored;
        }

        return stored;
    }

    public StoredScan? GetScan(Guid scanId)
    {
        string? filePath = null;
        StoredScan? stored = null;

        lock (_gate)
        {
            if (!_scans.TryGetValue(scanId, out stored))
            {
                return null;
            }

            if (stored.ExpiresAtUtc <= DateTime.UtcNow)
            {
                RemoveScan(scanId, out filePath);
                stored = null;
            }
        }

        if (filePath is not null)
        {
            TryDeleteFile(filePath);
        }

        return stored;
    }

    public bool DeleteScan(Guid scanId)
    {
        string? filePath;
        lock (_gate)
        {
            if (!RemoveScan(scanId, out filePath))
            {
                return false;
            }
        }

        TryDeleteFile(filePath);
        return true;
    }

    public int CleanupExpired()
    {
        List<(Guid Id, string FilePath)> expired;
        lock (_gate)
        {
            expired = _scans
                .Where(pair => pair.Value.ExpiresAtUtc <= DateTime.UtcNow)
                .Select(pair => (pair.Key, pair.Value.FilePath))
                .ToList();

            foreach (var (id, _) in expired)
            {
                _scans.Remove(id);
            }
        }

        foreach (var (_, filePath) in expired)
        {
            TryDeleteFile(filePath);
        }

        if (expired.Count > 0)
        {
            _logger.LogInformation("Removed {Count} expired scan temp file(s).", expired.Count);
        }

        return expired.Count;
    }

    private bool RemoveScan(Guid scanId, out string? filePath)
    {
        filePath = null;
        if (!_scans.TryGetValue(scanId, out var stored))
        {
            return false;
        }

        _scans.Remove(scanId);
        filePath = stored.FilePath;
        return true;
    }

    private void TryDeleteFile(string? path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return;
        }

        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to delete temp scan file {Path}", path);
        }
    }

    public ScanResponseDto ToResponse(StoredScan stored) => new()
    {
        ScanId = stored.ScanId.ToString("D"),
        ContentType = stored.ContentType,
        FileName = stored.FileName,
        Width = stored.Width,
        Height = stored.Height,
        PreviewBase64 = stored.PreviewBase64,
        ExpiresAtUtc = stored.ExpiresAtUtc.ToString("O")
    };
}
