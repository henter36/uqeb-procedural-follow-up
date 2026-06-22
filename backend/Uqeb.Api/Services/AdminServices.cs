using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Uqeb.Api.Data;
using Uqeb.Api.DTOs.Common;
using Uqeb.Api.DTOs.Departments;
using Uqeb.Api.DTOs.ExternalParties;
using Uqeb.Api.DTOs.Transactions;
using Uqeb.Api.DTOs.Users;
using Uqeb.Api.Helpers;
using Uqeb.Api.Models.Entities;
using Uqeb.Api.Models.Enums;

namespace Uqeb.Api.Services;

public interface IUserService
{
    Task<List<UserDto>> GetAllAsync();
    Task<PagedResult<UserDto>> SearchAsync(ReferenceDataListRequest request, CancellationToken cancellationToken = default);
    Task<UserDto?> GetByIdAsync(int id);
    Task<UserDto> CreateAsync(CreateUserRequest request, int actorUserId);
    Task<UserDto?> UpdateAsync(int id, UpdateUserRequest request, int actorUserId);
    Task<bool> ResetPasswordAsync(int id, ResetPasswordRequest request, int actorUserId);
}

public class UserService : IUserService
{
    private readonly AppDbContext _db;
    private readonly IAuditService _audit;

    public UserService(AppDbContext db, IAuditService audit)
    {
        _db = db;
        _audit = audit;
    }

    public async Task<List<UserDto>> GetAllAsync()
    {
        var users = await _db.Users.Include(u => u.Department).OrderBy(u => u.FullName).ToListAsync();
        return users.Select(MapUser).ToList();
    }

    public async Task<PagedResult<UserDto>> SearchAsync(ReferenceDataListRequest request, CancellationToken cancellationToken = default)
    {
        var query = _db.Users.Include(u => u.Department).AsQueryable();
        query = ReferenceDataQueryHelper.ApplyStatusFilter(
            query,
            request.Status,
            q => q.Where(u => u.IsActive),
            q => q.Where(u => !u.IsActive));

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var term = request.Search.Trim();
            query = query.Where(u =>
                u.Username.Contains(term) ||
                u.FullName.Contains(term) ||
                (u.Email != null && u.Email.Contains(term)) ||
                (u.Department != null && u.Department.Name.Contains(term)));
        }

