namespace Uqeb.ScannerBridge.Services;

public sealed record ScannerDeviceInfo(string Id, string Name, bool Default);

public sealed record ScanInput(
    string ScannerId,
    string Format,
    int Dpi,
    string ColorMode);

public sealed record ScanOutput(
    string FilePath,
    string ContentType,
    string FileName,
    int Width,
    int Height,
    string PreviewBase64);

public interface IScannerProvider
{
    string ApiName { get; }
    bool IsAvailable { get; }
    IReadOnlyList<ScannerDeviceInfo> ListScanners();
    Task<ScanOutput> ScanAsync(ScanInput input, CancellationToken cancellationToken);
}
