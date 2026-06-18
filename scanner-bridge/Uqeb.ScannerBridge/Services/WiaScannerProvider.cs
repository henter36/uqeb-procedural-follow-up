using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using Uqeb.ScannerBridge.Options;
using Microsoft.Extensions.Options;

namespace Uqeb.ScannerBridge.Services;

public sealed class WiaScannerProvider : IScannerProvider
{
    private const int ScannerDeviceType = 1;
    private const string JpegFormatId = "{B96B3CAE-0728-11D3-9D7B-0000F81EF32E}";
    private const int WiaHorizontalResolution = 6147;
    private const int WiaVerticalResolution = 6148;
    private const int WiaIntentProperty = 6146;
    private const int WiaIntentColor = 1;
    private const int WiaIntentGrayscale = 2;

    private readonly ScannerBridgeOptions _options;
    private readonly ILogger<WiaScannerProvider> _logger;

    public WiaScannerProvider(IOptions<ScannerBridgeOptions> options, ILogger<WiaScannerProvider> logger)
    {
        _options = options.Value;
        _logger = logger;
        IsAvailable = OperatingSystem.IsWindows() && Type.GetTypeFromProgID("WIA.DeviceManager") is not null;
    }

    public string ApiName => "WIA";
    public bool IsAvailable { get; }

    public IReadOnlyList<ScannerDeviceInfo> ListScanners()
    {
        if (!IsAvailable)
        {
            return [];
        }

        var scanners = new List<ScannerDeviceInfo>();
        dynamic? manager = null;

        try
        {
            manager = CreateDeviceManager();
            foreach (dynamic deviceInfo in manager.DeviceInfos)
            {
                if ((int)deviceInfo.Type != ScannerDeviceType)
                {
                    continue;
                }

                var id = (string)deviceInfo.DeviceID;
                var name = GetDeviceName(deviceInfo);
                scanners.Add(new ScannerDeviceInfo($"wia:{id}", name, scanners.Count == 0));
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to enumerate WIA scanners.");
            return [];
        }
        finally
        {
            ReleaseCom(manager);
        }

        return scanners;
    }

    public Task<ScanOutput> ScanAsync(ScanInput input, CancellationToken cancellationToken)
    {
        if (!IsAvailable)
        {
            throw new InvalidOperationException("WIA is not available on this machine.");
        }

        cancellationToken.ThrowIfCancellationRequested();

        var wiaDeviceId = input.ScannerId.StartsWith("wia:", StringComparison.OrdinalIgnoreCase)
            ? input.ScannerId["wia:".Length..]
            : input.ScannerId;

        var tempPath = Path.Combine(Path.GetTempPath(), $"uqeb-wia-{Guid.NewGuid():N}.jpg");
        dynamic? manager = null;
        dynamic? device = null;
        dynamic? item = null;
        dynamic? imageFile = null;

        try
        {
            manager = CreateDeviceManager();
            device = ConnectDevice(manager, wiaDeviceId);
            item = device.Items[1];

            SetResolution(item, input.Dpi);
            SetColorMode(item, input.ColorMode);

            imageFile = item.Transfer(JpegFormatId);
            imageFile.SaveFile(tempPath);

            var fileName = $"scan-{DateTime.UtcNow:yyyyMMdd-HHmmss}.jpg";
            var preview = ScanImageHelper.CreatePreviewFromFile(tempPath, _options.PreviewMaxWidth, out var width, out var height);

            return Task.FromResult(new ScanOutput(
                tempPath,
                "image/jpeg",
                fileName,
                width,
                height,
                preview));
        }
        catch
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }

            throw;
        }
        finally
        {
            ReleaseCom(imageFile);
            ReleaseCom(item);
            ReleaseCom(device);
            ReleaseCom(manager);
        }
    }

    private static dynamic CreateDeviceManager()
    {
        var type = Type.GetTypeFromProgID("WIA.DeviceManager")
            ?? throw new InvalidOperationException("WIA.DeviceManager is not registered.");
        return Activator.CreateInstance(type)
            ?? throw new InvalidOperationException("Failed to create WIA.DeviceManager.");
    }

    private static dynamic ConnectDevice(dynamic manager, string deviceId)
    {
        foreach (dynamic deviceInfo in manager.DeviceInfos)
        {
            if ((string)deviceInfo.DeviceID == deviceId)
            {
                return deviceInfo.Connect();
            }
        }

        throw new InvalidOperationException($"Scanner not found: {deviceId}");
    }

    private static string GetDeviceName(dynamic deviceInfo)
    {
        try
        {
            return (string)deviceInfo.Properties["Name"].get_Value();
        }
        catch
        {
            return "Scanner";
        }
    }

    private static void SetResolution(dynamic item, int dpi)
    {
        try
        {
            item.Properties[WiaHorizontalResolution].Value = dpi;
            item.Properties[WiaVerticalResolution].Value = dpi;
        }
        catch
        {
            // Some drivers reject explicit DPI; continue with defaults.
        }
    }

    private static void SetColorMode(dynamic item, string colorMode)
    {
        try
        {
            item.Properties[WiaIntentProperty].Value =
                colorMode.Equals("grayscale", StringComparison.OrdinalIgnoreCase)
                    ? WiaIntentGrayscale
                    : WiaIntentColor;
        }
        catch
        {
            // Optional property.
        }
    }

    private static void ReleaseCom(object? comObject)
    {
        if (comObject is null)
        {
            return;
        }

        try
        {
            if (Marshal.IsComObject(comObject))
            {
                Marshal.FinalReleaseComObject(comObject);
            }
        }
        catch
        {
            // Best effort COM cleanup.
        }
    }
}
