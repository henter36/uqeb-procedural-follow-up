namespace Uqeb.Api.Services.Health;

public enum HealthDatabaseStatus
{
    Ready,
    Unreachable,
    Timeout,
    Error,
}

public sealed record HealthDatabaseCheckResult(
    HealthDatabaseStatus Status,
    Exception? Exception = null);

public interface IHealthDatabaseProbe
{
    Task<HealthDatabaseCheckResult> CheckAsync(CancellationToken cancellationToken);
}
