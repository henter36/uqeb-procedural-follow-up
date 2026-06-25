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

    private sealed record DepartmentCodeSnapshot(int Id, string? Code, string NameNormalized);

    public static async Task<int> ApplyAsync(AppDbContext db, bool enabled, CancellationToken cancellationToken = default)
    {
        if (!enabled)
            return 0;

        await EnsureRequiredDepartmentsAsync(db, cancellationToken);

        var existingUsernames = await db.Users
            .Select(u => u.Username)
            .ToListAsync(cancellationToken);
        var existing = existingUsernames.ToHashSet(StringComparer.OrdinalIgnoreCase);

        var departmentByCode = await BuildDepartmentCodeMapAsync(db, cancellationToken);
        ValidateRequiredDepartmentCodes(departmentByCode);

        var added = 0;
        foreach (var definition in DefaultUsers)
        {
            if (existing.Contains(definition.Username))
                continue;

            int? departmentId = null;
            if (!string.IsNullOrWhiteSpace(definition.DepartmentCode))
            {
                departmentId = departmentByCode[definition.DepartmentCode];
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

    private static async Task<Dictionary<string, int>> BuildDepartmentCodeMapAsync(
        AppDbContext db,
        CancellationToken cancellationToken)
    {
        var snapshots = await db.Departments
            .Where(department => department.Code != null)
            .Select(department => new DepartmentCodeSnapshot(
                department.Id,
                department.Code,
                department.NameNormalized))
            .ToListAsync(cancellationToken);

        AssertNoDuplicateDepartmentCodes(snapshots);

        return snapshots
            .Where(department => !string.IsNullOrWhiteSpace(department.Code))
            .ToDictionary(
                department => department.Code!,
                department => department.Id,
                StringComparer.OrdinalIgnoreCase);
    }

    private static void ValidateRequiredDepartmentCodes(IReadOnlyDictionary<string, int> departmentByCode)
    {
        foreach (var definition in DefaultUsers)
        {
            if (string.IsNullOrWhiteSpace(definition.DepartmentCode))
                continue;

            if (!departmentByCode.ContainsKey(definition.DepartmentCode))
            {
                throw new InvalidOperationException(
                    $"Default user provisioning failed: department code '{definition.DepartmentCode}' required by user '{definition.Username}' could not be resolved.");
            }
        }
    }

    private static async Task EnsureRequiredDepartmentsAsync(AppDbContext db, CancellationToken cancellationToken)
    {
        var requiredCodes = DefaultUsers
            .Select(definition => definition.DepartmentCode)
            .Where(code => !string.IsNullOrWhiteSpace(code))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (requiredCodes.Count == 0)
            return;

        var snapshots = await db.Departments
            .Select(department => new DepartmentCodeSnapshot(
                department.Id,
                department.Code,
                department.NameNormalized))
            .ToListAsync(cancellationToken);

        AssertNoDuplicateDepartmentCodes(snapshots);

        var changed = false;
        foreach (var requiredCode in requiredCodes)
        {
            changed |= await EnsureRequiredDepartmentAsync(
                db,
                snapshots,
                requiredCode!,
                cancellationToken);
        }

        if (changed)
            await db.SaveChangesAsync(cancellationToken);
    }

    private static async Task<bool> EnsureRequiredDepartmentAsync(
        AppDbContext db,
        List<DepartmentCodeSnapshot> snapshots,
        string code,
        CancellationToken cancellationToken)
    {
        var displayName = GetRequiredDepartmentDisplayName(code);
        var expectedNameNormalized = ReferenceNameNormalizer.NormalizeKey(
            ReferenceNameNormalizer.FormatDisplayName(displayName));

        var byCode = snapshots.FirstOrDefault(department =>
            !string.IsNullOrWhiteSpace(department.Code) &&
            string.Equals(department.Code, code, StringComparison.OrdinalIgnoreCase));
        if (byCode is not null)
            return false;

        var byName = snapshots.FirstOrDefault(department =>
            department.NameNormalized == expectedNameNormalized);
        if (byName is not null)
            return await UpdateRequiredDepartmentCodeAsync(db, snapshots, byName, code, cancellationToken);

        db.Departments.Add(CreateDepartment(displayName, code));
        snapshots.Add(new DepartmentCodeSnapshot(0, code, expectedNameNormalized));
        AssertNoDuplicateDepartmentCodes(snapshots);
        return true;
    }

    private static async Task<bool> UpdateRequiredDepartmentCodeAsync(
        AppDbContext db,
        List<DepartmentCodeSnapshot> snapshots,
        DepartmentCodeSnapshot byName,
        string code,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(byName.Code))
        {
            throw new InvalidOperationException(
                $"Default user provisioning failed: department id {byName.Id} (name key '{byName.NameNormalized}') has code '{byName.Code}' which conflicts with required code '{code}'.");
        }

        var department = await db.Departments.FindAsync([byName.Id], cancellationToken)
            ?? throw new InvalidOperationException(
                $"Default user provisioning failed: department id {byName.Id} was not found while assigning code '{code}'.");

        department.Code = code;

        var snapshotIndex = snapshots.FindIndex(snapshot => snapshot.Id == byName.Id);
        if (snapshotIndex < 0)
        {
            throw new InvalidOperationException(
                $"Default user provisioning failed: snapshot for department id {byName.Id} was not found.");
        }

        snapshots[snapshotIndex] = byName with { Code = code };
        AssertNoDuplicateDepartmentCodes(snapshots);
        return true;
    }

    private static void AssertNoDuplicateDepartmentCodes(IReadOnlyList<DepartmentCodeSnapshot> departments)
    {
        var duplicateGroups = departments
            .Where(department => !string.IsNullOrWhiteSpace(department.Code))
            .GroupBy(department => department.Code!, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .ToList();

        if (duplicateGroups.Count == 0)
            return;

        var details = string.Join(
            "; ",
            duplicateGroups.Select(group =>
                $"code '{group.Key}' (department ids: {string.Join(", ", group.Select(department => department.Id))})"));

        throw new InvalidOperationException(
            $"Default user provisioning failed: duplicate department codes detected: {details}.");
    }

    private static string GetRequiredDepartmentDisplayName(string code)
    {
        if (string.Equals(code, "ADM", StringComparison.OrdinalIgnoreCase))
            return "الشؤون الإدارية";

        throw new InvalidOperationException(
            $"Default user provisioning failed: unsupported required department code '{code}'.");
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
