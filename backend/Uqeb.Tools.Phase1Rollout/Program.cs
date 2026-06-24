using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Uqeb.Api.Data;
using Uqeb.Api.Reporting.Operations;

var parsed = Phase1ArgumentParser.Parse(args);
if (parsed.ShowHelp)
{
    await Console.Error.WriteLineAsync(
        "Usage: resolve-admin --settings-path <appsettings.json> [--admin-username admin] [--verbose]");
    return 2;
}

var settingsPath = Path.GetFullPath(parsed.SettingsPath!);
if (!File.Exists(settingsPath))
{
    await Console.Error.WriteLineAsync("Settings file was not found.");
    return 3;
}

var configuration = new ConfigurationBuilder()
    .AddJsonFile(settingsPath, optional: false)
    .AddEnvironmentVariables()
    .Build();

var connectionString = configuration.GetConnectionString("DefaultConnection");
if (string.IsNullOrWhiteSpace(connectionString))
{
    await Console.Error.WriteLineAsync("DefaultConnection is missing from settings.");
    return 4;
}

try
{
    var options = new DbContextOptionsBuilder<AppDbContext>()
        .UseSqlServer(connectionString)
        .Options;

    await using var db = new AppDbContext(options);
    var resolver = new ReportingPhase1AdminUserResolver(db);
    var result = await resolver.ResolveAsync(parsed.AdminUsername);

    var payload = new
    {
        status = result.Status.ToString(),
        userId = result.UserId,
        maskedUserId = result.UserId is int id ? ReportingPhase1AdminUserResolver.MaskUserId(id) : null,
        detail = result.Detail,
    };

    await Console.Out.WriteLineAsync(JsonSerializer.Serialize(payload));

    return result.Status switch
    {
        ReportingPhase1AdminUserResolutionStatus.Success => 0,
        _ => 1,
    };
}
catch (Exception ex)
{
    await Console.Error.WriteLineAsync($"Admin username resolution failed ({ex.GetType().Name}).");
    if (parsed.Verbose)
        await Console.Error.WriteLineAsync(ex.Message);

    return 5;
}

internal sealed class Phase1Arguments
{
    public string? SettingsPath { get; set; }
    public string AdminUsername { get; set; } = "admin";
    public bool Verbose { get; set; }
    public bool ShowHelp { get; set; }
}

internal static class Phase1ArgumentParser
{
    internal static Phase1Arguments Parse(string[] args)
    {
        var queue = new Queue<string>(args);
        var result = new Phase1Arguments();

        while (queue.TryDequeue(out var argument))
        {
            switch (argument)
            {
                case "--admin-username":
                    result.AdminUsername = ReadRequiredValue(queue, argument);
                    break;
                case "--settings-path":
                    result.SettingsPath = ReadRequiredValue(queue, argument);
                    break;
                case "--verbose":
                    result.Verbose = true;
                    break;
                case "--help":
                case "-h":
                    result.ShowHelp = true;
                    break;
                default:
                    throw new ArgumentException($"Unknown argument: {argument}");
            }
        }

        if (string.IsNullOrWhiteSpace(result.SettingsPath))
            result.ShowHelp = true;

        return result;
    }

    private static string ReadRequiredValue(Queue<string> queue, string argument)
    {
        if (!queue.TryDequeue(out var value) || string.IsNullOrWhiteSpace(value))
            throw new ArgumentException($"Missing value for {argument}.");

        return value;
    }
}
