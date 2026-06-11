using Microsoft.EntityFrameworkCore;
using Uqeb.Api.Data;
using Uqeb.Api.DTOs.Departments;
using Uqeb.Api.DTOs.ExternalParties;
using Uqeb.Api.DTOs.Users;
using Uqeb.Api.Helpers;
using Uqeb.Api.Models.Entities;

namespace Uqeb.Api.Services;

public interface IUserService
{
    Task<List<UserDto>> GetAllAsync();
    Task<UserDto?> GetByIdAsync(int id);
    Task<UserDto> CreateAsync(CreateUserRequest request);
    Task<UserDto?> UpdateAsync(int id, UpdateUserRequest request);
}

public class UserService : IUserService
{
    private readonly AppDbContext _db;

    public UserService(AppDbContext db) => _db = db;

    public async Task<List<UserDto>> GetAllAsync()
    {
        var users = await _db.Users.Include(u => u.Department).ToListAsync();
        return users.Select(MapUser).ToList();
    }

    public async Task<UserDto?> GetByIdAsync(int id)
    {
        var user = await _db.Users.Include(u => u.Department).FirstOrDefaultAsync(u => u.Id == id);
        return user == null ? null : MapUser(user);
    }

    public async Task<UserDto> CreateAsync(CreateUserRequest request)
    {
        if (await _db.Users.AnyAsync(u => u.Username == request.Username))
            throw new InvalidOperationException("اسم المستخدم موجود مسبقاً");

        var user = new User
        {
            Username = request.Username,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password),
            FullName = request.FullName,
            Email = request.Email,
            Role = EnumHelper.ParseUserRole(request.Role),
            DepartmentId = request.DepartmentId,
            IsActive = true
        };
        _db.Users.Add(user);
        await _db.SaveChangesAsync();
        return (await GetByIdAsync(user.Id))!;
    }

    public async Task<UserDto?> UpdateAsync(int id, UpdateUserRequest request)
    {
        var user = await _db.Users.FindAsync(id);
        if (user == null) return null;

        if (!string.IsNullOrEmpty(request.FullName)) user.FullName = request.FullName;
        if (request.Email != null) user.Email = request.Email;
        if (!string.IsNullOrEmpty(request.Role)) user.Role = EnumHelper.ParseUserRole(request.Role);
        if (request.DepartmentId.HasValue) user.DepartmentId = request.DepartmentId;
        if (request.IsActive.HasValue) user.IsActive = request.IsActive.Value;
        if (!string.IsNullOrEmpty(request.Password))
            user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password);

        await _db.SaveChangesAsync();
        return await GetByIdAsync(id);
    }

    private static UserDto MapUser(User u) => new()
    {
        Id = u.Id,
        Username = u.Username,
        FullName = u.FullName,
        Email = u.Email,
        Role = u.Role.ToString(),
        DepartmentId = u.DepartmentId,
        DepartmentName = u.Department?.Name,
        IsActive = u.IsActive
    };
}

public interface IDepartmentService
{
    Task<List<DepartmentDto>> GetAllAsync(bool activeOnly = true);
    Task<DepartmentDto> CreateAsync(CreateDepartmentRequest request);
    Task<DepartmentDto?> UpdateAsync(int id, UpdateDepartmentRequest request);
}

public class DepartmentService : IDepartmentService
{
    private readonly AppDbContext _db;

    public DepartmentService(AppDbContext db) => _db = db;

    public async Task<List<DepartmentDto>> GetAllAsync(bool activeOnly = true)
    {
        var query = _db.Departments.AsQueryable();
        if (activeOnly) query = query.Where(d => d.IsActive);
        return await query.Select(d => new DepartmentDto
        {
            Id = d.Id, Name = d.Name, Code = d.Code, IsActive = d.IsActive
        }).ToListAsync();
    }

    public async Task<DepartmentDto> CreateAsync(CreateDepartmentRequest request)
    {
        var dept = new Department { Name = request.Name, Code = request.Code, IsActive = true };
        _db.Departments.Add(dept);
        await _db.SaveChangesAsync();
        return new DepartmentDto { Id = dept.Id, Name = dept.Name, Code = dept.Code, IsActive = true };
    }

    public async Task<DepartmentDto?> UpdateAsync(int id, UpdateDepartmentRequest request)
    {
        var dept = await _db.Departments.FindAsync(id);
        if (dept == null) return null;
        if (!string.IsNullOrEmpty(request.Name)) dept.Name = request.Name;
        if (request.Code != null) dept.Code = request.Code;
        if (request.IsActive.HasValue) dept.IsActive = request.IsActive.Value;
        await _db.SaveChangesAsync();
        return new DepartmentDto { Id = dept.Id, Name = dept.Name, Code = dept.Code, IsActive = dept.IsActive };
    }
}

public interface IExternalPartyService
{
    Task<List<ExternalPartyDto>> GetAllAsync(bool activeOnly = true);
    Task<ExternalPartyDto> CreateAsync(CreateExternalPartyRequest request);
    Task<ExternalPartyDto?> UpdateAsync(int id, UpdateExternalPartyRequest request);
}

public class ExternalPartyService : IExternalPartyService
{
    private readonly AppDbContext _db;

    public ExternalPartyService(AppDbContext db) => _db = db;

    public async Task<List<ExternalPartyDto>> GetAllAsync(bool activeOnly = true)
    {
        var query = _db.ExternalParties.AsQueryable();
        if (activeOnly) query = query.Where(p => p.IsActive);
        return await query.Select(p => new ExternalPartyDto
        {
            Id = p.Id, Name = p.Name, Type = p.Type, ContactInfo = p.ContactInfo, IsActive = p.IsActive
        }).ToListAsync();
    }

    public async Task<ExternalPartyDto> CreateAsync(CreateExternalPartyRequest request)
    {
        var party = new ExternalParty { Name = request.Name, Type = request.Type, ContactInfo = request.ContactInfo, IsActive = true };
        _db.ExternalParties.Add(party);
        await _db.SaveChangesAsync();
        return new ExternalPartyDto { Id = party.Id, Name = party.Name, Type = party.Type, ContactInfo = party.ContactInfo, IsActive = true };
    }

    public async Task<ExternalPartyDto?> UpdateAsync(int id, UpdateExternalPartyRequest request)
    {
        var party = await _db.ExternalParties.FindAsync(id);
        if (party == null) return null;
        if (!string.IsNullOrEmpty(request.Name)) party.Name = request.Name;
        if (request.Type != null) party.Type = request.Type;
        if (request.ContactInfo != null) party.ContactInfo = request.ContactInfo;
        if (request.IsActive.HasValue) party.IsActive = request.IsActive.Value;
        await _db.SaveChangesAsync();
        return new ExternalPartyDto { Id = party.Id, Name = party.Name, Type = party.Type, ContactInfo = party.ContactInfo, IsActive = party.IsActive };
    }
}
