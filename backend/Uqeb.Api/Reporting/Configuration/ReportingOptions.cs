using Uqeb.Api.Reporting.Enums;

namespace Uqeb.Api.Reporting.Configuration;

public sealed class ReportingOptions
{
    public const string SectionName = "Reporting";

    /// <summary>Maximum detail rows shown in preview.</summary>
    public int MaxPreviewDetailRows { get; set; } = 500;

    /// <summary>Maximum detail rows embedded in a single PDF/DOCX export before overflow handling.</summary>
    public int MaxPdfDetailRows { get; set; } = 5_000;

    /// <summary>Maximum detail rows embedded in a single split PDF part.</summary>
    public int MaxPdfDetailRowsPerPart { get; set; } = 5_000;

    public int MaxDocxDetailRows { get; set; } = 20_000;
    public int MaxXlsxDetailRows { get; set; } = 100_000;
    public int MaxHtmlDetailRows { get; set; } = 20_000;
    public int MaxPdfParts { get; set; } = 20;
    public int MaxExportFileSizeMb { get; set; } = 100;
    public int MaxExportDurationSeconds { get; set; } = 120;

    public int MaxConcurrentReportBuilds { get; set; } = 4;
    public int MaxConcurrentPdfExports { get; set; } = 2;
    public int MaxConcurrentNonPdfExports { get; set; } = 4;
    public int ExportConcurrencyWaitSeconds { get; set; } = 30;
    public int MinFreeTempSpaceMb { get; set; } = 512;
    public long MaxTempBytesPerExport { get; set; } = 500_000_000;
    public long MaxTotalTempBytes { get; set; } = 2_000_000_000;
    public int TempFileMaxAgeMinutes { get; set; } = 120;
    public string TempFileRoot { get; set; } = string.Empty;
    public int ReadinessCacheSeconds { get; set; } = 45;
    public int ChromiumProbeTimeoutSeconds { get; set; } = 15;

    public int ResolvePdfPartDetailLimit() =>
        MaxPdfDetailRowsPerPart > 0 ? MaxPdfDetailRowsPerPart : MaxPdfDetailRows;

    public int ResolveDetailLimit(ExportFormat format) => format switch
    {
        ExportFormat.Docx => MaxDocxDetailRows,
        ExportFormat.Xlsx => MaxXlsxDetailRows,
        ExportFormat.Html => MaxHtmlDetailRows,
        _ => MaxPdfDetailRows,
    };

    public void Validate()
    {
        ValidateDetailLimit(MaxPreviewDetailRows);
        ValidateDetailLimit(MaxPdfDetailRows);
        ValidateDetailLimit(ResolvePdfPartDetailLimit());
        ValidateDetailLimit(MaxDocxDetailRows);
        ValidateDetailLimit(MaxXlsxDetailRows);
        ValidateDetailLimit(MaxHtmlDetailRows);

        if (MaxPdfParts <= 0)
            throw new InvalidOperationException("Reporting:MaxPdfParts must be greater than zero.");

        if (MaxExportFileSizeMb <= 0)
            throw new InvalidOperationException("Reporting:MaxExportFileSizeMb must be greater than zero.");

        if (MaxExportDurationSeconds <= 0)
            throw new InvalidOperationException("Reporting:MaxExportDurationSeconds must be greater than zero.");

        if (MaxConcurrentReportBuilds <= 0)
            throw new InvalidOperationException("Reporting:MaxConcurrentReportBuilds must be greater than zero.");

        if (MaxConcurrentPdfExports <= 0)
            throw new InvalidOperationException("Reporting:MaxConcurrentPdfExports must be greater than zero.");

        if (MaxConcurrentNonPdfExports <= 0)
            throw new InvalidOperationException("Reporting:MaxConcurrentNonPdfExports must be greater than zero.");

        if (ExportConcurrencyWaitSeconds <= 0)
            throw new InvalidOperationException("Reporting:ExportConcurrencyWaitSeconds must be greater than zero.");

        if (MinFreeTempSpaceMb <= 0)
            throw new InvalidOperationException("Reporting:MinFreeTempSpaceMb must be greater than zero.");

        if (MaxTempBytesPerExport <= 0)
            throw new InvalidOperationException("Reporting:MaxTempBytesPerExport must be greater than zero.");

        if (MaxTotalTempBytes < MaxTempBytesPerExport)
            throw new InvalidOperationException("Reporting:MaxTotalTempBytes must be greater than or equal to MaxTempBytesPerExport.");

        if (TempFileMaxAgeMinutes <= 0)
            throw new InvalidOperationException("Reporting:TempFileMaxAgeMinutes must be greater than zero.");

        if (ReadinessCacheSeconds <= 0)
            throw new InvalidOperationException("Reporting:ReadinessCacheSeconds must be greater than zero.");

        if (ChromiumProbeTimeoutSeconds <= 0)
            throw new InvalidOperationException("Reporting:ChromiumProbeTimeoutSeconds must be greater than zero.");

        if (MaxXlsxDetailRows < MaxPdfDetailRows)
            throw new InvalidOperationException("Reporting:MaxXlsxDetailRows must be greater than or equal to MaxPdfDetailRows.");
    }

    public static void ValidateDetailLimit(int detailLimit)
    {
        if (detailLimit <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(detailLimit),
                detailLimit,
                "detailLimit must be greater than zero.");
        }
    }
}
