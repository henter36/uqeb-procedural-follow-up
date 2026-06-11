using Uqeb.Api.Models.Enums;

namespace Uqeb.Api.Services;

public interface ICurrentUserService
{
    int UserId { get; }
    string Username { get; }
    UserRole Role { get; }
    int? DepartmentId { get; }
    bool IsAuthenticated { get; }
}
