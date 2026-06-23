using System.Text;
using Microsoft.Playwright;
using Uqeb.Api.Reporting.DTOs;

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
    private IPlaywright? _playwright;
    private IBrowser? _browser;

    public async Task<byte[]> ExportAsync(RenderedReportManifestDto manifest, string htmlDocument, CancellationToken ct = default)
    {
        if (manifest.Pages.Count == 0)
            throw new InvalidOperationException("لا توجد صفحات للتصدير إلى PDF.");

        if (string.IsNullOrWhiteSpace(htmlDocument))
            throw new InvalidOperationException("مستند HTML فارغ.");

        if (htmlDocument.Length > MaxHtmlLength)
            throw new InvalidOperationException("حجم HTML يتجاوز الحد المسموح للتصدير.");

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

            return await page.PdfAsync(new PagePdfOptions
            {
                Format = "A4",
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
        finally
        {
            BrowserGate.Release();
        }
    }

    private async Task EnsureBrowserAsync(CancellationToken ct)
    {
        if (_browser is { IsConnected: true })
            return;

        _playwright ??= await Playwright.CreateAsync();
        _browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = true,
            Args = ["--font-render-hinting=none", "--disable-dev-shm-usage"],
        });
        ct.ThrowIfCancellationRequested();
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
