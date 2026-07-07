using Microsoft.Extensions.Logging;
using Microsoft.Playwright;
using Uqeb.Api.Reporting.DTOs;
using Uqeb.Api.Reporting.Enums;

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
    private readonly IReportingPlaywrightBrowserHost _browserHost;

    public InstitutionalReportPdfPaginationMeasurer(IReportingPlaywrightBrowserHost browserHost)
    {
        _browserHost = browserHost;
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

        return await _browserHost.RunWithPageAsync("PDF pagination measurement", async (page, token) =>
        {
            await page.SetContentAsync(preflightHtmlDocument, new PageSetContentOptions
            {
                WaitUntil = WaitUntilState.NetworkIdle,
                Timeout = 30_000,
            });
            token.ThrowIfCancellationRequested();

            await _browserHost.WaitForFontsAsync(page, token);

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
            token.ThrowIfCancellationRequested();

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
        }, ct);
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

    public async ValueTask DisposeAsync()
    {
        if (_browserHost is IAsyncDisposable disposable)
            await disposable.DisposeAsync();
    }

    private sealed class TransactionDetailDomMeasurement
    {
        public double FirstAvailableRowsHeight { get; set; }
        public double ContinuationAvailableRowsHeight { get; set; }
        public List<double> RowHeights { get; set; } = [];
    }
}

public sealed record TransactionDetailRowRange(int Start, int Count);
