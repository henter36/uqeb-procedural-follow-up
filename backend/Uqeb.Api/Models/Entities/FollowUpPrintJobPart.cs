namespace Uqeb.Api.Models.Entities;

using Uqeb.Api.Models.Enums;

public class FollowUpPrintJobPart
{
    public int Id { get; set; }
    public int JobId { get; set; }
    public int PartNumber { get; set; }
    public FollowUpPrintJobPartStatus Status { get; set; } = FollowUpPrintJobPartStatus.Pending;
    public int LetterCount { get; set; }
    public int EstimatedPages { get; set; }
    public string PayloadJson { get; set; } = "[]";
    public string? FailureReason { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ReadyAt { get; set; }
    public DateTime? PrintedAt { get; set; }
    public byte[] RowVersion { get; set; } = Array.Empty<byte>();

    public FollowUpPrintJob Job { get; set; } = null!;
    public ICollection<FollowUpPrintJobPayload> Payloads { get; set; } = new List<FollowUpPrintJobPayload>();
}
