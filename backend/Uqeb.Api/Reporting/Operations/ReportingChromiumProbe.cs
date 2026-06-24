using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Microsoft.Playwright;
using Uqeb.Api.Reporting.Configuration;

namespace Uqeb.Api.Reporting.Operations;

public enum ReportingChromiumProbeState
{
    Unknown,
    ExecutableMissing,
    LaunchFailed,
    Ready,
}

public sealed class ReportingChromiumProbeResult
{
    public ReportingChromiumProbeState State { get; init; }
    public bool ExecutableAvailable { get; init; }
    public bool LaunchSuccessful { get; init; }
    public string Summary { get; init; } = string.Empty;
}

public interface IReportingChromiumProbe
{
    Task<ReportingChromiumProbeResult> ProbeAsync(CancellationToken cancellationToken = default);
}

public sealed class ReportingChromiumProbe : IReportingChromiumProbe
{
    private const string CacheKey = "reporting.chromium.probe";
    private readonly ReportingOptions _options;
    private readonly IMemoryCache _cache;
    private readonly IReportingMetrics _metrics;
    private readonly ILogger<ReportingChromiumProbe> _logger;

    public ReportingChromiumProbe(
        IOptions<ReportingOptions> options,
        IMemoryCache cache,
        IReportingMetrics metrics,
        ILogger<ReportingChromiumProbe> logger)
    {
        _options = options.Value;
        _cache = cache;
        _metrics = metrics;
        _logger = logger;
    }

    public Task<ReportingChromiumProbeResult> ProbeAsync(CancellationToken cancellationToken = default)
    {
        return _cache.GetOrCreateAsync(CacheKey, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(_options.ReadinessCacheSeconds);
            return await ProbeCoreAsync(cancellationToken);
        })!;
    }

    private async Task<ReportingChromiumProbeResult> ProbeCoreAsync(CancellationToken cancellationToken)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(_options.ChromiumProbeTimeoutSeconds));

        try
        {
            var playwright = await Playwright.CreateAsync();
            try
            {
                var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
                {
                    Headless = true,
                });
                try
                {
                    await using var context = await browser.NewContextAsync();
                    var page = await context.NewPageAsync();
                    await page.SetContentAsync("<html><body><p>probe</p></body></html>", new PageSetContentOptions
                    {
                        WaitUntil = WaitUntilState.DOMContentLoaded,
                        Timeout = _options.ChromiumProbeTimeoutSeconds * 1000,
                    });
                    var pdf = await page.PdfAsync();
                    await page.CloseAsync();

                    if (pdf.Length == 0)
                    {
                        _metrics.RecordChromiumLaunchFailure();
                        return new ReportingChromiumProbeResult
                        {
                            State = ReportingChromiumProbeState.LaunchFailed,
                            ExecutableAvailable = true,
                            LaunchSuccessful = false,
                            Summary = "Chromium launched but PDF probe returned empty output.",
                        };
                    }

                    return new ReportingChromiumProbeResult
                    {
                        State = ReportingChromiumProbeState.Ready,
                        ExecutableAvailable = true,
                        LaunchSuccessful = true,
                        Summary = "Chromium launch and PDF probe succeeded.",
                    };
                }
                finally
                {
                    await browser.CloseAsync();
                }
            }
            finally
            {
                playwright.Dispose();
            }
        }
        catch (PlaywrightException ex) when (ex.Message.Contains("Executable doesn't exist", StringComparison.OrdinalIgnoreCase))
        {
            _metrics.RecordChromiumLaunchFailure();
            _logger.LogWarning(
                ReportingEventIds.ChromiumUnavailable,
                "Chromium executable is not available for reporting.");
            return new ReportingChromiumProbeResult
            {
                State = ReportingChromiumProbeState.ExecutableMissing,
                ExecutableAvailable = false,
                LaunchSuccessful = false,
                Summary = "Chromium executable is not installed.",
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _metrics.RecordChromiumLaunchFailure();
            _logger.LogWarning(
                ReportingEventIds.ChromiumUnavailable,
                ex,
                "Chromium probe failed.");
            return new ReportingChromiumProbeResult
            {
                State = ReportingChromiumProbeState.LaunchFailed,
                ExecutableAvailable = true,
                LaunchSuccessful = false,
                Summary = "Chromium probe failed.",
            };
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            _metrics.RecordChromiumLaunchFailure();
            return new ReportingChromiumProbeResult
            {
                State = ReportingChromiumProbeState.LaunchFailed,
                ExecutableAvailable = true,
                LaunchSuccessful = false,
                Summary = "Chromium probe timed out.",
            };
        }
    }
}
