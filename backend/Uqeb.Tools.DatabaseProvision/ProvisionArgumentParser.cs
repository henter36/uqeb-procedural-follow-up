using System.Text.RegularExpressions;

namespace Uqeb.Tools.DatabaseProvision;

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
            && !Regex.IsMatch(
                result.BackupSha256,
                "^[0-9A-Fa-f]{64}$",
                RegexOptions.None,
                TimeSpan.FromSeconds(1)))
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
