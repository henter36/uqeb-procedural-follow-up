using Uqeb.Api.Authorization;

namespace Uqeb.Api.Models.Entities;

public class UserPermission
{
    public int Id { get; set; }

    public int UserId { get; set; }
    public User User { get; set; } = null!;

    public PermissionCode PermissionCode { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public int? CreatedById { get; set; }
}
