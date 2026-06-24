using Microsoft.Extensions.Options;
using Uqeb.Api.Configuration;
using Uqeb.Api.Reporting.Assets;
using Uqeb.Api.Reporting.Configuration;
using Uqeb.Api.Reporting.DTOs;
using Uqeb.Api.Reporting.Rendering;

namespace Uqeb.Api.Reporting.Services;

public interface IReportingReadinessService
{
    ReportingConfigurationDto GetConfiguration();
    ReportingReadinessDto GetReadiness();
}

public sealed class ReportingReadinessService : IReportingReadinessService
{
    private readonly ReportingOptions _options;
    private readonly FeatureFlagsSettings _featureFlags;

    public ReportingReadinessService(IOptions<ReportingOptions> options, IOptions<FeatureFlagsSettings> featureFlags)
    {
        _options = options.Value;
        _featureFlags = featureFlags.Value;
    }

    public ReportingConfigurationDto GetConfiguration() => new()
    {
        MaxPreviewDetailRows = _options.MaxPreviewDetailRows,
        MaxPdfDetailRows = _options.MaxPdfDetailRows,
        MaxPdfDetailRowsPerPart = _options.ResolvePdfPartDetailLimit(),
        MaxDocxDetailRows = _options.MaxDocxDetailRows,
        MaxXlsxDetailRows = _options.MaxXlsxDetailRows,
        MaxHtmlDetailRows = _options.MaxHtmlDetailRows,
        MaxPdfParts = _options.MaxPdfParts,
        MaxExportFileSizeMb = _options.MaxExportFileSizeMb,
        MaxExportDurationSeconds = _options.MaxExportDurationSeconds,
        TemplateVersion = InstitutionalReportStyles.TemplateVersion,
    };

    public ReportingReadinessDto GetReadiness()
    {
        var tempWritable = false;
        try
        {
            var probe = Path.Combine(Path.GetTempPath(), $"uqeb-report-probe-{Guid.NewGuid():N}.tmp");
            File.WriteAllText(probe, "ok");
            File.Delete(probe);
            tempWritable = true;
        }
        catch
        {
            tempWritable = false;
        }

        var stylesheetAvailable = false;
        try
        {
            stylesheetAvailable = InstitutionalReportStyles.BuildDocumentStylesheet().Length > 100;
        }
        catch
        {
            stylesheetAvailable = false;
        }

        return new ReportingReadinessDto
        {
            FeatureEnabled = _featureFlags.InstitutionalReports,
            FontAssetsAvailable = InstitutionalReportFontAssets.BuildFontFaceCss().Contains("@font-face"),
            StylesheetAvailable = stylesheetAvailable,
            TempDirectoryWritable = tempWritable,
            ConfigurationValid = _options.MaxPreviewDetailRows > 0 && _options.MaxPdfDetailRows > 0,
            ChromiumStatus = "Use pdf-linux CI job or local Playwright install to verify Chromium.",
        };
    }
}
