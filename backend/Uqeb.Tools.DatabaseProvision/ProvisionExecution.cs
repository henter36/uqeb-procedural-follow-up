using System.Security.Cryptography;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Uqeb.Api.Data;
using Uqeb.Api.Data.Provisioning;

namespace Uqeb.Tools.DatabaseProvision;

internal static class ProvisionExecution
{
    internal static async Task<int> RunAsync(ProvisionArguments parsed)
    {
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
        var validationCode = ValidateConnection(builder, parsed);
        if (validationCode is not null)
        {
            await Console.Error.WriteLineAsync(validationCode.Value.Message);
            return validationCode.Value.ExitCode;
        }

        if (!string.IsNullOrWhiteSpace(parsed.BackupPath))
        {
            var backupValidation = await ValidateBackupAsync(builder.ConnectionString, parsed);
            if (backupValidation is not null)
            {
                await Console.Error.WriteLineAsync(backupValidation.Value.Message);
                return backupValidation.Value.ExitCode;
            }
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
    }

    private static (int ExitCode, string Message)? ValidateConnection(
        SqlConnectionStringBuilder builder,
        ProvisionArguments parsed)
    {
        if (string.IsNullOrWhiteSpace(builder.DataSource))
            return (6, "Connection string server is missing.");

        if (string.IsNullOrWhiteSpace(builder.InitialCatalog))
            return (7, "Connection string database is missing.");

        if (!string.IsNullOrWhiteSpace(parsed.ExpectedDatabaseName)
            && !string.Equals(builder.InitialCatalog, parsed.ExpectedDatabaseName, StringComparison.OrdinalIgnoreCase))
        {
            return (8, $"Expected database '{parsed.ExpectedDatabaseName}' but connection targets '{builder.InitialCatalog}'.");
        }

        if (!string.IsNullOrWhiteSpace(parsed.ConfirmationToken)
            && !string.Equals(parsed.ConfirmationToken, builder.InitialCatalog, StringComparison.Ordinal))
        {
            return (11, "Confirmation token does not match target database name.");
        }

        return null;
    }

    private static async Task<(int ExitCode, string Message)?> ValidateBackupAsync(
        string connectionString,
        ProvisionArguments parsed)
    {
        if (!File.Exists(parsed.BackupPath))
            return (9, "Backup file was not found.");

        if (!string.IsNullOrWhiteSpace(parsed.BackupSha256))
        {
            var actualHash = await ComputeFileSha256HexAsync(parsed.BackupPath!);
            if (!string.Equals(actualHash, parsed.BackupSha256, StringComparison.OrdinalIgnoreCase))
                return (10, "Backup SHA256 mismatch.");
        }

        return null;
    }

    internal static async Task<string> ComputeFileSha256HexAsync(string path)
    {
        await using var stream = File.OpenRead(path);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream));
    }
}
