using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Uqeb.Api.Authorization;

[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = true)]
public sealed class RequirePermissionAttribute : Attribute, IAsyncAuthorizationFilter
{
    private readonly PermissionCode _permission;

    public RequirePermissionAttribute(PermissionCode permission) => _permission = permission;

    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        var user = context.HttpContext.User;

        if (user.Identity?.IsAuthenticated != true)
        {
            context.Result = new UnauthorizedResult();
            return;
        }

        var userIdValue = user.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!int.TryParse(userIdValue, out var userId))
        {
            context.Result = new UnauthorizedResult();
            return;
        }

        var service = context.HttpContext.RequestServices.GetRequiredService<IUserPermissionService>();
        var allowed = await service.HasPermissionAsync(
            userId,
            _permission,
            context.HttpContext.RequestAborted);

        if (!allowed)
            context.Result = new ForbidResult();
    }
}
