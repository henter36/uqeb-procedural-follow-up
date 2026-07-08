using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Uqeb.Api.Authorization;

[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = true)]
public sealed class RequirePermissionAttribute : Attribute, IAsyncAuthorizationFilter
{
    private readonly PermissionCode _permission;

    public RequirePermissionAttribute(PermissionCode permission) => _permission = permission;

    public Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        var user = context.HttpContext.User;

        if (user.Identity?.IsAuthenticated != true)
        {
            context.Result = new UnauthorizedResult();
            return Task.CompletedTask;
        }

        var allowed = user.HasClaim(PermissionClaims.PermissionClaimType, _permission.ToString());

        if (!allowed)
            context.Result = new ForbidResult();

        return Task.CompletedTask;
    }
}
