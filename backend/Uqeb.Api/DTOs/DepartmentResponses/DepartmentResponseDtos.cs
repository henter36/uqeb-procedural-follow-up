using System.Text.Json.Serialization;

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

// RowVersion exists on DepartmentResponse for optimistic concurrency but is not yet wired
// end-to-end through DTOs / requests / DbUpdateConcurrencyException handling. Deferred to a future PR.
public record CreateDepartmentResponseRequest(
    [property: JsonRequired] int TransactionId,
    [property: JsonRequired] string ResponseText,
    int? DepartmentId = null
);

public record UpdateDepartmentResponseRequest(
    string ResponseText
);

public record ReviewDepartmentResponseRequest(
    string? ReviewNote
);

public record DepartmentTransactionResponseItemDto(
    int TransactionId,
    string InternalTrackingNumber,
    string Subject,
    DateTime? IncomingDate,
    string Priority,
    DateTime? AssignedDate,
    int DepartmentId,
    string DepartmentName,
    int? DepartmentResponseId,
    string? DepartmentResponseStatus,
    bool CanCreateResponse,
    bool CanEditResponse,
    bool CanSubmitResponse
);
