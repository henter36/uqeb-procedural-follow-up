using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Diagnostics;
using Uqeb.Api.Helpers;
using Uqeb.Api.Reporting.Configuration;
using Uqeb.Api.Reporting.DTOs;
using Uqeb.Api.Reporting.Enums;
using Uqeb.Api.Reporting.Exporters;
using Uqeb.Api.Reporting.Helpers;
using Uqeb.Api.Reporting.Operations;
using Uqeb.Api.Reporting.Rendering;

namespace Uqeb.Api.Reporting.Services;

public interface IInstitutionalReportExportService
{
    Task<ReportExportResultDto> ExportAsync(ReportExportRequestDto request, CancellationToken ct = default);
}

public sealed class InstitutionalReportExportService : IInstitutionalReportExportService
{
    private const string SelectedPagesField = "selectedPages";

    private readonly IInstitutionalReportBuildSupport _buildSupport;
    private readonly IInstitutionalReportPdfExporter _pdfExporter;
    private readonly IInstitutionalReportPdfPaginationMeasurer? _pdfPaginationMeasurer;
    private readonly ReportingOptions _reportingOptions;
    private readonly IReportingExportGuard _exportGuard;
    private readonly IReportingMetrics _metrics;
    private readonly ILogger<InstitutionalReportExportService> _logger;
    private readonly IReportingCorrelationIdProvider _correlationIdProvider;
    private readonly InstitutionalReportRenderer _renderer = new();

    public InstitutionalReportExportService(
        IInstitutionalReportBuildSupport buildSupport,
        IInstitutionalReportPdfExporter pdfExporter,
        IOptions<ReportingOptions> reportingOptions,
        IReportingExportGuard exportGuard,
        IReportingMetrics metrics,
        ILogger<InstitutionalReportExportService> logger,
        IReportingCorrelationIdProvider correlationIdProvider,
        IInstitutionalReportPdfPaginationMeasurer? pdfPaginationMeasurer = null)
    {
        _buildSupport = buildSupport;
        _pdfExporter = pdfExporter;
        _pdfPaginationMeasurer = pdfPaginationMeasurer;
        _reportingOptions = reportingOptions.Value;
        _exportGuard = exportGuard;
        _metrics = metrics;
        _logger = logger;
        _correlationIdProvider = correlationIdProvider;
    }