        return await ReferenceDataQueryHelper.ToPagedAsync(
            query,
            request,
            ApplyUserSort,
            u => new UserDto
            {
                Id = u.Id,
                Username = u.Username,
                FullName = u.FullName,
                Email = u.Email,
                Role = u.Role.ToString(),
                DepartmentId = u.DepartmentId,
                DepartmentName = u.Department != null ? u.Department.Name : null,
                IsActive = u.IsActive,
                CreatedAt = u.CreatedAt
            },
            cancellationToken);
    }

    public async Task<UserDto?> GetByIdAsync(int id)
    {
        var user = await _db.Users.Include(u => u.Department).FirstOrDefaultAsync(u => u.Id == id);
        return user == null ? null : MapUser(user);
    }

    public async Task<UserDto> CreateAsync(CreateUserRequest request, int actorUserId)
    {
        var username = ReferenceNameNormalizer.FormatDisplayName(request.Username);
        var normalizedUsername = ReferenceNameNormalizer.NormalizeKey(username);
        if (await _db.Users.AnyAsync(u => u.Username.ToLower() == normalizedUsername))
            throw new DuplicateReferenceException("اسم المستخدم موجود مسبقاً");

        if (!string.IsNullOrWhiteSpace(request.Email))
        {
            var email = request.Email.Trim();
            if (await _db.Users.AnyAsync(u => u.Email != null && u.Email.ToLower() == email.ToLower()))
                throw new DuplicateReferenceException("البريد الإلكتروني مستخدم مسبقاً");
        }

        var user = new User
        {
            Username = username,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password),
            FullName = ReferenceNameNormalizer.FormatDisplayName(request.FullName),
            Email = string.IsNullOrWhiteSpace(request.Email) ? null : request.Email.Trim(),
            Role = EnumHelper.ParseUserRole(request.Role),
            DepartmentId = request.DepartmentId,
            IsActive = true
        };
        _db.Users.Add(user);
        await _db.SaveChangesAsync();

        await _audit.LogAsync(actorUserId, AuditAction.Create, "User", user.Id, null, null,
            JsonSerializer.Serialize(new { user.Username, user.FullName, user.Email, Role = user.Role.ToString(), user.DepartmentId, user.IsActive }));

        return (await GetByIdAsync(user.Id))!;
    }

    public async Task<UserDto?> UpdateAsync(int id, UpdateUserRequest request, int actorUserId)
    {
        var user = await _db.Users.FindAsync(id);
        if (user == null) return null;

        var oldSnapshot = SnapshotUser(user);

        await UpdateUsernameAsync(user, id, request.Username);
        ApplyProfileChanges(user, request);
        await UpdateEmailAsync(user, id, request.Email);
        ApplyRoleAndDepartment(user, request);
        await ValidateAdminStatusChangeAsync(user, request.IsActive, request.Role);
        ApplyStatusChange(user, request.IsActive);
        ApplyPasswordChange(user, request.Password);

        await _db.SaveChangesAsync();

        await _audit.LogAsync(actorUserId, AuditAction.Update, "User", user.Id, null,
            JsonSerializer.Serialize(oldSnapshot),
            JsonSerializer.Serialize(SnapshotUser(user)));

        return await GetByIdAsync(id);
    }

    private async Task UpdateUsernameAsync(User user, int id, string? username)
    {
        if (string.IsNullOrWhiteSpace(username) || username == user.Username)
            return;

        var formatted = ReferenceNameNormalizer.FormatDisplayName(username);
        var normalizedUsername = ReferenceNameNormalizer.NormalizeKey(formatted);
        if (await _db.Users.AnyAsync(u => u.Username.ToLower() == normalizedUsername && u.Id != id))
            throw new DuplicateReferenceException("اسم المستخدم موجود مسبقاً");
        user.Username = formatted;
    }

    private static void ApplyProfileChanges(User user, UpdateUserRequest request)
    {
        if (!string.IsNullOrEmpty(request.FullName))
            user.FullName = ReferenceNameNormalizer.FormatDisplayName(request.FullName);
    }

    private async Task UpdateEmailAsync(User user, int id, string? emailValue)
    {
        if (emailValue == null)
            return;

        var email = string.IsNullOrWhiteSpace(emailValue) ? null : emailValue.Trim();
        if (email != null && await _db.Users.AnyAsync(u => u.Email != null && u.Email.ToLower() == email.ToLower() && u.Id != id))
            throw new DuplicateReferenceException("البريد الإلكتروني مستخدم مسبقاً");
        user.Email = email;
    }

    private static void ApplyRoleAndDepartment(User user, UpdateUserRequest request)
    {
        if (!string.IsNullOrEmpty(request.Role))
            user.Role = EnumHelper.ParseUserRole(request.Role);

        if (request.DepartmentId.HasValue)
            user.DepartmentId = request.DepartmentId;
    }

    private static void ApplyStatusChange(User user, bool? isActive)
    {
        if (isActive.HasValue)
            user.IsActive = isActive.Value;
    }

    private static void ApplyPasswordChange(User user, string? password)
    {
        if (!string.IsNullOrEmpty(password))
            user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(password);
    }

    private async Task ValidateAdminStatusChangeAsync(User user, bool? newIsActive, string? newRole)
    {
        if (!newIsActive.HasValue && string.IsNullOrEmpty(newRole))
            return;

        await EnsureCanChangeAdminStatusAsync(user, newIsActive, newRole);
    }

    public async Task<bool> ResetPasswordAsync(int id, ResetPasswordRequest request, int actorUserId)
    {
        var user = await _db.Users.FindAsync(id);
        if (user == null) return false;

        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.NewPassword);
        await _db.SaveChangesAsync();

        await _audit.LogAsync(actorUserId, AuditAction.ResetPassword, "User", user.Id, null,
            null, JsonSerializer.Serialize(new { userId = user.Id, username = user.Username }));

        return true;
    }

    private static object SnapshotUser(User u) => new
    {
        u.Username,
        u.FullName,
        u.Email,
        Role = u.Role.ToString(),
        u.DepartmentId,
        u.IsActive
    };

    private async Task EnsureCanChangeAdminStatusAsync(User user, bool? newIsActive, string? newRole)
    {
        if (user.Role != UserRole.Admin || !user.IsActive)
            return;

        var becomingInactive = newIsActive == false;
        var becomingNonAdmin = !string.IsNullOrEmpty(newRole) && EnumHelper.ParseUserRole(newRole) != UserRole.Admin;
        if (!becomingInactive && !becomingNonAdmin)
            return;

        var activeAdmins = await _db.Users.CountAsync(u => u.IsActive && u.Role == UserRole.Admin);
        if (activeAdmins <= 1)
            throw new LastActiveAdminException();
    }

    private static IQueryable<User> ApplyUserSort(IQueryable<User> query, string sortBy, bool sortDesc)
    {
        return (sortBy.ToLowerInvariant()) switch
        {
            "username" => sortDesc ? query.OrderByDescending(u => u.Username) : query.OrderBy(u => u.Username),
            "department" => sortDesc ? query.OrderByDescending(u => u.Department!.Name) : query.OrderBy(u => u.Department!.Name),
            "status" or "isactive" => sortDesc ? query.OrderByDescending(u => u.IsActive) : query.OrderBy(u => u.IsActive),
            "createdat" => sortDesc ? query.OrderByDescending(u => u.CreatedAt) : query.OrderBy(u => u.CreatedAt),
            _ => sortDesc ? query.OrderByDescending(u => u.FullName) : query.OrderBy(u => u.FullName)
        };
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
        IsActive = u.IsActive,
        CreatedAt = u.CreatedAt
    };
}

