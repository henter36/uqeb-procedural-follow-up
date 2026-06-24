using Uqeb.Api.Middleware;

namespace Uqeb.Api.Reporting.Operations;

public interface IReportingCorrelationIdProvider
{
    string? GetCorrelationId();
}

public sealed class ReportingCorrelationIdProvider(IHttpContextAccessor httpContextAccessor)
    : IReportingCorrelationIdProvider
{
    public string? GetCorrelationId() =>
        httpContextAccessor.HttpContext?.Items[CorrelationIdMiddleware.ItemKey] as string;
}
