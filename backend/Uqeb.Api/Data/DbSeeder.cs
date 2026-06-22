using Microsoft.EntityFrameworkCore;
using Uqeb.Api.Helpers;
using Uqeb.Api.Models.Entities;
using Uqeb.Api.Models.Enums;
using Uqeb.Api.Services;

namespace Uqeb.Api.Data;

public static class DbSeeder
{
    public static async Task SeedAsync(AppDbContext db)
    {
        await db.Database.MigrateAsync();

        if (!await db.LetterTemplates.AnyAsync(t => t.Code == LetterTemplateService.FollowUpTemplateCode))
        {
            db.LetterTemplates.Add(new LetterTemplate
            {
                Code = LetterTemplateService.FollowUpTemplateCode,
                Name = "قالب خطاب التعقيب",
                Content = LetterTemplateService.DefaultFollowUpContent,
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }

        if (!await db.Categories.AnyAsync())
        {
            db.Categories.AddRange(
                CreateCategory("استفسار", "INQ"),
                CreateCategory("تعميم", "CIRC"),
                CreateCategory("طلب", "REQ"),
                CreateCategory("شكوى", "COMP"),
                CreateCategory("متابعة", "FUP"),
                CreateCategory("عام", "GEN")
            );
            await db.SaveChangesAsync();
        }
        else if (!await db.Categories.AnyAsync(c => c.NameNormalized == ReferenceNameNormalizer.NormalizeKey("عام")))
        {
            db.Categories.Add(CreateCategory("عام", "GEN"));
            await db.SaveChangesAsync();
        }

        if (await db.Users.AnyAsync()) return;

        var departments = new[]
        {
            CreateDepartment("الشؤون الإدارية", "ADM"),
            CreateDepartment("الموارد البشرية", "HR"),
            CreateDepartment("الشؤون القانونية", "LEG"),
            CreateDepartment("تقنية المعلومات", "IT"),
            CreateDepartment("المالية", "FIN")
        };
        db.Departments.AddRange(departments);
        await db.SaveChangesAsync();

        var parties = new[]
        {
            CreateParty("وزارة الداخلية", "حكومي"),
            CreateParty("وزارة المالية", "حكومي"),
            CreateParty("شركة الاتصالات", "خاص"),
            CreateParty("البنك الأهلي", "خاص"),
            CreateParty("أمانة المنطقة", "حكومي")
        };
        db.ExternalParties.AddRange(parties);
        await db.SaveChangesAsync();

        var categories = await db.Categories.ToListAsync();

        var admin = new User
        {
            Username = "admin",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("Admin@123"),
            FullName = "مدير النظام",
            Email = "admin@uqeb.local",
            Role = UserRole.Admin,
            IsActive = true
        };
        var supervisor = new User
        {
            Username = "supervisor",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("Super@123"),
            FullName = "مشرف المعاملات",
            Role = UserRole.Supervisor,
            IsActive = true
        };
        var dataEntry = new User
        {
            Username = "dataentry",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("Data@123"),
            FullName = "مدخل بيانات",
            Role = UserRole.DataEntry,
            IsActive = true
        };
        var deptUser = new User
        {
            Username = "deptuser",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("Dept@123"),
            FullName = "موظف إدارة",
            Role = UserRole.DepartmentUser,
            DepartmentId = departments[0].Id,
            IsActive = true
        };
        var reader = new User
        {
            Username = "reader",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("Read@123"),
            FullName = "قارئ",
            Role = UserRole.Reader,
            IsActive = true
        };
        db.Users.AddRange(admin, supervisor, dataEntry, deptUser, reader);
        await db.SaveChangesAsync();

        var year = DateTime.UtcNow.Year;
        var transactions = new[]
        {
            new Transaction
            {
                InternalTrackingNumber = $"UQEB-{year}-00001",
                IncomingNumber = "و/1447/001",
                IncomingDate = DateTime.UtcNow.AddDays(-10),
                Subject = "طلب معلومات عن المشروع",
                IncomingFrom = "وزارة الداخلية",
                IncomingFromPartyId = parties[0].Id,
                RequiresResponse = true,
                ResponseType = ResponseType.External,
                ResponseDueDays = 14,
                ResponseDueDate = DateTime.UtcNow.AddDays(-10).Date.AddDays(14),
                Status = TransactionStatus.InProgress,
                Priority = Priority.Normal,
                CategoryId = categories[0].Id,
                Category = categories[0].Name,
                CreatedById = admin.Id,
                CreatedAt = DateTime.UtcNow.AddDays(-10)
            },
            new Transaction
            {
                InternalTrackingNumber = $"UQEB-{year}-00002",
                IncomingNumber = "و/1447/002",
                IncomingDate = DateTime.UtcNow.AddDays(-5),
                Subject = "تعميم بشأن السياسات الجديدة",
                IncomingFrom = "وزارة المالية",
                IncomingFromPartyId = parties[1].Id,
                RequiresResponse = false,
                Status = TransactionStatus.Assigned,
                Priority = Priority.Urgent,
                CategoryId = categories[1].Id,
                Category = categories[1].Name,
                CreatedById = dataEntry.Id,
                CreatedAt = DateTime.UtcNow.AddDays(-5)
            },
            new Transaction
            {
                InternalTrackingNumber = $"UQEB-{year}-00003",
                IncomingNumber = "و/1447/003",
                IncomingDate = DateTime.UtcNow.AddDays(-20),
                Subject = "متابعة عقد الصيانة",
                IncomingFrom = "شركة الاتصالات",
                IncomingFromPartyId = parties[2].Id,
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
                CreatedById = supervisor.Id,
                CreatedAt = DateTime.UtcNow.AddDays(-20)
            }
        };
        db.Transactions.AddRange(transactions);
        await db.SaveChangesAsync();

        db.TransactionOutgoingDepartments.AddRange(
            new TransactionOutgoingDepartment { TransactionId = transactions[2].Id, DepartmentId = departments[3].Id, CreatedById = supervisor.Id },
            new TransactionOutgoingDepartment { TransactionId = transactions[0].Id, DepartmentId = departments[0].Id, CreatedById = admin.Id },
            new TransactionOutgoingDepartment { TransactionId = transactions[0].Id, DepartmentId = departments[1].Id, CreatedById = admin.Id }
        );

        db.Assignments.AddRange(
            new Assignment
            {
                TransactionId = transactions[1].Id,
                DepartmentId = departments[0].Id,
                AssignedDate = DateTime.UtcNow.AddDays(-4),
                RequiredAction = "مراجعة التعميم وإبداء الرأي",
                RequiresReply = true,
                ReplyDueDays = 5,
                DueDate = DateTime.UtcNow.AddDays(-1),
                ReplyStatus = ReplyStatus.Overdue,
                Status = AssignmentStatus.Active,
                CreatedById = supervisor.Id
            },
            new Assignment
            {
                TransactionId = transactions[0].Id,
                DepartmentId = departments[2].Id,
                AssignedDate = DateTime.UtcNow.AddDays(-8),
                RequiredAction = "إعداد الرد القانوني",
                RequiresReply = true,
                ReplyDueDays = 10,
                DueDate = DateTime.UtcNow.AddDays(3),
                ReplyStatus = ReplyStatus.Pending,
                Status = AssignmentStatus.Active,
                CreatedById = supervisor.Id
            }
        );

        var followUp = new FollowUp
        {
            TransactionId = transactions[0].Id,
            FollowUpNumber = "ت/001",
            FollowUpDate = DateTime.UtcNow.AddDays(-3),
            SentTo = "وزارة الداخلية",
            Notes = "تعقيب أول بخصوص الطلب",
            RequiresReply = true,
            ReplyStatus = ReplyStatus.Pending,
            CreatedById = dataEntry.Id
        };
        db.FollowUps.Add(followUp);
        await db.SaveChangesAsync();

        db.FollowUpDepartments.Add(new FollowUpDepartment
        {
            FollowUpId = followUp.Id,
            DepartmentId = departments[0].Id,
            CreatedById = dataEntry.Id
        });

        await db.SaveChangesAsync();
    }

    private static Category CreateCategory(string name, string code)
    {
        var formatted = ReferenceNameNormalizer.FormatDisplayName(name);
        return new Category
        {
            Name = formatted,
            NameNormalized = ReferenceNameNormalizer.NormalizeKey(formatted),
            Code = code,
            IsActive = true
        };
    }

    private static Department CreateDepartment(string name, string code)
    {
        var formatted = ReferenceNameNormalizer.FormatDisplayName(name);
        return new Department
        {
            Name = formatted,
            NameNormalized = ReferenceNameNormalizer.NormalizeKey(formatted),
            Code = code,
            IsActive = true
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
            IsActive = true
        };
    }
}
