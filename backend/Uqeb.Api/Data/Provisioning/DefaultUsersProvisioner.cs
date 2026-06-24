using Microsoft.EntityFrameworkCore;
using Uqeb.Api.Helpers;
using Uqeb.Api.Models.Entities;
using Uqeb.Api.Models.Enums;

namespace Uqeb.Api.Data.Provisioning;

public static class DefaultUsersProvisioner
{
    internal static readonly IReadOnlyList<DefaultUserDefinition> DefaultUsers =
    [
        new("admin", "Admin@123", "مدير النظام", UserRole.Admin, null, "admin@uqeb.local"),
        new("supervisor", "Super@123", "مشرف المعاملات", UserRole.Supervisor, null, null),
        new("dataentry", "Data@123", "مدخل بيانات", UserRole.DataEntry, null, null),
        new("deptuser", "Dept@123", "موظف إدارة", UserRole.DepartmentUser, "ADM", null),
        new("reader", "Read@123", "قارئ", UserRole.Reader, null, null),
    ];

    public static async Task<int> ApplyAsync(AppDbContext db, bool enabled, CancellationToken cancellationToken = default)
    {
        if (!enabled)
            return 0;

        await EnsureRequiredDepartmentsAsync(db, cancellationToken);

        var existingUsernames = await db.Users
            .Select(u => u.Username)
            .ToListAsync(cancellationToken);
        var existing = existingUsernames.ToHashSet(StringComparer.OrdinalIgnoreCase);

        var departments = await db.Departments.ToListAsync(cancellationToken);
        var departmentByCode = departments.ToDictionary(d => d.Code, d => d.Id, StringComparer.OrdinalIgnoreCase);

        var added = 0;
        foreach (var definition in DefaultUsers)
        {
            if (existing.Contains(definition.Username))
                continue;

            int? departmentId = null;
            if (!string.IsNullOrWhiteSpace(definition.DepartmentCode)
                && departmentByCode.TryGetValue(definition.DepartmentCode, out var resolvedDepartmentId))
            {
                departmentId = resolvedDepartmentId;
            }

            db.Users.Add(new User
            {
                Username = definition.Username,
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(definition.PlainPassword),
                FullName = definition.FullName,
                Email = definition.Email,
                Role = definition.Role,
                DepartmentId = departmentId,
                IsActive = true,
            });
            added++;
        }

        if (added > 0)
            await db.SaveChangesAsync(cancellationToken);

        return added;
    }

    private static async Task EnsureRequiredDepartmentsAsync(AppDbContext db, CancellationToken cancellationToken)
    {
        var existingCodes = await db.Departments.Select(d => d.Code).ToListAsync(cancellationToken);
        var known = existingCodes.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var added = false;

        foreach (var definition in DefaultUsers.Where(u => !string.IsNullOrWhiteSpace(u.DepartmentCode)))
        {
            if (known.Contains(definition.DepartmentCode!))
                continue;

            db.Departments.Add(CreateDepartment("الشؤون الإدارية", definition.DepartmentCode!));
            known.Add(definition.DepartmentCode!);
            added = true;
        }

        if (added)
            await db.SaveChangesAsync(cancellationToken);
    }

    private static Department CreateDepartment(string name, string code)
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

    internal sealed record DefaultUserDefinition(
        string Username,
        string PlainPassword,
        string FullName,
        UserRole Role,
        string? DepartmentCode,
        string? Email);
}
