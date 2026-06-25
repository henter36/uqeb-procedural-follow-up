namespace Uqeb.Api.Models.Entities;

using Uqeb.Api.Models.Enums;

public class FollowUpPrintJob
{
    public int Id { get; set; }
    public int RequestedById { get; set; }
    public FollowUpPrintJobStatus Status { get; set; } = FollowUpPrintJobStatus.Queued;
    public string FilterSnapshotJson { get; set; } = "{}";
    public int TemplateId { get; set; }
    public int? ResponseDeadlineDays { get; set; }
    public bool ExcludeRecentlyPrinted { get; set; } = true;
    public int PrintedLetterExclusionDays { get; set; } = 7;
    public int DaysSinceLastFollowUp { get; set; } = 10;
    public int BatchSize { get; set; } = 25;
    public int NextPayloadOrdinal { get; set; }
    public int TotalTransactions { get; set; }
    public int TotalLetters { get; set; }
    public int ProcessedLetters { get; set; }
    public int ReadyLetters { get; set; }
    public int FailedLetters { get; set; }
    public int SkippedLetters { get; set; }
    public int TotalParts { get; set; }
    public int ReadyParts { get; set; }
    public int PrintedParts { get; set; }
    public int CurrentPart { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? StartedAt { get; set; }
    public DateTime? ReadyAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public DateTime? FailedAt { get; set; }
    public DateTime? CancelledAt { get; set; }
    public string? FailureReason { get; set; }
    public string? LeaseOwner { get; set; }
    public DateTime? LeaseExpiresAt { get; set; }
    public int RetryCount { get; set; }
    public byte[] RowVersion { get; set; } = Array.Empty<byte>();

    public User RequestedBy { get; set; } = null!;
    public LetterTemplate Template { get; set; } = null!;
    public ICollection<FollowUpPrintJobPart> Parts { get; set; } = new List<FollowUpPrintJobPart>();
    public ICollection<FollowUpPrintJobPayload> Payloads { get; set; } = new List<FollowUpPrintJobPayload>();
    public ICollection<FollowUpLetterPrintRecord> PrintRecords { get; set; } = new List<FollowUpLetterPrintRecord>();
}
