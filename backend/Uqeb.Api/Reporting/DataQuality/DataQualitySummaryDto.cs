namespace Uqeb.Api.Reporting.DataQuality;

public sealed class DataQualitySummaryDto
{
    public int TotalIssues { get; set; }
    public int CriticalCount { get; set; }
    public int HighCount { get; set; }
    public int MediumCount { get; set; }
    public int LowCount { get; set; }
    public int AffectedTransactions { get; set; }
    public DateTime GeneratedAtUtc { get; set; }
    public List<DataQualityIssueDto> Issues { get; set; } = [];
}
