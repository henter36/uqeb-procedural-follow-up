namespace Uqeb.Api.Reporting.Services;

public sealed class InstitutionalReportExportException : Exception
{
    public InstitutionalReportExportException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
