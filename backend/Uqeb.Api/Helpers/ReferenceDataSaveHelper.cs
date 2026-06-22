using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Uqeb.Api.Data;

namespace Uqeb.Api.Helpers;

public static class ReferenceDataSaveHelper
{
    public static async Task SaveChangesAsync(AppDbContext db, CancellationToken cancellationToken = default)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex))
        {
            throw new DuplicateReferenceException(ResolveDuplicateMessage(ex));
        }
    }

    private static bool IsUniqueConstraintViolation(DbUpdateException ex)
    {
        for (var current = ex.InnerException; current != null; current = current.InnerException)
        {
            if (current is SqlException sql && sql.Number is 2601 or 2627)
                return true;

            if (current.Message.Contains("duplicate key", StringComparison.OrdinalIgnoreCase) ||
                current.Message.Contains("UNIQUE KEY constraint", StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static string ResolveDuplicateMessage(DbUpdateException ex)
    {
        var message = ex.InnerException?.Message ?? ex.Message;
        if (message.Contains("IX_Users_Email", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("Email", StringComparison.OrdinalIgnoreCase))
            return "البريد الإلكتروني مستخدم مسبقاً";

        if (message.Contains("IX_Departments_NameNormalized", StringComparison.OrdinalIgnoreCase))
            return "توجد إدارة مسجلة مسبقًا بالاسم نفسه.";

        if (message.Contains("IX_ExternalParties_NameNormalized", StringComparison.OrdinalIgnoreCase))
            return "توجد جهة خارجية مسجلة مسبقًا بالاسم نفسه.";

        if (message.Contains("IX_Categories_NameNormalized", StringComparison.OrdinalIgnoreCase))
            return "يوجد تصنيف مسجل مسبقًا بالاسم نفسه.";

        if (message.Contains("Username", StringComparison.OrdinalIgnoreCase))
            return "اسم المستخدم موجود مسبقاً";

        return "القيمة مسجلة مسبقًا.";
    }
}