public interface IDepartmentService
{
    Task<List<DepartmentDto>> GetAllAsync(bool activeOnly = true);
    Task<PagedResult<DepartmentDto>> SearchAsync(ReferenceDataListRequest request, CancellationToken cancellationToken = default);
    Task<List<LookupItemDto>> LookupAsync(LookupRequest request, CancellationToken cancellationToken = default);
    Task<DepartmentDto?> GetByIdAsync(int id);
    Task<DepartmentDto> CreateAsync(CreateDepartmentRequest request, int actorUserId);
    Task<DepartmentDto?> UpdateAsync(int id, UpdateDepartmentRequest request, int actorUserId);
}

public class DepartmentService : IDepartmentService
{
    private readonly AppDbContext _db;
    private readonly IAuditService _audit;

    public DepartmentService(AppDbContext db, IAuditService audit)
    {
        _db = db;
        _audit = audit;
    }

    public async Task<List<DepartmentDto>> GetAllAsync(bool activeOnly = true)
    {
        var query = _db.Departments.AsQueryable();
        if (activeOnly) query = query.Where(d => d.IsActive);
        return await query.OrderBy(d => d.Name).Select(MapDepartmentExpr).ToListAsync();
    }

    public async Task<PagedResult<DepartmentDto>> SearchAsync(ReferenceDataListRequest request, CancellationToken cancellationToken = default)
    {
        var query = _db.Departments.AsQueryable();
        query = ReferenceDataQueryHelper.ApplyStatusFilter(
            query,
            request.Status,
            q => q.Where(d => d.IsActive),
            q => q.Where(d => !d.IsActive));

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var term = request.Search.Trim();
            query = query.Where(d => d.Name.Contains(term) || (d.Code != null && d.Code.Contains(term)));
        }

