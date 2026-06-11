namespace Uqeb.Api.Models.Entities;

public class Attachment
{
    public int Id { get; set; }
    public int TransactionId { get; set; }
    public string? AttachmentType { get; set; }
    public string OriginalFileName { get; set; } = string.Empty;
    public string StoredFileName { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public string? ContentType { get; set; }
    public long FileSize { get; set; }
    public int UploadedById { get; set; }
    public DateTime UploadedAt { get; set; } = DateTime.UtcNow;

    public Transaction Transaction { get; set; } = null!;
    public User UploadedBy { get; set; } = null!;
}
