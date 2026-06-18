namespace Uqeb.ScannerBridge.Endpoints;

internal static class ScanFormatHelper
{
    private const string JpegFormat = "image/jpeg";

    public static bool TryNormalizeAndValidate(string? format, out string normalized, out string errorMessage)
    {
        normalized = string.IsNullOrWhiteSpace(format) ? JpegFormat : format.Trim();

        if (!normalized.Equals(JpegFormat, StringComparison.OrdinalIgnoreCase))
        {
            errorMessage = "Only image/jpeg is supported in this version.";
            normalized = string.Empty;
            return false;
        }

        normalized = JpegFormat;
        errorMessage = string.Empty;
        return true;
    }
}
