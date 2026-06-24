using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Uqeb.Api.Data;
using Uqeb.Api.Reporting.Operations;

if (!TryParseArgs(args, out var adminUsername, out var settingsPath))
{
    Console.Error.WriteLine("Usage: resolve-admin --settings-path <appsettings.json> [--admin-username admin]");
    return 2;
}

var configuration = new ConfigurationBuilder()
    .AddJsonFile(settingsPath, optional: false)
    .AddEnvironmentVariables()
    .Build();

var connectionString = configuration.GetConnectionString("DefaultConnection");
if (string.IsNullOrWhiteSpace(connectionString))
{
    Console.Error.WriteLine("DefaultConnection is missing from settings.");
    return 3;
}

var options = new DbContextOptionsBuilder<AppDbContext>()
    .UseSqlServer(connectionString)
    .Options;

await using var db = new AppDbContext(options);
var resolver = new ReportingPhase1AdminUserResolver(db);
var result = await resolver.ResolveAsync(adminUsername);

var payload = new
{
    status = result.Status.ToString(),
    userId = result.UserId,
    maskedUserId = result.UserId is int id ? ReportingPhase1AdminUserResolver.MaskUserId(id) : null,
    detail = result.Detail,
};

Console.WriteLine(JsonSerializer.Serialize(payload));

return result.Status switch
{
    ReportingPhase1AdminUserResolutionStatus.Success => 0,
    _ => 1,
};

static bool TryParseArgs(string[] args, out string adminUsername, out string settingsPath)
{
    adminUsername = "admin";
    settingsPath = string.Empty;

    for (var i = 0; i < args.Length; i++)
    {
        switch (args[i])
        {
            case "--admin-username" when i + 1 < args.Length:
                adminUsername = args[++i];
                break;
            case "--settings-path" when i + 1 < args.Length:
                settingsPath = args[++i];
                break;
        }
    }

    return !string.IsNullOrWhiteSpace(settingsPath);
}
