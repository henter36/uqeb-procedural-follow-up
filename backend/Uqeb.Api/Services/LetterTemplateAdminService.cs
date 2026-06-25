using Microsoft.EntityFrameworkCore;
using Uqeb.Api.Data;
using Uqeb.Api.DTOs.LetterTemplates;
using Uqeb.Api.Helpers;
using Uqeb.Api.Models.Entities;
using Uqeb.Api.Models.Enums;

namespace Uqeb.Api.Services;

public interface ILetterTemplateAdminService
{
    Task<List<LetterTemplateDto>> ListAsync(LetterTemplateListRequest request, CancellationToken cancellationToken = default);
    Task<LetterTemplateDto?> GetByIdAsync(int id, CancellationToken cancellationToken = default);
    Task<LetterTemplateDto> CreateAsync(CreateLetterTemplateRequest request, int actorUserId, CancellationToken cancellationToken = default);
    Task<LetterTemplateDto?> UpdateAsync(int id, UpdateLetterTemplateAdminRequest request, int actorUserId, CancellationToken cancellationToken = default);
    Task<LetterTemplateDto> CopyAsync(int id, int actorUserId, CancellationToken cancellationToken = default);
    Task<LetterTemplateDto?> SetDefaultAsync(int id, int actorUserId, CancellationToken cancellationToken = default);
    Task<LetterTemplateDto?> SetActiveAsync(int id, bool isActive, int actorUserId, CancellationToken cancellationToken = default);
    Task ReorderAsync(ReorderLetterTemplatesRequest request, int actorUserId, CancellationToken cancellationToken = default);
    Task DeleteAsync(int id, int? replacementDefaultId, int actorUserId, CancellationToken cancellationToken = default);
    LetterTemplateValidationResult ValidateContent(string content);
    Task<LetterTemplateDto?> GetDefaultFollowUpTemplateAsync(CancellationToken cancellationToken = default);
    Task<LetterTemplateDto?> UpdateDefaultFollowUpTemplateAsync(string content, int actorUserId, CancellationToken cancellationToken = default);
}

public sealed class LetterTemplateAdminService : ILetterTemplateAdminService
{
    private readonly AppDbContext _db;

    public LetterTemplateAdminService(AppDbContext db) => _db = db;

    public async Task<List<LetterTemplateDto>> ListAsync(LetterTemplateListRequest request, CancellationToken cancellationToken = default)
    {
        var query = _db.LetterTemplates.AsNoTracking().AsQueryable();

        if (request.Type.HasValue)
            query = query.Where(t => t.TemplateType == request.Type.Value);

        if (request.Active.HasValue)
            query = query.Where(t => t.IsActive == request.Active.Value);

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var term = request.Search.Trim();
            query = query.Where(t =>
                t.Name.Contains(term) ||
                t.Code.Contains(term) ||
                (t.Description != null && t.Description.Contains(term)));
        }

