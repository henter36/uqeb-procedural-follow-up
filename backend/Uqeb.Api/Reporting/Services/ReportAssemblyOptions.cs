using Uqeb.Api.Reporting.Configuration;

namespace Uqeb.Api.Reporting.Services;

/// <summary>Controls how aggregate metrics vs. transaction detail rows are assembled.</summary>
public sealed record ReportAssemblyOptions
{
    public int? TotalMatchedOverride { get; init; }

    public int DetailRowLimit { get; init; } = 10_000;

    /// <summary>When set, only this many detail rows are loaded into <see cref="DTOs.InstitutionalReportModel.Transactions"/>.</summary>
    public int? DetailRowsToLoad { get; init; }

    /// <summary>When true, no detail rows are loaded (summary-only export).</summary>
    public bool OmitDetailRows { get; init; }

    public int DetailPartsCount { get; init; }

    public bool DetailRowsTruncated { get; init; }

    /// <summary>Rows actually exported/embedded in detail output (may span multiple parts).</summary>
    public int? ExportedDetailRowsOverride { get; init; }

    public ReportBuildPurpose Purpose { get; init; } = ReportBuildPurpose.Export;

    internal static ReportAssemblyOptions ForPreview(int totalMatched, int detailLimit)
    {
        ReportingOptions.ValidateDetailLimit(detailLimit);
        return new()
        {
            Purpose = ReportBuildPurpose.Preview,
            TotalMatchedOverride = totalMatched,
            DetailRowLimit = detailLimit,
            DetailRowsToLoad = detailLimit,
            DetailRowsTruncated = totalMatched > detailLimit,
            DetailPartsCount = totalMatched > detailLimit
                ? (int)Math.Ceiling(totalMatched / (double)detailLimit)
                : 0,
            ExportedDetailRowsOverride = Math.Min(totalMatched, detailLimit),
        };
    }

    internal static ReportAssemblyOptions ForFullDetailExport(int totalMatched, int detailLimit)
    {
        ReportingOptions.ValidateDetailLimit(detailLimit);
        return new()
        {
            TotalMatchedOverride = totalMatched,
            DetailRowLimit = detailLimit,
            DetailRowsToLoad = null,
            DetailRowsTruncated = false,
            DetailPartsCount = 1,
            ExportedDetailRowsOverride = totalMatched,
        };
    }

    internal static ReportAssemblyOptions ForSummaryOnlyExport(int totalMatched, int detailLimit)
    {
        ReportingOptions.ValidateDetailLimit(detailLimit);
        return new()
        {
            TotalMatchedOverride = totalMatched,
            DetailRowLimit = detailLimit,
            OmitDetailRows = true,
            DetailRowsTruncated = totalMatched > 0,
            DetailPartsCount = 0,
            ExportedDetailRowsOverride = 0,
        };
    }

    internal static ReportAssemblyOptions ForSplitPdfExport(int totalMatched, int detailLimit)
    {
        ReportingOptions.ValidateDetailLimit(detailLimit);
        return new()
        {
            TotalMatchedOverride = totalMatched,
            DetailRowLimit = detailLimit,
            DetailRowsToLoad = null,
            DetailRowsTruncated = true,
            DetailPartsCount = (int)Math.Ceiling(totalMatched / (double)detailLimit),
            ExportedDetailRowsOverride = totalMatched,
        };
    }
}
