using Microsoft.EntityFrameworkCore;
using Uqeb.Api.Data;
using Uqeb.Api.Models.Entities;
using Uqeb.Api.Models.Enums;
using Uqeb.Api.Reporting.Operations;
using Xunit;

namespace Uqeb.Api.Tests.Reporting;

public class ReportingPhase1AdminUserResolverTests
{
    [Fact]
    public async Task ResolveAsync_ReturnsSuccess_ForSingleActiveAdmin()
    {
        await using var db = CreateDb();
        db.Users.Add(CreateUser("admin", UserRole.Admin, isActive: true, id: 11));
        await db.SaveChangesAsync();

        var result = await CreateResolver(db).ResolveAsync("admin");

        Assert.Equal(ReportingPhase1AdminUserResolutionStatus.Success, result.Status);
        Assert.Equal(11, result.UserId);
    }

    [Fact]
    public async Task ResolveAsync_IsCaseInsensitive_AndTrimsUsername()
    {
        await using var db = CreateDb();
        db.Users.Add(CreateUser("Admin", UserRole.Admin, isActive: true, id: 5));
        await db.SaveChangesAsync();

        var result = await CreateResolver(db).ResolveAsync("  admin  ");

        Assert.Equal(ReportingPhase1AdminUserResolutionStatus.Success, result.Status);
        Assert.Equal(5, result.UserId);
    }

    [Fact]
    public async Task ResolveAsync_ReturnsNotFound_WhenAdminMissing()
    {
        await using var db = CreateDb();
        var result = await CreateResolver(db).ResolveAsync("admin");

        Assert.Equal(ReportingPhase1AdminUserResolutionStatus.NotFound, result.Status);
    }

    [Fact]
    public async Task ResolveAsync_ReturnsDuplicate_WhenMultipleAdminUsernamesExist()
    {
        await using var db = CreateDb();
        db.Users.AddRange(
            CreateUser("admin", UserRole.Admin, isActive: true, id: 1),
            CreateUser("ADMIN", UserRole.Admin, isActive: true, id: 2));
        await db.SaveChangesAsync();

        var result = await CreateResolver(db).ResolveAsync("admin");

        Assert.Equal(ReportingPhase1AdminUserResolutionStatus.Duplicate, result.Status);
    }

    [Fact]
    public async Task ResolveAsync_ReturnsInactive_WhenAdminDisabled()
    {
        await using var db = CreateDb();
        db.Users.Add(CreateUser("admin", UserRole.Admin, isActive: false, id: 7));
        await db.SaveChangesAsync();

        var result = await CreateResolver(db).ResolveAsync("admin");

        Assert.Equal(ReportingPhase1AdminUserResolutionStatus.Inactive, result.Status);
    }

    [Fact]
    public async Task ResolveAsync_ReturnsNotAdmin_WhenUsernameIsNotAdminRole()
    {
        await using var db = CreateDb();
        db.Users.Add(CreateUser("admin", UserRole.DataEntry, isActive: true, id: 9));
        await db.SaveChangesAsync();

        var result = await CreateResolver(db).ResolveAsync("admin");

        Assert.Equal(ReportingPhase1AdminUserResolutionStatus.NotAdmin, result.Status);
    }

    [Fact]
    public async Task ResolveAsync_UsesEnvironmentSpecificUserId()
    {
        await using var developmentDb = CreateDb("development");
        developmentDb.Users.Add(CreateUser("admin", UserRole.Admin, isActive: true, id: 101));
        await developmentDb.SaveChangesAsync();

        await using var productionDb = CreateDb("production");
        productionDb.Users.Add(CreateUser("admin", UserRole.Admin, isActive: true, id: 909));
        await productionDb.SaveChangesAsync();

        var development = await CreateResolver(developmentDb).ResolveAsync("admin");
        var production = await CreateResolver(productionDb).ResolveAsync("admin");

        Assert.Equal(101, development.UserId);
        Assert.Equal(909, production.UserId);
        Assert.NotEqual(development.UserId, production.UserId);
    }

    [Fact]
    public void MaskUserId_ObfuscatesMiddleSegment()
    {
        Assert.Equal("1234…5678", ReportingPhase1AdminUserResolver.MaskUserId(12345678));
    }

    private static ReportingPhase1AdminUserResolver CreateResolver(AppDbContext db) => new(db);

    private static AppDbContext CreateDb(string? name = null)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(name ?? Guid.NewGuid().ToString("N"))
            .Options;
        return new AppDbContext(options);
    }

    private static User CreateUser(string username, UserRole role, bool isActive, int id) => new()
    {
        Id = id,
        Username = username,
        PasswordHash = "hash",
        FullName = username,
        Role = role,
        IsActive = isActive,
    };
}
