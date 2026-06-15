namespace Uqeb.Api.DTOs.LetterTemplates;

public class LetterTemplateDto
{
    public int Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public bool IsActive { get; set; }
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
