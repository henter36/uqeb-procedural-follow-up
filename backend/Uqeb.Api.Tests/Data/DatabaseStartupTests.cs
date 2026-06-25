using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Uqeb.Api.Configuration;
using Uqeb.Api.Data;
using Uqeb.Api.Data.Provisioning;
using Uqeb.Api.Helpers;
using Uqeb.Api.Models.Entities;
using Uqeb.Api.Models.Enums;
using Xunit;

namespace Uqeb.Api.Tests.Data;

public class DatabaseStartupRunnerTests
{
    [Fact]
    public async Task RunAsync_ProductionWithDefaults_DoesNotSeedReferenceDataOrUsers()
    {
        var db = DatabaseStartupTestHelpers.CreateDbContext();
        await DatabaseStartupRunner.RunAsync(
            db,
            new DatabaseStartupOptions(),
            new TestHostEnvironment(Environments.Production));

        Assert.False(await db.Categories.AnyAsync());
        Assert.False(await db.Users.AnyAsync());
        Assert.False(await db.Transactions.AnyAsync());
    }

    [Fact]
    public async Task RunAsync_DevelopmentWithExplicitFlags_SeedsOnlyRequestedData()
    {
        var db = DatabaseStartupTestHelpers.CreateDbContext();
        await DatabaseStartupRunner.RunAsync(
            db,
            new DatabaseStartupOptions
            {
                RunReferenceSeedOnStartup = true,
                RunDefaultUsersSeedOnStartup = true,
            },
            new TestHostEnvironment(Environments.Development));

        Assert.True(await db.Categories.AnyAsync());
        Assert.True(await db.Users.AnyAsync(u => u.Username == "admin"));
        Assert.False(await db.Transactions.AnyAsync());
    }
}

public class DefaultUsersProvisionerTests
{
    [Fact]
    public async Task ApplyAsync_DoesNotChangeExistingPasswordOrRole()
    {
        var db = DatabaseStartupTestHelpers.CreateDbContext();
        var originalHash = BCrypt.Net.BCrypt.HashPassword("Existing@123");
        db.Users.Add(new User
        {
            Username = "admin",
            PasswordHash = originalHash,
            FullName = "Existing Admin",
            Role = UserRole.Reader,
            IsActive = false,
        });
        await db.SaveChangesAsync();

        await DefaultUsersProvisioner.ApplyAsync(db, enabled: true);

        var admin = await db.Users.SingleAsync(u => u.Username == "admin");
        Assert.Equal(originalHash, admin.PasswordHash);
        Assert.Equal(UserRole.Reader, admin.Role);
        Assert.False(admin.IsActive);
    }

    [Fact]
    public async Task ApplyAsync_SecondRunCausesNoChanges()
    {
        var db = DatabaseStartupTestHelpers.CreateDbContext();
        var first = await DefaultUsersProvisioner.ApplyAsync(db, enabled: true);
        var second = await DefaultUsersProvisioner.ApplyAsync(db, enabled: true);

        Assert.True(first > 0);
        Assert.Equal(0, second);
        Assert.Equal(DefaultUsersProvisioner.DefaultUsers.Count, await db.Users.CountAsync());
    }

    [Fact]
    public async Task ApplyAsync_WhenDisabled_DoesNotChangeDatabase()
    {
        var db = DatabaseStartupTestHelpers.CreateDbContext();
        var changes = await DefaultUsersProvisioner.ApplyAsync(db, enabled: false);

        Assert.Equal(0, changes);
        Assert.False(await db.Users.AnyAsync());
        Assert.False(await db.Departments.AnyAsync());
    }

    [Fact]
    public async Task ApplyAsync_UnrelatedDepartmentWithNullCode_StillCreatesDefaultUsersAndLinksDeptUser()
    {
        var db = DatabaseStartupTestHelpers.CreateDbContext();
        db.Departments.Add(CreateTestDepartment("قسم آخر", null));
        await db.SaveChangesAsync();

        var added = await DefaultUsersProvisioner.ApplyAsync(db, enabled: true);

        Assert.True(added > 0);
        var admDepartment = await db.Departments.SingleAsync(d => d.Code == "ADM");
        var deptUser = await db.Users.SingleAsync(u => u.Username == "deptuser");
        Assert.Equal(admDepartment.Id, deptUser.DepartmentId);
        Assert.Equal(DefaultUsersProvisioner.DefaultUsers.Count, await db.Users.CountAsync());
    }

