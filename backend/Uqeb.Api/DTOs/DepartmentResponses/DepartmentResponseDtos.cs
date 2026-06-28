using Uqeb.Api.Models.Enums;

namespace Uqeb.Api.DTOs.DepartmentResponses;

public record DepartmentResponseDto(
    int Id,
    int TransactionId,
    string TransactionSubject,
    string InternalTrackingNumber,
    int DepartmentId,
    string DepartmentName,
    string ResponseText,
    string Status,
    string SubmittedByName,
    DateTime? SubmittedAt,
    string? ReviewedByName,
    DateTime? ReviewedAt,
    string? ReviewNote,
    DateTime CreatedAt,
    DateTime? UpdatedAt,
    IReadOnlyList<DepartmentResponseAttachmentDto> Attachments
);

public record DepartmentResponseAttachmentDto(
    int Id,
    string OriginalFileName,
    string? ContentType,
    long FileSizeBytes,
    string UploadedByName,
    DateTime UploadedAt
);

public record DepartmentResponseSummaryDto(
    int Id,
    int TransactionId,
    string TransactionSubject,
    string InternalTrackingNumber,
    int DepartmentId,
    string DepartmentName,
    string Status,
    DateTime? SubmittedAt,
    DateTime CreatedAt
);

public record CreateDepartmentResponseRequest(
    int TransactionId,
    string ResponseText
);

public record UpdateDepartmentResponseRequest(
    string ResponseText
);

public record ReviewDepartmentResponseRequest(
    string? ReviewNote
);

public record DepartmentTransactionItem(
    int TransactionId,
    string InternalTrackingNumber,
    string Subject,
    string TransactionStatus,
    DateTime? AssignedDate,
    int? ResponseId,
    string? ResponseStatus
);