    public async Task<ReportExportResultDto> ExportAsync(ReportExportRequestDto request, CancellationToken ct = default)
    {
        var correlationId = _correlationIdProvider.GetCorrelationId() ?? string.Empty;
        var exportOptions = InstitutionalReportExportOptionsResolver.Resolve(request);
        var effectiveRequest = InstitutionalReportExportOptionsResolver.WithResolvedValues(request, exportOptions);

        _logger.LogInformation(
            "Institutional report export started. ExportFormat={ExportFormat} ReportType={ReportType} CorrelationId={CorrelationId}",
            effectiveRequest.ExportFormat,
            effectiveRequest.BuildRequest.ReportType,
            correlationId);

        await using var exportScope = await _exportGuard.BeginExportAsync(request, ct);
        var exportToken = exportScope.Token;
        var stopwatch = Stopwatch.StartNew();

        try
        {
            InstitutionalReportRequestValidator.ValidateBuildRequest(effectiveRequest.BuildRequest, _reportingOptions);
            var detailLimit = _reportingOptions.ResolveDetailLimit(exportOptions.Format);
            var pdfPartLimit = _reportingOptions.ResolvePdfPartDetailLimit();

            _logger.LogDebug(
                "Institutional report export count_matching started. ExportStage=count_matching CorrelationId={CorrelationId}",
                correlationId);
            var buildStopwatch = Stopwatch.StartNew();
            var totalMatching = await _buildSupport.CountMatchingTransactionsAsync(effectiveRequest.BuildRequest, exportToken);
            buildStopwatch.Stop();
            _metrics.RecordBuildDuration(
                buildStopwatch.Elapsed.TotalMilliseconds,
                effectiveRequest.BuildRequest.ReportType.ToString());

            await exportScope.LogStartedAsync(totalMatching);

            var overflow = totalMatching > detailLimit;
            var sections = InstitutionalReportRequestValidator.ResolveSections(effectiveRequest.BuildRequest);
            var includesDetails = sections.Contains(ReportSectionId.TransactionDetails);
            var overflowAction = ResolveOverflowAction(effectiveRequest, overflow, includesDetails);

            ValidateOverflowAction(effectiveRequest, overflow, includesDetails, overflowAction, totalMatching, detailLimit);

            var assemblyOptions = ResolveAssemblyOptions(
                totalMatching,
                detailLimit,
                overflow,
                overflowAction,
                includesDetails);

            _logger.LogDebug(
                "Institutional report export build_model started. ExportStage=build_model CorrelationId={CorrelationId}",
                correlationId);
            var model = await _buildSupport.BuildInternalAsync(effectiveRequest.BuildRequest, exportToken, assemblyOptions);

            ReportExportResultDto result;
            if (overflow && overflowAction == DetailOverflowAction.FullDetailsXlsx)
            {
                var xlsxRequest = CloneExportRequest(effectiveRequest);
                xlsxRequest.ExportFormat = ExportFormat.Xlsx;
                result = await ExportDocumentAsync(
                    new ExportDocumentContext(
                        model,
                        xlsxRequest,
                        InstitutionalReportExportOptionsResolver.Resolve(xlsxRequest),
                        sections,
                        includesDetails,
                        IncludeDetailsInDocument: true,
                        overflowAction,
                        totalMatching,
                        detailLimit),
                    exportToken,
                    correlationId);
            }
            else if (overflow && overflowAction == DetailOverflowAction.SplitPdf)
            {
                result = await ExportSplitPdfZipAsync(
                    model,
                    effectiveRequest,
                    sections,
                    overflowAction,
                    pdfPartLimit,
                    exportToken,
                    correlationId);
            }
            else
            {
                var includeDetailsInDocument = !overflow || overflowAction != DetailOverflowAction.SummaryOnly;
                result = await ExportDocumentAsync(
                    new ExportDocumentContext(
                        model,
                        effectiveRequest,
                        exportOptions,
                        sections,
                        includesDetails,
                        includeDetailsInDocument,
                        overflowAction,
                        totalMatching,
                        detailLimit),
                    exportToken,
                    correlationId);
            }

            await exportScope.LogCompletedAsync(
                result.Manifest?.ExportedDetailRows ?? totalMatching,
                result.Content.LongLength,
                result.Manifest?.DetailPartsCount ?? 1,
                result.FileFingerprint);

            stopwatch.Stop();
            _logger.LogInformation(
                "Institutional report export completed. ExportFormat={ExportFormat} ReportType={ReportType} ExportStage=completed Bytes={Bytes} DurationMs={DurationMs} CorrelationId={CorrelationId}",
                effectiveRequest.ExportFormat,
                effectiveRequest.BuildRequest.ReportType,
                result.Content.LongLength,
                stopwatch.Elapsed.TotalMilliseconds,
                correlationId);

            return result;
        }
        catch (OperationCanceledException ex) when (exportToken.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            await exportScope.LogFailedAsync(ReportingErrorCodes.ExportTimeout);
            throw new ReportingExportRejectedException(
                ReportingErrorCodes.ExportTimeout,
                "انتهت مهلة تصدير التقرير.",
                StatusCodes.Status503ServiceUnavailable,
                ex);
        }
        catch (OperationCanceledException)
        {
            await exportScope.LogCancelledAsync();
            throw;
        }
        catch (Exception ex) when (
            ex is not ReportingExportRejectedException &&
            ex is not ReportingConfigurationException &&
            ex is not FieldValidationException)
        {
            await exportScope.LogFailedAsync("export_failed");
            throw new InstitutionalReportExportException(
                $"Institutional report export failed. ExportFormat={effectiveRequest.ExportFormat}; ReportType={effectiveRequest.BuildRequest.ReportType}; CorrelationId={correlationId}.",
                ex);
        }
    }

