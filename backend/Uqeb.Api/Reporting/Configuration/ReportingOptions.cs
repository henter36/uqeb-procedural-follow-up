namespace Uqeb.Api.Reporting.Configuration;

public sealed class ReportingOptions
{
    public const string SectionName = "Reporting";

    /// <summary>Maximum transaction detail rows embedded in a single PDF/DOCX file.</summary>
    public int MaxPdfDetailRows { get; set; } = 10_000;
}