        return await ReferenceDataQueryHelper.ToPagedAsync(
            query,
            request,
            ApplyDepartmentSort,
            MapDepartmentExpr,
            cancellationToken);
    }

    public async Task<List<LookupItemDto>> LookupAsync(LookupRequest request, CancellationToken cancellationToken = default)
    {
        var normalized = ReferenceDataQueryHelper.NormalizeLookupRequest(request);
        var limit = normalized.Limit ?? 50;
        var query = _db.Departments.AsQueryable();
        if (normalized.ActiveOnly != false)
            query = query.Where(d => d.IsActive);

        if (!string.IsNullOrWhiteSpace(normalized.Search))
        {
            var term = normalized.Search.Trim();
            query = query.Where(d => d.Name.Contains(term) || (d.Code != null && d.Code.Contains(term)));
        }

        return await query
            .OrderBy(d => d.Name)
            .Take(limit)
            .Select(d => new LookupItemDto
            {
                Id = d.Id,
                Name = d.Name,
                IsActive = d.IsActive,
                SubLabel = d.Code
            })
            .ToListAsync(cancellationToken);
    }

    public async Task<DepartmentDto?> GetByIdAsync(int id) =>
        await _db.Departments.Where(d => d.Id == id).Select(MapDepartmentExpr).FirstOrDefaultAsync();

    public async Task<DepartmentDto> CreateAsync(CreateDepartmentRequest request, int actorUserId)
    {
        var name = ReferenceNameNormalizer.FormatDisplayName(request.Name);
        var normalized = ReferenceNameNormalizer.NormalizeKey(name);
        if (await _db.Departments.AnyAsync(d => d.NameNormalized == normalized))
            throw new DuplicateReferenceException("توجد إدارة مسجلة مسبقًا بالاسم نفسه.");

        var dept = new Department
        {
            Name = name,
            NameNormalized = normalized,
            Code = string.IsNullOrWhiteSpace(request.Code) ? null : request.Code.Trim(),
            IsActive = true
        };
        _db.Departments.Add(dept);
        await _db.SaveChangesAsync();

        await _audit.LogAsync(actorUserId, AuditAction.Create, "Department", dept.Id, null, null,
            JsonSerializer.Serialize(new { dept.Name, dept.Code, dept.IsActive }));

        return await GetByIdAsync(dept.Id) ?? new DepartmentDto
        {
            Id = dept.Id,
            Name = dept.Name,
            Code = dept.Code,
            IsActive = dept.IsActive,
            CreatedAt = dept.CreatedAt
        };
    }

    public async Task<DepartmentDto?> UpdateAsync(int id, UpdateDepartmentRequest request, int actorUserId)
    {
        var dept = await _db.Departments.FindAsync(id);
        if (dept == null) return null;

        var oldSnapshot = new { dept.Name, dept.Code, dept.IsActive };

        if (!string.IsNullOrEmpty(request.Name))
        {
            var name = ReferenceNameNormalizer.FormatDisplayName(request.Name);
            var normalized = ReferenceNameNormalizer.NormalizeKey(name);
            if (await _db.Departments.AnyAsync(d => d.NameNormalized == normalized && d.Id != id))
                throw new DuplicateReferenceException("توجد إدارة مسجلة مسبقًا بالاسم نفسه.");
            dept.Name = name;
            dept.NameNormalized = normalized;
        }

        if (request.Code != null)
            dept.Code = string.IsNullOrWhiteSpace(request.Code) ? null : request.Code.Trim();

        if (request.IsActive.HasValue)
            dept.IsActive = request.IsActive.Value;

        await _db.SaveChangesAsync();

        var action = request.IsActive.HasValue && request.IsActive != oldSnapshot.IsActive
            ? AuditAction.StatusChange
            : AuditAction.Update;

        await _audit.LogAsync(actorUserId, action, "Department", dept.Id, null,
            JsonSerializer.Serialize(oldSnapshot),
            JsonSerializer.Serialize(new { dept.Name, dept.Code, dept.IsActive }));

        return await GetByIdAsync(id);
    }

    private static readonly System.Linq.Expressions.Expression<Func<Department, DepartmentDto>> MapDepartmentExpr = d => new DepartmentDto
    {
        Id = d.Id,
        Name = d.Name,
        Code = d.Code,
        IsActive = d.IsActive,
        CreatedAt = d.CreatedAt
    };

    private static IQueryable<Department> ApplyDepartmentSort(IQueryable<Department> query, string sortBy, bool sortDesc) =>
        (sortBy.ToLowerInvariant()) switch
        {
            "status" or "isactive" => sortDesc ? query.OrderByDescending(d => d.IsActive) : query.OrderBy(d => d.IsActive),
            "createdat" => sortDesc ? query.OrderByDescending(d => d.CreatedAt) : query.OrderBy(d => d.CreatedAt),
            "code" => sortDesc ? query.OrderByDescending(d => d.Code) : query.OrderBy(d => d.Code),
            _ => sortDesc ? query.OrderByDescending(d => d.Name) : query.OrderBy(d => d.Name)
        };
}

