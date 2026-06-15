using System.ComponentModel.DataAnnotations;

namespace Uqeb.Api.DTOs.Transactions;

public class OutgoingPartyDto
{
    public int Id { get; set; }
    public int ExternalPartyId { get; set; }
    public string PartyName { get; set; } = string.Empty;
}

public class OutgoingDepartmentDto
{
    public int Id { get; set; }
    public int DepartmentId { get; set; }
    public string DepartmentName { get; set; } = string.Empty;
}

public class TransactionListDto
{
    public int Id { get; set; }
    public string InternalTrackingNumber { get; set; } = string.Empty;
    public string IncomingNumber { get; set; } = string.Empty;
    public DateTime IncomingDate { get; set; }
    public string Subject { get; set; } = string.Empty;
    public string? IncomingFrom { get; set; }
    public string IncomingSourceType { get; set; } = "External";
    public string? OutgoingNumber { get; set; }
    public DateTime? OutgoingDate { get; set; }
    public List<string> OutgoingPartyNames { get; set; } = new();
    public List<string> OutgoingDepartmentNames { get; set; } = new();
    public string Status { get; set; } = string.Empty;
    public string Priority { get; set; } = string.Empty;
    public string? CategoryName { get; set; }
    public bool RequiresResponse { get; set; }
    public bool ResponseCompleted { get; set; }
    public int? ResponseDays { get; set; }
    public DateTime? ResponseDueDate { get; set; }
    public int? DaysRemainingForResponse { get; set; }
    public int DaysSinceIncoming { get; set; }
    public int? DaysSinceLastFollowUp { get; set; }
    public DateTime? LastFollowUpDate { get; set; }
    public string? ResponseTimingStatus { get; set; }
    public string? ResponseTimingLabel { get; set; }
    public bool IsOverdue { get; set; }
    public bool IsResponseOverdue { get; set; }
    public bool HasPendingAssignments { get; set; }
    public bool IsArchived { get; set; }
    public string CreatedByName { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}

public class TransactionDetailDto : TransactionListDto
{
    public string? OutgoingTo { get; set; }
    public int? IncomingFromPartyId { get; set; }
    public int? IncomingFromDepartmentId { get; set; }
    public int? CategoryId { get; set; }
    public string ResponseType { get; set; } = string.Empty;
    public int? ResponseDueDays { get; set; }
    public DateTime? ResponseCompletedDate { get; set; }
    public string? ResponseSummary { get; set; }
    public string? Category { get; set; }
    public string? Notes { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public List<OutgoingPartyDto> OutgoingParties { get; set; } = new();
    public List<OutgoingDepartmentDto> OutgoingDepartments { get; set; } = new();
    public List<string> RepliedDepartmentNames { get; set; } = new();
    public List<string> PendingDepartmentNames { get; set; } = new();
    public List<FollowUpDto> FollowUps { get; set; } = new();
    public List<AssignmentDto> Assignments { get; set; } = new();
    public List<AttachmentDto> Attachments { get; set; } = new();
    public List<AuditLogDto> AuditLogs { get; set; } = new();
}

public class CreateTransactionRequest
{
    [Required(ErrorMessage = "رقم الوارد مطلوب")]
    public string IncomingNumber { get; set; } = string.Empty;

    [Required(ErrorMessage = "تاريخ الوارد مطلوب")]
    public DateTime IncomingDate { get; set; }

    [Required(ErrorMessage = "الموضوع مطلوب")]
    public string Subject { get; set; } = string.Empty;

    public string? IncomingSourceType { get; set; }
    public int? IncomingFromPartyId { get; set; }
    public int? IncomingFromDepartmentId { get; set; }
    public string? OutgoingNumber { get; set; }
    public DateTime? OutgoingDate { get; set; }
    public List<int>? OutgoingPartyIds { get; set; }
    public List<int>? OutgoingDepartmentIds { get; set; }
    public bool RequiresResponse { get; set; }
    public string? ResponseType { get; set; }
    public int? ResponseDueDays { get; set; }
    public string Priority { get; set; } = "Normal";
    public int? CategoryId { get; set; }
    public string? Notes { get; set; }
}

public class UpdateTransactionRequest
{
    public string? IncomingNumber { get; set; }
    public DateTime? IncomingDate { get; set; }
    public string? Subject { get; set; }
    public string? IncomingSourceType { get; set; }
    public int? IncomingFromPartyId { get; set; }
    public int? IncomingFromDepartmentId { get; set; }
    public string? OutgoingNumber { get; set; }
    public DateTime? OutgoingDate { get; set; }
    public List<int>? OutgoingPartyIds { get; set; }
    public List<int>? OutgoingDepartmentIds { get; set; }
    public bool? RequiresResponse { get; set; }
    public string? ResponseType { get; set; }
    public int? ResponseDueDays { get; set; }
    public bool? ResponseCompleted { get; set; }
    public DateTime? ResponseCompletedDate { get; set; }
    public string? Status { get; set; }
    public string? Priority { get; set; }
    public int? CategoryId { get; set; }
    public string? Notes { get; set; }
}

public class CompleteResponseRequest
{
    [Required(ErrorMessage = "تاريخ الإفادة مطلوب")]
    public DateTime ResponseDate { get; set; }

