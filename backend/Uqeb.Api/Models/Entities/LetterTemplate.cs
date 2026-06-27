namespace Uqeb.Api.Models.Entities;

using System.ComponentModel.DataAnnotations;
using Uqeb.Api.Models.Enums;

public class LetterTemplate
{
    public int Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public LetterTemplateType TemplateType { get; set; } = LetterTemplateType.FollowUp;
    public string Content { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public bool IsDefault { get; set; }
    public int SortOrder { get; set; }

    [MaxLength(200)]
    public string? DefaultSignatoryPosition { get; set; }

    [MaxLength(200)]
    public string? DefaultSignatoryName { get; set; }

    [MaxLength(200)]
    public string? DefaultSignatoryRank { get; set; }

    public int? CreatedById { get; set; }
    public int? UpdatedById { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
    public byte[] RowVersion { get; set; } = Array.Empty<byte>();

    public User? CreatedBy { get; set; }
    public User? UpdatedBy { get; set; }
}
