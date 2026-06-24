namespace Uqeb.Api.Reporting.DTOs;

public enum ReportingReadinessState
{
    NotApplicable,
    Configured,
    Ready,
    Degraded,
    Unavailable,
}

public sealed class ReportingConfigurationDto
{
    public int MaxPreviewDetailRows { get; set; }
    public int MaxPdfDetailRows { get; set; }
    public int MaxPdfDetailRowsPerPart { get; set; }
    public int MaxDocxDetailRows { get; set; }
    public int MaxXlsxDetailRows { get; set; }
    public int MaxHtmlDetailRows { get; set; }
    public int MaxPdfParts { get; set; }
    public int MaxExportFileSizeMb { get; set; }
    public int MaxExportDurationSeconds { get; set; }
    public int MaxConcurrentPdfExports { get; set; }
    public int MaxConcurrentNonPdfExports { get; set; }
    public int MinFreeTempSpaceMb { get; set; }
    public string TemplateVersion { get; set; } = string.Empty;
}

public sealed class ReportingReadinessDto
{
    public ReportingReadinessState State { get; set; } = ReportingReadinessState.NotApplicable;
    public bool FeatureEnabled { get; set; }
    public bool ConfigurationValid { get; set; }
    public bool FontAssetsAvailable { get; set; }
    public bool StylesheetAvailable { get; set; }
    public bool TempDirectoryWritable { get; set; }
    public long? TempDirectoryFreeSpaceMb { get; set; }
    public bool ChromiumExecutableAvailable { get; set; }
    public bool ChromiumLaunchSuccessful { get; set; }
    public bool DatabaseReachable { get; set; }
    public bool ExportConcurrencyAvailable { get; set; }
    public string TemplateVersion { get; set; } = string.Empty;
    public string? ChromiumStatus { get; set; }
}
