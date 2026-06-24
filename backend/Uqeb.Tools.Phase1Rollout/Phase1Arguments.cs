namespace Uqeb.Tools.Phase1Rollout;

internal sealed class Phase1Arguments
{
    public string? SettingsPath { get; set; }
    public string AdminUsername { get; set; } = "admin";
    public bool Verbose { get; set; }
    public bool ShowHelp { get; set; }
}
