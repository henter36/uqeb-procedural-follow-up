namespace Uqeb.Api.Reporting.Assets;

/// <summary>
/// Embedded Arabic fonts for institutional report HTML/PDF rendering.
/// Noto Sans Arabic — SIL Open Font License 1.1 (see Reporting/Assets/Fonts/OFL.txt).
/// </summary>
public static class InstitutionalReportFontAssets
{
    public static string BuildFontFaceCss()
    {
        var regular = LoadFontDataUri("NotoSansArabic-Regular.woff2", "woff2");
        var bold = LoadFontDataUri("NotoSansArabic-Bold.woff2", "woff2");

        return $$"""
            @font-face {
              font-family: 'Uqeb Report Arabic';
              src: url('{{regular}}') format('woff2');
              font-weight: 400;
              font-style: normal;
              font-display: swap;
            }
            @font-face {
              font-family: 'Uqeb Report Arabic';
              src: url('{{bold}}') format('woff2');
              font-weight: 700;
              font-style: normal;
              font-display: swap;
            }
            """;
    }

    private static string LoadFontDataUri(string fileName, string format)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Reporting", "Assets", "Fonts", fileName);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException(
                $"Required report font asset was not found: {fileName}",
                path);
        }

        var bytes = File.ReadAllBytes(path);
        return $"data:font/{format};base64,{Convert.ToBase64String(bytes)}";
    }
}
