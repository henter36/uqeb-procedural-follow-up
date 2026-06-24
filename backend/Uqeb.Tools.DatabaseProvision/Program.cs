using System.Data;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Uqeb.Api.Data;
using Uqeb.Api.Data.Provisioning;

var parsed = ProvisionArgumentParser.Parse(args);
if (parsed.ShowHelp)
{
    await Console.Error.WriteLineAsync(
        """
        Usage:
          provision-uqeb-database --settings-path <appsettings.json>
            [--apply-migrations]
            [--create-reference-data]
            [--create-default-users]
            [--create-demo-data]
            [--expected-database-name UqebDb]
            [--backup-path <file.bak> [--backup-sha256 <hash>]]
            [--confirmation-token <token>]
        """);
    return 2;
}

if (!parsed.ApplyMigrations
    && !parsed.CreateReferenceData
    && !parsed.CreateDefaultUsers
    && !parsed.CreateDemoData)
{
    await Console.Error.WriteLineAsync("At least one provisioning action must be selected.");
    return 3;
}

var settingsPath = Path.GetFullPath(parsed.SettingsPath!);
if (!File.Exists(settingsPath))
{
    await Console.Error.WriteLineAsync("Settings file was not found.");
    return 4;
}

var configuration = new ConfigurationBuilder()
    .AddJsonFile(settingsPath, optional: false)
    .AddEnvironmentVariables()
    .Build();

var connectionString = configuration.GetConnectionString("DefaultConnection");
if (string.IsNullOrWhiteSpace(connectionString))
{
    await Console.Error.WriteLineAsync("DefaultConnection is missing from settings.");
    return 5;
}

var builder = new SqlConnectionStringBuilder(connectionString);
if (string.IsNullOrWhiteSpace(builder.DataSource))
{
    await Console.Error.WriteLineAsync("Connection string server is missing.");
    return 6;
}

if (string.IsNullOrWhiteSpace(builder.InitialCatalog))
{
    await Console.Error.WriteLineAsync("Connection string database is missing.");
    return 7;
}

if (!string.IsNullOrWhiteSpace(parsed.ExpectedDatabaseName)
    && !string.Equals(builder.InitialCatalog, parsed.ExpectedDatabaseName, StringComparison.OrdinalIgnoreCase))
{
    await Console.Error.WriteLineAsync(
        $"Expected database '{parsed.ExpectedDatabaseName}' but connection targets '{builder.InitialCatalog}'.");
    return 8;
}

if (!string.IsNullOrWhiteSpace(parsed.BackupPath))
{
    if (!File.Exists(parsed.BackupPath))
    {
        await Console.Error.WriteLineAsync("Backup file was not found.");
        return 9;
    }

    if (!string.IsNullOrWhiteSpace(parsed.BackupSha256))
    {
        var actualHash = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(parsed.BackupPath)));
        if (!string.Equals(actualHash, parsed.BackupSha256, StringComparison.OrdinalIgnoreCase))
        {
            await Console.Error.WriteLineAsync("Backup SHA256 mismatch.");
            return 10;
        }
    }

    await VerifyBackupAsync(builder.ConnectionString, parsed.BackupPath);
}

if (!string.IsNullOrWhiteSpace(parsed.ConfirmationToken)
    && !string.Equals(parsed.ConfirmationToken, builder.InitialCatalog, StringComparison.Ordinal))
{
    await Console.Error.WriteLineAsync("Confirmation token does not match target database name.");
    return 11;
}

try
{
    var options = new DbContextOptionsBuilder<AppDbContext>()
        .UseSqlServer(builder.ConnectionString)
        .Options;

    await using var db = new AppDbContext(options);
    await DatabaseProvisionApplication.RunAsync(
        db,
        new DatabaseProvisionRequest
        {
            ApplyMigrations = parsed.ApplyMigrations,
            CreateReferenceData = parsed.CreateReferenceData,
            CreateDefaultUsers = parsed.CreateDefaultUsers,
            CreateDemoData = parsed.CreateDemoData,
        });

    await Console.Out.WriteLineAsync("Database provisioning completed.");
    return 0;
}
catch (Exception ex)
{
    await Console.Error.WriteLineAsync($"Database provisioning failed ({ex.GetType().Name}).");
    if (parsed.Verbose)
        await Console.Error.WriteLineAsync(ex.Message);

    return 12;
}

static async Task VerifyBackupAsync(string connectionString, string backupPath)
{
    await using var connection = new SqlConnection(connectionString);
    await connection.OpenAsync();
    var escapedPath = backupPath.Replace("'", "''");
    await using var command = connection.CreateCommand();
    command.CommandText = $"RESTORE VERIFYONLY FROM DISK = N'{escapedPath}'";
    command.CommandType = CommandType.Text;
    await command.ExecuteNonQueryAsync();
}

internal static class ProvisionArgumentParser
{
    internal static ProvisionArguments Parse(string[] args)
    {
        var result = new ProvisionArguments();
        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--help":
                case "-h":
                    result.ShowHelp = true;
                    break;
                case "--settings-path":
                    result.SettingsPath = ReadValue(args, ref i);
                    break;
                case "--apply-migrations":
                    result.ApplyMigrations = true;
                    break;
                case "--create-reference-data":
                    result.CreateReferenceData = true;
                    break;
                case "--create-default-users":
                    result.CreateDefaultUsers = true;
                    break;
                case "--create-demo-data":
                    result.CreateDemoData = true;
                    break;
                case "--expected-database-name":
                    result.ExpectedDatabaseName = ReadValue(args, ref i);
                    break;
                case "--backup-path":
                    result.BackupPath = ReadValue(args, ref i);
                    break;
                case "--backup-sha256":
                    result.BackupSha256 = ReadValue(args, ref i);
                    break;
                case "--confirmation-token":
                    result.ConfirmationToken = ReadValue(args, ref i);
                    break;
                case "--verbose":
                    result.Verbose = true;
                    break;
            }
        }

        if (string.IsNullOrWhiteSpace(result.SettingsPath))
            result.ShowHelp = true;

        if (!string.IsNullOrWhiteSpace(result.BackupSha256)
            && !Regex.IsMatch(result.BackupSha256, "^[0-9A-Fa-f]{64}$"))
        {
            throw new ArgumentException("Backup SHA256 must be a 64-character hex string.");
        }

        return result;
    }

    private static string ReadValue(string[] args, ref int index)
    {
        if (index + 1 >= args.Length)
            throw new ArgumentException($"Missing value for {args[index]}.");

        index++;
        return args[index];
    }
}

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
