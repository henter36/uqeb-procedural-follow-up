using System.Drawing;
using System.Drawing.Imaging;
using Uqeb.ScannerBridge.Options;
using Microsoft.Extensions.Options;

namespace Uqeb.ScannerBridge.Services;

public sealed class MockScannerProvider : IScannerProvider
{
    private static readonly ScannerDeviceInfo DefaultDevice =
        new("mock:scanner-1", "Mock Scanner (development)", true);

    private readonly ScannerBridgeOptions _options;

    public MockScannerProvider(IOptions<ScannerBridgeOptions> options)
    {
        _options = options.Value;
    }

    public string ApiName => "Mock";
    public bool IsAvailable => true;

    public IReadOnlyList<ScannerDeviceInfo> ListScanners() => [DefaultDevice];

    public Task<ScanOutput> ScanAsync(ScanInput input, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var fileName = $"scan-mock-{DateTime.UtcNow:yyyyMMdd-HHmmss}.jpg";
        var tempPath = Path.Combine(Path.GetTempPath(), $"uqeb-mock-{Guid.NewGuid():N}.jpg");

        try
        {
            using (var bitmap = CreateDocumentBitmap(1240, 1754))
            {
                bitmap.Save(tempPath, ImageFormat.Jpeg);
            }

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
            TryDeleteTempFile(tempPath);
            throw;
        }
    }

    private static void TryDeleteTempFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Best-effort cleanup; preserve the original exception.
        }
    }

    internal static Bitmap CreateDocumentBitmap(int width, int height)
    {
        var bitmap = new Bitmap(width, height);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.Clear(Color.White);
        graphics.DrawRectangle(Pens.LightGray, 24, 24, width - 48, height - 48);

        using var titleFont = new Font("Arial", 28, FontStyle.Bold);
        using var bodyFont = new Font("Arial", 18);
        graphics.DrawString("Uqeb Mock Scan", titleFont, Brushes.Black, 40, 60);
        graphics.DrawString("Local Scanner Bridge — development only", bodyFont, Brushes.DimGray, 40, 110);
        graphics.DrawString(DateTime.Now.ToString("u"), bodyFont, Brushes.DimGray, 40, 150);
        return bitmap;
    }
}

internal static class ScanImageHelper
{
    public static string CreatePreviewFromFile(string filePath, int maxWidth, out int fullWidth, out int fullHeight)
    {
        using var image = Image.FromFile(filePath);
        fullWidth = image.Width;
        fullHeight = image.Height;

        var scale = Math.Min(1d, maxWidth / (double)Math.Max(image.Width, 1));
        var previewWidth = Math.Max(1, (int)Math.Round(image.Width * scale));
        var previewHeight = Math.Max(1, (int)Math.Round(image.Height * scale));

        using var preview = new Bitmap(previewWidth, previewHeight);
        using (var graphics = Graphics.FromImage(preview))
        {
            graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            graphics.DrawImage(image, 0, 0, previewWidth, previewHeight);
        }

        using var stream = new MemoryStream();
        preview.Save(stream, ImageFormat.Jpeg);
        return Convert.ToBase64String(stream.ToArray());
    }
}
