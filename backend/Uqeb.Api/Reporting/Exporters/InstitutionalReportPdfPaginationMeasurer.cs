using Microsoft.Extensions.Logging;
using Microsoft.Playwright;
using Uqeb.Api.Reporting.DTOs;
using Uqeb.Api.Reporting.Enums;
using Uqeb.Api.Reporting.Operations;

namespace Uqeb.Api.Reporting.Exporters;

public interface IInstitutionalReportPdfPaginationMeasurer
{
    Task<IReadOnlyList<IReadOnlyList<TransactionDetailRowDto>>> MeasureTransactionDetailChunksAsync(
        RenderedReportManifestDto preflightManifest,
        string preflightHtmlDocument,
        IReadOnlyList<TransactionDetailRowDto> sourceRows,
        CancellationToken ct = default);
}

public sealed class InstitutionalReportPdfPaginationMeasurer : IInstitutionalReportPdfPaginationMeasurer, IAsyncDisposable
{
    private const int MaxHtmlLength = 8 * 1024 * 1024;
    private static readonly SemaphoreSlim BrowserGate = new(1, 1);

    private readonly IReportingChromiumProbe _chromiumProbe;
    private readonly ILogger<InstitutionalReportPdfPaginationMeasurer> _logger;
    private IPlaywright? _playwright;
    private IBrowser? _browser;

    public InstitutionalReportPdfPaginationMeasurer(
        IReportingChromiumProbe chromiumProbe,
        ILogger<InstitutionalReportPdfPaginationMeasurer> logger)
    {
        _chromiumProbe = chromiumProbe;
        _logger = logger;
    }

    public async Task<IReadOnlyList<IReadOnlyList<TransactionDetailRowDto>>> MeasureTransactionDetailChunksAsync(
        RenderedReportManifestDto preflightManifest,
        string preflightHtmlDocument,
        IReadOnlyList<TransactionDetailRowDto> sourceRows,
        CancellationToken ct = default)
    {
        if (sourceRows.Count == 0)
            return [];

        if (preflightManifest.Pages.All(p => p.SectionId != ReportSectionId.TransactionDetails))
            return [];

        if (string.IsNullOrWhiteSpace(preflightHtmlDocument))
            throw new InvalidOperationException("مستند HTML الخاص بقياس PDF فارغ.");

        if (preflightHtmlDocument.Length > MaxHtmlLength)
            throw new InvalidOperationException("حجم HTML الخاص بقياس PDF يتجاوز الحد المسموح.");

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

            await page.SetContentAsync(preflightHtmlDocument, new PageSetContentOptions
            {
                WaitUntil = WaitUntilState.NetworkIdle,
                Timeout = 30_000,
            });
            ct.ThrowIfCancellationRequested();

            await page.EvaluateAsync("() => document.fonts ? document.fonts.ready : Promise.resolve()");
            ct.ThrowIfCancellationRequested();

            var measurement = await page.EvaluateAsync<TransactionDetailDomMeasurement>("""
                () => {
                  const tables = Array.from(document.querySelectorAll('.report-table--transactions'));
                  const pages = tables
                    .map(table => ({ table, page: table.closest('.report-page') }))
                    .filter(item => item.page);

                  const measurePage = (item) => {
                    const page = item.page;
                    const content = page.querySelector('.report-content');
                    const table = item.table;
                    const contentRect = content.getBoundingClientRect();
                    const tableHeaderHeight = table.querySelector('thead')?.getBoundingClientRect().height ?? 0;
                    const fixedContentHeight = Array.from(content.children)
                      .filter(el => el !== table)
                      .reduce((sum, el) => {
                        const rect = el.getBoundingClientRect();
                        const style = window.getComputedStyle(el);
                        const marginTop = Number.parseFloat(style.marginTop) || 0;
                        const marginBottom = Number.parseFloat(style.marginBottom) || 0;
                        return sum + rect.height + marginTop + marginBottom;
                      }, 0);
                    return Math.max(0, contentRect.height - fixedContentHeight - tableHeaderHeight);
                  };

                  const rowHeights = pages.flatMap(item =>
                    Array.from(item.table.querySelectorAll('tbody tr')).map(row => row.getBoundingClientRect().height));

                  const firstAvailableRowsHeight = pages.length > 0 ? measurePage(pages[0]) : 0;
                  const continuationAvailableRowsHeight = pages.length > 1 ? measurePage(pages[1]) : firstAvailableRowsHeight;

                  return {
                    firstAvailableRowsHeight,
                    continuationAvailableRowsHeight,
                    rowHeights,
                  };
                }
                """);
            ct.ThrowIfCancellationRequested();

            if (measurement.RowHeights.Count != sourceRows.Count)
            {
                throw new InvalidOperationException(
                    $"فشل قياس صفوف التفاصيل: عدد الصفوف المقاسة ({measurement.RowHeights.Count}) لا يطابق المصدر ({sourceRows.Count}).");
            }

            var ranges = BuildMeasuredChunks(
                measurement.RowHeights,
                measurement.FirstAvailableRowsHeight,
                measurement.ContinuationAvailableRowsHeight);

            return ranges
                .Select(range => (IReadOnlyList<TransactionDetailRowDto>)sourceRows.Skip(range.Start).Take(range.Count).ToList())
                .ToList();
        }
        catch (PlaywrightException ex) when (IsMissingChromiumExecutable(ex))
        {
            _logger.LogWarning(ex, "Chromium executable is missing during PDF pagination measurement.");
            throw CreateChromiumUnavailableException();
        }
        finally
        {
            BrowserGate.Release();
        }
    }

    internal static IReadOnlyList<TransactionDetailRowRange> BuildMeasuredChunks(
        IReadOnlyList<double> rowHeights,
        double firstAvailableRowsHeight,
        double continuationAvailableRowsHeight)
    {
        var chunks = new List<TransactionDetailRowRange>();
        var index = 0;
        var isFirstPage = true;

        while (index < rowHeights.Count)
        {
            var availableHeight = isFirstPage ? firstAvailableRowsHeight : continuationAvailableRowsHeight;
            availableHeight = Math.Max(0, availableHeight);
            var usedHeight = 0d;
            var start = index;

            while (index < rowHeights.Count)
            {
                var rowHeight = Math.Max(0, rowHeights[index]);
                if (index > start && usedHeight + rowHeight > availableHeight)
                    break;

                usedHeight += rowHeight;
                index++;

                if (rowHeight > availableHeight)
                    break;
            }

            if (index == start)
                index++;

            chunks.Add(new TransactionDetailRowRange(start, index - start));
            isFirstPage = false;
        }

        return chunks;
    }

    private async Task EnsureChromiumReadyAsync(CancellationToken ct)
    {
        var probe = await _chromiumProbe.ProbeAsync(ct);
        if (probe.State == ReportingChromiumProbeState.Ready)
            return;

        _logger.LogWarning(
            "PDF pagination measurement blocked because Chromium is unavailable. ProbeState={ProbeState} Summary={Summary}",
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
            _logger.LogWarning(ex, "Chromium executable is missing during pagination browser launch.");
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

    private sealed class TransactionDetailDomMeasurement
    {
        public double FirstAvailableRowsHeight { get; set; }
        public double ContinuationAvailableRowsHeight { get; set; }
        public List<double> RowHeights { get; set; } = [];
    }
}

public sealed record TransactionDetailRowRange(int Start, int Count);
