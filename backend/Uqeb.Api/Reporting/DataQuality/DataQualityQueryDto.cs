namespace Uqeb.Api.Reporting.DataQuality;

public sealed class DataQualityQueryDto
{
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
    public string? Severity { get; set; }
    public string? Category { get; set; }
    public int? DepartmentId { get; set; }
    public int Limit { get; set; } = 500;
    public int? OverdueMoreThanDays { get; set; }
    public bool IncludeReferralDateAfterIncomingDate { get; set; }
    public int? ResponsePeriodLessThanDays { get; set; }
    public bool IncludeReviewed { get; set; }
    public bool ReviewedOnly { get; set; }
}
