using Uqeb.ScannerBridge.Models;
using Uqeb.ScannerBridge.Options;
using Uqeb.ScannerBridge.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Uqeb.ScannerBridge.Endpoints;

internal static class ScannerEndpoints
{
    public static void MapScannerEndpoints(this WebApplication app)
    {
        app.MapGet("/status", GetStatus);
        app.MapGet("/scanners", GetScanners);
        app.MapPost("/scan", PostScan);
        app.MapGet("/scan/{scanId}/file", GetScanFile);
        app.MapDelete("/scan/{scanId}", DeleteScan);
    }

    private static IResult GetStatus(ScannerProviderResolver resolver, IOptions<ScannerBridgeOptions> options)
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
    }

    private static IResult GetScanners(ScannerProviderResolver resolver)
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
    }

    private static async Task<IResult> PostScan(
        [FromBody] ScanRequestDto? request,
        ScannerProviderResolver resolver,
        ScanTempStore store,
        CancellationToken cancellationToken)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.ScannerId))
        {
            return EndpointResults.BadRequest("INVALID_REQUEST", "scannerId is required.");
        }

        if (!ScanFormatHelper.TryNormalizeAndValidate(request.Format, out var format, out var formatError))
        {
            return EndpointResults.BadRequest("INVALID_FORMAT", formatError);
        }

        try
        {
            var provider = resolver.Resolve();
            var scanners = ScannerProviderResolver.ListScannersSafe(provider);
            if (scanners.Count == 0)
            {
                return EndpointResults.Unavailable("NO_SCANNER", "No scanner is available on this machine.");
            }

            if (scanners.All(scanner => scanner.Id != request.ScannerId))
            {
                return EndpointResults.NotFound("SCANNER_NOT_FOUND", "The requested scanner was not found.");
            }

            var input = new ScanInput(
                request.ScannerId,
                format,
                request.Dpi is > 0 ? request.Dpi.Value : 300,
                string.IsNullOrWhiteSpace(request.ColorMode) ? "color" : request.ColorMode);

            var output = await provider.ScanAsync(input, cancellationToken);
            var stored = store.SaveScan(output);
            return Results.Json(store.ToResponse(stored));
        }
        catch (InvalidOperationException ex)
        {
            return EndpointResults.Unavailable("PROVIDER_UNAVAILABLE", ex.Message);
        }
        catch (Exception ex)
        {
            return EndpointResults.ScanFailed("SCAN_FAILED", ex.Message);
        }
    }

    private static IResult GetScanFile(string scanId, ScanTempStore store)
    {
        if (!Guid.TryParse(scanId, out var id))
        {
            return EndpointResults.BadRequest("INVALID_SCAN_ID", "scanId must be a GUID.");
        }

        var stored = store.GetScan(id);
        if (stored is null || !File.Exists(stored.FilePath))
        {
            return EndpointResults.NotFound("SCAN_NOT_FOUND", "Scan file was not found or has expired.");
        }

        return Results.File(stored.FilePath, stored.ContentType, stored.FileName);
    }

    private static IResult DeleteScan(string scanId, ScanTempStore store)
    {
        if (!Guid.TryParse(scanId, out var id))
        {
            return Results.NoContent();
        }

        store.DeleteScan(id);
        return Results.NoContent();
    }
}
