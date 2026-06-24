namespace Uqeb.Api.Reporting.DTOs;

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
    public string TemplateVersion { get; set; } = string.Empty;
}

public sealed class ReportingReadinessDto
{
    public bool FeatureEnabled { get; set; }
    public bool FontAssetsAvailable { get; set; }
    public bool StylesheetAvailable { get; set; }
    public bool TempDirectoryWritable { get; set; }
    public bool ConfigurationValid { get; set; }
    public string? ChromiumStatus { get; set; }
}
