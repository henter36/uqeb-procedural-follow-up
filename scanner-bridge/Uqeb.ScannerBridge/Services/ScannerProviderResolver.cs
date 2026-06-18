using Uqeb.ScannerBridge.Options;
using Microsoft.Extensions.Options;

namespace Uqeb.ScannerBridge.Services;

public sealed class ScannerProviderResolver
{
    private readonly WiaScannerProvider _wiaProvider;
    private readonly MockScannerProvider _mockProvider;
    private readonly ScannerBridgeOptions _options;
    private readonly IHostEnvironment _environment;
    private readonly ILogger<ScannerProviderResolver> _logger;

    public ScannerProviderResolver(
        WiaScannerProvider wiaProvider,
        MockScannerProvider mockProvider,
        IOptions<ScannerBridgeOptions> options,
        IHostEnvironment environment,
        ILogger<ScannerProviderResolver> logger)
    {
        _wiaProvider = wiaProvider;
        _mockProvider = mockProvider;
        _options = options.Value;
        _environment = environment;
        _logger = logger;
    }

    public IScannerProvider Resolve()
    {
        var provider = _options.Provider.Trim();

        if (provider.Equals("Mock", StringComparison.OrdinalIgnoreCase))
        {
            if (_environment.IsDevelopment() || _options.AllowMockFallback)
            {
                return _mockProvider;
            }

            if (_wiaProvider.IsAvailable)
            {
                _logger.LogWarning("Mock provider denied outside Development; falling back to WIA.");
                return _wiaProvider;
            }

            throw new InvalidOperationException(
                "Mock provider is not allowed outside Development unless ScannerBridge:AllowMockFallback is true.");
        }

        if (provider.Equals("WIA", StringComparison.OrdinalIgnoreCase))
        {
            return _wiaProvider;
        }

        if (_wiaProvider.IsAvailable)
        {
            return _wiaProvider;
        }

        if (_options.AllowMockFallback || _environment.IsDevelopment())
        {
            _logger.LogWarning("WIA unavailable; using Mock scanner provider.");
            return _mockProvider;
        }

        return _wiaProvider;
    }

    public static IReadOnlyList<ScannerDeviceInfo> ListScannersSafe(IScannerProvider provider)
    {
        try
        {
            return provider.ListScanners();
        }
        catch (Exception)
        {
            return [];
        }
    }
}
