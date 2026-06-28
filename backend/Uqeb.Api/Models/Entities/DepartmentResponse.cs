using Uqeb.Api.Models.Enums;

namespace Uqeb.Api.Models.Entities;

public class DepartmentResponse
{
    public int Id { get; set; }
    public int TransactionId { get; set; }
    public int DepartmentId { get; set; }
    public string ResponseText { get; set; } = string.Empty;
    public DepartmentResponseStatus Status { get; set; } = DepartmentResponseStatus.Draft;
    public int SubmittedByUserId { get; set; }
    public DateTime? SubmittedAt { get; set; }
    public int? ReviewedByUserId { get; set; }
    public DateTime? ReviewedAt { get; set; }
    public string? ReviewNote { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
    public byte[] RowVersion { get; set; } = [];

    public Transaction Transaction { get; set; } = null!;
    public Department Department { get; set; } = null!;
    public User SubmittedBy { get; set; } = null!;
    public User? ReviewedBy { get; set; }
    public ICollection<DepartmentResponseAttachment> Attachments { get; set; } = new List<DepartmentResponseAttachment>();
}
