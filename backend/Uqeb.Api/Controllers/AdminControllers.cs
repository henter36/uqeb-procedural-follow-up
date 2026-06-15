using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Uqeb.Api.Authorization;
using Uqeb.Api.DTOs.Departments;
using Uqeb.Api.DTOs.ExternalParties;
using Uqeb.Api.DTOs.Users;
using Uqeb.Api.Services;

namespace Uqeb.Api.Controllers;

[ApiController]
[Route("api/users")]
[Authorize(Policy = Policies.CanManageUsers)]
public class UsersController : ControllerBase
{
    private readonly IUserService _users;

    public UsersController(IUserService users) => _users = users;

    [HttpGet]
    public async Task<IActionResult> GetAll() => Ok(await _users.GetAllAsync());

    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(int id)
    {
        var user = await _users.GetByIdAsync(id);
        return user == null ? NotFound() : Ok(user);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateUserRequest request)
    {
        try
        {
            return Ok(await _users.CreateAsync(request));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(int id, [FromBody] UpdateUserRequest request)
    {
        var user = await _users.UpdateAsync(id, request);
        return user == null ? NotFound() : Ok(user);
    }
}

[ApiController]
[Route("api/departments")]
[Authorize]
public class DepartmentsController : ControllerBase
{
    private readonly IDepartmentService _departments;
    private readonly IMemoryCache _cache;
    private readonly ICacheInvalidationService _cacheInvalidation;

    public DepartmentsController(
        IDepartmentService departments,
        IMemoryCache cache,
        ICacheInvalidationService cacheInvalidation)
    {
        _departments = departments;
        _cache = cache;
        _cacheInvalidation = cacheInvalidation;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] bool activeOnly = true)
    {
        var cacheKey = _cacheInvalidation.BuildDepartmentsKey(activeOnly);
        if (_cache.TryGetValue(cacheKey, out List<DepartmentDto>? cached) && cached != null)
            return Ok(cached);

        var result = await _departments.GetAllAsync(activeOnly);
        _cache.Set(cacheKey, result, _cacheInvalidation.ReferenceDataCacheDuration);
        return Ok(result);
    }

    [HttpPost]
    [Authorize(Policy = Policies.AdminOnly)]
    public async Task<IActionResult> Create([FromBody] CreateDepartmentRequest request)
    {
        var result = await _departments.CreateAsync(request);
        _cacheInvalidation.InvalidateReferenceData();
        return Ok(result);
    }

    [HttpPut("{id}")]
    [Authorize(Policy = Policies.AdminOnly)]
    public async Task<IActionResult> Update(int id, [FromBody] UpdateDepartmentRequest request)
    {
        var dept = await _departments.UpdateAsync(id, request);
        if (dept == null) return NotFound();
        _cacheInvalidation.InvalidateReferenceData();
        return Ok(dept);
    }
}

[ApiController]
[Route("api/external-parties")]
[Authorize]
public class ExternalPartiesController : ControllerBase
{
    private readonly IExternalPartyService _parties;
    private readonly IMemoryCache _cache;
    private readonly ICacheInvalidationService _cacheInvalidation;

    public ExternalPartiesController(
        IExternalPartyService parties,
        IMemoryCache cache,
        ICacheInvalidationService cacheInvalidation)
    {
        _parties = parties;
        _cache = cache;
        _cacheInvalidation = cacheInvalidation;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] bool activeOnly = true)
    {
        var cacheKey = _cacheInvalidation.BuildExternalPartiesKey(activeOnly);
        if (_cache.TryGetValue(cacheKey, out List<ExternalPartyDto>? cached) && cached != null)
            return Ok(cached);

        var result = await _parties.GetAllAsync(activeOnly);
        _cache.Set(cacheKey, result, _cacheInvalidation.ReferenceDataCacheDuration);
        return Ok(result);
    }

    [HttpPost]
    [Authorize(Policy = Policies.AdminOnly)]
    public async Task<IActionResult> Create([FromBody] CreateExternalPartyRequest request)
    {
        var result = await _parties.CreateAsync(request);
        _cacheInvalidation.InvalidateReferenceData();
        return Ok(result);
    }

    [HttpPut("{id}")]
    [Authorize(Policy = Policies.AdminOnly)]
    public async Task<IActionResult> Update(int id, [FromBody] UpdateExternalPartyRequest request)
    {
        var party = await _parties.UpdateAsync(id, request);
        if (party == null) return NotFound();
        _cacheInvalidation.InvalidateReferenceData();
        return Ok(party);
    }
}
