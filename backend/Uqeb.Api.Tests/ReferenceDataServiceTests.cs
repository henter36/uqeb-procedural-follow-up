using Microsoft.EntityFrameworkCore;
using Uqeb.Api.Data;
using Uqeb.Api.DTOs.Common;
using Uqeb.Api.DTOs.Departments;
using Uqeb.Api.DTOs.Users;
using Uqeb.Api.Helpers;
using Uqeb.Api.Models.Entities;
using Uqeb.Api.Models.Enums;
using Uqeb.Api.Services;
using Xunit;

namespace Uqeb.Api.Tests;

public class ReferenceDataServiceTests
{
    private static AppDbContext CreateDb(string name)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(name)
            .Options;
        return new AppDbContext(options);
    }

    [Fact]
    public async Task CreateDepartment_AddsActiveDepartment()
    {
        await using var db = CreateDb(nameof(CreateDepartment_AddsActiveDepartment));
        var service = new DepartmentService(db, new AuditService(db));

        var created = await service.CreateAsync(new CreateDepartmentRequest { Name = "إدارة الاختبار", Code = "TST" }, 1);

        Assert.Equal("إدارة الاختبار", created.Name);
        Assert.True(created.IsActive);
    }

    [Fact]
    public async Task CreateDepartment_RejectsDuplicateName()
    {
        await using var db = CreateDb(nameof(CreateDepartment_RejectsDuplicateName));
        var service = new DepartmentService(db, new AuditService(db));
        await service.CreateAsync(new CreateDepartmentRequest { Name = "المالية" }, 1);

        await Assert.ThrowsAsync<DuplicateReferenceException>(() =>
            service.CreateAsync(new CreateDepartmentRequest { Name = "  المالية " }, 1));
    }

    [Fact]
    public async Task UpdateDepartment_ChangesName()
    {
        await using var db = CreateDb(nameof(UpdateDepartment_ChangesName));
        var service = new DepartmentService(db, new AuditService(db));
        var created = await service.CreateAsync(new CreateDepartmentRequest { Name = "قديم" }, 1);

        var updated = await service.UpdateAsync(created.Id, new UpdateDepartmentRequest { Name = "جديد" }, 1);

        Assert.NotNull(updated);
        Assert.Equal("جديد", updated!.Name);
    }

    [Fact]
    public async Task SearchDepartments_FiltersByStatusAndSearch()
    {
        await using var db = CreateDb(nameof(SearchDepartments_FiltersByStatusAndSearch));
        db.Departments.AddRange(
            new Department { Name = "نشطة", NameNormalized = "نشطة", IsActive = true },
            new Department { Name = "معطلة", NameNormalized = "معطلة", IsActive = false });
        await db.SaveChangesAsync();

        var service = new DepartmentService(db, new AuditService(db));
        var result = await service.SearchAsync(new ReferenceDataListRequest
        {
            Search = "نشطة",
            Status = "active",
            Page = 1,
            PageSize = 10
        });

        Assert.Single(result.Items);
        Assert.Equal("نشطة", result.Items[0].Name);
    }

    [Fact]
    public async Task CreateUser_RejectsDuplicateUsername()
    {
        await using var db = CreateDb(nameof(CreateUser_RejectsDuplicateUsername));
        db.Users.Add(new User
        {
            Username = "tester",
            PasswordHash = "x",
            FullName = "Tester",
            Role = UserRole.DataEntry,
            IsActive = true
        });
        await db.SaveChangesAsync();

        var service = new UserService(db, new AuditService(db));
        await Assert.ThrowsAsync<DuplicateReferenceException>(() =>
            service.CreateAsync(new CreateUserRequest
            {
                Username = "Tester",
                Password = "Pass@123",
                FullName = "Other",
                Role = "DataEntry"
            }, 1));
    }

    [Fact]
    public async Task UpdateUser_PreventsDeactivatingLastAdmin()
    {
        await using var db = CreateDb(nameof(UpdateUser_PreventsDeactivatingLastAdmin));
        db.Users.Add(new User
        {
            Username = "admin",
            PasswordHash = "x",
            FullName = "Admin",
            Role = UserRole.Admin,
            IsActive = true
        });
        await db.SaveChangesAsync();

        var service = new UserService(db, new AuditService(db));
        await Assert.ThrowsAsync<LastActiveAdminException>(() =>
            service.UpdateAsync(1, new UpdateUserRequest { IsActive = false }, 1));
    }

    [Fact]
    public void ReferenceNameNormalizer_IgnoresCaseAndSpaces()
    {
        Assert.Equal(
            ReferenceNameNormalizer.NormalizeKey("  IT  Dept "),
            ReferenceNameNormalizer.NormalizeKey("it dept"));
    }
}