    [Fact]
    public async Task ApplyAsync_UnrelatedDepartmentWithWhitespaceCode_DoesNotPolluteCodeMap()
    {
        var db = DatabaseStartupTestHelpers.CreateDbContext();
        db.Departments.Add(CreateTestDepartment("قسم آخر", "   "));
        await db.SaveChangesAsync();

        var added = await DefaultUsersProvisioner.ApplyAsync(db, enabled: true);

        Assert.True(added > 0);
        var admDepartment = await db.Departments.SingleAsync(d => d.Code == "ADM");
        var deptUser = await db.Users.SingleAsync(u => u.Username == "deptuser");
        Assert.Equal(admDepartment.Id, deptUser.DepartmentId);
        Assert.Equal(2, await db.Departments.CountAsync());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ApplyAsync_ExistingRequiredDepartmentWithMissingCode_UpdatesCodeWithoutDuplicate(string? existingCode)
    {
        var db = DatabaseStartupTestHelpers.CreateDbContext();
        var requiredName = ReferenceNameNormalizer.FormatDisplayName("الشؤون الإدارية");
        db.Departments.Add(new Department
        {
            Name = requiredName,
            NameNormalized = ReferenceNameNormalizer.NormalizeKey(requiredName),
            Code = existingCode,
            IsActive = true,
        });
        await db.SaveChangesAsync();

        var added = await DefaultUsersProvisioner.ApplyAsync(db, enabled: true);

        Assert.True(added > 0);
        Assert.Equal(1, await db.Departments.CountAsync());
        var department = await db.Departments.SingleAsync();
        Assert.Equal("ADM", department.Code);
        var deptUser = await db.Users.SingleAsync(u => u.Username == "deptuser");
        Assert.Equal(department.Id, deptUser.DepartmentId);
    }

    [Fact]
    public async Task ApplyAsync_SecondRun_KeepsDeptUserDepartmentStable()
    {
        var db = DatabaseStartupTestHelpers.CreateDbContext();
        _ = await DefaultUsersProvisioner.ApplyAsync(db, enabled: true);

        var departmentIdAfterFirst = await db.Users
            .Where(u => u.Username == "deptuser")
            .Select(u => u.DepartmentId)
            .SingleAsync();

        var second = await DefaultUsersProvisioner.ApplyAsync(db, enabled: true);

        Assert.Equal(0, second);
        Assert.Equal(DefaultUsersProvisioner.DefaultUsers.Count, await db.Users.CountAsync());
        Assert.Equal(1, await db.Departments.CountAsync(d => d.Code == "ADM"));
        var departmentIdAfterSecond = await db.Users
            .Where(u => u.Username == "deptuser")
            .Select(u => u.DepartmentId)
            .SingleAsync();
        Assert.Equal(departmentIdAfterFirst, departmentIdAfterSecond);
    }

    [Fact]
    public async Task ApplyAsync_DuplicateDepartmentCodes_ThrowsBeforeCreatingUsers()
    {
        var db = DatabaseStartupTestHelpers.CreateDbContext();
        db.Departments.Add(CreateTestDepartment("إدارة أولى", "ADM"));
        db.Departments.Add(CreateTestDepartment("إدارة ثانية", "adm"));
        await db.SaveChangesAsync();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => DefaultUsersProvisioner.ApplyAsync(db, enabled: true));

        Assert.Contains("duplicate department codes", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ADM", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(await db.Users.AnyAsync());
    }

    [Fact]
    public async Task ApplyAsync_RequiredDepartmentNameWithConflictingCode_Throws()
    {
        var db = DatabaseStartupTestHelpers.CreateDbContext();
        var requiredName = ReferenceNameNormalizer.FormatDisplayName("الشؤون الإدارية");
        db.Departments.Add(new Department
        {
            Name = requiredName,
            NameNormalized = ReferenceNameNormalizer.NormalizeKey(requiredName),
            Code = "OPS",
            IsActive = true,
        });
        await db.SaveChangesAsync();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => DefaultUsersProvisioner.ApplyAsync(db, enabled: true));

        Assert.Contains("conflicts with required code 'ADM'", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(await db.Users.AnyAsync());
    }

    private static Department CreateTestDepartment(string name, string? code)
    {
        var formatted = ReferenceNameNormalizer.FormatDisplayName(name);
        return new Department
        {
            Name = formatted,
            NameNormalized = ReferenceNameNormalizer.NormalizeKey(formatted),
            Code = code,
            IsActive = true,
        };
    }
}

public class DemoDataProvisionerTests
{
    [Fact]
    public async Task ApplyAsync_OnlyRunsWhenExplicitlyEnabled()
    {
        var db = DatabaseStartupTestHelpers.CreateDbContext();
        var changes = await DemoDataProvisioner.ApplyAsync(db, enabled: false);
        Assert.Equal(0, changes);
        Assert.False(await db.Transactions.AnyAsync());
    }

    [Fact]
    public async Task ApplyAsync_CreatesDemoTransactionsWhenEnabled()
    {
        var db = DatabaseStartupTestHelpers.CreateDbContext();
        await ReferenceDataProvisioner.ApplyAsync(db, enabled: true);
        await DefaultUsersProvisioner.ApplyAsync(db, enabled: true);

        var changes = await DemoDataProvisioner.ApplyAsync(db, enabled: true);

        Assert.True(changes > 0);
        Assert.True(await db.Transactions.AnyAsync(t => t.InternalTrackingNumber.StartsWith(DemoDataProvisioner.DemoTrackingPrefix)));
    }
}

public class MigrationProvisionerTests
{
    [Fact]
    public async Task ApplyAsync_DoesNothingWhenDisabled()
    {
        var db = DatabaseStartupTestHelpers.CreateDbContext();
        var result = await MigrationProvisioner.ApplyAsync(db, enabled: false);
        Assert.Equal(0, result);
    }
}

internal static class DatabaseStartupTestHelpers
{
    internal static AppDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"database-startup-{Guid.NewGuid():N}")
            .Options;
        return new AppDbContext(options);
    }
}

file sealed class TestHostEnvironment(string environmentName) : IHostEnvironment
{
    public string EnvironmentName { get; set; } = environmentName;
    public string ApplicationName { get; set; } = "Uqeb.Api.Tests";
    public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
    public IFileProvider ContentRootFileProvider { get; set; } = null!;
}
