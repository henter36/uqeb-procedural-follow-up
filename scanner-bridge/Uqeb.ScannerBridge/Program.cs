using Uqeb.ScannerBridge.Endpoints;
using Uqeb.ScannerBridge.Options;
using Uqeb.ScannerBridge.Services;
using Microsoft.Extensions.Options;

if (!OperatingSystem.IsWindows())
{
    await Console.Error.WriteLineAsync("Uqeb.ScannerBridge requires Windows.");
    return 1;
}

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<ScannerBridgeOptions>(builder.Configuration.GetSection(ScannerBridgeOptions.SectionName));
builder.Services.Configure<CorsOptions>(builder.Configuration.GetSection(CorsOptions.SectionName));
builder.Services.AddSingleton<WiaScannerProvider>();
builder.Services.AddSingleton<MockScannerProvider>();
builder.Services.AddSingleton<ScannerProviderResolver>();
builder.Services.AddSingleton<ScanTempStore>();
builder.Services.AddHostedService<ScanCleanupService>();

var bridgeOptions = builder.Configuration.GetSection(ScannerBridgeOptions.SectionName).Get<ScannerBridgeOptions>()
    ?? new ScannerBridgeOptions();

builder.WebHost.ConfigureKestrel(options =>
{
    options.ListenLocalhost(bridgeOptions.Port);
});

builder.Services.AddCors();

var app = builder.Build();

var corsOptions = app.Services.GetRequiredService<IOptions<CorsOptions>>().Value;
app.UseCors(policy => policy
    .SetIsOriginAllowed(origin => CorsOriginHelper.IsAllowed(origin, corsOptions.AllowedOrigins))
    .AllowAnyHeader()
    .AllowAnyMethod());

app.MapScannerEndpoints();

await app.RunAsync();
return 0;

internal static class CorsOriginHelper
{
    public static bool IsAllowed(string origin, string[] configuredOrigins)
    {
        if (configuredOrigins.Contains(origin, StringComparer.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri))
        {
            return false;
        }

        if (!string.Equals(uri.Scheme, "http", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var host = uri.Host;
        return host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
            || host == "127.0.0.1"
            || host == "::1";
    }
}
