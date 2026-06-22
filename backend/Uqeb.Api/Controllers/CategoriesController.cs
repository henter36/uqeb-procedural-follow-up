using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Uqeb.Api.Authorization;
using Uqeb.Api.DTOs.Categories;
using Uqeb.Api.DTOs.Common;
using Uqeb.Api.Helpers;
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
    private readonly ICurrentUserService _currentUser;

    public CategoriesController(
        ICategoryService categories,
        IMemoryCache cache,
        ICacheInvalidationService cacheInvalidation,
        ICurrentUserService currentUser)
    {
        _categories = categories;
        _cache = cache;
        _cacheInvalidation = cacheInvalidation;
        _currentUser = currentUser;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll(
        [FromQuery] bool activeOnly = true,
        [FromQuery] ReferenceDataListRequest? list = null,
        CancellationToken cancellationToken = default)
    {
        if (Request.IsPagedReferenceDataRequest())
        {
            var request = ReferenceDataQueryHelper.NormalizeListRequest(list ?? new ReferenceDataListRequest());
            return Ok(await _categories.SearchAsync(request, cancellationToken));
        }

        var cacheKey = _cacheInvalidation.BuildCategoriesKey(activeOnly);
        if (_cache.TryGetValue(cacheKey, out List<CategoryDto>? cached) && cached != null)
            return Ok(cached);

        var result = await _categories.GetAllAsync(activeOnly);
        _cache.Set(cacheKey, result, _cacheInvalidation.ReferenceDataCacheDuration);
        return Ok(result);
    }

    [HttpGet("lookup")]
    public async Task<IActionResult> Lookup([FromQuery] LookupRequest request, CancellationToken cancellationToken) =>
        Ok(await _categories.LookupAsync(request, cancellationToken));

    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(int id)
    {
        var cat = await _categories.GetByIdAsync(id);
        return cat == null ? NotFound() : Ok(cat);
    }

    [HttpPost]
    [Authorize(Policy = Policies.AdminOnly)]
    public async Task<IActionResult> Create([FromBody] CreateCategoryRequest request)
    {
        try
        {
            var result = await _categories.CreateAsync(request, _currentUser.UserId);
            _cacheInvalidation.InvalidateReferenceData();
            return Ok(result);
        }
        catch (DuplicateReferenceException ex)
        {
            return Conflict(new { message = ex.Message });
        }
    }

    [HttpPut("{id}")]
    [Authorize(Policy = Policies.AdminOnly)]
    public async Task<IActionResult> Update(int id, [FromBody] UpdateCategoryRequest request)
    {
        try
        {
            var cat = await _categories.UpdateAsync(id, request, _currentUser.UserId);
            if (cat == null) return NotFound();
            _cacheInvalidation.InvalidateReferenceData();
            return Ok(cat);
        }
        catch (DuplicateReferenceException ex)
        {
            return Conflict(new { message = ex.Message });
        }
    }
}
