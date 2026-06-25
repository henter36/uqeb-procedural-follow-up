namespace Uqeb.Api.DTOs.Security;

public sealed class AuditIntegrityDiagnosticReportDto
{
    public int TotalAuditsScanned { get; set; }
    public int MissingTransactionIdCount { get; set; }
    public int MissingEntityIdCount { get; set; }
    public int RepairableAssignmentLinkCount { get; set; }
    public int AmbiguousCount { get; set; }
    public IReadOnlyList<AuditIntegrityIssueDto> Issues { get; set; } = Array.Empty<AuditIntegrityIssueDto>();
}

public sealed class AuditIntegrityIssueDto
{
    public int AuditLogId { get; set; }
    public string Classification { get; set; } = "";
    public string? EntityName { get; set; }
    public int? EntityId { get; set; }
    public int? TransactionId { get; set; }
    public string? Action { get; set; }
    public DateTime CreatedAt { get; set; }
    public string? Detail { get; set; }
}
