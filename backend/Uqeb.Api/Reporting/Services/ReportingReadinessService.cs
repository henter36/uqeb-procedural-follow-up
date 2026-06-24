using Microsoft.Extensions.Options;
using Uqeb.Api.Configuration;
using Uqeb.Api.Reporting.Assets;
using Uqeb.Api.Reporting.Configuration;
using Uqeb.Api.Reporting.DTOs;
using Uqeb.Api.Reporting.Operations;
using Uqeb.Api.Reporting.Rendering;
using Uqeb.Api.Services.Health;

namespace Uqeb.Api.Reporting.Services;

public interface IReportingReadinessService
{
    ReportingConfigurationDto GetConfiguration();
    Task<ReportingReadinessDto> GetReadinessAsync(CancellationToken cancellationToken = default);
}

public sealed class ReportingReadinessService : IReportingReadinessService
{
    private readonly ReportingOptions _options;
    private readonly FeatureFlagsSettings _featureFlags;
    private readonly IReportingChromiumProbe _chromiumProbe;
    private readonly IReportingExportConcurrencyGate _concurrencyGate;
    private readonly IReportingTempFileManager _tempFileManager;
    private readonly IHealthDatabaseProbe _databaseProbe;
    private readonly IReportNumberSequenceSchemaProbe _sequenceSchemaProbe;

    public ReportingReadinessService(
        IOptions<ReportingOptions> options,
        IOptions<FeatureFlagsSettings> featureFlags,
        IReportingChromiumProbe chromiumProbe,
        IReportingExportConcurrencyGate concurrencyGate,
        IReportingTempFileManager tempFileManager,
        IHealthDatabaseProbe databaseProbe,
        IReportNumberSequenceSchemaProbe sequenceSchemaProbe)
    {
        _options = options.Value;
        _featureFlags = featureFlags.Value;
        _chromiumProbe = chromiumProbe;
        _concurrencyGate = concurrencyGate;
        _tempFileManager = tempFileManager;
        _databaseProbe = databaseProbe;
        _sequenceSchemaProbe = sequenceSchemaProbe;
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
        MaxConcurrentPdfExports = _options.MaxConcurrentPdfExports,
        MaxConcurrentNonPdfExports = _options.MaxConcurrentNonPdfExports,
        MinFreeTempSpaceMb = _options.MinFreeTempSpaceMb,
        TemplateVersion = InstitutionalReportStyles.TemplateVersion,
    };

    public async Task<ReportingReadinessDto> GetReadinessAsync(CancellationToken cancellationToken = default)
    {
        if (!_featureFlags.InstitutionalReports)
        {
            return new ReportingReadinessDto
            {
                State = ReportingReadinessState.NotApplicable,
                FeatureEnabled = false,
                TemplateVersion = InstitutionalReportStyles.TemplateVersion,
            };
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

        var tempWritable = CanWriteToTempDirectory(_tempFileManager.RootDirectory);
        var freeSpaceMb = TryGetFreeSpaceMb(_tempFileManager.RootDirectory);
        var configurationValid = IsConfigurationValid();
        var chromium = await _chromiumProbe.ProbeAsync(cancellationToken);
        var database = await _databaseProbe.CheckAsync(cancellationToken);
        var databaseReachable = database.Status == HealthDatabaseStatus.Ready;
        var exportConcurrencyAvailable = _concurrencyGate.HasCapacity(Enums.ExportFormat.Pdf)
                                         || _concurrencyGate.HasCapacity(Enums.ExportFormat.Xlsx);
        var reportNumberSequenceTableAvailable = databaseReachable
            && await _sequenceSchemaProbe.IsTableAvailableAsync(cancellationToken);
        var reportNumberAllocationReady = reportNumberSequenceTableAvailable;

        var state = ResolveReadinessState(
            configurationValid,
            stylesheetAvailable,
            tempWritable,
            chromium.LaunchSuccessful,
            databaseReachable,
            exportConcurrencyAvailable,
            reportNumberSequenceTableAvailable);

        return new ReportingReadinessDto
        {
            State = state,
            FeatureEnabled = true,
            ConfigurationValid = configurationValid,
            FontAssetsAvailable = InstitutionalReportFontAssets.BuildFontFaceCss().Contains("@font-face"),
            StylesheetAvailable = stylesheetAvailable,
            TempDirectoryWritable = tempWritable,
            TempDirectoryFreeSpaceMb = freeSpaceMb,
            ChromiumExecutableAvailable = chromium.ExecutableAvailable,
            ChromiumLaunchSuccessful = chromium.LaunchSuccessful,
            DatabaseReachable = databaseReachable,
            ExportConcurrencyAvailable = exportConcurrencyAvailable,
            ReportNumberSequenceTableAvailable = reportNumberSequenceTableAvailable,
            ReportNumberAllocationReady = reportNumberAllocationReady,
            TemplateVersion = InstitutionalReportStyles.TemplateVersion,
            ChromiumStatus = chromium.Summary,
        };
    }

    internal static ReportingReadinessState ResolveReadinessState(
        bool configurationValid,
        bool stylesheetAvailable,
        bool tempWritable,
        bool chromiumLaunchSuccessful,
        bool databaseReachable,
        bool exportConcurrencyAvailable,
        bool reportNumberSequenceTableAvailable)
    {
        if (!configurationValid)
            return ReportingReadinessState.Unavailable;

        var dependencySignals = new[]
        {
            stylesheetAvailable,
            tempWritable,
            chromiumLaunchSuccessful,
            databaseReachable,
            exportConcurrencyAvailable,
            reportNumberSequenceTableAvailable,
        };

        if (dependencySignals.All(signal => signal))
            return ReportingReadinessState.Ready;

        if (dependencySignals.Any(signal => signal))
            return ReportingReadinessState.Degraded;

        return ReportingReadinessState.Unavailable;
    }

    private bool IsConfigurationValid()
    {
        try
        {
            _options.Validate();
            return true;
        }
        catch
        {
            return false;
        }
    }

    internal static bool CanWriteToTempDirectory(string rootDirectory)
    {
        var tempWritable = false;
        string probePath;

        try
        {
            Directory.CreateDirectory(rootDirectory);
            probePath = Path.Combine(rootDirectory, $"uqeb-report-probe-{Guid.NewGuid():N}.tmp");
        }
        catch
        {
            return false;
        }

        try
        {
            File.WriteAllText(probePath, "ok");
            tempWritable = true;
        }
        catch
        {
            tempWritable = false;
        }
        finally
        {
            try
            {
                if (File.Exists(probePath))
                    File.Delete(probePath);
            }
            catch
            {
                // Cleanup failure must not crash readiness.
            }
        }

        return tempWritable;
    }

    internal static long? TryGetFreeSpaceMb(string rootDirectory)
    {
        try
        {
            var drive = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(rootDirectory))!);
            return drive.AvailableFreeSpace / (1024 * 1024);
        }
        catch
        {
            return null;
        }
    }
}
