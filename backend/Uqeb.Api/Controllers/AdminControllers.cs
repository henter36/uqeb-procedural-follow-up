using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
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

    public DepartmentsController(IDepartmentService departments) => _departments = departments;

    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] bool activeOnly = true) =>
        Ok(await _departments.GetAllAsync(activeOnly));

    [HttpPost]
    [Authorize(Policy = Policies.AdminOnly)]
    public async Task<IActionResult> Create([FromBody] CreateDepartmentRequest request) =>
        Ok(await _departments.CreateAsync(request));

    [HttpPut("{id}")]
    [Authorize(Policy = Policies.AdminOnly)]
    public async Task<IActionResult> Update(int id, [FromBody] UpdateDepartmentRequest request)
    {
        var dept = await _departments.UpdateAsync(id, request);
        return dept == null ? NotFound() : Ok(dept);
    }
}

[ApiController]
[Route("api/external-parties")]
[Authorize]
public class ExternalPartiesController : ControllerBase
{
    private readonly IExternalPartyService _parties;

    public ExternalPartiesController(IExternalPartyService parties) => _parties = parties;

    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] bool activeOnly = true) =>
        Ok(await _parties.GetAllAsync(activeOnly));

    [HttpPost]
    [Authorize(Policy = Policies.AdminOnly)]
    public async Task<IActionResult> Create([FromBody] CreateExternalPartyRequest request) =>
        Ok(await _parties.CreateAsync(request));

    [HttpPut("{id}")]
    [Authorize(Policy = Policies.AdminOnly)]
    public async Task<IActionResult> Update(int id, [FromBody] UpdateExternalPartyRequest request)
    {
        var party = await _parties.UpdateAsync(id, request);
        return party == null ? NotFound() : Ok(party);
    }
}