        return await query
            .OrderBy(t => t.SortOrder)
            .ThenBy(t => t.Name)
            .Select(MapExpr)
            .ToListAsync(cancellationToken);
    }

    public async Task<LetterTemplateDto?> GetByIdAsync(int id, CancellationToken cancellationToken = default) =>
        await _db.LetterTemplates.AsNoTracking().Where(t => t.Id == id).Select(MapExpr).FirstOrDefaultAsync(cancellationToken);

    public async Task<LetterTemplateDto> CreateAsync(
        CreateLetterTemplateRequest request,
        int actorUserId,
        CancellationToken cancellationToken = default)
    {
        ValidateTemplateContent(request.Content);
        var name = RequireName(request.Name);
        var code = await GenerateUniqueCodeAsync(name, cancellationToken);
        var sortOrder = await _db.LetterTemplates
            .Where(t => t.TemplateType == request.TemplateType)
            .Select(t => (int?)t.SortOrder)
            .MaxAsync(cancellationToken) ?? -1;

        var template = new LetterTemplate
        {
            Code = code,
            Name = name,
            Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim(),
            TemplateType = request.TemplateType,
            Content = request.Content.Trim(),
            IsActive = request.IsActive,
            IsDefault = request.IsDefault,
            SortOrder = sortOrder + 1,
            CreatedById = actorUserId,
            CreatedAt = DateTime.UtcNow,
        };

        if (template.IsDefault)
            await ClearDefaultAsync(template.TemplateType, null, cancellationToken);

        _db.LetterTemplates.Add(template);
        await _db.SaveChangesAsync(cancellationToken);
        return (await GetByIdAsync(template.Id, cancellationToken))!;
    }

    public async Task<LetterTemplateDto?> UpdateAsync(
        int id,
        UpdateLetterTemplateAdminRequest request,
        int actorUserId,
        CancellationToken cancellationToken = default)
    {
        ValidateTemplateContent(request.Content);
        var template = await _db.LetterTemplates.FirstOrDefaultAsync(t => t.Id == id, cancellationToken);
        if (template == null)
            return null;

        template.Name = RequireName(request.Name);
        template.Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();
        template.TemplateType = request.TemplateType;
        template.Content = request.Content.Trim();
        template.IsActive = request.IsActive;
        template.UpdatedById = actorUserId;
        template.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync(cancellationToken);
        return await GetByIdAsync(id, cancellationToken);
    }

    public async Task<LetterTemplateDto> CopyAsync(int id, int actorUserId, CancellationToken cancellationToken = default)
    {
        var source = await _db.LetterTemplates.AsNoTracking().FirstOrDefaultAsync(t => t.Id == id, cancellationToken)
            ?? throw new InvalidOperationException("القالب غير موجود.");

        var copyName = $"{source.Name} (نسخة)";
        var code = await GenerateUniqueCodeAsync(copyName, cancellationToken);
        var sortOrder = await _db.LetterTemplates
            .Where(t => t.TemplateType == source.TemplateType)
            .Select(t => (int?)t.SortOrder)
            .MaxAsync(cancellationToken) ?? -1;

        var copy = new LetterTemplate
        {
            Code = code,
            Name = copyName,
            Description = source.Description,
            TemplateType = source.TemplateType,
            Content = source.Content,
            IsActive = source.IsActive,
            IsDefault = false,
            SortOrder = sortOrder + 1,
            CreatedById = actorUserId,
            CreatedAt = DateTime.UtcNow,
        };

        _db.LetterTemplates.Add(copy);
        await _db.SaveChangesAsync(cancellationToken);
        return (await GetByIdAsync(copy.Id, cancellationToken))!;
    }

    public async Task<LetterTemplateDto?> SetDefaultAsync(int id, int actorUserId, CancellationToken cancellationToken = default)
    {
        var template = await _db.LetterTemplates.FirstOrDefaultAsync(t => t.Id == id, cancellationToken);
        if (template == null)
            return null;

        if (!template.IsActive)
            throw new InvalidOperationException("لا يمكن تعيين قالب غير نشط كافتراضي.");

        await ClearDefaultAsync(template.TemplateType, template.Id, cancellationToken);
        template.IsDefault = true;
        template.UpdatedById = actorUserId;
        template.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
        return await GetByIdAsync(id, cancellationToken);
    }

    public async Task<LetterTemplateDto?> SetActiveAsync(
        int id,
        bool isActive,
        int actorUserId,
        CancellationToken cancellationToken = default)
    {
        var template = await _db.LetterTemplates.FirstOrDefaultAsync(t => t.Id == id, cancellationToken);
        if (template == null)
            return null;

        if (!isActive && template.IsDefault)
            throw new InvalidOperationException("لا يمكن إلغاء تفعيل القالب الافتراضي. عيّن قالبًا افتراضيًا آخر أولًا.");

        template.IsActive = isActive;
        template.UpdatedById = actorUserId;
        template.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
        return await GetByIdAsync(id, cancellationToken);
    }

    public async Task ReorderAsync(ReorderLetterTemplatesRequest request, int actorUserId, CancellationToken cancellationToken = default)
    {
        if (request.Items.Count == 0)
            return;

        var ids = request.Items.Select(i => i.Id).ToList();
        var templates = await _db.LetterTemplates.Where(t => ids.Contains(t.Id)).ToListAsync(cancellationToken);
        if (templates.Count != ids.Count)
            throw new InvalidOperationException("تعذر إعادة ترتيب القوالب: أحد المعرفات غير موجود.");

        foreach (var item in request.Items)
        {
            var template = templates.First(t => t.Id == item.Id);
            template.SortOrder = item.SortOrder;
            template.UpdatedById = actorUserId;
            template.UpdatedAt = DateTime.UtcNow;
        }

        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAsync(int id, int? replacementDefaultId, int actorUserId, CancellationToken cancellationToken = default)
    {
        var template = await _db.LetterTemplates.FirstOrDefaultAsync(t => t.Id == id, cancellationToken)
            ?? throw new InvalidOperationException("القالب غير موجود.");

        if (template.IsDefault)
        {
            if (!replacementDefaultId.HasValue || replacementDefaultId.Value == id)
                throw new InvalidOperationException("لا يمكن حذف القالب الافتراضي دون تحديد قالب بديل.");

            var replacement = await _db.LetterTemplates.FirstOrDefaultAsync(t => t.Id == replacementDefaultId.Value, cancellationToken)
                ?? throw new InvalidOperationException("قالب البديل غير موجود.");

            if (replacement.TemplateType != template.TemplateType)
                throw new InvalidOperationException("يجب أن يكون قالب البديل من نفس نوع القالب.");

            await ClearDefaultAsync(template.TemplateType, replacement.Id, cancellationToken);
            replacement.IsDefault = true;
            replacement.UpdatedById = actorUserId;
            replacement.UpdatedAt = DateTime.UtcNow;
        }

        _db.LetterTemplates.Remove(template);
        await _db.SaveChangesAsync(cancellationToken);
    }

    public LetterTemplateValidationResult ValidateContent(string content) => new()
    {
        UnknownVariables = FollowUpLetterVariableRegistry.FindUnknownVariables(content),
    };

    public async Task<LetterTemplateDto?> GetDefaultFollowUpTemplateAsync(CancellationToken cancellationToken = default)
    {
        var template = await _db.LetterTemplates.AsNoTracking()
            .Where(t => t.TemplateType == LetterTemplateType.FollowUp && t.IsDefault)
            .OrderBy(t => t.SortOrder)
            .Select(MapExpr)
            .FirstOrDefaultAsync(cancellationToken);

        if (template != null)
            return template;

        template = await _db.LetterTemplates.AsNoTracking()
            .Where(t => t.Code == FollowUpLetterRenderService.FollowUpTemplateCode)
            .Select(MapExpr)
            .FirstOrDefaultAsync(cancellationToken);

        return template;
    }

    public async Task<LetterTemplateDto?> UpdateDefaultFollowUpTemplateAsync(
        string content,
        int actorUserId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(content))
            throw new InvalidOperationException("محتوى القالب مطلوب");

        ValidateTemplateContent(content);

        var template = await _db.LetterTemplates
            .FirstOrDefaultAsync(
                t => t.TemplateType == LetterTemplateType.FollowUp && t.IsDefault,
                cancellationToken);

        template ??= await _db.LetterTemplates
            .FirstOrDefaultAsync(t => t.Code == FollowUpLetterRenderService.FollowUpTemplateCode, cancellationToken);

        if (template == null)
        {
            template = new LetterTemplate
            {
                Code = FollowUpLetterRenderService.FollowUpTemplateCode,
                Name = "قالب خطاب التعقيب",
                TemplateType = LetterTemplateType.FollowUp,
                IsActive = true,
                IsDefault = true,
                SortOrder = 0,
                CreatedById = actorUserId,
                CreatedAt = DateTime.UtcNow,
            };
            _db.LetterTemplates.Add(template);
        }

        template.Content = content.Trim();
        template.UpdatedById = actorUserId;
        template.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
        return await GetByIdAsync(template.Id, cancellationToken);
    }

    private async Task ClearDefaultAsync(LetterTemplateType templateType, int? exceptId, CancellationToken cancellationToken)
    {
        var defaults = await _db.LetterTemplates
            .Where(t => t.TemplateType == templateType && t.IsDefault && (!exceptId.HasValue || t.Id != exceptId.Value))
            .ToListAsync(cancellationToken);

        foreach (var item in defaults)
            item.IsDefault = false;
    }

    private async Task<string> GenerateUniqueCodeAsync(string name, CancellationToken cancellationToken)
    {
        var baseCode = new string(name
            .Trim()
            .ToLowerInvariant()
            .Select(ch => char.IsLetterOrDigit(ch) ? ch : '_')
            .ToArray())
            .Trim('_');

        if (string.IsNullOrWhiteSpace(baseCode))
            baseCode = "template";

        var code = baseCode;
        var suffix = 1;
        while (await _db.LetterTemplates.AnyAsync(t => t.Code == code, cancellationToken))
        {
            code = $"{baseCode}_{suffix++}";
        }

        return code;
    }

    private static string RequireName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new InvalidOperationException("اسم القالب مطلوب");
        return name.Trim();
    }

    private static void ValidateTemplateContent(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
            throw new InvalidOperationException("محتوى القالب مطلوب");
    }

    private static readonly System.Linq.Expressions.Expression<Func<LetterTemplate, LetterTemplateDto>> MapExpr = t => new LetterTemplateDto
    {
        Id = t.Id,
        Code = t.Code,
        Name = t.Name,
        Description = t.Description,
        TemplateType = t.TemplateType,
        Content = t.Content,
        IsActive = t.IsActive,
        IsDefault = t.IsDefault,
        SortOrder = t.SortOrder,
        CreatedAt = t.CreatedAt,
        UpdatedAt = t.UpdatedAt,
    };
}
