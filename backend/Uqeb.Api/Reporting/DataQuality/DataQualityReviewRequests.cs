namespace Uqeb.Api.Reporting.DataQuality;

public sealed class MarkDataQualityReviewRequest
{
    public string IssueKey { get; set; } = string.Empty;
    public int? TransactionId { get; set; }
    public string RuleCode { get; set; } = string.Empty;
    public string? Note { get; set; }
}
