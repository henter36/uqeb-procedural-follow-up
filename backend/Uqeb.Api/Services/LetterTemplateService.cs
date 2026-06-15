using Microsoft.EntityFrameworkCore;
using Uqeb.Api.Data;
using Uqeb.Api.DTOs.LetterTemplates;
using Uqeb.Api.Helpers;
using Uqeb.Api.Models.Entities;
using Uqeb.Api.Models.Enums;

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
    public const string FollowUpTemplateCode = "follow_up_letter";

    public static readonly string DefaultFollowUpContent =
        "السلام عليكم ورحمة الله وبركاته،،\n\n" +
        "إشارةً إلى المعاملة رقم {IncomingNumber} وتاريخ {IncomingDate} بشأن: {Subject}\n\n" +
        "والمحالة إلى: {TargetEntity}\n\n" +
        "نأمل سرعة الإفادة عما تم حيال الموضوع، وتزويدنا بما لديكم من مرئيات أو إجراءات أو مستندات ذات علاقة، وذلك خلال المدة النظامية، مع التأكيد على أهمية استكمال اللازم والرفع بما يتم حيالها.\n\n" +
        "والسلام عليكم ورحمة الله وبركاته،،";

    private readonly AppDbContext _db;

    public LetterTemplateService(AppDbContext db) => _db = db;

    public async Task<LetterTemplateDto?> GetFollowUpTemplateAsync()
    {
        var template = await GetOrCreateFollowUpTemplateAsync();
        return template == null ? null : Map(template);
    }

    public async Task<LetterTemplateDto?> UpdateFollowUpTemplateAsync(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
            throw new InvalidOperationException("محتوى القالب مطلوب");

        var template = await GetOrCreateFollowUpTemplateAsync();
        if (template == null) return null;

        template.Content = content.Trim();
        template.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return Map(template);
    }

    public async Task<FollowUpLetterPreviewDto?> RenderFollowUpLetterAsync(
        int transactionId,
        string? targetEntity,
        string? optionalEditedContent,
        ICurrentUserService currentUser)
    {
        var transaction = await LoadTransactionAsync(transactionId, currentUser);
        if (transaction == null) return null;

        var resolvedTarget = await ResolveTargetEntityAsync(transactionId, targetEntity, transaction);
        var content = !string.IsNullOrWhiteSpace(optionalEditedContent)
            ? optionalEditedContent.Trim()
            : await RenderFromTemplateAsync(transaction, resolvedTarget);

        return new FollowUpLetterPreviewDto
        {
            Content = content,
            TargetEntity = resolvedTarget
        };
    }

    public async Task<byte[]?> GenerateFollowUpLetterPdfAsync(
        int transactionId,
        string? targetEntity,
        string? optionalEditedContent,
        ICurrentUserService currentUser)
    {
        var preview = await RenderFollowUpLetterAsync(transactionId, targetEntity, optionalEditedContent, currentUser);
        if (preview == null) return null;
        return FollowUpLetterPdfExporter.ToPdf(preview.Content, DateTime.UtcNow);
    }

    private async Task<LetterTemplate?> GetOrCreateFollowUpTemplateAsync()
    {
        var template = await _db.LetterTemplates
            .FirstOrDefaultAsync(t => t.Code == FollowUpTemplateCode);

        if (template != null) return template;

        template = new LetterTemplate
        {
            Code = FollowUpTemplateCode,
            Name = "قالب خطاب التعقيب",
            Content = DefaultFollowUpContent,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };
        _db.LetterTemplates.Add(template);
        await _db.SaveChangesAsync();
        return template;
    }

    private async Task<string> RenderFromTemplateAsync(Transaction transaction, string targetEntity)
    {
        var template = await GetOrCreateFollowUpTemplateAsync();
        return LetterTemplateRenderer.Render(
            template!.Content,
            transaction.IncomingNumber,
            transaction.IncomingDate,
            transaction.Subject,
            targetEntity,
            DateTime.UtcNow);
    }

    private async Task<Transaction?> LoadTransactionAsync(int transactionId, ICurrentUserService currentUser)
    {
        var transaction = await _db.Transactions.AsNoTracking()
            .Include(t => t.IncomingFromParty)
            .Include(t => t.IncomingFromDepartment)
            .FirstOrDefaultAsync(t => t.Id == transactionId);

        if (transaction == null) return null;
        if (!await CanAccessTransactionAsync(transactionId, currentUser)) return null;
        return transaction;
    }

    private async Task<bool> CanAccessTransactionAsync(int transactionId, ICurrentUserService currentUser)
    {
        if (currentUser.Role != UserRole.DepartmentUser || !currentUser.DepartmentId.HasValue)
            return true;

        return await _db.Assignments.AsNoTracking()
            .AnyAsync(a => a.TransactionId == transactionId && a.DepartmentId == currentUser.DepartmentId.Value);
    }

    private async Task<string> ResolveTargetEntityAsync(int transactionId, string? targetEntity, Transaction transaction)
    {
        if (!string.IsNullOrWhiteSpace(targetEntity))
            return targetEntity.Trim();

        var outgoing = await _db.TransactionOutgoingDepartments.AsNoTracking()
            .Where(x => x.TransactionId == transactionId)
            .OrderBy(x => x.Id)
            .Select(x => x.Department.Name)
            .FirstOrDefaultAsync();
        if (!string.IsNullOrWhiteSpace(outgoing)) return outgoing;

        var assignmentDept = await _db.Assignments.AsNoTracking()
            .Where(a => a.TransactionId == transactionId)
            .OrderBy(a => a.CreatedAt)
            .Select(a => a.Department.Name)
            .FirstOrDefaultAsync();
        if (!string.IsNullOrWhiteSpace(assignmentDept)) return assignmentDept;

        return transaction.IncomingSourceType switch
        {
            IncomingSourceType.Internal => transaction.IncomingFromDepartment?.Name ?? transaction.IncomingFrom ?? "",
            IncomingSourceType.External => transaction.IncomingFromParty?.Name ?? transaction.IncomingFrom ?? "",
            _ => transaction.IncomingFrom ?? ""
        };
    }

    private static LetterTemplateDto Map(LetterTemplate template) => new()
    {
        Id = template.Id,
        Code = template.Code,
        Name = template.Name,
        Content = template.Content,
        IsActive = template.IsActive
    };
}
