namespace Uqeb.Api.Helpers;

public static class DepartmentObligationSnapshotLabels
{
    public static string InvolvementCategory(string involvementCategory) => involvementCategory switch
    {
        "OwnerOnly" => "مالكة فقط",
        "ResponsibleOrReferredOnly" => "مسؤولة/محالة فقط",
        "Both" => "مالكة ومسؤولة/محالة",
        _ => involvementCategory
    };
}
