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
        var actorUserId = GetCurrentUserId();
        if (actorUserId is null)
            return Unauthorized();

        var actor = await _db.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == actorUserId, cancellationToken);
        if (actor is null)
            return Unauthorized();

        var targetUser = await _db.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == userId, cancellationToken);
        if (targetUser is null)
            return NotFound();

        if (actor.Id == userId && actor.Role != UserRole.Admin)
            return Forbid();

        var requestedPermissions = new HashSet<PermissionCode>();
        foreach (var value in request.Permissions ?? [])
        {
            if (!Enum.TryParse<PermissionCode>(value, ignoreCase: true, out var permission) ||
                !Enum.IsDefined(permission))
            {
                return BadRequest(new { error = $"Invalid permission: {value}" });
            }

            requestedPermissions.Add(permission);
        }

        var current = await _db.UserPermissions
            .Where(x => x.UserId == userId)
            .ToListAsync(cancellationToken);
        var oldPermissions = current.Select(x => x.PermissionCode.ToString()).OrderBy(x => x).ToList();
        var newPermissions = requestedPermissions.Select(x => x.ToString()).OrderBy(x => x).ToList();

        if (RemovesUserPermissionsManager(targetUser, current, requestedPermissions) &&
            !await AnotherUserPermissionsManagerExistsAsync(userId, cancellationToken))
        {
            return Conflict(new { error = "Cannot remove the last user permissions manager." });
        }

        _db.UserPermissions.RemoveRange(current);
        foreach (var permission in requestedPermissions)
        {
            _db.UserPermissions.Add(new UserPermission
            {
                UserId = userId,
                PermissionCode = permission,
                CreatedAt = DateTime.UtcNow,
                CreatedById = actorUserId
            });
        }

        _audit.TrackLog(
            actorUserId.Value,
            AuditAction.UpdateUserPermissions,
            "UserPermission",
            userId,
            null,
            JsonSerializer.Serialize(oldPermissions),
            JsonSerializer.Serialize(newPermissions));

        await _db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    private static bool RemovesUserPermissionsManager(
        User targetUser,
        IReadOnlyCollection<UserPermission> currentPermissions,
        IReadOnlySet<PermissionCode> requestedPermissions)
    {
        var currentlyCanManage =
            RolePermissionDefaults.GetPermissions(targetUser.Role).Contains(PermissionCode.UserPermissionsManage) ||
            currentPermissions.Any(x => x.PermissionCode == PermissionCode.UserPermissionsManage);
        var willManage =
            RolePermissionDefaults.GetPermissions(targetUser.Role).Contains(PermissionCode.UserPermissionsManage) ||
            requestedPermissions.Contains(PermissionCode.UserPermissionsManage);

        return currentlyCanManage && !willManage;
    }

    private async Task<bool> AnotherUserPermissionsManagerExistsAsync(int targetUserId, CancellationToken cancellationToken)
    {
        var activeAdminExists = await _db.Users
            .AsNoTracking()
            .AnyAsync(user => user.Id != targetUserId && user.IsActive && user.Role == UserRole.Admin, cancellationToken);
        if (activeAdminExists)
            return true;

        return await _db.Users
            .AsNoTracking()
            .AnyAsync(user =>
                user.Id != targetUserId &&
                user.IsActive &&
                _db.UserPermissions.Any(permission =>
                    permission.UserId == user.Id &&
                    permission.PermissionCode == PermissionCode.UserPermissionsManage),
                cancellationToken);
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
