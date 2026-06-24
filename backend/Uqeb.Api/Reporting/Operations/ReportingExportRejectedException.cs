using Microsoft.AspNetCore.Http;

namespace Uqeb.Api.Reporting.Operations;

public sealed class ReportingExportRejectedException : Exception
{
    public ReportingExportRejectedException(
        string errorCode,
        string message,
        int statusCode = StatusCodes.Status429TooManyRequests,
        int? retryAfterSeconds = null)
        : base(message)
    {
        ErrorCode = errorCode;
        StatusCode = statusCode;
        RetryAfterSeconds = retryAfterSeconds;
    }

    public string ErrorCode { get; }
    public int StatusCode { get; }
    public int? RetryAfterSeconds { get; }
}

public static class ReportingErrorCodes
{
    public const string ConcurrencyLimit = "REPORTING_CONCURRENCY_LIMIT";
    public const string TempSpaceInsufficient = "REPORTING_TEMP_SPACE_INSUFFICIENT";
    public const string ExportTimeout = "REPORTING_EXPORT_TIMEOUT";
    public const string ExportCancelled = "REPORTING_EXPORT_CANCELLED";
    public const string ReportNumberSequenceSchemaMissing = "report_number_sequence_schema_missing";
    public const string ChromiumUnavailable = "reporting_chromium_unavailable";
}
