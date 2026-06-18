using Uqeb.ScannerBridge.Models;

namespace Uqeb.ScannerBridge.Endpoints;

internal static class EndpointResults
{
    public static IResult BadRequest(string code, string message) =>
        JsonError(code, message, StatusCodes.Status400BadRequest);

    public static IResult NotFound(string code, string message) =>
        JsonError(code, message, StatusCodes.Status404NotFound);

    public static IResult Unavailable(string code, string message) =>
        JsonError(code, message, StatusCodes.Status503ServiceUnavailable);

    public static IResult ScanFailed(string code, string message) =>
        JsonError(code, message, StatusCodes.Status422UnprocessableEntity);

    private static IResult JsonError(string code, string message, int statusCode) =>
        Results.Json(new ErrorResponse { Code = code, Message = message }, statusCode: statusCode);
}
