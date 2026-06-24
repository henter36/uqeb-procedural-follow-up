using System.Reflection;
using Uqeb.Api.Reporting.Assets;

namespace Uqeb.Api.Reporting.Rendering;

/// <summary>Loads the shared institutional report stylesheet used by preview and PDF export.</summary>
public static class InstitutionalReportStyles
{
    public const string TemplateVersion = "2026.06.2";

    private static readonly Lazy<string> LayoutCss = new(LoadLayoutCss);

    public static string BuildDocumentStylesheet() =>
        InstitutionalReportFontAssets.BuildFontFaceCss() + LayoutCss.Value;

    public static string LayoutStylesheet => LayoutCss.Value;

    private static string LoadLayoutCss()
    {
        var assembly = typeof(InstitutionalReportStyles).Assembly;
        var resourceName = assembly
            .GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith("institutional-report.css", StringComparison.Ordinal));

        if (resourceName is null)
            throw new InvalidOperationException("Embedded institutional report stylesheet was not found.");

        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Unable to open embedded stylesheet '{resourceName}'.");

        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
