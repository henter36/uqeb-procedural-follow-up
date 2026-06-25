using Uqeb.Api.Models.Enums;

namespace Uqeb.Api.DTOs.FollowUpPrint;

public class FollowUpPrintFilterRequest
{
    public int DaysSinceLastFollowUp { get; set; } = 10;
    public bool ExcludeRecentlyPrinted { get; set; } = true;
    public int PrintedLetterExclusionDays { get; set; } = 7;
    public int? DepartmentId { get; set; }
    public int? CategoryId { get; set; }
    public string? Search { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 25;
}

public class FollowUpPrintFilterSnapshot
{
    public int DaysSinceLastFollowUp { get; set; }
    public bool ExcludeRecentlyPrinted { get; set; }
    public int PrintedLetterExclusionDays { get; set; }
    public int? DepartmentId { get; set; }
    public int? CategoryId { get; set; }
    public string? Search { get; set; }
    public List<int>? TransactionIds { get; set; }
}

public class EligibleTransactionDto
{
    public int TransactionId { get; set; }
    public string IncomingNumber { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public DateTime IncomingDate { get; set; }
    public DateTime ReferenceDate { get; set; }
    public int DaysSinceReference { get; set; }
    public int ExpectedFollowUpSequence { get; set; }
    public bool RecentlyPrintedExcluded { get; set; }
    public DateTime? LastPrintRequestedAt { get; set; }
    public string? PrimaryTargetEntity { get; set; }
}

public class FollowUpPrintEligibilityPreviewDto
{
    public int MatchedCount { get; set; }
    public int EligibleTransactionCount { get; set; }
    public int EstimatedLetterCount { get; set; }
    public int NotDueYetCount { get; set; }
    public int RecentlyPrintedExcludedCount { get; set; }
    public int NoTargetCount { get; set; }
    public int EstimatedPartCount { get; set; }
}

public class PagedEligibleTransactionsDto
{
    public int TotalCount { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
    public List<EligibleTransactionDto> Items { get; set; } = [];
}

public class CreateFollowUpPrintJobRequest
{
    public FollowUpPrintFilterRequest Filter { get; set; } = new();
    public int? TemplateId { get; set; }
    public int? ResponseDeadlineDays { get; set; }
    public int? BatchSize { get; set; }
    public string? IdempotencyKey { get; set; }
}

public class FollowUpPrintJobDto
{
    public int Id { get; set; }
    public FollowUpPrintJobStatus Status { get; set; }
    public int TemplateId { get; set; }
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
    public DateTime CreatedAt { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? ReadyAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public DateTime? FailedAt { get; set; }
    public DateTime? CancelledAt { get; set; }
    public string? FailureReason { get; set; }
    public List<FollowUpPrintJobPartDto> Parts { get; set; } = [];
}

public class PagedFollowUpPrintJobsDto
{
    public int TotalCount { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
    public List<FollowUpPrintJobDto> Items { get; set; } = [];
}

public class FollowUpPrintJobPartDto
{
    public int Id { get; set; }
    public int JobId { get; set; }
    public int PartNumber { get; set; }
    public FollowUpPrintJobPartStatus Status { get; set; }
    public int LetterCount { get; set; }
    public int EstimatedPages { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? ReadyAt { get; set; }
    public DateTime? PrintedAt { get; set; }
    public string? FailureReason { get; set; }
}

public class FollowUpLetterPrintRecordDto
{
    public int Id { get; set; }
    public int TransactionId { get; set; }
    public string IncomingNumber { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public int? TargetDepartmentId { get; set; }
    public int? TargetEntityId { get; set; }
    public string? TargetEntityNameSnapshot { get; set; }
    public int TemplateId { get; set; }
    public int FollowUpSequence { get; set; }
    public int? ResponseDeadlineDays { get; set; }
    public DateTime PrintRequestedAt { get; set; }
    public DateTime? PrintConfirmedAt { get; set; }
    public int? RegisteredFollowUpId { get; set; }
    public bool IsCancelled { get; set; }
    public int? ReprintOfId { get; set; }
}

public class FollowUpPrintPendingSummaryDto
{
    public int Total { get; set; }
    public int WithinExclusionDays { get; set; }
    public int OlderThanExclusionDays { get; set; }
}

public class PendingPrintRecordsRequest
{
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 25;
}

public class CancelPrintRecordRequest
{
    public string Reason { get; set; } = string.Empty;
}

public class LinkPrintRecordRequest
{
    public int FollowUpId { get; set; }
}

public class UserNotificationDto
{
    public int Id { get; set; }
    public string Type { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public string? Link { get; set; }
    public bool IsRead { get; set; }
    public DateTime CreatedAt { get; set; }
}

public sealed class FollowUpPrintJobLetterPayload
{
    public int TransactionId { get; set; }
    public int? TargetDepartmentId { get; set; }
    public int? TargetEntityId { get; set; }
    public string TargetEntityName { get; set; } = string.Empty;
    public int FollowUpSequence { get; set; }
    public int? ResponseDeadlineDays { get; set; }
}
