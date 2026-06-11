using Microsoft.EntityFrameworkCore;
using Uqeb.Api.Data;
using Uqeb.Api.DTOs.Categories;
using Uqeb.Api.Models.Entities;

namespace Uqeb.Api.Services;

public interface ICategoryService
{
    Task<List<CategoryDto>> GetAllAsync(bool activeOnly = true);
    Task<CategoryDto> CreateAsync(CreateCategoryRequest request);
    Task<CategoryDto?> UpdateAsync(int id, UpdateCategoryRequest request);
}

public class CategoryService : ICategoryService
{
    private readonly AppDbContext _db;

    public CategoryService(AppDbContext db) => _db = db;

    public async Task<List<CategoryDto>> GetAllAsync(bool activeOnly = true)
    {
        var query = _db.Categories.AsQueryable();
        if (activeOnly) query = query.Where(c => c.IsActive);
        return await query.OrderBy(c => c.Name).Select(c => new CategoryDto
        {
            Id = c.Id, Name = c.Name, Code = c.Code, IsActive = c.IsActive
        }).ToListAsync();
    }

    public async Task<CategoryDto> CreateAsync(CreateCategoryRequest request)
    {
        var cat = new Category { Name = request.Name, Code = request.Code, IsActive = true };
        _db.Categories.Add(cat);
        await _db.SaveChangesAsync();
        return new CategoryDto { Id = cat.Id, Name = cat.Name, Code = cat.Code, IsActive = true };
    }

    public async Task<CategoryDto?> UpdateAsync(int id, UpdateCategoryRequest request)
    {
        var cat = await _db.Categories.FindAsync(id);
        if (cat == null) return null;
        if (!string.IsNullOrEmpty(request.Name)) cat.Name = request.Name;
        if (request.Code != null) cat.Code = request.Code;
        if (request.IsActive.HasValue) cat.IsActive = request.IsActive.Value;
        await _db.SaveChangesAsync();
        return new CategoryDto { Id = cat.Id, Name = cat.Name, Code = cat.Code, IsActive = cat.IsActive };
    }
}
