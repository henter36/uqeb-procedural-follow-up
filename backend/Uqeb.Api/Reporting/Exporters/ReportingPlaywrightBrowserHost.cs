using Microsoft.Extensions.Logging;
using Microsoft.Playwright;
using Uqeb.Api.Reporting.Operations;

namespace Uqeb.Api.Reporting.Exporters;

public interface IReportingPlaywrightBrowserHost
{
    Task<T> RunWithPageAsync<T>(
        string operationName,
        Func<IPage, CancellationToken, Task<T>> action,
        CancellationToken ct = default);

    Task WaitForFontsAsync(IPage page, CancellationToken ct = default);
}

public sealed class ReportingPlaywrightBrowserHost : IReportingPlaywrightBrowserHost, IAsyncDisposable
{
    private static readonly SemaphoreSlim BrowserGate = new(1, 1);

    private readonly IReportingChromiumProbe _chromiumProbe;
    private readonly ILogger<ReportingPlaywrightBrowserHost> _logger;
    private IPlaywright? _playwright;
    private IBrowser? _browser;

    public ReportingPlaywrightBrowserHost(
        IReportingChromiumProbe chromiumProbe,
        ILogger<ReportingPlaywrightBrowserHost> logger)
    {
        _chromiumProbe = chromiumProbe;
        _logger = logger;
    }

    public async Task<T> RunWithPageAsync<T>(
        string operationName,
        Func<IPage, CancellationToken, Task<T>> action,
        CancellationToken ct = default)
    {
        await EnsureChromiumReadyAsync(operationName, ct);

        await BrowserGate.WaitAsync(ct);
        try
        {
            await EnsureBrowserAsync(operationName, ct);
            await using var context = await _browser!.NewContextAsync(new BrowserNewContextOptions
            {
                Locale = "ar-SA",
            });
            var page = await context.NewPageAsync();
            await ConfigureRoutePolicyAsync(page);

            return await action(page, ct);
        }
        catch (PlaywrightException ex) when (IsMissingChromiumExecutable(ex))
        {
            _logger.LogWarning(ex, "Chromium executable is missing during {OperationName}.", operationName);
            throw CreateChromiumUnavailableException();
        }
        finally
        {
            BrowserGate.Release();
        }
    }

    public async Task WaitForFontsAsync(IPage page, CancellationToken ct = default)
    {
        await page.EvaluateAsync("""
            async () => {
              if (document.fonts) {
                await document.fonts.ready;
              }
            }
            """);
        ct.ThrowIfCancellationRequested();
    }

    private static Task ConfigureRoutePolicyAsync(IPage page) =>
        page.RouteAsync("**/*", route =>
        {
            var url = route.Request.Url;
            if (url.StartsWith("data:", StringComparison.OrdinalIgnoreCase)
                || url.StartsWith("about:", StringComparison.OrdinalIgnoreCase)
                || url.StartsWith("blob:", StringComparison.OrdinalIgnoreCase))
            {
                return route.ContinueAsync();
            }

            return route.AbortAsync();
        });

    private async Task EnsureChromiumReadyAsync(string operationName, CancellationToken ct)
    {
        var probe = await _chromiumProbe.ProbeAsync(ct);
        if (probe.State == ReportingChromiumProbeState.Ready)
            return;

        _logger.LogWarning(
            "Playwright operation blocked because Chromium is unavailable. OperationName={OperationName} ProbeState={ProbeState} Summary={Summary}",
            operationName,
            probe.State,
            probe.Summary);
        throw CreateChromiumUnavailableException();
    }

    private async Task EnsureBrowserAsync(string operationName, CancellationToken ct)
    {
        if (_browser is { IsConnected: true })
            return;

        try
        {
            if (_browser is not null)
            {
                await _browser.DisposeAsync();
                _browser = null;
            }

            _playwright ??= await Playwright.CreateAsync();
            _browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
            {
                Headless = true,
                Args = ["--font-render-hinting=none", "--disable-dev-shm-usage"],
            });
            ct.ThrowIfCancellationRequested();
        }
        catch (PlaywrightException ex) when (IsMissingChromiumExecutable(ex))
        {
            _logger.LogWarning(ex, "Chromium executable is missing during {OperationName} browser launch.", operationName);
            throw CreateChromiumUnavailableException();
        }
    }

    private static ReportingConfigurationException CreateChromiumUnavailableException() =>
        new(
            ReportingErrorCodes.ChromiumUnavailable,
            "متصفح Chromium غير متاح لتصدير PDF. ثبّت Chromium عبر playwright.ps1 install chromium كما في README.");

    private static bool IsMissingChromiumExecutable(PlaywrightException ex) =>
        ex.Message.Contains("Executable doesn't exist", StringComparison.OrdinalIgnoreCase);

    public async ValueTask DisposeAsync()
    {
        if (_browser is not null)
        {
            await _browser.DisposeAsync();
            _browser = null;
        }

        _playwright?.Dispose();
        _playwright = null;
    }
}
