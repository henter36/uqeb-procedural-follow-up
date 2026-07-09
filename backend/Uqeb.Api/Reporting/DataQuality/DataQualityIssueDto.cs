namespace Uqeb.Api.Reporting.DataQuality;

public sealed class DataQualityIssueDto
{
    public string Id { get; set; } = string.Empty;
    public string IssueKey { get; set; } = string.Empty;
    public string RuleCode { get; set; } = string.Empty;
    public DataQualitySeverity Severity { get; set; }
    public string SeverityLabel { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string IssueType { get; set; } = string.Empty;
    public int? TransactionId { get; set; }
    public string? TrackingNumber { get; set; }
    public string? IncomingNumber { get; set; }
    public string? Subject { get; set; }
    public int? RelatedTransactionId { get; set; }
    public string? RelatedTrackingNumber { get; set; }
    public string? RelatedIncomingNumber { get; set; }
    public DateTime? RelatedIncomingDate { get; set; }
    public string? SimilarityReason { get; set; }
    public double? SimilarityScore { get; set; }
    public string? DepartmentName { get; set; }
    public string FieldName { get; set; } = string.Empty;
    public string? CurrentValue { get; set; }
    public int? DaysValue { get; set; }
    public DateTime? PrimaryDate { get; set; }
    public DateTime? ComparedDate { get; set; }
    public string Impact { get; set; } = string.Empty;
    public string SuggestedAction { get; set; } = string.Empty;
    public bool IsReviewed { get; set; }
    public DateTime? ReviewedAtUtc { get; set; }
    public string? ReviewedByName { get; set; }
    public string? ReviewNote { get; set; }
}

public enum DataQualitySeverity
{
    Low = 1,
    Medium = 2,
    High = 3,
    Critical = 4
}
