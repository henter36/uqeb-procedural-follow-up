namespace Uqeb.ScannerBridge.Models;

public sealed class StatusResponse
{
    public bool Ok { get; init; } = true;
    public required string Version { get; init; }
    public required string ScannerApi { get; init; }
    public int ScannerCount { get; init; }
    public required string TempFolder { get; init; }
}

public sealed class ScannersResponse
{
    public required IReadOnlyList<ScannerDeviceDto> Scanners { get; init; }
}

public sealed class ScannerDeviceDto
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public bool Default { get; init; }
}

public sealed class ScanRequestDto
{
    public string? ScannerId { get; init; }
    public string? Format { get; init; }
    public int? Dpi { get; init; }
    public string? ColorMode { get; init; }
}

public sealed class ScanResponseDto
{
    public required string ScanId { get; init; }
    public required string ContentType { get; init; }
    public required string FileName { get; init; }
    public int Width { get; init; }
    public int Height { get; init; }
    public required string PreviewBase64 { get; init; }
    public required string ExpiresAtUtc { get; init; }
}

public sealed class ErrorResponse
{
    public required string Code { get; init; }
    public required string Message { get; init; }
}
