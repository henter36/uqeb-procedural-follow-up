using Uqeb.Api.Reporting.Enums;

namespace Uqeb.Api.Models.Entities;

public class ReportExportTemplate
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public InstitutionalReportType ReportType { get; set; }
    public string SectionIdsJson { get; set; } = "[]";
    public string DefaultFiltersJson { get; set; } = "{}";
    public ExportFormat DefaultFormat { get; set; } = ExportFormat.Pdf;
    public PageNumberingMode PageNumberingMode { get; set; } = PageNumberingMode.Restart;
    public bool IncludePartialCover { get; set; }
    public bool IncludePartialManifest { get; set; }
    public ReportDetailSortBy DetailSortBy { get; set; } = ReportDetailSortBy.Default;
    public bool GroupDetailsByDepartment { get; set; }
    public int CreatedById { get; set; }
    public User CreatedBy { get; set; } = null!;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
