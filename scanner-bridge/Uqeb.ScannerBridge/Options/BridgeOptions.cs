namespace Uqeb.ScannerBridge.Options;

public sealed class ScannerBridgeOptions
{
    public const string SectionName = "ScannerBridge";

    public int Port { get; set; } = 5055;
    public string Version { get; set; } = "0.1.0";
    public string Provider { get; set; } = "Auto";
    public bool AllowMockFallback { get; set; }
    public int TempTtlMinutes { get; set; } = 10;
    public long MaxFileSizeBytes { get; set; } = 25 * 1024 * 1024;
    public int PreviewMaxWidth { get; set; } = 420;
}

public sealed class CorsOptions
{
    public const string SectionName = "Cors";

    public string[] AllowedOrigins { get; set; } =
    [
        "http://localhost",
        "http://127.0.0.1",
        "http://[::1]"
    ];
}
