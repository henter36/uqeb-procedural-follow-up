using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Uqeb.Api.Authorization;
using Uqeb.Api.Data;
using Uqeb.Api.Models.Entities;
using Uqeb.Api.Models.Enums;
using Uqeb.Api.Services;

namespace Uqeb.Api.Controllers;

[ApiController]
[Route("api/users/{userId:int}/permissions")]
[Authorize]
[RequirePermission(PermissionCode.UserPermissionsManage)]
public sealed class UserPermissionsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IAuditService _audit;

    public UserPermissionsController(AppDbContext db, IAuditService audit)
    {
        _db = db;
        _audit = audit;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<string>>> Get(int userId, CancellationToken cancellationToken)
    {
        var exists = await _db.Users.AnyAsync(x => x.Id == userId, cancellationToken);
        if (!exists)
            return NotFound();

        var permissions = await _db.UserPermissions
            .AsNoTracking()
            .Where(x => x.UserId == userId)
            .Select(x => x.PermissionCode.ToString())
            .OrderBy(x => x)
            .ToListAsync(cancellationToken);

        return permissions;
    }

    [HttpPut]
    public async Task<IActionResult> Replace(
        int userId,
        [FromBody] ReplaceUserPermissionsRequest request,
        CancellationToken cancellationToken)
    {
        var exists = await _db.Users.AnyAsync(x => x.Id == userId, cancellationToken);
        if (!exists)
            return NotFound();

        var requestedPermissions = new HashSet<PermissionCode>();
        foreach (var value in request.Permissions ?? [])
        {
            if (!Enum.TryParse<PermissionCode>(value, ignoreCase: true, out var permission))
                return BadRequest(new { error = $"Invalid permission: {value}" });

            requestedPermissions.Add(permission);
        }

        var current = await _db.UserPermissions
            .Where(x => x.UserId == userId)
            .ToListAsync(cancellationToken);
        var oldPermissions = current.Select(x => x.PermissionCode.ToString()).OrderBy(x => x).ToList();
        var newPermissions = requestedPermissions.Select(x => x.ToString()).OrderBy(x => x).ToList();

        _db.UserPermissions.RemoveRange(current);
        foreach (var permission in requestedPermissions)
        {
            _db.UserPermissions.Add(new UserPermission
            {
                UserId = userId,
                PermissionCode = permission,
                CreatedAt = DateTime.UtcNow,
                CreatedById = GetCurrentUserId()
            });
        }

        if (GetCurrentUserId() is int actorUserId)
        {
            _audit.TrackLog(
                actorUserId,
                AuditAction.UpdateUserPermissions,
                "UserPermission",
                userId,
                null,
                JsonSerializer.Serialize(oldPermissions),
                JsonSerializer.Serialize(newPermissions));
        }

        await _db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    private int? GetCurrentUserId()
    {
        var raw = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return int.TryParse(raw, out var id) ? id : null;
    }
}

public sealed class ReplaceUserPermissionsRequest
{
    public List<string>? Permissions { get; set; }
}
