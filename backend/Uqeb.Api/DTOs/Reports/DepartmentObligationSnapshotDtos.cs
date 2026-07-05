namespace Uqeb.Api.DTOs.Reports;

public class DepartmentObligationSnapshotFilterRequest
{
    public DateTime? DateFrom { get; set; }
    public DateTime? DateTo { get; set; }
    public int? DepartmentId { get; set; }
    public int DueSoonWithinDays { get; set; } = 7;
}

public class DepartmentObligationSnapshotRowDto
{
    public int DepartmentId { get; set; }
    public string DepartmentName { get; set; } = string.Empty;

    // Owner vs. responsible/referred attribution (see docs/reporting/department-obligation-snapshot.md).
    public int OwnedCount { get; set; }
    public int ResponsibleCount { get; set; }
    public int ReferredCount { get; set; }

    // Action/response state, each counted as distinct obligations (transactions), never
    // raw Assignment/DepartmentResponse row counts.
    public int OpenActionCount { get; set; }
    public int PendingActionCount { get; set; }
    public int CompletedActionCount { get; set; }
    public int SubmittedResponseCount { get; set; }
    public int ApprovedResponseCount { get; set; }
    public int OverdueCount { get; set; }
    public int DueSoonCount { get; set; }

    public double? AverageDaysOpenAction { get; set; }

    // Data-quality signal: obligations where the Assignment-level reply flag and the
    // DepartmentResponse approval workflow disagree (the two are not automatically
    // synchronized in the current data model).
    public int AttributionMismatchCount { get; set; }

    // "OwnerOnly" | "ResponsibleOrReferredOnly" | "Both" — answers "is this department
    // involved only as owner vs. only as responsible/referred party, or both".
    public string InvolvementCategory { get; set; } = string.Empty;
}

public class DepartmentObligationSnapshotDto
{
    public int TotalDepartmentsInScope { get; set; }
    public int TotalDistinctObligations { get; set; }
    public int MultiDepartmentObligationsCount { get; set; }
    public List<DepartmentObligationSnapshotRowDto> Departments { get; set; } = new();
}