    [Required(ErrorMessage = "ملخص الإفادة مطلوب")]
    public string ResponseSummary { get; set; } = string.Empty;

    public string? OutgoingNumber { get; set; }
    public DateTime? OutgoingDate { get; set; }
}

public class TransactionSearchRequest
{
    public string? IncomingNumber { get; set; }
    public string? OutgoingNumber { get; set; }
    public string? Subject { get; set; }
    public string? IncomingSourceType { get; set; }
    public int? IncomingFromPartyId { get; set; }
    public int? IncomingFromDepartmentId { get; set; }
    public string? Status { get; set; }
    public int? DepartmentId { get; set; }
    public int? CategoryId { get; set; }
    public int? OutgoingPartyId { get; set; }
    public int? OutgoingDepartmentId { get; set; }
    public DateTime? DateFrom { get; set; }
    public DateTime? DateTo { get; set; }
    public DateTime? ResponseDueDateFrom { get; set; }
    public DateTime? ResponseDueDateTo { get; set; }
    public bool? OverdueOnly { get; set; }
    public bool? RequiresResponse { get; set; }
    public bool? ResponseCompleted { get; set; }
    public bool? ResponseOverdue { get; set; }
    public bool? HasPendingAssignments { get; set; }
    public bool? HasPartialReplies { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
    public string? SortBy { get; set; } = "IncomingDate";
    public bool SortDesc { get; set; } = true;
}

public class PagedResult<T>
{
    public List<T> Items { get; set; } = new();
    public int TotalCount { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalPages { get; set; }
    public bool HasNextPage { get; set; }
    public bool HasPreviousPage { get; set; }

    public static PagedResult<T> Create(List<T> items, int totalCount, int page, int pageSize) =>
        new()
        {
            Items = items,
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize,
            TotalPages = pageSize > 0 ? (int)Math.Ceiling((double)totalCount / pageSize) : 0,
            HasNextPage = pageSize > 0 && page * pageSize < totalCount,
            HasPreviousPage = page > 1
        };
}

public class FollowUpRecipientDto
{
    public int Id { get; set; }
    public int ExternalPartyId { get; set; }
    public string PartyName { get; set; } = string.Empty;
}

public class FollowUpDepartmentDto
{
    public int Id { get; set; }
    public int DepartmentId { get; set; }
    public string DepartmentName { get; set; } = string.Empty;
}

public class FollowUpDepartmentOptionDto
{
    public int DepartmentId { get; set; }
    public string DepartmentName { get; set; } = string.Empty;
    public bool IsDefaultSelected { get; set; }
    public string Source { get; set; } = string.Empty;
}

public class FollowUpDto
{
    public int Id { get; set; }
    public string? FollowUpNumber { get; set; }
    public DateTime FollowUpDate { get; set; }
    public string? SentTo { get; set; }
    public List<FollowUpRecipientDto> Recipients { get; set; } = new();
    public List<FollowUpDepartmentDto> Departments { get; set; } = new();
    public string? Notes { get; set; }
    public bool RequiresReply { get; set; }
    public string ReplyStatus { get; set; } = string.Empty;
    public DateTime? ReplyDate { get; set; }
    public string? ReplySummary { get; set; }
    public string CreatedByName { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}

public class CreateFollowUpRequest
{
    public string? FollowUpNumber { get; set; }
    public DateTime FollowUpDate { get; set; }
    public List<int>? RecipientPartyIds { get; set; }
    public List<int>? DepartmentIds { get; set; }
    public string? Notes { get; set; }
}

public class ReplyFollowUpRequest
{
    public DateTime ReplyDate { get; set; }
    public string ReplySummary { get; set; } = string.Empty;
}

public class AssignmentDto
{
    public int Id { get; set; }
    public int DepartmentId { get; set; }
    public string DepartmentName { get; set; } = string.Empty;
    public DateTime AssignedDate { get; set; }
    public string? RequiredAction { get; set; }
    public bool RequiresReply { get; set; }
    public int? ReplyDueDays { get; set; }
    public DateTime? DueDate { get; set; }
    public string ReplyStatus { get; set; } = string.Empty;
    public DateTime? ReplyDate { get; set; }
    public string? ReplySummary { get; set; }
    public string Status { get; set; } = string.Empty;
    public bool IsOverdue { get; set; }
    public string CreatedByName { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}

public class CreateAssignmentRequest
{
    public int DepartmentId { get; set; }
    public DateTime AssignedDate { get; set; }
    public string? RequiredAction { get; set; }
    public int? ReplyDueDays { get; set; }
    public DateTime? DueDate { get; set; }
}

public class ReplyAssignmentRequest
{
    public DateTime ReplyDate { get; set; }
    public string ReplySummary { get; set; } = string.Empty;
}

public class AttachmentDto
{
    public int Id { get; set; }
    public string? AttachmentType { get; set; }
    public string OriginalFileName { get; set; } = string.Empty;
    public string? ContentType { get; set; }
    public long FileSize { get; set; }
    public string UploadedByName { get; set; } = string.Empty;
    public DateTime UploadedAt { get; set; }
}

public class AuditLogDto
{
    public int Id { get; set; }
    public string Action { get; set; } = string.Empty;
    public string? EntityName { get; set; }
    public int? EntityId { get; set; }
    public string? OldValue { get; set; }
    public string? NewValue { get; set; }
    public string UserName { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}
