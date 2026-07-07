using Microsoft.Extensions.Logging;
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
    private readonly IReportingPlaywrightBrowserHost _browserHost;

    public InstitutionalReportPlaywrightPdfExporter(IReportingPlaywrightBrowserHost browserHost)
    {
        _browserHost = browserHost;
    }

    public async Task<byte[]> ExportAsync(RenderedReportManifestDto manifest, string htmlDocument, CancellationToken ct = default)
    {
        if (manifest.Pages.Count == 0)
            throw new InvalidOperationException("لا توجد صفحات للتصدير إلى PDF.");

        if (string.IsNullOrWhiteSpace(htmlDocument))
            throw new InvalidOperationException("مستند HTML فارغ.");

        if (htmlDocument.Length > MaxHtmlLength)
            throw new InvalidOperationException("حجم HTML يتجاوز الحد المسموح للتصدير.");

        return await _browserHost.RunWithPageAsync("PDF export", async (page, token) =>
        {
            await page.SetContentAsync(htmlDocument, new PageSetContentOptions
            {
                WaitUntil = WaitUntilState.NetworkIdle,
                Timeout = 30_000,
            });
            token.ThrowIfCancellationRequested();

            await _browserHost.WaitForFontsAsync(page, token);

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
        }, ct);
    }

    public async ValueTask DisposeAsync()
    {
        if (_browserHost is IAsyncDisposable disposable)
            await disposable.DisposeAsync();
    }
}
