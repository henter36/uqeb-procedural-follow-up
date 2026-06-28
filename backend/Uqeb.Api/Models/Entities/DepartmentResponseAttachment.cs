namespace Uqeb.Api.Models.Entities;

public class DepartmentResponseAttachment
{
    public int Id { get; set; }
    public int DepartmentResponseId { get; set; }
    public string OriginalFileName { get; set; } = string.Empty;
    public string StoredFileName { get; set; } = string.Empty;
    public string? ContentType { get; set; }
    public long FileSizeBytes { get; set; }
    public string StoragePath { get; set; } = string.Empty;
    public int UploadedByUserId { get; set; }
    public DateTime UploadedAt { get; set; } = DateTime.UtcNow;
    public string? Sha256 { get; set; }
    public bool IsDeleted { get; set; }
    public int? DeletedByUserId { get; set; }
    public DateTime? DeletedAt { get; set; }

    public DepartmentResponse DepartmentResponse { get; set; } = null!;
    public User UploadedBy { get; set; } = null!;
    public User? DeletedBy { get; set; }
}
