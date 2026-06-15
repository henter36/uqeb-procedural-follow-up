using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Uqeb.Api.Authorization;
using Uqeb.Api.DTOs.Categories;
using Uqeb.Api.Services;

namespace Uqeb.Api.Controllers;

[ApiController]
[Route("api/categories")]
[Authorize]
public class CategoriesController : ControllerBase
{
    private readonly ICategoryService _categories;
    private readonly IMemoryCache _cache;
    private readonly ICacheInvalidationService _cacheInvalidation;

    public CategoriesController(
        ICategoryService categories,
        IMemoryCache cache,
        ICacheInvalidationService cacheInvalidation)
    {
        _categories = categories;
        _cache = cache;
        _cacheInvalidation = cacheInvalidation;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] bool activeOnly = true)
    {
        var cacheKey = _cacheInvalidation.BuildCategoriesKey(activeOnly);
        if (_cache.TryGetValue(cacheKey, out List<CategoryDto>? cached) && cached != null)
            return Ok(cached);

        var result = await _categories.GetAllAsync(activeOnly);
        _cache.Set(cacheKey, result, _cacheInvalidation.ReferenceDataCacheDuration);
        return Ok(result);
    }

    [HttpPost]
    [Authorize(Policy = Policies.AdminOnly)]
    public async Task<IActionResult> Create([FromBody] CreateCategoryRequest request)
    {
        var result = await _categories.CreateAsync(request);
        _cacheInvalidation.InvalidateReferenceData();
        return Ok(result);
    }

    [HttpPut("{id}")]
    [Authorize(Policy = Policies.AdminOnly)]
    public async Task<IActionResult> Update(int id, [FromBody] UpdateCategoryRequest request)
    {
        var cat = await _categories.UpdateAsync(id, request);
        if (cat == null) return NotFound();
        _cacheInvalidation.InvalidateReferenceData();
        return Ok(cat);
    }
}
