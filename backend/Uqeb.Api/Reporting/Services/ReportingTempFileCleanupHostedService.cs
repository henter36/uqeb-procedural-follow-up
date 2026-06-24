using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Uqeb.Api.Reporting.Operations;

namespace Uqeb.Api.Reporting.Services;

public sealed class ReportingTempFileCleanupHostedService : BackgroundService
{
    private readonly IReportingTempFileManager _tempFileManager;
    private readonly ILogger<ReportingTempFileCleanupHostedService> _logger;

    public ReportingTempFileCleanupHostedService(
        IReportingTempFileManager tempFileManager,
        ILogger<ReportingTempFileCleanupHostedService> logger)
    {
        _tempFileManager = tempFileManager;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                _tempFileManager.CleanupOrphanedSessions();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Periodic reporting temp cleanup failed.");
            }

            await Task.Delay(TimeSpan.FromMinutes(15), stoppingToken);
        }
    }
}
