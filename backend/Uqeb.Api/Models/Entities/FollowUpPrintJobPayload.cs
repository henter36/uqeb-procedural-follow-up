namespace Uqeb.Api.Models.Entities;

using Uqeb.Api.Models.Enums;

public class FollowUpPrintJobPayload
{
    public int Id { get; set; }
    public int JobId { get; set; }
    public int PayloadOrdinal { get; set; }
    public int TransactionId { get; set; }
    public int? TargetDepartmentId { get; set; }
    public int? TargetEntityId { get; set; }
    public string TargetEntityName { get; set; } = string.Empty;
    public int FollowUpSequence { get; set; }
    public int? ResponseDeadlineDays { get; set; }
    public string SnapshotJson { get; set; } = "{}";
    public int? PartId { get; set; }
    public FollowUpPrintJobPayloadStatus Status { get; set; } = FollowUpPrintJobPayloadStatus.Pending;
    public string? FailureReason { get; set; }
    public byte[] RowVersion { get; set; } = Array.Empty<byte>();

    public FollowUpPrintJob Job { get; set; } = null!;
    public FollowUpPrintJobPart? Part { get; set; }
}