    private async Task<ReportExportResultDto> ExportDocumentAsync(
        ExportDocumentContext context,
        CancellationToken ct,
        string correlationId)
    {
        _logger.LogDebug(
            "Institutional report export render_document started. ExportFormat={ExportFormat} ExportStage=render_document CorrelationId={CorrelationId}",
            context.Options.Format,
            correlationId);

        var manifest = _renderer.RenderManifest(
            context.Model,
            context.Sections,
            includeTransactionDetails: context.IncludesDetails && context.IncludeDetailsInDocument);

        if (ShouldMeasureTransactionDetailsForPdf(context))
        {
            var preflightChunks = context.Model.Transactions
                .Select(row => (IReadOnlyList<TransactionDetailRowDto>)new[] { row })
                .ToList();
            var preflightManifest = _renderer.RenderManifestWithMeasuredTransactionPages(
                context.Model,
                context.Sections,
                preflightChunks,
                includeTransactionDetails: true);
            var preflightHtml = InstitutionalReportRenderer.RenderHtmlDocument(preflightManifest);
            var measuredChunks = await _pdfPaginationMeasurer!.MeasureTransactionDetailChunksAsync(
                preflightManifest,
                preflightHtml,
                context.Model.Transactions,
                ct);

            manifest = _renderer.RenderManifestWithMeasuredTransactionPages(
                context.Model,
                context.Sections,
                measuredChunks,
                includeTransactionDetails: true);
        }

        var selectedPages = ResolveSelectedPages(context.Request, manifest);

        if (selectedPages.Count == 0)
        {
            throw new FieldValidationException(new Dictionary<string, string>
            {
                [SelectedPagesField] = "يجب اختيار صفحة واحدة على الأقل للتصدير.",
            });
        }

        var exportManifest = _renderer.BuildExportManifest(manifest, selectedPages, context.Request);

        if (exportManifest.Pages.Count == 0)
        {
            throw new FieldValidationException(new Dictionary<string, string>
            {
                [SelectedPagesField] = "لا توجد صفحات قابلة للتصدير ضمن الاختيار الحالي.",
            });
        }

        byte[] content;
        string contentType;
        string extension;

        var renderStopwatch = Stopwatch.StartNew();
        switch (context.Options.Format)
        {
            case ExportFormat.Docx:
                content = InstitutionalReportDocxExporter.Export(context.Model, exportManifest, context.Request);
                contentType = "application/vnd.openxmlformats-officedocument.wordprocessingml.document";
                extension = "docx";
                break;
            case ExportFormat.Xlsx:
                content = InstitutionalReportXlsxExporter.Export(context.Model, exportManifest, context.Request);
                contentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
                extension = "xlsx";
                break;
            case ExportFormat.Html:
                content = System.Text.Encoding.UTF8.GetBytes(InstitutionalReportRenderer.RenderHtmlDocument(exportManifest));
                contentType = "text/html; charset=utf-8";
                extension = "html";
                break;
            default:
                var html = InstitutionalReportRenderer.RenderHtmlDocument(exportManifest);
                content = await _pdfExporter.ExportAsync(exportManifest, html, ct);
                contentType = "application/pdf";
                extension = "pdf";
                break;
        }
        renderStopwatch.Stop();
        _metrics.RecordRenderDuration(
            renderStopwatch.Elapsed.TotalMilliseconds,
            ReportingMetrics.FormatLabel(context.Options.Format),
            context.Request.BuildRequest.ReportType.ToString());
        _metrics.RecordExportFileSize(content.LongLength, ReportingMetrics.FormatLabel(context.Options.Format));
        _metrics.RecordExportRows(context.Model.ExportedDetailRows, ReportingMetrics.FormatLabel(context.Options.Format));

        var fingerprint = InstitutionalReportFingerprint.Compute(content);
        context.Model.Metadata.FileFingerprint = fingerprint;

        return new ReportExportResultDto
        {
            Content = content,
            ContentType = contentType,
            FileName = BuildFileName(context.Model.Metadata.ReportNumber, context.Request, extension, selectedPages),
            FileFingerprint = fingerprint,
            Manifest = InstitutionalReportManifestEnricher.Enrich(
                exportManifest.CloneWithoutHtml(),
                context.Model,
                !context.IncludeDetailsInDocument && context.OverflowAction == DetailOverflowAction.SummaryOnly,
                context.OverflowAction),
        };
    }

    private bool ShouldMeasureTransactionDetailsForPdf(ExportDocumentContext context) =>
        context.Options.Format == ExportFormat.Pdf
        && context.IncludesDetails
        && context.IncludeDetailsInDocument
        && context.Model.Metadata.ReportType != InstitutionalReportType.DepartmentTransactions
        && context.Model.Transactions.Count > 0
        && _pdfPaginationMeasurer is not null;

