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

    public string TempRoot =>
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
        File.Move(output.FilePath, destination, overwrite: true);

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
        lock (_gate)
        {
            if (_scans.TryGetValue(scanId, out var stored))
            {
                if (stored.ExpiresAtUtc <= DateTime.UtcNow)
                {
                    RemoveScan(scanId);
                    return null;
                }

                return stored;
            }
        }

        return null;
    }

    public bool DeleteScan(Guid scanId)
    {
        lock (_gate)
        {
            return RemoveScan(scanId);
        }
    }

    public int CleanupExpired()
    {
        List<Guid> expired;
        lock (_gate)
        {
            expired = _scans
                .Where(pair => pair.Value.ExpiresAtUtc <= DateTime.UtcNow)
                .Select(pair => pair.Key)
                .ToList();
        }

        var removed = 0;
        foreach (var scanId in expired)
        {
            if (DeleteScan(scanId))
            {
                removed++;
            }
        }

        if (removed > 0)
        {
            _logger.LogInformation("Removed {Count} expired scan temp file(s).", removed);
        }

        return removed;
    }

    private bool RemoveScan(Guid scanId)
    {
        if (!_scans.TryGetValue(scanId, out var stored))
        {
            return false;
        }

        _scans.Remove(scanId);
        TryDeleteFile(stored.FilePath);
        return true;
    }

    private void TryDeleteFile(string path)
    {
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
