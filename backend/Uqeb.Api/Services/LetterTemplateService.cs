using Uqeb.Api.DTOs.LetterTemplates;

namespace Uqeb.Api.Services;

public interface ILetterTemplateService
{
    Task<LetterTemplateDto?> GetFollowUpTemplateAsync();
    Task<LetterTemplateDto?> UpdateFollowUpTemplateAsync(string content);
    Task<FollowUpLetterPreviewDto?> RenderFollowUpLetterAsync(
        int transactionId,
        string? targetEntity,
        string? optionalEditedContent,
        ICurrentUserService currentUser);
    Task<byte[]?> GenerateFollowUpLetterPdfAsync(
        int transactionId,
        string? targetEntity,
        string? optionalEditedContent,
        ICurrentUserService currentUser);
}

public class LetterTemplateService : ILetterTemplateService
{
    public const string FollowUpTemplateCode = FollowUpLetterRenderService.FollowUpTemplateCode;
    public static readonly string DefaultFollowUpContent = FollowUpLetterRenderService.DefaultFollowUpContent;

    private readonly ILetterTemplateAdminService _admin;
    private readonly IFollowUpLetterRenderService _render;

    public LetterTemplateService(
        ILetterTemplateAdminService admin,
        IFollowUpLetterRenderService render)
    {
        _admin = admin;
        _render = render;
    }

    public Task<LetterTemplateDto?> GetFollowUpTemplateAsync() =>
        _admin.GetDefaultFollowUpTemplateAsync();

    public Task<LetterTemplateDto?> UpdateFollowUpTemplateAsync(string content) =>
        _admin.UpdateDefaultFollowUpTemplateAsync(content, 0);

    public Task<FollowUpLetterPreviewDto?> RenderFollowUpLetterAsync(
        int transactionId,
        string? targetEntity,
        string? optionalEditedContent,
        ICurrentUserService currentUser) =>
        _render.RenderFollowUpLetterAsync(transactionId, targetEntity, optionalEditedContent, currentUser);

    public Task<byte[]?> GenerateFollowUpLetterPdfAsync(
        int transactionId,
        string? targetEntity,
        string? optionalEditedContent,
        ICurrentUserService currentUser) =>
        _render.GeneratePdfAsync(transactionId, targetEntity, optionalEditedContent, currentUser);
}
