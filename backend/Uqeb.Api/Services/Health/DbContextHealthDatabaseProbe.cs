using Microsoft.EntityFrameworkCore;
using Uqeb.Api.Data;

namespace Uqeb.Api.Services.Health;

public sealed class DbContextHealthDatabaseProbe : IHealthDatabaseProbe
{
    private readonly AppDbContext _db;
    private readonly IConfiguration _configuration;

    public DbContextHealthDatabaseProbe(AppDbContext db, IConfiguration configuration)
    {
        _db = db;
        _configuration = configuration;
    }

    public async Task<HealthDatabaseCheckResult> CheckAsync(CancellationToken cancellationToken)
    {
        var timeoutSeconds = _configuration.GetValue("HealthChecks:DatabaseTimeoutSeconds", 5);
        if (timeoutSeconds < 1)
            timeoutSeconds = 5;

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));

        try
        {
            var canConnect = await _db.Database.CanConnectAsync(timeoutCts.Token);
            return canConnect
                ? new HealthDatabaseCheckResult(HealthDatabaseStatus.Ready)
                : new HealthDatabaseCheckResult(HealthDatabaseStatus.Unreachable);
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            return new HealthDatabaseCheckResult(HealthDatabaseStatus.Timeout);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            return new HealthDatabaseCheckResult(HealthDatabaseStatus.Error, exception);
        }
    }
}
