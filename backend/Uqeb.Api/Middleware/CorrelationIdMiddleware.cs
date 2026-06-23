namespace Uqeb.Api.Middleware;

public sealed class CorrelationIdMiddleware
{
    public const string HeaderName = "X-Correlation-ID";
    public const string ItemKey = "CorrelationId";

    private readonly RequestDelegate _next;
    private readonly ILogger<CorrelationIdMiddleware> _logger;

    public CorrelationIdMiddleware(
        RequestDelegate next,
        ILogger<CorrelationIdMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var suppliedValues = context.Request.Headers[HeaderName];
        string? suppliedId = null;
        if (suppliedValues.Count == 1)
            suppliedId = suppliedValues[0];

        if (!CorrelationIdValidator.IsValid(suppliedId))
        {
            if (!string.IsNullOrEmpty(suppliedId))
            {
                _logger.LogDebug(
                    "Rejected invalid {HeaderName} from client request.",
                    HeaderName);
            }

            suppliedId = null;
        }

        var correlationId = suppliedId ?? Guid.NewGuid().ToString("N");

        context.Items[ItemKey] = correlationId;

        context.Response.OnStarting(() =>
        {
            context.Response.Headers[HeaderName] = correlationId;
            return Task.CompletedTask;
        });

        using (_logger.BeginScope(new Dictionary<string, object>
        {
            ["CorrelationId"] = correlationId,
        }))
        {
            await _next(context);
        }
    }
}
