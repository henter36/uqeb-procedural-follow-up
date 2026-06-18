using Uqeb.ScannerBridge.Models;
using Uqeb.ScannerBridge.Options;
using Uqeb.ScannerBridge.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

if (!OperatingSystem.IsWindows())
{
    Console.Error.WriteLine("Uqeb.ScannerBridge requires Windows.");
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
    .WithOrigins(corsOptions.AllowedOrigins)
    .AllowAnyHeader()
    .AllowAnyMethod());

app.MapGet("/status", (ScannerProviderResolver resolver, IOptions<ScannerBridgeOptions> options) =>
{
    try
    {
        var provider = resolver.Resolve();
        var scanners = ScannerProviderResolver.ListScannersSafe(provider);

        return Results.Json(new StatusResponse
        {
            Ok = true,
            Version = options.Value.Version,
            ScannerApi = provider.ApiName,
            ScannerCount = scanners.Count
        });
    }
    catch (InvalidOperationException)
    {
        return Results.Json(new StatusResponse
        {
            Ok = true,
            Version = options.Value.Version,
            ScannerApi = "Unavailable",
            ScannerCount = 0
        });
    }
});

app.MapGet("/scanners", (ScannerProviderResolver resolver) =>
{
    try
    {
        var provider = resolver.Resolve();
        var scanners = ScannerProviderResolver.ListScannersSafe(provider)
            .Select(scanner => new ScannerDeviceDto
            {
                Id = scanner.Id,
                Name = scanner.Name,
                Default = scanner.Default
            })
            .ToList();

        return Results.Json(new ScannersResponse { Scanners = scanners });
    }
    catch (InvalidOperationException)
    {
        return Results.Json(new ScannersResponse { Scanners = [] });
    }
});

app.MapPost("/scan", async Task<IResult> (
    [FromBody] ScanRequestDto? request,
    ScannerProviderResolver resolver,
    ScanTempStore store,
    CancellationToken cancellationToken) =>
{
    if (request is null || string.IsNullOrWhiteSpace(request.ScannerId))
    {
        return ErrorResult("INVALID_REQUEST", "scannerId is required.");
    }

    try
    {
        var provider = resolver.Resolve();
        var scanners = ScannerProviderResolver.ListScannersSafe(provider);
        if (scanners.Count == 0)
        {
            return ErrorResult("NO_SCANNER", "No scanner is available on this machine.");
        }

        if (scanners.All(scanner => scanner.Id != request.ScannerId))
        {
            return ErrorResult("SCANNER_NOT_FOUND", "The requested scanner was not found.");
        }

        var input = new ScanInput(
            request.ScannerId,
            string.IsNullOrWhiteSpace(request.Format) ? "image/jpeg" : request.Format,
            request.Dpi is > 0 ? request.Dpi.Value : 300,
            string.IsNullOrWhiteSpace(request.ColorMode) ? "color" : request.ColorMode);

        var output = await provider.ScanAsync(input, cancellationToken);
        var stored = store.SaveScan(output);
        return Results.Json(store.ToResponse(stored));
    }
    catch (InvalidOperationException ex)
    {
        return ErrorResult("PROVIDER_UNAVAILABLE", ex.Message);
    }
    catch (Exception ex)
    {
        return ErrorResult("SCAN_FAILED", ex.Message);
    }
});

app.MapGet("/scan/{scanId}/file", (string scanId, ScanTempStore store) =>
{
    if (!Guid.TryParse(scanId, out var id))
    {
        return ErrorResult("INVALID_SCAN_ID", "scanId must be a GUID.");
    }

    var stored = store.GetScan(id);
    if (stored is null || !File.Exists(stored.FilePath))
    {
        return ErrorResult("SCAN_NOT_FOUND", "Scan file was not found or has expired.");
    }

    return Results.File(stored.FilePath, stored.ContentType, stored.FileName);
});

app.MapDelete("/scan/{scanId}", (string scanId, ScanTempStore store) =>
{
    if (!Guid.TryParse(scanId, out var id))
    {
        return Results.NoContent();
    }

    store.DeleteScan(id);
    return Results.NoContent();
});

app.Run();
return 0;

static IResult ErrorResult(string code, string message) =>
    Results.Json(new ErrorResponse { Code = code, Message = message }, statusCode: StatusCodes.Status400BadRequest);
