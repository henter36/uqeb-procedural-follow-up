namespace Uqeb.Tools.DatabaseProvision;

internal sealed class ProvisionArguments
{
    public bool ShowHelp { get; set; }
    public string? SettingsPath { get; set; }
    public bool ApplyMigrations { get; set; }
    public bool CreateReferenceData { get; set; }
    public bool CreateDefaultUsers { get; set; }
    public bool CreateDemoData { get; set; }
    public string? ExpectedDatabaseName { get; set; }
    public string? BackupPath { get; set; }
    public string? BackupSha256 { get; set; }
    public string? ConfirmationToken { get; set; }
    public bool Verbose { get; set; }
}
