using Microsoft.EntityFrameworkCore;
using Uqeb.Api.Helpers;
using Uqeb.Api.Models.Entities;
using Uqeb.Api.Models.Enums;

namespace Uqeb.Api.Data.Provisioning;

public static class DemoDataProvisioner
{
    public const string DemoTrackingPrefix = "UQEB-";

    public static async Task<int> ApplyAsync(AppDbContext db, bool enabled, CancellationToken cancellationToken = default)
    {
        if (!enabled)
            return 0;

        if (await db.Transactions.AnyAsync(cancellationToken))
            return 0;

        var changes = 0;

        var departments = await EnsureDepartmentsAsync(db, cancellationToken);
        changes += departments.Added;
        var parties = await EnsurePartiesAsync(db, cancellationToken);
        changes += parties.Added;

        var categories = await db.Categories.ToListAsync(cancellationToken);
        if (categories.Count == 0)
            return changes;

        var users = await db.Users.ToListAsync(cancellationToken);
        var admin = users.FirstOrDefault(u => u.Username == "admin") ?? users.FirstOrDefault();
        var supervisor = users.FirstOrDefault(u => u.Username == "supervisor") ?? admin;
        var dataEntry = users.FirstOrDefault(u => u.Username == "dataentry") ?? admin;

        if (admin is null)
            return changes;

        var year = DateTime.UtcNow.Year;
        var transactions = new[]
        {
            new Transaction
            {
                InternalTrackingNumber = $"{DemoTrackingPrefix}{year}-00001",
                IncomingNumber = "و/1447/001",
                IncomingDate = DateTime.UtcNow.AddDays(-10),
                Subject = "طلب معلومات عن المشروع",
                IncomingFrom = "وزارة الداخلية",
                IncomingFromPartyId = parties.Parties[0].Id,
                RequiresResponse = true,
                ResponseType = ResponseType.External,
                ResponseDueDays = 14,
                ResponseDueDate = DateTime.UtcNow.AddDays(-10).Date.AddDays(14),
                Status = TransactionStatus.InProgress,
                Priority = Priority.Normal,
                CategoryId = categories[0].Id,
                Category = categories[0].Name,
                CreatedById = admin.Id,
                CreatedAt = DateTime.UtcNow.AddDays(-10),
            },
            new Transaction
            {
                InternalTrackingNumber = $"{DemoTrackingPrefix}{year}-00002",
                IncomingNumber = "و/1447/002",
                IncomingDate = DateTime.UtcNow.AddDays(-5),
                Subject = "تعميم بشأن السياسات الجديدة",
                IncomingFrom = "وزارة المالية",
                IncomingFromPartyId = parties.Parties[1].Id,
                RequiresResponse = false,
                Status = TransactionStatus.Assigned,
                Priority = Priority.Urgent,
                CategoryId = categories[1].Id,
                Category = categories[1].Name,
                CreatedById = dataEntry!.Id,
                CreatedAt = DateTime.UtcNow.AddDays(-5),
            },
            new Transaction
            {
                InternalTrackingNumber = $"{DemoTrackingPrefix}{year}-00003",
                IncomingNumber = "و/1447/003",
                IncomingDate = DateTime.UtcNow.AddDays(-20),
                Subject = "متابعة عقد الصيانة",
                IncomingFrom = "شركة الاتصالات",
                IncomingFromPartyId = parties.Parties[2].Id,
                OutgoingNumber = "ص/1447/050",
                OutgoingDate = DateTime.UtcNow.AddDays(-3),
                OutgoingTo = "شركة الاتصالات",
                RequiresResponse = true,
                ResponseType = ResponseType.Both,
                ResponseCompleted = true,
                ResponseCompletedDate = DateTime.UtcNow.AddDays(-1),
                Status = TransactionStatus.ResponseCompleted,
                Priority = Priority.Normal,
                CategoryId = categories[4].Id,
                CreatedById = supervisor!.Id,
                CreatedAt = DateTime.UtcNow.AddDays(-20),
            },
        };

        db.Transactions.AddRange(transactions);
        await db.SaveChangesAsync(cancellationToken);
        changes++;

        db.TransactionOutgoingDepartments.AddRange(
            new TransactionOutgoingDepartment { TransactionId = transactions[2].Id, DepartmentId = departments.Departments[3].Id, CreatedById = supervisor!.Id },
            new TransactionOutgoingDepartment { TransactionId = transactions[0].Id, DepartmentId = departments.Departments[0].Id, CreatedById = admin.Id },
            new TransactionOutgoingDepartment { TransactionId = transactions[0].Id, DepartmentId = departments.Departments[1].Id, CreatedById = admin.Id });

        db.Assignments.AddRange(
            new Assignment
            {
                TransactionId = transactions[1].Id,
                DepartmentId = departments.Departments[0].Id,
                AssignedDate = DateTime.UtcNow.AddDays(-4),
                RequiredAction = "مراجعة التعميم وإبداء الرأي",
                RequiresReply = true,
                ReplyDueDays = 5,
                DueDate = DateTime.UtcNow.AddDays(-1),
                ReplyStatus = ReplyStatus.Overdue,
                Status = AssignmentStatus.Active,
                CreatedById = supervisor!.Id,
            },
            new Assignment
            {
                TransactionId = transactions[0].Id,
                DepartmentId = departments.Departments[2].Id,
                AssignedDate = DateTime.UtcNow.AddDays(-8),
                RequiredAction = "إعداد الرد القانوني",
                RequiresReply = true,
                ReplyDueDays = 10,
                DueDate = DateTime.UtcNow.AddDays(3),
                ReplyStatus = ReplyStatus.Pending,
                Status = AssignmentStatus.Active,
                CreatedById = supervisor!.Id,
            });

        var followUp = new FollowUp
        {
            TransactionId = transactions[0].Id,
            FollowUpNumber = "ت/001",
            FollowUpDate = DateTime.UtcNow.AddDays(-3),
            SentTo = "وزارة الداخلية",
            Notes = "تعقيب أول بخصوص الطلب",
            RequiresReply = true,
            ReplyStatus = ReplyStatus.Pending,
            CreatedById = dataEntry!.Id,
        };
        db.FollowUps.Add(followUp);
        await db.SaveChangesAsync(cancellationToken);

        db.FollowUpDepartments.Add(new FollowUpDepartment
        {
            FollowUpId = followUp.Id,
            DepartmentId = departments.Departments[0].Id,
            CreatedById = dataEntry!.Id,
        });

        await db.SaveChangesAsync(cancellationToken);
        return changes + 1;
    }

