using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Uqeb.Api.Authorization;
using Uqeb.Api.DTOs.Common;
using Uqeb.Api.DTOs.Departments;
using Uqeb.Api.DTOs.ExternalParties;
using Uqeb.Api.DTOs.Users;
using Uqeb.Api.Helpers;
using Uqeb.Api.Services;

namespace Uqeb.Api.Controllers;

[ApiController]
[Route("api/users")]
[Authorize(Policy = Policies.CanManageUsers)]
public class UsersController : ControllerBase
{
    private readonly IUserService _users;
    private readonly ICurrentUserService _currentUser;

    public UsersController(IUserService users, ICurrentUserService currentUser)
    {
        _users = users;
        _currentUser = currentUser;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] ReferenceDataListRequest? list, CancellationToken cancellationToken)
    {
        if (Request.IsPagedReferenceDataRequest())
        {
            var request = ReferenceDataQueryHelper.NormalizeListRequest(list ?? new ReferenceDataListRequest());
            return Ok(await _users.SearchAsync(request, cancellationToken));
        }

        return Ok(await _users.GetAllAsync());
    }

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
            return Ok(await _users.CreateAsync(request, _currentUser.UserId));
        }
        catch (DuplicateReferenceException ex)
        {
            return Conflict(new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(int id, [FromBody] UpdateUserRequest request)
    {
        try
        {
            var user = await _users.UpdateAsync(id, request, _currentUser.UserId);
            if (user == null) return NotFound();
            return Ok(user);
        }
        catch (DuplicateReferenceException ex)
        {
            return Conflict(new { message = ex.Message });
        }
        catch (LastActiveAdminException ex)
        {
            return Conflict(new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost("{id}/reset-password")]
    public async Task<IActionResult> ResetPassword(int id, [FromBody] ResetPasswordRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.NewPassword))
            return BadRequest(new { message = "كلمة المرور الجديدة مطلوبة" });

        var ok = await _users.ResetPasswordAsync(id, request, _currentUser.UserId);
        return ok ? Ok(new { message = "تم إعادة تعيين كلمة المرور" }) : NotFound();
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
    private readonly ICurrentUserService _currentUser;

    public DepartmentsController(
        IDepartmentService departments,
        IMemoryCache cache,
        ICacheInvalidationService cacheInvalidation,
        ICurrentUserService currentUser)
    {
        _departments = departments;
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
            return Ok(await _departments.SearchAsync(request, cancellationToken));
        }

        var cacheKey = _cacheInvalidation.BuildDepartmentsKey(activeOnly);
        if (_cache.TryGetValue(cacheKey, out List<DepartmentDto>? cached) && cached != null)
            return Ok(cached);

        var result = await _departments.GetAllAsync(activeOnly);
        _cache.Set(cacheKey, result, _cacheInvalidation.ReferenceDataCacheDuration);
        return Ok(result);
    }

    [HttpGet("lookup")]
    public async Task<IActionResult> Lookup([FromQuery] LookupRequest request, CancellationToken cancellationToken) =>
        Ok(await _departments.LookupAsync(request, cancellationToken));

    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(int id)
    {
        var dept = await _departments.GetByIdAsync(id);
        return dept == null ? NotFound() : Ok(dept);
    }

    [HttpPost]
    [Authorize(Policy = Policies.AdminOnly)]
    public async Task<IActionResult> Create([FromBody] CreateDepartmentRequest request)
    {
        try
        {
            var result = await _departments.CreateAsync(request, _currentUser.UserId);
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
    public async Task<IActionResult> Update(int id, [FromBody] UpdateDepartmentRequest request)
    {
        try
        {
            var dept = await _departments.UpdateAsync(id, request, _currentUser.UserId);
            if (dept == null) return NotFound();
            _cacheInvalidation.InvalidateReferenceData();
            return Ok(dept);
        }
        catch (DuplicateReferenceException ex)
        {
            return Conflict(new { message = ex.Message });
        }
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
    private readonly ICurrentUserService _currentUser;

    public ExternalPartiesController(
        IExternalPartyService parties,
        IMemoryCache cache,
        ICacheInvalidationService cacheInvalidation,
        ICurrentUserService currentUser)
    {
        _parties = parties;
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
            return Ok(await _parties.SearchAsync(request, cancellationToken));
        }

        var cacheKey = _cacheInvalidation.BuildExternalPartiesKey(activeOnly);
        if (_cache.TryGetValue(cacheKey, out List<ExternalPartyDto>? cached) && cached != null)
            return Ok(cached);

        var result = await _parties.GetAllAsync(activeOnly);
        _cache.Set(cacheKey, result, _cacheInvalidation.ReferenceDataCacheDuration);
        return Ok(result);
    }

    [HttpGet("lookup")]
    public async Task<IActionResult> Lookup([FromQuery] LookupRequest request, CancellationToken cancellationToken) =>
        Ok(await _parties.LookupAsync(request, cancellationToken));

    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(int id)
    {
        var party = await _parties.GetByIdAsync(id);
        return party == null ? NotFound() : Ok(party);
    }

    [HttpPost]
    [Authorize(Policy = Policies.AdminOnly)]
    public async Task<IActionResult> Create([FromBody] CreateExternalPartyRequest request)
    {
        try
        {
            var result = await _parties.CreateAsync(request, _currentUser.UserId);
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
    public async Task<IActionResult> Update(int id, [FromBody] UpdateExternalPartyRequest request)
    {
        try
        {
            var party = await _parties.UpdateAsync(id, request, _currentUser.UserId);
            if (party == null) return NotFound();
            _cacheInvalidation.InvalidateReferenceData();
            return Ok(party);
        }
        catch (DuplicateReferenceException ex)
        {
            return Conflict(new { message = ex.Message });
        }
    }
}
