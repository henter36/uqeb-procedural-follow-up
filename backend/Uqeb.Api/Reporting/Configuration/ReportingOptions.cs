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