public interface IExternalPartyService
{
    Task<List<ExternalPartyDto>> GetAllAsync(bool activeOnly = true);
    Task<PagedResult<ExternalPartyDto>> SearchAsync(ReferenceDataListRequest request, CancellationToken cancellationToken = default);
    Task<List<LookupItemDto>> LookupAsync(LookupRequest request, CancellationToken cancellationToken = default);
    Task<ExternalPartyDto?> GetByIdAsync(int id);
    Task<ExternalPartyDto> CreateAsync(CreateExternalPartyRequest request, int actorUserId);
    Task<ExternalPartyDto?> UpdateAsync(int id, UpdateExternalPartyRequest request, int actorUserId);
}

public class ExternalPartyService : IExternalPartyService
{
    private readonly AppDbContext _db;
    private readonly IAuditService _audit;

    public ExternalPartyService(AppDbContext db, IAuditService audit)
    {
        _db = db;
        _audit = audit;
    }

    public async Task<List<ExternalPartyDto>> GetAllAsync(bool activeOnly = true)
    {
        var query = _db.ExternalParties.AsQueryable();
        if (activeOnly) query = query.Where(p => p.IsActive);
        return await query.OrderBy(p => p.Name).Select(MapPartyExpr).ToListAsync();
    }

    public async Task<PagedResult<ExternalPartyDto>> SearchAsync(ReferenceDataListRequest request, CancellationToken cancellationToken = default)
    {
        var query = _db.ExternalParties.AsQueryable();
        query = ReferenceDataQueryHelper.ApplyStatusFilter(
            query,
            request.Status,
            q => q.Where(p => p.IsActive),
            q => q.Where(p => !p.IsActive));

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var term = request.Search.Trim();
            query = query.Where(p =>
                p.Name.Contains(term) ||
                (p.Type != null && p.Type.Contains(term)) ||
                (p.ContactInfo != null && p.ContactInfo.Contains(term)));
        }

