namespace Uqeb.Api.Reporting.DataQuality;

public sealed class DataQualityReview
{
    public int Id { get; set; }
    public string IssueKey { get; set; } = string.Empty;
    public int? TransactionId { get; set; }
    public string RuleCode { get; set; } = string.Empty;
    public bool IsReviewed { get; set; } = true;
    public string? ReviewNote { get; set; }
    public DateTime ReviewedAtUtc { get; set; } = DateTime.UtcNow;
    public int ReviewedByUserId { get; set; }
}
