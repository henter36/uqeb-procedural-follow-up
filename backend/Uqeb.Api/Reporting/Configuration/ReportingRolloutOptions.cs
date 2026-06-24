namespace Uqeb.Api.Reporting.Configuration;

public sealed class ReportingRolloutOptions
{
    public const string SectionName = "ReportingRollout";

    public ReportingRolloutEnforcementMode EnforcementMode { get; set; } = ReportingRolloutEnforcementMode.ObserveOnly;
    public List<string> EnabledForRoles { get; set; } = [];
    public List<int> EnabledForUserIds { get; set; } = [];
    public List<int> EnabledForDepartments { get; set; } = [];
    public int Percentage { get; set; }
    public bool EmergencyDisable { get; set; }

    public void Validate()
    {
        if (Percentage is < 0 or > 100)
            throw new InvalidOperationException("ReportingRollout:Percentage must be between 0 and 100.");

        if (!Enum.IsDefined(EnforcementMode))
            throw new InvalidOperationException("ReportingRollout:EnforcementMode is invalid.");
    }
}
