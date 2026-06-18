namespace Uqeb.ScannerBridge.Services;

public sealed class ScanCleanupService : BackgroundService
{
    private readonly ScanTempStore _store;
    private readonly ILogger<ScanCleanupService> _logger;

    public ScanCleanupService(ScanTempStore store, ILogger<ScanCleanupService> logger)
    {
        _store = store;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                _store.CleanupExpired();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Scan temp cleanup failed.");
            }

            await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
        }
    }
}
