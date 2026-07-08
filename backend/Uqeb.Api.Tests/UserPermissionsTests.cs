using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Uqeb.Api.Authorization;
using Uqeb.Api.Controllers;
using Uqeb.Api.Data;
using Uqeb.Api.Models.Entities;
using Uqeb.Api.Models.Enums;
using Uqeb.Api.Services;
using Xunit;

namespace Uqeb.Api.Tests;

public class UserPermissionsTests
{
    [Fact]
    public async Task HasPermissionAsync_Admin_HasAllPermissions()
    {
        await using var db = CreateDb(nameof(HasPermissionAsync_Admin_HasAllPermissions));
        db.Users.Add(CreateUser(1, UserRole.Admin));
        await db.SaveChangesAsync();

        var service = new UserPermissionService(db);

        Assert.True(await service.HasPermissionAsync(1, PermissionCode.UserPermissionsManage));
        Assert.Contains(PermissionCode.ReportsExportPdf, await service.GetUserPermissionsAsync(1));
    }

    [Fact]
    public async Task RequirePermissionAttribute_UserWithoutPermission_Forbids()
    {
        await using var db = CreateDb(nameof(RequirePermissionAttribute_UserWithoutPermission_Forbids));

        var context = CreateAuthorizationContext(db, userId: 2);
        var attribute = new RequirePermissionAttribute(PermissionCode.ReportsExportPdf);

        await attribute.OnAuthorizationAsync(context);

        Assert.IsType<ForbidResult>(context.Result);
    }

    [Fact]
    public async Task RequirePermissionAttribute_UserWithPermission_Allows()
    {
        await using var db = CreateDb(nameof(RequirePermissionAttribute_UserWithPermission_Allows));
        db.Users.Add(CreateUser(2, UserRole.DepartmentUser));
        db.UserPermissions.Add(new UserPermission { UserId = 2, PermissionCode = PermissionCode.ReportsExportPdf });
        await db.SaveChangesAsync();

        var context = CreateAuthorizationContext(db, userId: 2, PermissionCode.ReportsExportPdf);
        var attribute = new RequirePermissionAttribute(PermissionCode.ReportsExportPdf);

        await attribute.OnAuthorizationAsync(context);

        Assert.Null(context.Result);
    }

    [Fact]
    public async Task RequirePermissionAttribute_StaleJwtClaimWithoutDatabasePermission_Forbids()
    {
        await using var db = CreateDb(nameof(RequirePermissionAttribute_StaleJwtClaimWithoutDatabasePermission_Forbids));
        db.Users.Add(CreateUser(2, UserRole.DepartmentUser));
        await db.SaveChangesAsync();

        var context = CreateAuthorizationContext(db, userId: 2, PermissionCode.ReportsExportPdf);
        var attribute = new RequirePermissionAttribute(PermissionCode.ReportsExportPdf);

        await attribute.OnAuthorizationAsync(context);

        Assert.IsType<ForbidResult>(context.Result);
    }

    [Fact]
    public void RolePermissionDefaults_ReaderCannotWriteOrOpenReports()
    {
        var permissions = RolePermissionDefaults.GetPermissions(UserRole.Reader);

        Assert.DoesNotContain(PermissionCode.TransactionsCreate, permissions);
        Assert.DoesNotContain(PermissionCode.TransactionsEdit, permissions);
        Assert.DoesNotContain(PermissionCode.ReportsView, permissions);
        Assert.DoesNotContain(PermissionCode.UserPermissionsManage, permissions);
    }

    [Fact]
    public void RolePermissionDefaults_DataEntryAndSupervisorRetainExpectedAccess()
    {
        var dataEntry = RolePermissionDefaults.GetPermissions(UserRole.DataEntry);
        var supervisor = RolePermissionDefaults.GetPermissions(UserRole.Supervisor);

        Assert.Contains(PermissionCode.TransactionsCreate, dataEntry);
        Assert.Contains(PermissionCode.TransactionsEdit, dataEntry);
        Assert.DoesNotContain(PermissionCode.TransactionsCancel, dataEntry);
        Assert.Contains(PermissionCode.TransactionsCancel, supervisor);
        Assert.DoesNotContain(PermissionCode.ReportsBuild, supervisor);
        Assert.Contains(PermissionCode.ReportsExportPdf, supervisor);
        Assert.DoesNotContain(PermissionCode.ReportsTemplatesManage, supervisor);
    }

