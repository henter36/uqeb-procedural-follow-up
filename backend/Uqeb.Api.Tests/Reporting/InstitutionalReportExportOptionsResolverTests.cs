using Uqeb.Api.Reporting.DTOs;
using Uqeb.Api.Reporting.Enums;
using Uqeb.Api.Reporting.Services;
using Xunit;

namespace Uqeb.Api.Tests.Reporting;

public class InstitutionalReportExportOptionsResolverTests
{
    [Fact]
    public void Resolve_ExportRequest_AppliesDocumentedDefaults()
    {
        var options = InstitutionalReportExportOptionsResolver.Resolve(new ReportExportRequestDto());

        Assert.Equal(ExportFormat.Pdf, options.Format);
        Assert.Equal(ExportMode.FullReport, options.Mode);
        Assert.False(options.IncludePartialCover);
        Assert.False(options.IncludePartialManifest);
        Assert.Equal(PageNumberingMode.Restart, options.NumberingMode);
    }

    [Fact]
    public void WithResolvedValues_PreservesExplicitExportChoices()
    {
        var request = new ReportExportRequestDto
        {
            ExportFormat = ExportFormat.Xlsx,
            ExportMode = ExportMode.SelectedPages,
            IncludePartialCover = true,
            IncludePartialManifest = true,
            PageNumberingMode = PageNumberingMode.Original,
        };

        var effective = InstitutionalReportExportOptionsResolver.WithResolvedValues(request);

        Assert.Equal(ExportFormat.Xlsx, effective.ExportFormat);
        Assert.Equal(ExportMode.SelectedPages, effective.ExportMode);
        Assert.True(effective.IncludePartialCover);
        Assert.True(effective.IncludePartialManifest);
        Assert.Equal(PageNumberingMode.Original, effective.PageNumberingMode);
    }
}