        return await ReferenceDataQueryHelper.ToPagedAsync(
            query,
            request,
            ApplyPartySort,
            MapPartyExpr,
            cancellationToken);
    }

    public async Task<List<LookupItemDto>> LookupAsync(LookupRequest request, CancellationToken cancellationToken = default)
    {
        var normalized = ReferenceDataQueryHelper.NormalizeLookupRequest(request);
        var limit = normalized.Limit ?? 50;
        var query = _db.ExternalParties.AsQueryable();
        if (normalized.ActiveOnly != false)
            query = query.Where(p => p.IsActive);

        if (!string.IsNullOrWhiteSpace(normalized.Search))
        {
            var term = normalized.Search.Trim();
            query = query.Where(p => p.Name.Contains(term) || (p.Type != null && p.Type.Contains(term)));
        }

        return await query
            .OrderBy(p => p.Name)
            .Take(limit)
            .Select(p => new LookupItemDto
            {
                Id = p.Id,
                Name = p.Name,
                IsActive = p.IsActive,
                SubLabel = p.Type
            })
            .ToListAsync(cancellationToken);
    }

    public async Task<ExternalPartyDto?> GetByIdAsync(int id) =>
        await _db.ExternalParties.Where(p => p.Id == id).Select(MapPartyExpr).FirstOrDefaultAsync();

    public async Task<ExternalPartyDto> CreateAsync(CreateExternalPartyRequest request, int actorUserId)
    {
        var name = ReferenceNameNormalizer.FormatDisplayName(request.Name);
        var normalized = ReferenceNameNormalizer.NormalizeKey(name);
        if (await _db.ExternalParties.AnyAsync(p => p.NameNormalized == normalized))
            throw new DuplicateReferenceException("توجد جهة خارجية مسجلة مسبقًا بالاسم نفسه.");

        var party = new ExternalParty
        {
            Name = name,
            NameNormalized = normalized,
            Type = string.IsNullOrWhiteSpace(request.Type) ? null : request.Type.Trim(),
            ContactInfo = string.IsNullOrWhiteSpace(request.ContactInfo) ? null : request.ContactInfo.Trim(),
            IsActive = true
        };
        _db.ExternalParties.Add(party);
        await _db.SaveChangesAsync();

        await _audit.LogAsync(actorUserId, AuditAction.Create, "ExternalParty", party.Id, null, null,
            JsonSerializer.Serialize(new { party.Name, party.Type, party.ContactInfo, party.IsActive }));

        return (await GetByIdAsync(party.Id))!;
    }

    public async Task<ExternalPartyDto?> UpdateAsync(int id, UpdateExternalPartyRequest request, int actorUserId)
    {
        var party = await _db.ExternalParties.FindAsync(id);
        if (party == null) return null;

        var oldSnapshot = new { party.Name, party.Type, party.ContactInfo, party.IsActive };

        if (!string.IsNullOrEmpty(request.Name))
        {
            var name = ReferenceNameNormalizer.FormatDisplayName(request.Name);
            var normalized = ReferenceNameNormalizer.NormalizeKey(name);
            if (await _db.ExternalParties.AnyAsync(p => p.NameNormalized == normalized && p.Id != id))
                throw new DuplicateReferenceException("توجد جهة خارجية مسجلة مسبقًا بالاسم نفسه.");
            party.Name = name;
            party.NameNormalized = normalized;
        }

        if (request.Type != null)
            party.Type = string.IsNullOrWhiteSpace(request.Type) ? null : request.Type.Trim();
        if (request.ContactInfo != null)
            party.ContactInfo = string.IsNullOrWhiteSpace(request.ContactInfo) ? null : request.ContactInfo.Trim();
        if (request.IsActive.HasValue)
            party.IsActive = request.IsActive.Value;

        await _db.SaveChangesAsync();

        var action = request.IsActive.HasValue && request.IsActive != oldSnapshot.IsActive
            ? AuditAction.StatusChange
            : AuditAction.Update;

        await _audit.LogAsync(actorUserId, action, "ExternalParty", party.Id, null,
            JsonSerializer.Serialize(oldSnapshot),
            JsonSerializer.Serialize(new { party.Name, party.Type, party.ContactInfo, party.IsActive }));

        return await GetByIdAsync(id);
    }

    private static readonly System.Linq.Expressions.Expression<Func<ExternalParty, ExternalPartyDto>> MapPartyExpr = p => new ExternalPartyDto
    {
        Id = p.Id,
        Name = p.Name,
        Type = p.Type,
        ContactInfo = p.ContactInfo,
        IsActive = p.IsActive,
        CreatedAt = p.CreatedAt
    };

    private static IQueryable<ExternalParty> ApplyPartySort(IQueryable<ExternalParty> query, string sortBy, bool sortDesc) =>
        (sortBy.ToLowerInvariant()) switch
        {
            "status" or "isactive" => sortDesc ? query.OrderByDescending(p => p.IsActive) : query.OrderBy(p => p.IsActive),
            "createdat" => sortDesc ? query.OrderByDescending(p => p.CreatedAt) : query.OrderBy(p => p.CreatedAt),
            "type" => sortDesc ? query.OrderByDescending(p => p.Type) : query.OrderBy(p => p.Type),
            _ => sortDesc ? query.OrderByDescending(p => p.Name) : query.OrderBy(p => p.Name)
        };
}
