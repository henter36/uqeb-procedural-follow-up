using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Uqeb.Api.Data;
using Uqeb.Api.DTOs.Categories;
using Uqeb.Api.DTOs.Common;
using Uqeb.Api.DTOs.Transactions;
using Uqeb.Api.Helpers;
using Uqeb.Api.Models.Entities;
using Uqeb.Api.Models.Enums;

namespace Uqeb.Api.Services;

public interface ICategoryService
{
    Task<List<CategoryDto>> GetAllAsync(bool activeOnly = true);
    Task<PagedResult<CategoryDto>> SearchAsync(ReferenceDataListRequest request, CancellationToken cancellationToken = default);
    Task<List<LookupItemDto>> LookupAsync(LookupRequest request, CancellationToken cancellationToken = default);
    Task<CategoryDto?> GetByIdAsync(int id);
    Task<CategoryDto> CreateAsync(CreateCategoryRequest request, int actorUserId);
    Task<CategoryDto?> UpdateAsync(int id, UpdateCategoryRequest request, int actorUserId);
}

public class CategoryService : ICategoryService
{
    private readonly AppDbContext _db;
    private readonly IAuditService _audit;

    public CategoryService(AppDbContext db, IAuditService audit)
    {
        _db = db;
        _audit = audit;
    }

    public async Task<List<CategoryDto>> GetAllAsync(bool activeOnly = true)
    {
        var query = _db.Categories.AsQueryable();
        if (activeOnly) query = query.Where(c => c.IsActive);
        return await query.OrderBy(c => c.Name).Select(MapCategoryExpr).ToListAsync();
    }

    public async Task<PagedResult<CategoryDto>> SearchAsync(ReferenceDataListRequest request, CancellationToken cancellationToken = default)
    {
        var query = _db.Categories.AsQueryable();
        query = ReferenceDataQueryHelper.ApplyStatusFilter(
            query,
            request.Status,
            q => q.Where(c => c.IsActive),
            q => q.Where(c => !c.IsActive));

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var term = request.Search.Trim();
            query = query.Where(c => c.Name.Contains(term) || (c.Code != null && c.Code.Contains(term)));
        }

        return await ReferenceDataQueryHelper.ToPagedAsync(
            query,
            request,
            ApplyCategorySort,
            MapCategoryExpr,
            cancellationToken);
    }

    public async Task<List<LookupItemDto>> LookupAsync(LookupRequest request, CancellationToken cancellationToken = default)
    {
        var normalized = ReferenceDataQueryHelper.NormalizeLookupRequest(request);
        var limit = normalized.Limit ?? 50;
        var query = _db.Categories.AsQueryable();
        if (normalized.ActiveOnly != false)
            query = query.Where(c => c.IsActive);

        if (!string.IsNullOrWhiteSpace(normalized.Search))
        {
            var term = normalized.Search.Trim();
            query = query.Where(c => c.Name.Contains(term) || (c.Code != null && c.Code.Contains(term)));
        }

        return await query
            .OrderBy(c => c.Name)
            .Take(limit)
            .Select(c => new LookupItemDto
            {
                Id = c.Id,
                Name = c.Name,
                IsActive = c.IsActive,
                SubLabel = c.Code
            })
            .ToListAsync(cancellationToken);
    }

    public async Task<CategoryDto?> GetByIdAsync(int id) =>
        await _db.Categories.Where(c => c.Id == id).Select(MapCategoryExpr).FirstOrDefaultAsync();

    public async Task<CategoryDto> CreateAsync(CreateCategoryRequest request, int actorUserId)
    {
        var name = ReferenceNameNormalizer.FormatDisplayName(request.Name);
        var normalized = ReferenceNameNormalizer.NormalizeKey(name);
        if (await _db.Categories.AnyAsync(c => c.NameNormalized == normalized))
            throw new DuplicateReferenceException("يوجد تصنيف مسجل مسبقًا بالاسم نفسه.");

        var cat = new Category
        {
            Name = name,
            NameNormalized = normalized,
            Code = string.IsNullOrWhiteSpace(request.Code) ? null : request.Code.Trim(),
            IsActive = true
        };
        _db.Categories.Add(cat);
        await _db.SaveChangesAsync();

        await _audit.LogAsync(actorUserId, AuditAction.Create, "Category", cat.Id, null, null,
            JsonSerializer.Serialize(new { cat.Name, cat.Code, cat.IsActive }));

        return (await GetByIdAsync(cat.Id))!;
    }

    public async Task<CategoryDto?> UpdateAsync(int id, UpdateCategoryRequest request, int actorUserId)
    {
        var cat = await _db.Categories.FindAsync(id);
        if (cat == null) return null;

        var oldSnapshot = new { cat.Name, cat.Code, cat.IsActive };

        if (!string.IsNullOrEmpty(request.Name))
        {
            var name = ReferenceNameNormalizer.FormatDisplayName(request.Name);
            var normalized = ReferenceNameNormalizer.NormalizeKey(name);
            if (await _db.Categories.AnyAsync(c => c.NameNormalized == normalized && c.Id != id))
                throw new DuplicateReferenceException("يوجد تصنيف مسجل مسبقًا بالاسم نفسه.");
            cat.Name = name;
            cat.NameNormalized = normalized;
        }

        if (request.Code != null)
            cat.Code = string.IsNullOrWhiteSpace(request.Code) ? null : request.Code.Trim();
        if (request.IsActive.HasValue)
            cat.IsActive = request.IsActive.Value;

        await _db.SaveChangesAsync();

        var action = request.IsActive.HasValue && request.IsActive != oldSnapshot.IsActive
            ? AuditAction.StatusChange
            : AuditAction.Update;

        await _audit.LogAsync(actorUserId, action, "Category", cat.Id, null,
            JsonSerializer.Serialize(oldSnapshot),
            JsonSerializer.Serialize(new { cat.Name, cat.Code, cat.IsActive }));

        return await GetByIdAsync(id);
    }

    private static readonly System.Linq.Expressions.Expression<Func<Category, CategoryDto>> MapCategoryExpr = c => new CategoryDto
    {
        Id = c.Id,
        Name = c.Name,
        Code = c.Code,
        IsActive = c.IsActive,
        CreatedAt = c.CreatedAt
    };

    private static IQueryable<Category> ApplyCategorySort(IQueryable<Category> query, string sortBy, bool sortDesc) =>
        (sortBy.ToLowerInvariant()) switch
        {
            "status" or "isactive" => sortDesc ? query.OrderByDescending(c => c.IsActive) : query.OrderBy(c => c.IsActive),
            "createdat" => sortDesc ? query.OrderByDescending(c => c.CreatedAt) : query.OrderBy(c => c.CreatedAt),
            "code" => sortDesc ? query.OrderByDescending(c => c.Code) : query.OrderBy(c => c.Code),
            _ => sortDesc ? query.OrderByDescending(c => c.Name) : query.OrderBy(c => c.Name)
        };
}
