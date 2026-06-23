using Uqeb.Api.Reporting.DTOs;
using Uqeb.Api.Reporting.Enums;

namespace Uqeb.Api.Reporting.Services;

internal sealed record ExportDocumentContext(
    InstitutionalReportModel Model,
    ReportExportRequestDto Request,
    ResolvedReportExportOptions Options,
    IReadOnlyList<ReportSectionId> Sections,
    bool IncludesDetails,
    bool IncludeDetailsInDocument,
    DetailOverflowAction OverflowAction,
    int TotalMatching,
    int DetailLimit);