    [Fact]
    public async Task Replace_ReplacesPermissionsAndWritesAuditLog()
    {
        await using var db = CreateDb(nameof(Replace_ReplacesPermissionsAndWritesAuditLog));
        db.Users.Add(CreateUser(1, UserRole.Admin));
        db.Users.Add(CreateUser(2, UserRole.Reader));
        db.UserPermissions.Add(new UserPermission { UserId = 2, PermissionCode = PermissionCode.ReportsView });
        await db.SaveChangesAsync();
        var controller = CreateController(db, actorUserId: 1);

        var result = await controller.Replace(2, new ReplaceUserPermissionsRequest
        {
            Permissions = ["ReportsExportPdf", "UsersView"]
        }, CancellationToken.None);

        Assert.IsType<NoContentResult>(result);
        Assert.Equal(
            [PermissionCode.ReportsExportPdf, PermissionCode.UsersView],
            await db.UserPermissions.Where(x => x.UserId == 2).Select(x => x.PermissionCode).OrderBy(x => x).ToListAsync());
        Assert.True(await db.AuditLogs.AnyAsync(x =>
            x.Action == AuditAction.UpdateUserPermissions &&
            x.EntityName == "UserPermission" &&
            x.EntityId == 2));
    }

    [Fact]
    public async Task Replace_InvalidPermission_ReturnsBadRequest()
    {
        await using var db = CreateDb(nameof(Replace_InvalidPermission_ReturnsBadRequest));
        db.Users.Add(CreateUser(1, UserRole.Admin));
        db.Users.Add(CreateUser(2, UserRole.Reader));
        await db.SaveChangesAsync();
        var controller = CreateController(db, actorUserId: 1);

        var result = await controller.Replace(2, new ReplaceUserPermissionsRequest
        {
            Permissions = ["NotAPermission"]
        }, CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task Replace_NumericUndefinedPermission_ReturnsBadRequest()
    {
        await using var db = CreateDb(nameof(Replace_NumericUndefinedPermission_ReturnsBadRequest));
        db.Users.Add(CreateUser(1, UserRole.Admin));
        db.Users.Add(CreateUser(2, UserRole.Reader));
        await db.SaveChangesAsync();
        var controller = CreateController(db, actorUserId: 1);

        var result = await controller.Replace(2, new ReplaceUserPermissionsRequest
        {
            Permissions = ["999999"]
        }, CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task Replace_NonAdminCannotModifyOwnPermissions()
    {
        await using var db = CreateDb(nameof(Replace_NonAdminCannotModifyOwnPermissions));
        db.Users.Add(CreateUser(2, UserRole.Reader));
        db.UserPermissions.Add(new UserPermission { UserId = 2, PermissionCode = PermissionCode.UserPermissionsManage });
        await db.SaveChangesAsync();
        var controller = CreateController(db, actorUserId: 2);

        var result = await controller.Replace(2, new ReplaceUserPermissionsRequest
        {
            Permissions = ["UserPermissionsManage", "SystemSettingsManage"]
        }, CancellationToken.None);

        Assert.IsType<ForbidResult>(result);
    }

    [Fact]
    public async Task DeleteUser_CascadesUserPermissions()
    {
        await using var db = CreateDb(nameof(DeleteUser_CascadesUserPermissions));
        var user = CreateUser(2, UserRole.Reader);
        db.Users.Add(user);
        db.UserPermissions.Add(new UserPermission { User = user, PermissionCode = PermissionCode.ReportsView });
        await db.SaveChangesAsync();

        db.Users.Remove(user);
        await db.SaveChangesAsync();

        Assert.False(await db.UserPermissions.AnyAsync(x => x.UserId == 2));
    }

    private static AppDbContext CreateDb(string name) =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"user-permissions-{name}-{Guid.NewGuid():N}")
            .Options);

    private static User CreateUser(int id, UserRole role) => new()
    {
        Id = id,
        Username = $"user{id}",
        FullName = $"User {id}",
        PasswordHash = "unused",
        Role = role,
        IsActive = true,
    };

    private static AuthorizationFilterContext CreateAuthorizationContext(
        AppDbContext db,
        int userId,
        params PermissionCode[] permissions)
    {
        var services = new ServiceCollection()
            .AddScoped(_ => db)
            .AddScoped<IUserPermissionService, UserPermissionService>()
            .BuildServiceProvider();

        var httpContext = new DefaultHttpContext
        {
            RequestServices = services,
            User = CreatePrincipal(userId, permissions),
        };

        return new AuthorizationFilterContext(
            new ActionContext(httpContext, new RouteData(), new ActionDescriptor()),
            []);
    }

    private static UserPermissionsController CreateController(AppDbContext db, int actorUserId)
    {
        var controller = new UserPermissionsController(db, new AuditService(db));
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = CreatePrincipal(actorUserId),
            },
            ActionDescriptor = new ControllerActionDescriptor(),
        };
        return controller;
    }

    private static ClaimsPrincipal CreatePrincipal(int userId, params PermissionCode[] permissions) =>
        new(new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
                .. permissions.Select(permission => new Claim(PermissionClaims.PermissionClaimType, permission.ToString())),
            ],
            authenticationType: "Test"));
}
