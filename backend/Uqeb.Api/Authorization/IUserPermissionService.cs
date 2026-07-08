namespace Uqeb.Api.Authorization;

public interface IUserPermissionService
{
    Task<bool> HasPermissionAsync(int userId, PermissionCode permission, CancellationToken cancellationToken = default);
    Task<IReadOnlySet<PermissionCode>> GetUserPermissionsAsync(int userId, CancellationToken cancellationToken = default);
}
