using Uqeb.Api.Reporting.DTOs;
using Uqeb.Api.Reporting.Enums;

namespace Uqeb.Api.Reporting.Services;

internal sealed record ResolvedReportExportOptions(
    ExportFormat Format,
    ExportMode Mode,
    bool IncludePartialCover,
    bool IncludePartialManifest,
    PageNumberingMode NumberingMode);

internal sealed record ResolvedSaveTemplateOptions(
    InstitutionalReportType ReportType,
    ExportFormat DefaultFormat,
    PageNumberingMode PageNumberingMode,
    bool IncludePartialCover,
    bool IncludePartialManifest);

internal static class InstitutionalReportExportOptionsResolver
{
    internal static ResolvedReportExportOptions Resolve(ReportExportRequestDto request) => new(
        request.ExportFormat ?? ExportFormat.Pdf,
        request.ExportMode ?? ExportMode.FullReport,
        request.IncludePartialCover ?? false,
        request.IncludePartialManifest ?? false,
        request.PageNumberingMode ?? PageNumberingMode.Restart);

    internal static ReportExportRequestDto WithResolvedValues(ReportExportRequestDto request)
    {
        var options = Resolve(request);
        return WithResolvedValues(request, options);
    }

    internal static ReportExportRequestDto WithResolvedValues(
        ReportExportRequestDto request,
        ResolvedReportExportOptions options) =>
        new()
        {
            ReportId = request.ReportId,
            BuildRequest = request.BuildRequest,
            ExportFormat = options.Format,
            ExportMode = options.Mode,
            SelectedSectionIds = request.SelectedSectionIds.ToList(),
            SelectedPageNumbers = request.SelectedPageNumbers.ToList(),
            PageRangeExpression = request.PageRangeExpression,
            CurrentPageNumber = request.CurrentPageNumber,
            IncludePartialCover = options.IncludePartialCover,
            IncludePartialManifest = options.IncludePartialManifest,
            PageNumberingMode = options.NumberingMode,
            TemplateId = request.TemplateId,
            Reason = request.Reason,
            DetailOverflowAction = request.DetailOverflowAction,
        };

    internal static ResolvedSaveTemplateOptions Resolve(SaveReportTemplateRequestDto request) => new(
        request.ReportType!.Value,
        request.DefaultFormat ?? ExportFormat.Pdf,
        request.PageNumberingMode ?? PageNumberingMode.Restart,
        request.IncludePartialCover ?? false,
        request.IncludePartialManifest ?? false);
}
