using System.Security.Claims;
using Uqeb.Api.Models.Enums;

namespace Uqeb.Api.Services;

public class CurrentUserService : ICurrentUserService
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CurrentUserService(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    private ClaimsPrincipal? User => _httpContextAccessor.HttpContext?.User;

    public bool IsAuthenticated => User?.Identity?.IsAuthenticated == true;

    public int UserId => int.Parse(User?.FindFirstValue(ClaimTypes.NameIdentifier) ?? "0");

    public string Username => User?.FindFirstValue(ClaimTypes.Name) ?? string.Empty;

    public UserRole Role => Enum.TryParse<UserRole>(User?.FindFirstValue(ClaimTypes.Role), out var role)
        ? role
        : UserRole.Reader;

    public int? DepartmentId
    {
        get
        {
            var value = User?.FindFirstValue("departmentId");
            return string.IsNullOrEmpty(value) ? null : int.Parse(value);
        }
    }
}
