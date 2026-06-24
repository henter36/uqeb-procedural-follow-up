using Microsoft.Extensions.Logging;
using Microsoft.Playwright;
using Uqeb.Api.Reporting.DTOs;
using Uqeb.Api.Reporting.Operations;

namespace Uqeb.Api.Reporting.Exporters;

public interface IInstitutionalReportPdfExporter
{
    Task<byte[]> ExportAsync(RenderedReportManifestDto manifest, string htmlDocument, CancellationToken ct = default);
}

/// <summary>
/// Renders institutional report HTML to PDF via headless Chromium (Playwright).
/// HTML/CSS is the single visual source; no plain-text stripping.
/// </summary>
public sealed class InstitutionalReportPlaywrightPdfExporter : IInstitutionalReportPdfExporter, IAsyncDisposable
{
    private const int MaxHtmlLength = 8 * 1024 * 1024;
    private static readonly SemaphoreSlim BrowserGate = new(1, 1);
    private readonly IReportingChromiumProbe _chromiumProbe;
    private readonly ILogger<InstitutionalReportPlaywrightPdfExporter> _logger;
    private IPlaywright? _playwright;
    private IBrowser? _browser;

    public InstitutionalReportPlaywrightPdfExporter(
        IReportingChromiumProbe chromiumProbe,
        ILogger<InstitutionalReportPlaywrightPdfExporter> logger)
    {
        _chromiumProbe = chromiumProbe;
        _logger = logger;
    }

    public async Task<byte[]> ExportAsync(RenderedReportManifestDto manifest, string htmlDocument, CancellationToken ct = default)
    {
        if (manifest.Pages.Count == 0)
            throw new InvalidOperationException("لا توجد صفحات للتصدير إلى PDF.");

        if (string.IsNullOrWhiteSpace(htmlDocument))
            throw new InvalidOperationException("مستند HTML فارغ.");

        if (htmlDocument.Length > MaxHtmlLength)
            throw new InvalidOperationException("حجم HTML يتجاوز الحد المسموح للتصدير.");

        await EnsureChromiumReadyAsync(ct);

        await BrowserGate.WaitAsync(ct);
        try
        {
            await EnsureBrowserAsync(ct);
            await using var context = await _browser!.NewContextAsync(new BrowserNewContextOptions
            {
                Locale = "ar-SA",
            });
            var page = await context.NewPageAsync();

            await page.RouteAsync("**/*", route =>
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

            await page.SetContentAsync(htmlDocument, new PageSetContentOptions
            {
                WaitUntil = WaitUntilState.NetworkIdle,
                Timeout = 30_000,
            });

            await page.EvaluateAsync("() => document.fonts ? document.fonts.ready : Promise.resolve()");

            return await page.PdfAsync(new PagePdfOptions
            {
                PrintBackground = true,
                PreferCSSPageSize = true,
                Margin = new Margin
                {
                    Top = "0",
                    Bottom = "0",
                    Left = "0",
                    Right = "0",
                },
            });
        }
        catch (PlaywrightException ex) when (IsMissingChromiumExecutable(ex))
        {
            _logger.LogWarning(ex, "Chromium executable is missing during PDF export.");
            throw CreateChromiumUnavailableException();
        }
        finally
        {
            BrowserGate.Release();
        }
    }

    private async Task EnsureChromiumReadyAsync(CancellationToken ct)
    {
        var probe = await _chromiumProbe.ProbeAsync(ct);
        if (probe.State == ReportingChromiumProbeState.Ready)
            return;

        _logger.LogWarning(
            "PDF export blocked because Chromium is unavailable. ProbeState={ProbeState} Summary={Summary}",
            probe.State,
            probe.Summary);
        throw CreateChromiumUnavailableException();
    }

    private static ReportingConfigurationException CreateChromiumUnavailableException() =>
        new(
            ReportingErrorCodes.ChromiumUnavailable,
            "متصفح Chromium غير متاح لتصدير PDF. ثبّت Chromium عبر playwright.ps1 install chromium كما في README.");

    private static bool IsMissingChromiumExecutable(PlaywrightException ex) =>
        ex.Message.Contains("Executable doesn't exist", StringComparison.OrdinalIgnoreCase);

    private async Task EnsureBrowserAsync(CancellationToken ct)
    {
        if (_browser is { IsConnected: true })
            return;

        try
        {
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
            _logger.LogWarning(ex, "Chromium executable is missing during browser launch.");
            throw CreateChromiumUnavailableException();
        }
    }

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
