namespace Uqeb.Api.Helpers;

public sealed record FollowUpLetterVariableDefinition(
    string Name,
    string ArabicDescription,
    string Example,
    bool MayBeEmpty);

public static class FollowUpLetterVariableRegistry
{
    public static IReadOnlyList<FollowUpLetterVariableDefinition> All { get; } =
    [
        new("TransactionId", "رقم المعاملة الداخلي", "1234", false),
        new("TransactionNumber", "رقم المعاملة", "1234", false),
        new("IncomingNumber", "رقم الوارد", "456/2025", false),
        new("IncomingDate", "تاريخ الوارد", "2025-06-01", true),
        new("IncomingDateGregorian", "تاريخ الوارد (ميلادي)", "01/06/2025", true),
        new("IncomingDateHijri", "تاريخ الوارد (هجري)", "03/12/1446 هـ", true),
        new("Subject", "موضوع المعاملة", "طلب إفادة", false),
        new("TargetEntity", "الجهة المستهدفة", "إدارة الشؤون", true),
        new("TargetEntities", "الجهات المستهدفة", "إدارة الشؤون، إدارة الموارد", true),
        new("TargetDepartments", "الإدارات المستهدفة", "إدارة الشؤون", true),
        new("AssignmentDate", "تاريخ التكليف", "2025-06-05", true),
        new("DueDate", "تاريخ الاستحقاق", "2025-06-15", true),
        new("DaysOverdue", "أيام التأخير", "5", true),
        new("Priority", "الأولوية", "عادي", true),
        new("Category", "التصنيف", "مراسلات", true),
        new("TodayDate", "تاريخ اليوم", "2025-06-25", false),
        new("TodayDateGregorian", "تاريخ اليوم (ميلادي)", "25/06/2025", false),
        new("TodayDateHijri", "تاريخ اليوم (هجري)", "28/12/1446 هـ", false),
        new("SenderDepartment", "الإدارة المرسلة", "إدارة المتابعة", true),
        new("PreparedBy", "معد الخطاب", "أحمد محمد", true),
        new("FollowUpNumber", "رقم التعقيب", "FU-2025-001", true),
        new("FollowUpDate", "تاريخ التعقيب", "2025-06-25", true),
        new("FollowUpDateGregorian", "تاريخ التعقيب (ميلادي)", "25/06/2025", true),
        new("FollowUpDateHijri", "تاريخ التعقيب (هجري)", "28/12/1446 هـ", true),
        new("FollowUpSequence", "ترتيب التعقيب", "1", false),
        new("FollowUpSequenceText", "نص ترتيب التعقيب", "التعقيب الأول", false),
        new("ResponseDeadlineDays", "مهلة الرد بالأيام", "7", true),
    ];

    private static readonly HashSet<string> KnownNames = All.Select(v => v.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

    public static bool IsKnown(string name) => KnownNames.Contains(name.Trim('{', '}'));

    public static IReadOnlyList<string> FindUnknownVariables(string content)
    {
        if (string.IsNullOrWhiteSpace(content)) return [];

        var matches = System.Text.RegularExpressions.Regex.Matches(content, @"\{([A-Za-z0-9_]+)\}");
        return matches
            .Select(m => m.Groups[1].Value)
            .Where(name => !KnownNames.Contains(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
