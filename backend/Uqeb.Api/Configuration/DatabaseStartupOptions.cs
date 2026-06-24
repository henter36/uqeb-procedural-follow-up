namespace Uqeb.Api.Configuration;

public sealed class DatabaseStartupOptions
{
    public const string SectionName = "DatabaseStartup";

    public bool RunMigrationsOnStartup { get; set; }
    public bool RunReferenceSeedOnStartup { get; set; }
    public bool RunDefaultUsersSeedOnStartup { get; set; }
    public bool RunDemoSeedOnStartup { get; set; }

    public void Validate()
    {
        // Boolean flags only; no cross-field constraints required.
    }
}
