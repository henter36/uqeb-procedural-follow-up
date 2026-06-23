namespace Uqeb.Api.Configuration;

public sealed class FeatureFlagsSettings
{
    public const string SectionName = "FeatureFlags";

    public bool InstitutionalReports { get; set; }
}
