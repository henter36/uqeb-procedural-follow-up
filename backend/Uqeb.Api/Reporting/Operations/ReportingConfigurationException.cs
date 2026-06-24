using Microsoft.AspNetCore.Http;

namespace Uqeb.Api.Reporting.Operations;

public sealed class ReportingConfigurationException : Exception
{
    public ReportingConfigurationException(
        string errorCode,
        string message,
        int statusCode = StatusCodes.Status503ServiceUnavailable)
        : base(message)
    {
        ErrorCode = errorCode;
        StatusCode = statusCode;
    }

    public string ErrorCode { get; }
    public int StatusCode { get; }
}
