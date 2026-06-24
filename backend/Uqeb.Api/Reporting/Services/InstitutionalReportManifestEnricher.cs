using Uqeb.Api.Reporting.DTOs;
using Uqeb.Api.Reporting.Enums;
using Uqeb.Api.Reporting.Rendering;

namespace Uqeb.Api.Reporting.Services;

internal static class InstitutionalReportManifestEnricher
{
    internal static RenderedReportManifestDto Enrich(
        RenderedReportManifestDto manifest,
        InstitutionalReportModel model,
        bool isSummaryOnly,
        DetailOverflowAction? overflowAction)
    {
        manifest.LoadedDetailRows = model.Transactions.Count;
        manifest.IsSummaryOnly = isSummaryOnly;
        manifest.OverflowAction = overflowAction;
        manifest.Stylesheet = InstitutionalReportStyles.BuildDocumentStylesheet();
        manifest.TemplateVersion = InstitutionalReportStyles.TemplateVersion;
        manifest.FileFingerprint = model.Metadata.FileFingerprint;
        return manifest;
    }
}