    private async Task<ReportExportResultDto> ExportSplitPdfZipAsync(
        InstitutionalReportModel model,
        ReportExportRequestDto request,
        IReadOnlyList<ReportSectionId> sections,
        DetailOverflowAction overflowAction,
        int detailLimit,
        CancellationToken ct,
        string correlationId)
    {
        _logger.LogDebug(
            "Institutional report export split_pdf started. ExportStage=split_pdf CorrelationId={CorrelationId}",
            correlationId);

        var summaryManifest = _renderer.RenderManifest(model, sections, includeTransactionDetails: false);
        var summaryHtml = InstitutionalReportRenderer.RenderHtmlDocument(summaryManifest);
        var summaryPdf = await _pdfExporter.ExportAsync(summaryManifest, summaryHtml, ct);

        var zipEntries = new Dictionary<string, byte[]>
        {
            [$"{SanitizeFileStem(model.Metadata.ReportNumber)}-summary.pdf"] = summaryPdf,
        };

        var chunks = model.Transactions.Chunk(detailLimit).Take(_reportingOptions.MaxPdfParts).ToList();
        for (var i = 0; i < chunks.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var partNumber = i + 1;
            var rowsFrom = i * detailLimit + 1;
            var rowsTo = rowsFrom + chunks[i].Length - 1;
            var partLabel = $"PART-{partNumber:D2}-OF-{chunks.Count:D2}";
            var partManifest = _renderer.RenderTransactionDetailsManifest(model, chunks[i], partLabel);
            partManifest.CurrentPartNumber = partNumber;
            partManifest.RowsFrom = rowsFrom;
            partManifest.RowsTo = rowsTo;
            var partHtml = InstitutionalReportRenderer.RenderHtmlDocument(partManifest);
            var partPdf = await _pdfExporter.ExportAsync(partManifest, partHtml, ct);
            zipEntries[$"{SanitizeFileStem(model.Metadata.ReportNumber)}-{partLabel}.pdf"] = partPdf;
        }

        var content = InstitutionalReportZipExporter.CreateArchive(zipEntries);
        var fingerprint = InstitutionalReportFingerprint.Compute(content);
        model.Metadata.FileFingerprint = fingerprint;
        _metrics.RecordPdfParts(chunks.Count, request.BuildRequest.ReportType.ToString());

        return new ReportExportResultDto
        {
            Content = content,
            ContentType = "application/zip",
            FileName = $"{SanitizeFileStem(model.Metadata.ReportNumber)}-SPLIT.zip",
            FileFingerprint = fingerprint,
            Manifest = InstitutionalReportManifestEnricher.Enrich(
                summaryManifest.CloneWithoutHtml(),
                model,
                isSummaryOnly: false,
                overflowAction),
        };
    }

    private static DetailOverflowAction ResolveOverflowAction(
        ReportExportRequestDto request,
        bool overflow,
        bool includesDetails)
    {
        if (!overflow || !includesDetails)
            return request.DetailOverflowAction ?? DetailOverflowAction.None;

        if (request.ExportFormat == ExportFormat.Xlsx)
            return DetailOverflowAction.FullDetailsXlsx;

        return request.DetailOverflowAction ?? DetailOverflowAction.None;
    }

    private static void ValidateOverflowAction(
        ReportExportRequestDto request,
        bool overflow,
        bool includesDetails,
        DetailOverflowAction overflowAction,
        int totalMatching,
        int detailLimit)
    {
        if (!overflow || !includesDetails)
            return;

        var needsEmbeddedChoice = request.ExportFormat is ExportFormat.Pdf or ExportFormat.Docx or ExportFormat.Html;
        if (!needsEmbeddedChoice)
            return;

        if (overflowAction == DetailOverflowAction.None)
        {
            throw new FieldValidationException(new Dictionary<string, string>
            {
                ["detailOverflowAction"] =
                    $"يتجاوز التقرير حد صفوف التفاصيل ({detailLimit:N0}). المطلوب: {totalMatching:N0}. اختر ملخصًا كاملًا، أو تقسيم PDF، أو تصدير XLSX.",
                ["totalMatchingTransactions"] = totalMatching.ToString(),
                ["detailRowLimit"] = detailLimit.ToString(),
            });
        }

        if (overflowAction == DetailOverflowAction.SplitPdf && request.ExportFormat != ExportFormat.Pdf)
        {
            throw new FieldValidationException(new Dictionary<string, string>
            {
                ["detailOverflowAction"] = "تقسيم التفاصيل إلى عدة ملفات PDF متاح فقط عند اختيار صيغة PDF.",
            });
        }
    }

    private static string SanitizeFileStem(string reportNumber) =>
        reportNumber.Replace('/', '-').Replace('\\', '-');

    private static ReportExportRequestDto CloneExportRequest(ReportExportRequestDto request) => new()
    {
        ReportId = request.ReportId,
        BuildRequest = request.BuildRequest,
        ExportFormat = request.ExportFormat,
        ExportMode = request.ExportMode,
        SelectedSectionIds = request.SelectedSectionIds.ToList(),
        SelectedPageNumbers = request.SelectedPageNumbers.ToList(),
        PageRangeExpression = request.PageRangeExpression,
        CurrentPageNumber = request.CurrentPageNumber,
        IncludePartialCover = request.IncludePartialCover,
        IncludePartialManifest = request.IncludePartialManifest,
        PageNumberingMode = request.PageNumberingMode,
        TemplateId = request.TemplateId,
        Reason = request.Reason,
        DetailOverflowAction = request.DetailOverflowAction,
    };

