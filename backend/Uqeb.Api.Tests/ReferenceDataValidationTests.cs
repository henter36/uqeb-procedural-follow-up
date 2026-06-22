using Microsoft.EntityFrameworkCore;
using Uqeb.Api.Data;
using Uqeb.Api.DTOs.Categories;
using Uqeb.Api.DTOs.Departments;
using Uqeb.Api.DTOs.ExternalParties;
using Uqeb.Api.DTOs.Users;
using Uqeb.Api.Helpers;
using Uqeb.Api.Models.Entities;
using Uqeb.Api.Models.Enums;
using Uqeb.Api.Services;
using Xunit;

namespace Uqeb.Api.Tests;

public class ReferenceDataValidationTests
{
    private static AppDbContext CreateDb(string name)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(name)
            .Options;
        return new AppDbContext(options);
    }

    public static IEnumerable<object[]> BlankNameInputs() =>
    [
        [""],
        ["   "],
        ["\t\n"],
    ];

    [Theory]
    [MemberData(nameof(BlankNameInputs))]
    public async Task CreateDepartment_RejectsBlankName(string name)
    {
        await using var db = CreateDb(nameof(CreateDepartment_RejectsBlankName) + name);
        var service = new DepartmentService(db, new AuditService(db));

        var ex = await Assert.ThrowsAsync<EmptyReferenceNameException>(() =>
            service.CreateAsync(new CreateDepartmentRequest { Name = name }, 1));

        Assert.Equal(ReferenceNameNormalizer.EmptyNameMessage, ex.Message);
    }

    [Theory]
    [MemberData(nameof(BlankNameInputs))]
    public async Task CreateExternalParty_RejectsBlankName(string name)
    {
        await using var db = CreateDb(nameof(CreateExternalParty_RejectsBlankName) + name);
        var service = new ExternalPartyService(db, new AuditService(db));

        var ex = await Assert.ThrowsAsync<EmptyReferenceNameException>(() =>
            service.CreateAsync(new CreateExternalPartyRequest { Name = name }, 1));

        Assert.Equal(ReferenceNameNormalizer.EmptyNameMessage, ex.Message);
    }

    [Theory]
    [MemberData(nameof(BlankNameInputs))]
    public async Task CreateCategory_RejectsBlankName(string name)
    {
        await using var db = CreateDb(nameof(CreateCategory_RejectsBlankName) + name);
        var service = new CategoryService(db, new AuditService(db));

        var ex = await Assert.ThrowsAsync<EmptyReferenceNameException>(() =>
            service.CreateAsync(new CreateCategoryRequest { Name = name }, 1));

        Assert.Equal(ReferenceNameNormalizer.EmptyNameMessage, ex.Message);
    }

    [Theory]
    [MemberData(nameof(BlankNameInputs))]
    public async Task CreateUser_RejectsBlankUsername(string username)
    {
        await using var db = CreateDb(nameof(CreateUser_RejectsBlankUsername) + username);
        var service = new UserService(db, new AuditService(db));

        var ex = await Assert.ThrowsAsync<EmptyReferenceNameException>(() =>
            service.CreateAsync(new CreateUserRequest
            {
                Username = username,
                Password = "Pass@123",
                FullName = "Tester",
                Role = "DataEntry"
            }, 1));

        Assert.Equal(ReferenceNameNormalizer.EmptyUsernameMessage, ex.Message);
    }

    [Theory]
    [MemberData(nameof(BlankNameInputs))]
    public async Task CreateUser_RejectsBlankFullName(string fullName)
    {
        await using var db = CreateDb(nameof(CreateUser_RejectsBlankFullName) + fullName);
        var service = new UserService(db, new AuditService(db));

        var ex = await Assert.ThrowsAsync<EmptyReferenceNameException>(() =>
            service.CreateAsync(new CreateUserRequest
            {
                Username = "tester",
                Password = "Pass@123",
                FullName = fullName,
                Role = "DataEntry"
            }, 1));

        Assert.Equal(ReferenceNameNormalizer.EmptyNameMessage, ex.Message);
    }

    [Fact]
    public void EmailNormalizer_CanonicalizesMixedCase()
    {
        Assert.Equal("user@example.com", EmailNormalizer.Normalize("User@Example.COM"));
    }

    [Fact]
    public void EmailNormalizer_TreatsWhitespaceAsNull()
    {
        Assert.Null(EmailNormalizer.Normalize("   "));
    }

    [Fact]
    public async Task CreateUser_StoresCanonicalEmail()
    {
        await using var db = CreateDb(nameof(CreateUser_StoresCanonicalEmail));
        var service = new UserService(db, new AuditService(db));

        var created = await service.CreateAsync(new CreateUserRequest
        {
            Username = "mailuser",
            Password = "Pass@123",
            FullName = "Mail User",
            Email = " User@Example.COM ",
            Role = "DataEntry"
        }, 1);

        Assert.Equal("user@example.com", created.Email);
    }

    [Fact]
    public async Task CreateUser_RejectsDuplicateEmailIgnoringCase()
    {
        await using var db = CreateDb(nameof(CreateUser_RejectsDuplicateEmailIgnoringCase));
        db.Users.Add(new User
        {
            Username = "existing",
            PasswordHash = "x",
            FullName = "Existing",
            Email = "user@example.com",
            Role = UserRole.DataEntry,
            IsActive = true
        });
        await db.SaveChangesAsync();

        var service = new UserService(db, new AuditService(db));
        await Assert.ThrowsAsync<DuplicateReferenceException>(() =>
            service.CreateAsync(new CreateUserRequest
            {
                Username = "other",
                Password = "Pass@123",
                FullName = "Other",
                Email = "User@Example.COM",
                Role = "DataEntry"
            }, 1));
    }
}
