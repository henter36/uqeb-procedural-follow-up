using Microsoft.EntityFrameworkCore;
using Uqeb.Api.Data;
using Uqeb.Api.Models.Enums;

namespace Uqeb.Api.Authorization;

public sealed class UserPermissionService : IUserPermissionService
{
    private readonly AppDbContext _db;

    public UserPermissionService(AppDbContext db) => _db = db;

    public async Task<bool> HasPermissionAsync(
        int userId,
        PermissionCode permission,
        CancellationToken cancellationToken = default)
    {
        var user = await _db.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == userId, cancellationToken);

        if (user is null)
            return false;

        if (RolePermissionDefaults.GetPermissions(user.Role).Contains(permission))
            return true;

        return await _db.UserPermissions
            .AsNoTracking()
            .AnyAsync(x => x.UserId == userId && x.PermissionCode == permission, cancellationToken);
    }

    public async Task<IReadOnlySet<PermissionCode>> GetUserPermissionsAsync(
        int userId,
        CancellationToken cancellationToken = default)
    {
        var user = await _db.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == userId, cancellationToken);

        if (user is null)
            return new HashSet<PermissionCode>();

        var permissions = RolePermissionDefaults.GetPermissions(user.Role).ToHashSet();

        var customPermissions = await _db.UserPermissions
            .AsNoTracking()
            .Where(x => x.UserId == userId)
            .Select(x => x.PermissionCode)
            .ToListAsync(cancellationToken);

        permissions.UnionWith(customPermissions);
        return permissions;
    }
}