    private static ReportAssemblyOptions ResolveAssemblyOptions(
        int totalMatching,
        int detailLimit,
        bool overflow,
        DetailOverflowAction overflowAction,
        bool includesDetails)
    {
        if (!includesDetails)
            return ReportAssemblyOptions.ForFullDetailExport(totalMatching, detailLimit) with { OmitDetailRows = true, ExportedDetailRowsOverride = 0 };

        if (!overflow)
            return ReportAssemblyOptions.ForFullDetailExport(totalMatching, detailLimit);

        return overflowAction switch
        {
            DetailOverflowAction.SummaryOnly => ReportAssemblyOptions.ForSummaryOnlyExport(totalMatching, detailLimit),
            DetailOverflowAction.SplitPdf => ReportAssemblyOptions.ForSplitPdfExport(totalMatching, detailLimit),
            DetailOverflowAction.FullDetailsXlsx => ReportAssemblyOptions.ForFullDetailExport(totalMatching, detailLimit),
            _ => ReportAssemblyOptions.ForPreview(totalMatching, detailLimit),
        };
    }

    private static List<int> ResolveSelectedPages(ReportExportRequestDto request, RenderedReportManifestDto manifest)
    {
        var pages = request.ExportMode switch
        {
            ExportMode.CurrentPage when request.CurrentPageNumber.HasValue => [request.CurrentPageNumber.Value],
            ExportMode.CurrentPage => throw new FieldValidationException(new Dictionary<string, string>
            {
                [SelectedPagesField] = "يجب تحديد الصفحة الحالية للتصدير.",
            }),
            ExportMode.SelectedPages when !string.IsNullOrWhiteSpace(request.PageRangeExpression)
                => ParsePageRangeOrThrow(request.PageRangeExpression, manifest.TotalPages),
            ExportMode.SelectedPages when request.SelectedPageNumbers.Count > 0 => request.SelectedPageNumbers.Distinct().OrderBy(p => p).ToList(),
            ExportMode.SelectedPages => throw new FieldValidationException(new Dictionary<string, string>
            {
                [SelectedPagesField] = "يجب تحديد الصفحات عبر التحديد اليدوي أو نطاق الصفحات.",
            }),
            ExportMode.SelectedSections => manifest.Pages
                .Where(p => request.SelectedSectionIds.Contains(p.SectionId))
                .Select(p => p.OriginalPageNumber)
                .Distinct()
                .OrderBy(p => p)
                .ToList(),
            _ => manifest.Pages.Select(p => p.OriginalPageNumber).ToList(),
        };

        return NormalizeSelectedPages(pages, manifest.TotalPages);
    }

    private static List<int> NormalizeSelectedPages(List<int> pages, int totalPages)
    {
        var normalized = pages
            .Where(p => p >= 1 && p <= totalPages)
            .Distinct()
            .OrderBy(p => p)
            .ToList();

        if (normalized.Count == 0)
        {
            throw new FieldValidationException(new Dictionary<string, string>
            {
                [SelectedPagesField] = "لا توجد صفحات صالحة ضمن الاختيار الحالي.",
            });
        }

        return normalized;
    }

    private static List<int> ParsePageRangeOrThrow(string expression, int totalPages)
    {
        var parsed = ReportPageRangeParser.Parse(expression, totalPages);
        if (!parsed.IsValid)
        {
            throw new FieldValidationException(new Dictionary<string, string>
            {
                ["pageRangeExpression"] = parsed.ErrorMessage ?? "صيغة الصفحات غير صالحة.",
            });
        }

        if (parsed.PageNumbers.Count == 0)
        {
            throw new FieldValidationException(new Dictionary<string, string>
            {
                ["pageRangeExpression"] = "لم يتم العثور على صفحات صالحة ضمن التعبير المحدد.",
            });
        }

        return parsed.PageNumbers;
    }

    private static string BuildFileName(string reportNumber, ReportExportRequestDto request, string extension, List<int> pages)
    {
        var baseName = SanitizeFileStem(reportNumber);
        return request.ExportMode switch
        {
            ExportMode.SelectedSections => $"{baseName}-SECTIONS.{extension}",
            ExportMode.SelectedPages or ExportMode.CurrentPage => pages.Count <= 6
                ? $"{baseName}-PAGES-{string.Join('-', pages)}.{extension}"
                : $"{baseName}-PAGES-{Guid.NewGuid().ToString("N")[..8]}.{extension}",
            _ => $"{baseName}-FULL.{extension}",
        };
    }
}
