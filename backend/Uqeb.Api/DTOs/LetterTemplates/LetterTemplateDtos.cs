using Uqeb.Api.Models.Enums;

namespace Uqeb.Api.DTOs.LetterTemplates;

public class LetterTemplateDto
{
    public int Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public LetterTemplateType TemplateType { get; set; } = LetterTemplateType.FollowUp;
    public string Content { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public bool IsDefault { get; set; }
    public int SortOrder { get; set; }
    public string? DefaultSignatoryPosition { get; set; }
    public string? DefaultSignatoryName { get; set; }
    public string? DefaultSignatoryRank { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

public class LetterTemplateListRequest
{
    public LetterTemplateType? Type { get; set; }
    public bool? Active { get; set; }
    public string? Search { get; set; }
}

public class CreateLetterTemplateRequest
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public LetterTemplateType? TemplateType { get; set; }
    public string Content { get; set; } = string.Empty;
    public bool? IsActive { get; set; }
    public bool? IsDefault { get; set; }
    public string? DefaultSignatoryPosition { get; set; }
    public string? DefaultSignatoryName { get; set; }
    public string? DefaultSignatoryRank { get; set; }
}

public class UpdateLetterTemplateAdminRequest
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public LetterTemplateType? TemplateType { get; set; }
    public string Content { get; set; } = string.Empty;
    public bool? IsActive { get; set; }
    public string? DefaultSignatoryPosition { get; set; }
    public string? DefaultSignatoryName { get; set; }
    public string? DefaultSignatoryRank { get; set; }
}

public class ReorderLetterTemplatesRequest
{
    public List<LetterTemplateOrderItem> Items { get; set; } = [];
}

public class LetterTemplateOrderItem
{
    public int Id { get; set; }
    public int SortOrder { get; set; }
}

public class LetterTemplateValidationResult
{
    public IReadOnlyList<string> UnknownVariables { get; set; } = [];
    public bool IsValid => UnknownVariables.Count == 0;
}

public class LetterTemplatePreviewRequest
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public LetterTemplateType? TemplateType { get; set; }
    public string Content { get; set; } = string.Empty;
    public string? DefaultSignatoryPosition { get; set; }
    public string? DefaultSignatoryName { get; set; }
    public string? DefaultSignatoryRank { get; set; }
}

public class LetterTemplatePreviewResponse
{
    public string Html { get; set; } = string.Empty;
}

public class UpdateLetterTemplateRequest
{
    public string Content { get; set; } = string.Empty;
}

public class FollowUpLetterRequest
{
    public string? TargetEntity { get; set; }
    public string? Content { get; set; }
}

public class FollowUpLetterPreviewDto
{
    public string Content { get; set; } = string.Empty;
    public string TargetEntity { get; set; } = string.Empty;
}
