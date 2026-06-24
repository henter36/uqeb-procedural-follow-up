using Uqeb.Api.Reporting.Enums;

namespace Uqeb.Api.Reporting.Rendering;

public sealed record PdfPageProfile(
    string Name,
    string CssClass,
    decimal WidthMm,
    decimal HeightMm,
    decimal MarginTopMm,
    decimal MarginRightMm,
    decimal MarginBottomMm,
    decimal MarginLeftMm,
    decimal DefaultFontSizePx,
    decimal TableFontSizePx)
{
    public string CssPageName => "report-" + CssClass;
}

public static class InstitutionalReportPdfProfiles
{
    public static readonly PdfPageProfile StandardPortrait = new(
        "StandardPortrait",
        "standard-portrait",
        WidthMm: 210,
        HeightMm: 297,
        MarginTopMm: 14,
        MarginRightMm: 14,
        MarginBottomMm: 16,
        MarginLeftMm: 14,
        DefaultFontSizePx: 11,
        TableFontSizePx: 10.5m);

    public static readonly PdfPageProfile StandardLandscape = new(
        "StandardLandscape",
        "standard-landscape",
        WidthMm: 297,
        HeightMm: 210,
        MarginTopMm: 12,
        MarginRightMm: 12,
        MarginBottomMm: 14,
        MarginLeftMm: 12,
        DefaultFontSizePx: 10.5m,
        TableFontSizePx: 10);

    public static readonly PdfPageProfile WideLandscape = new(
        "WideLandscape",
        "wide-landscape",
        WidthMm: 356,
        HeightMm: 216,
        MarginTopMm: 12,
        MarginRightMm: 10,
        MarginBottomMm: 14,
        MarginLeftMm: 10,
        DefaultFontSizePx: 10.5m,
        TableFontSizePx: 9.8m);

    public static readonly PdfPageProfile ExtraWideLandscape = new(
        "ExtraWideLandscape",
        "extra-wide-landscape",
        WidthMm: 400,
        HeightMm: 216,
        MarginTopMm: 12,
        MarginRightMm: 10,
        MarginBottomMm: 14,
        MarginLeftMm: 10,
        DefaultFontSizePx: 10,
        TableFontSizePx: 9.2m);

    public static IReadOnlyList<PdfPageProfile> All { get; } =
    [
        StandardPortrait,
        StandardLandscape,
        WideLandscape,
        ExtraWideLandscape,
    ];

    public static PdfPageProfile ForSection(ReportSectionId section) =>
        section switch
        {
            ReportSectionId.IndicatorsDashboard => StandardLandscape,
            ReportSectionId.DepartmentPerformance => WideLandscape,
            ReportSectionId.ExternalPartyAnalysis => WideLandscape,
            ReportSectionId.ClassificationAndPriorityAnalysis => StandardLandscape,
            ReportSectionId.DelayAndBottleneckAnalysis => StandardLandscape,
            ReportSectionId.CriticalCases => ExtraWideLandscape,
            ReportSectionId.DataQuality => StandardLandscape,
            ReportSectionId.TransactionDetails => ExtraWideLandscape,
            _ => StandardPortrait,
        };

    public static PdfPageProfile GetByName(string? name) =>
        All.FirstOrDefault(profile => string.Equals(profile.Name, name, StringComparison.Ordinal))
        ?? StandardPortrait;

    public static PdfPageProfile WidestProfile(IEnumerable<string?> profileNames) =>
        profileNames
            .Select(GetByName)
            .OrderByDescending(profile => profile.WidthMm)
            .ThenByDescending(profile => profile.HeightMm)
            .ThenBy(profile => profile.Name, StringComparer.Ordinal)
            .FirstOrDefault()
        ?? StandardPortrait;
}
