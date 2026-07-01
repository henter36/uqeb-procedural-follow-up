using Uqeb.ScannerBridge.Cors;
using Uqeb.ScannerBridge.Endpoints;
using Uqeb.ScannerBridge.Options;
using Uqeb.ScannerBridge.Services;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Hosting.WindowsServices;

if (!OperatingSystem.IsWindows())
{
    await Console.Error.WriteLineAsync("Uqeb.ScannerBridge requires Windows.");
    return 1;
}

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseWindowsService(options =>
{
    options.ServiceName = "UqebScannerBridge";
});

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