    private static async Task<(List<Department> Departments, int Added)> EnsureDepartmentsAsync(
        AppDbContext db,
        CancellationToken cancellationToken)
    {
        var definitions = new (string Name, string Code)[]
        {
            ("الشؤون الإدارية", "ADM"),
            ("الموارد البشرية", "HR"),
            ("الشؤون القانونية", "LEG"),
            ("تقنية المعلومات", "IT"),
            ("المالية", "FIN"),
        };

        var existingCodes = await db.Departments.Select(d => d.Code).ToListAsync(cancellationToken);
        var known = existingCodes.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var added = new List<Department>();
        foreach (var (name, code) in definitions)
        {
            if (known.Contains(code))
                continue;

            var department = CreateDepartment(name, code);
            db.Departments.Add(department);
            added.Add(department);
        }

        if (added.Count > 0)
            await db.SaveChangesAsync(cancellationToken);

        var departments = await db.Departments.OrderBy(d => d.Id).ToListAsync(cancellationToken);
        return (departments, added.Count);
    }

    private static async Task<(List<ExternalParty> Parties, int Added)> EnsurePartiesAsync(
        AppDbContext db,
        CancellationToken cancellationToken)
    {
        if (await db.ExternalParties.AnyAsync(cancellationToken))
        {
            var existing = await db.ExternalParties.OrderBy(p => p.Id).ToListAsync(cancellationToken);
            return (existing, 0);
        }

        var parties = new[]
        {
            CreateParty("وزارة الداخلية", "حكومي"),
            CreateParty("وزارة المالية", "حكومي"),
            CreateParty("شركة الاتصالات", "خاص"),
            CreateParty("البنك الأهلي", "خاص"),
            CreateParty("أمانة المنطقة", "حكومي"),
        };
        db.ExternalParties.AddRange(parties);
        await db.SaveChangesAsync(cancellationToken);
        return (parties.ToList(), parties.Length);
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

    private static ExternalParty CreateParty(string name, string type)
    {
        var formatted = ReferenceNameNormalizer.FormatDisplayName(name);
        return new ExternalParty
        {
            Name = formatted,
            NameNormalized = ReferenceNameNormalizer.NormalizeKey(formatted),
            Type = type,
            IsActive = true,
        };
    }
}
