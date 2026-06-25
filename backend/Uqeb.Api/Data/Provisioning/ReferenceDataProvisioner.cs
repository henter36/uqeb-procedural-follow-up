using Microsoft.EntityFrameworkCore;
using Uqeb.Api.Helpers;
using Uqeb.Api.Models.Entities;
using Uqeb.Api.Services;

namespace Uqeb.Api.Data.Provisioning;

public static class ReferenceDataProvisioner
{
    public static async Task<int> ApplyAsync(AppDbContext db, bool enabled, CancellationToken cancellationToken = default)
    {
        if (!enabled)
            return 0;

        var changes = 0;

        if (!await db.LetterTemplates.AnyAsync(t => t.Code == LetterTemplateService.FollowUpTemplateCode, cancellationToken))
        {
            db.LetterTemplates.Add(new LetterTemplate
            {
                Code = LetterTemplateService.FollowUpTemplateCode,
                Name = "قالب خطاب التعقيب",
                Content = LetterTemplateService.DefaultFollowUpContent,
                TemplateType = Models.Enums.LetterTemplateType.FollowUp,
                IsActive = true,
                IsDefault = true,
                SortOrder = 0,
                CreatedAt = DateTime.UtcNow,
            });
            changes++;
        }

        if (!await db.Categories.AnyAsync(cancellationToken))
        {
            db.Categories.AddRange(
                CreateCategory("استفسار", "INQ"),
                CreateCategory("تعميم", "CIRC"),
                CreateCategory("طلب", "REQ"),
                CreateCategory("شكوى", "COMP"),
                CreateCategory("متابعة", "FUP"),
                CreateCategory("عام", "GEN"));
            changes++;
        }
        else if (!await db.Categories.AnyAsync(c => c.NameNormalized == ReferenceNameNormalizer.NormalizeKey("عام"), cancellationToken))
        {
            db.Categories.Add(CreateCategory("عام", "GEN"));
            changes++;
        }

        if (changes > 0)
            await db.SaveChangesAsync(cancellationToken);

        return changes;
    }

    private static Category CreateCategory(string name, string code)
    {
        var formatted = ReferenceNameNormalizer.FormatDisplayName(name);
        return new Category
        {
            Name = formatted,
            NameNormalized = ReferenceNameNormalizer.NormalizeKey(formatted),
            Code = code,
            IsActive = true,
        };
    }
}
