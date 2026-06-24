using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Diagnostics;
using System.Globalization;
using Uqeb.Api.Data;
using Uqeb.Api.DTOs.Reports;
using Uqeb.Api.Helpers;
using Uqeb.Api.Models.Enums;
using Uqeb.Api.Reporting.Exporters;
using Uqeb.Api.Reporting.Configuration;
using Uqeb.Api.Reporting.Helpers;
using Uqeb.Api.Reporting.DTOs;
using Uqeb.Api.Reporting.Enums;
using Uqeb.Api.Reporting.Models;
using Uqeb.Api.Reporting.Operations;
using Uqeb.Api.Reporting.Rendering;
using Uqeb.Api.Models.Entities;
using Uqeb.Api.Services;

namespace Uqeb.Api.Reporting.Services;

public interface IInstitutionalReportService
{
    Task<InstitutionalReportModel> BuildReportModelAsync(ReportBuildRequestDto request, CancellationToken ct = default);
    Task<RenderedReportManifestDto> RenderPreviewAsync(ReportBuildRequestDto request, CancellationToken ct = default);
    Task<ReportExportResultDto> ExportAsync(ReportExportRequestDto request, CancellationToken ct = default);
    Task<List<ReportTemplateDto>> GetTemplatesAsync(CancellationToken ct = default);
    Task<ReportTemplateDto> SaveTemplateAsync(SaveReportTemplateRequestDto request, CancellationToken ct = default);
    Task DeleteTemplateAsync(int id, CancellationToken ct = default);
}

public sealed class InstitutionalReportService : IInstitutionalReportService
{
    private const string SelectedPagesField = "selectedPages";
    private const string IsoDateFormat = "yyyy-MM-dd";

    private readonly IDbContextFactory<AppDbContext> _dbFactory;
    private readonly ICurrentUserService _currentUser;
    private readonly IInstitutionalReportNumberAllocator _reportNumberAllocator;
    private readonly ReportingOptions _reportingOptions;
    private readonly DepartmentRatingCriteria _ratingCriteria = new();
    private readonly InstitutionalReportRenderer _renderer = new();
    private readonly IInstitutionalReportPdfExporter _pdfExporter;
    private readonly IReportingExportGuard _exportGuard;
    private readonly IReportingMetrics _metrics;
    private readonly ILogger<InstitutionalReportService> _logger;
    private readonly IReportingCorrelationIdProvider _correlationIdProvider;

    public InstitutionalReportService(
        IDbContextFactory<AppDbContext> dbFactory,
        ICurrentUserService currentUser,
        IInstitutionalReportNumberAllocator reportNumberAllocator,
        IInstitutionalReportPdfExporter pdfExporter,
        IOptions<ReportingOptions> reportingOptions,
        IReportingExportGuard exportGuard,
        IReportingMetrics metrics,
        ILogger<InstitutionalReportService> logger,
        IReportingCorrelationIdProvider correlationIdProvider)
    {
        _dbFactory = dbFactory;
        _currentUser = currentUser;
        _reportNumberAllocator = reportNumberAllocator;
        _pdfExporter = pdfExporter;
        _reportingOptions = reportingOptions.Value;
        _exportGuard = exportGuard;
        _metrics = metrics;
        _logger = logger;
        _correlationIdProvider = correlationIdProvider;
    }

    public Task<InstitutionalReportModel> BuildReportModelAsync(ReportBuildRequestDto request, CancellationToken ct = default) =>
        BuildForApiAsync(request, ct);

    private async Task<InstitutionalReportModel> BuildForApiAsync(ReportBuildRequestDto request, CancellationToken ct)
    {
        var detailLimit = _reportingOptions.MaxPreviewDetailRows;
        var totalMatching = await CountMatchingTransactionsAsync(request, ct);
        return await BuildInternalAsync(request, ct, ReportAssemblyOptions.ForPreview(totalMatching, detailLimit));
    }

    public async Task<RenderedReportManifestDto> RenderPreviewAsync(ReportBuildRequestDto request, CancellationToken ct = default)
    {
        ValidateBuildRequest(request);
        var correlationId = _correlationIdProvider.GetCorrelationId();
        var stopwatch = Stopwatch.StartNew();

        _logger.LogInformation(
            "Institutional report preview build started. ReportType={ReportType} CorrelationId={CorrelationId}",
            request.ReportType,
            correlationId);

        var detailLimit = _reportingOptions.MaxPreviewDetailRows;

        _logger.LogDebug(
            "Institutional report preview count_matching_started. CorrelationId={CorrelationId}",
            correlationId);
        var totalMatching = await CountMatchingTransactionsAsync(request, ct);
        stopwatch.Stop();
        _logger.LogInformation(
            "Institutional report preview count_matching_completed. MatchedRows={MatchedRows} DurationMs={DurationMs} CorrelationId={CorrelationId}",
            totalMatching,
            stopwatch.Elapsed.TotalMilliseconds,
            correlationId);

        stopwatch.Restart();
        _logger.LogDebug(
            "Institutional report preview build_model_started. CorrelationId={CorrelationId}",
            correlationId);
        var model = await BuildInternalAsync(
            request,
            ct,
            ReportAssemblyOptions.ForPreview(totalMatching, detailLimit));
        stopwatch.Stop();
        _logger.LogInformation(
            "Institutional report preview build_model_completed. DurationMs={DurationMs} CorrelationId={CorrelationId}",
            stopwatch.Elapsed.TotalMilliseconds,
            correlationId);

        var sections = ResolveSections(request);
        _logger.LogDebug(
            "Institutional report preview resolve_sections_completed. SectionCount={SectionCount} CorrelationId={CorrelationId}",
            sections.Count,
            correlationId);

        stopwatch.Restart();
        _logger.LogDebug(
            "Institutional report preview render_manifest_started. CorrelationId={CorrelationId}",
            correlationId);
        var manifest = _renderer.RenderManifest(model, sections);
        stopwatch.Stop();
        _logger.LogInformation(
            "Institutional report preview render_manifest_completed. PageCount={PageCount} DurationMs={DurationMs} CorrelationId={CorrelationId}",
            manifest.Pages.Count,
            stopwatch.Elapsed.TotalMilliseconds,
            correlationId);

        var enriched = EnrichManifest(manifest, model, isSummaryOnly: false, overflowAction: null);
        _logger.LogInformation(
            "Institutional report preview enrich_manifest_completed. CorrelationId={CorrelationId}",
            correlationId);

        return enriched;
    }

    public async Task<ReportExportResultDto> ExportAsync(ReportExportRequestDto request, CancellationToken ct = default)
    {
        await using var exportScope = await _exportGuard.BeginExportAsync(request, ct);
        var exportToken = exportScope.Token;

        try
        {
            var exportOptions = InstitutionalReportExportOptionsResolver.Resolve(request);
            var effectiveRequest = InstitutionalReportExportOptionsResolver.WithResolvedValues(request, exportOptions);
            var detailLimit = _reportingOptions.ResolveDetailLimit(exportOptions.Format);
            var pdfPartLimit = _reportingOptions.ResolvePdfPartDetailLimit();

            var buildStopwatch = Stopwatch.StartNew();
            var totalMatching = await CountMatchingTransactionsAsync(effectiveRequest.BuildRequest, exportToken);
            buildStopwatch.Stop();
            _metrics.RecordBuildDuration(
                buildStopwatch.Elapsed.TotalMilliseconds,
                effectiveRequest.BuildRequest.ReportType.ToString());

            await exportScope.LogStartedAsync(totalMatching);

            var overflow = totalMatching > detailLimit;
            var sections = ResolveSections(effectiveRequest.BuildRequest);
            var includesDetails = sections.Contains(ReportSectionId.TransactionDetails);
            var overflowAction = ResolveOverflowAction(effectiveRequest, overflow, includesDetails);

            ValidateOverflowAction(effectiveRequest, overflow, includesDetails, overflowAction, totalMatching, detailLimit);

            var assemblyOptions = ResolveAssemblyOptions(
                totalMatching,
                detailLimit,
                overflow,
                overflowAction,
                includesDetails);

            var model = await BuildInternalAsync(effectiveRequest.BuildRequest, exportToken, assemblyOptions);

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
                    exportToken);
            }
            else if (overflow && overflowAction == DetailOverflowAction.SplitPdf)
            {
                result = await ExportSplitPdfZipAsync(
                    model,
                    effectiveRequest,
                    sections,
                    overflowAction,
                    pdfPartLimit,
                    exportToken);
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
                    exportToken);
            }

            await exportScope.LogCompletedAsync(
                result.Manifest?.ExportedDetailRows ?? totalMatching,
                result.Content.LongLength,
                result.Manifest?.DetailPartsCount ?? 1,
                result.FileFingerprint);
            return result;
        }
        catch (OperationCanceledException) when (exportToken.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            await exportScope.LogFailedAsync(ReportingErrorCodes.ExportTimeout);
            throw new ReportingExportRejectedException(
                ReportingErrorCodes.ExportTimeout,
                "انتهت مهلة تصدير التقرير.",
                StatusCodes.Status503ServiceUnavailable);
        }
        catch (OperationCanceledException)
        {
            await exportScope.LogCancelledAsync();
            throw;
        }
        catch (ReportingExportRejectedException)
        {
            throw;
        }
        catch (Exception)
        {
            await exportScope.LogFailedAsync("export_failed");
            throw;
        }
    }

    private async Task<ReportExportResultDto> ExportDocumentAsync(
        ExportDocumentContext context,
        CancellationToken ct)
    {
        var manifest = _renderer.RenderManifest(
            context.Model,
            context.Sections,
            includeTransactionDetails: context.IncludesDetails && context.IncludeDetailsInDocument);
        var selectedPages = ResolveSelectedPages(context.Request, manifest);

        if (selectedPages.Count == 0)
        {
            throw new FieldValidationException(new Dictionary<string, string>
            {
                [SelectedPagesField] = "يجب اختيار صفحة واحدة على الأقل للتصدير."
            });
        }

        var exportManifest = _renderer.BuildExportManifest(manifest, selectedPages, context.Request);

        if (exportManifest.Pages.Count == 0)
        {
            throw new FieldValidationException(new Dictionary<string, string>
            {
                [SelectedPagesField] = "لا توجد صفحات قابلة للتصدير ضمن الاختيار الحالي."
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
            Manifest = EnrichManifest(exportManifest.CloneWithoutHtml(), context.Model, !context.IncludeDetailsInDocument && context.OverflowAction == DetailOverflowAction.SummaryOnly, context.OverflowAction)
        };
    }

    private async Task<ReportExportResultDto> ExportSplitPdfZipAsync(
        InstitutionalReportModel model,
        ReportExportRequestDto request,
        IReadOnlyList<ReportSectionId> sections,
        DetailOverflowAction overflowAction,
        int detailLimit,
        CancellationToken ct)
    {
        var summaryManifest = _renderer.RenderManifest(model, sections, includeTransactionDetails: false);
        var summaryHtml = InstitutionalReportRenderer.RenderHtmlDocument(summaryManifest);
        var summaryPdf = await _pdfExporter.ExportAsync(summaryManifest, summaryHtml, ct);

        var zipEntries = new Dictionary<string, byte[]>
        {
            [$"{SanitizeFileStem(model.Metadata.ReportNumber)}-summary.pdf"] = summaryPdf
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
            Manifest = EnrichManifest(summaryManifest.CloneWithoutHtml(), model, isSummaryOnly: false, overflowAction)
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
                ["detailOverflowAction"] = "تقسيم التفاصيل إلى عدة ملفات PDF متاح فقط عند اختيار صيغة PDF."
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

    public async Task<List<ReportTemplateDto>> GetTemplatesAsync(CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var templates = await db.ReportExportTemplates.AsNoTracking()
            .OrderBy(t => t.Name)
            .ToListAsync(ct);

        return templates.Select(MapTemplate).ToList();
    }

    public async Task<ReportTemplateDto> SaveTemplateAsync(SaveReportTemplateRequestDto request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            throw new FieldValidationException(new Dictionary<string, string>
            {
                ["name"] = "اسم القالب مطلوب."
            });
        }

        if (request.ReportType is null || !Enum.IsDefined(request.ReportType.Value))
        {
            throw new FieldValidationException(new Dictionary<string, string>
            {
                ["reportType"] = "نوع التقرير غير صالح."
            });
        }

        if (request.DefaultFormat.HasValue && !Enum.IsDefined(request.DefaultFormat.Value))
        {
            throw new FieldValidationException(new Dictionary<string, string>
            {
                ["defaultFormat"] = "صيغة التصدير الافتراضية غير صالحة."
            });
        }

        var templateOptions = InstitutionalReportExportOptionsResolver.Resolve(request);

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var userExists = await db.Users.AsNoTracking().AnyAsync(u => u.Id == _currentUser.UserId, ct);
        if (!userExists)
        {
            throw new FieldValidationException(new Dictionary<string, string>
            {
                ["createdById"] = "المستخدم الحالي غير موجود."
            });
        }

        var entity = new ReportExportTemplate
        {
            Name = request.Name.Trim(),
            ReportType = templateOptions.ReportType,
            SectionIdsJson = System.Text.Json.JsonSerializer.Serialize(request.SectionIds),
            DefaultFiltersJson = System.Text.Json.JsonSerializer.Serialize(request.DefaultFilters),
            DefaultFormat = templateOptions.DefaultFormat,
            PageNumberingMode = templateOptions.PageNumberingMode,
            IncludePartialCover = templateOptions.IncludePartialCover,
            IncludePartialManifest = templateOptions.IncludePartialManifest,
            CreatedById = _currentUser.UserId,
            CreatedAt = DateTime.UtcNow
        };
        db.ReportExportTemplates.Add(entity);
        await db.SaveChangesAsync(ct);
        return MapTemplate(entity);
    }

    public async Task DeleteTemplateAsync(int id, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var entity = await db.ReportExportTemplates.FindAsync([id], ct)
            ?? throw new KeyNotFoundException("القالب غير موجود.");
        db.ReportExportTemplates.Remove(entity);
        await db.SaveChangesAsync(ct);
    }

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

    private async Task<InstitutionalReportModel> BuildInternalAsync(
        ReportBuildRequestDto request,
        CancellationToken ct,
        ReportAssemblyOptions options)
    {
        var detailLimit = options.DetailRowLimit > 0 ? options.DetailRowLimit : _reportingOptions.MaxPreviewDetailRows;
        ReportingOptions.ValidateDetailLimit(detailLimit);
        var totalMatched = options.TotalMatchedOverride ?? await CountMatchingTransactionsAsync(request, ct);

        var metricSnapshots = await LoadSnapshotsAsync(request, ct, takeLimit: null);
        var today = DateTime.UtcNow.Date;
        var metrics = InstitutionalReportMetricsCalculator.Calculate(metricSnapshots, today);

        List<TransactionReportSnapshot> detailSnapshots;
        int exportedDetailRows;

        if (options.OmitDetailRows)
        {
            detailSnapshots = [];
            exportedDetailRows = options.ExportedDetailRowsOverride ?? 0;
        }
        else if (options.DetailRowsToLoad.HasValue)
        {
            exportedDetailRows = options.ExportedDetailRowsOverride ?? Math.Min(totalMatched, options.DetailRowsToLoad.Value);
            detailSnapshots = exportedDetailRows >= metricSnapshots.Count
                ? metricSnapshots
                : await LoadSnapshotsAsync(request, ct, takeLimit: exportedDetailRows);
        }
        else
        {
            exportedDetailRows = options.ExportedDetailRowsOverride ?? totalMatched;
            detailSnapshots = metricSnapshots;
        }

        var detailRowsTruncated = options.DetailRowsTruncated || totalMatched > exportedDetailRows || options.DetailPartsCount > 1;
        var reportNumber = await _reportNumberAllocator.AllocateAsync(ct);
        var verificationId = Guid.NewGuid().ToString("N")[..12].ToUpperInvariant();

        var model = new InstitutionalReportModel
        {
            TotalMatchedRows = totalMatched,
            ExportedDetailRows = exportedDetailRows,
            DetailRowsTruncated = detailRowsTruncated,
            DetailPartsCount = options.DetailPartsCount,
            Metadata = new ReportMetadataDto
            {
                ReportNumber = reportNumber,
                ReportType = request.ReportType,
                ReportTypeName = ReportTypeLabel(request.ReportType),
                IssueDate = today,
                PeriodFrom = ResolvePeriodStart(request.Filters.DateFrom, metricSnapshots, s => s.IncomingDate, today),
                PeriodTo = ResolvePeriodEnd(request.Filters.DateTo, metricSnapshots, s => s.IncomingDate, today),
                Title = string.IsNullOrWhiteSpace(request.Title)
                    ? "تقرير المتابعة الإجرائية للمعاملات"
                    : request.Title.Trim(),
                Introduction = request.Introduction,
                VerificationId = verificationId,
                GeneratedAt = DateTime.UtcNow,
                TotalMatchingTransactions = totalMatched,
                IncludedTransactionCount = detailSnapshots.Count,
                DetailRowLimit = detailLimit,
            },
            Filters = request.Filters,
            Summary = BuildExecutiveSummary(metrics, metricSnapshots),
            Charts = BuildCharts(metrics, metricSnapshots),
            DepartmentPerformance = BuildDepartmentPerformance(metrics.Snapshots, today),
            Risks = BuildRisks(metrics.Snapshots),
            Recommendations = BuildRecommendations(metrics.Snapshots, today),
            RiskCounters = BuildRiskCounters(metrics.Snapshots, today),
            Transactions = BuildTransactionDetails(detailSnapshots),
            IntegrityWarnings = ValidateIntegrity(metrics)
        };

        if (detailRowsTruncated && totalMatched > detailSnapshots.Count)
        {
            model.IntegrityWarnings.Add(new IntegrityWarningDto
            {
                Code = "DETAIL_ROWS_TRUNCATED",
                Message = $"جدول التفاصيل يعرض {exportedDetailRows:N0} صفًا من إجمالي {totalMatched:N0} معاملة مطابقة للفلاتر.",
                Severity = "warning"
            });
        }

        return model;
    }

    private async Task<int> CountMatchingTransactionsAsync(ReportBuildRequestDto request, CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var query = InstitutionalReportQueryBuilder.BuildFilteredQuery(
            db,
            request,
            _currentUser.UserId,
            _currentUser.Role,
            _currentUser.DepartmentId);
        return await query.CountAsync(ct);
    }

    private async Task<List<TransactionReportSnapshot>> LoadSnapshotsAsync(
        ReportBuildRequestDto request,
        CancellationToken ct,
        int? takeLimit = null)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var query = InstitutionalReportQueryBuilder.BuildFilteredQuery(
            db,
            request,
            _currentUser.UserId,
            _currentUser.Role,
            _currentUser.DepartmentId);

        query = query
            .OrderByDescending(t => t.IncomingDate)
            .ThenByDescending(t => t.Id);

        if (takeLimit.HasValue)
            query = query.Take(takeLimit.Value);

        var rows = await query.Select(t => new InstitutionalReportSnapshotQuery.SnapshotRow
            {
                Id = t.Id,
                InternalTrackingNumber = t.InternalTrackingNumber,
                IncomingNumber = t.IncomingNumber,
                IncomingDate = t.IncomingDate,
                Subject = t.Subject,
                IncomingSourceType = t.IncomingSourceType,
                IncomingFromRaw = t.IncomingFrom,
                IncomingDepartmentName = t.IncomingFromDepartment != null ? t.IncomingFromDepartment.Name : null,
                IncomingPartyName = t.IncomingFromParty != null ? t.IncomingFromParty.Name : null,
                CategoryEntityName = t.CategoryEntity != null ? t.CategoryEntity.Name : null,
                CategoryRaw = t.Category,
                Priority = t.Priority,
                Status = t.Status,
                RequiresResponse = t.RequiresResponse,
                ResponseCompleted = t.ResponseCompleted,
                ResponseDueDate = t.ResponseDueDate,
                ClosedAt = t.ClosedAt,
                UpdatedAt = t.UpdatedAt,
                CreatedAt = t.CreatedAt,
                OutgoingNumber = t.OutgoingNumber,
                OutgoingDate = t.OutgoingDate,
                Assignments = t.Assignments.Select(a => new InstitutionalReportSnapshotQuery.AssignmentRow
                {
                    DepartmentId = a.DepartmentId,
                    DepartmentName = a.Department != null ? a.Department.Name : string.Empty,
                    RequiresReply = a.RequiresReply,
                    ReplyStatus = a.ReplyStatus,
                    Status = a.Status,
                    DueDate = a.DueDate
                }).ToList(),
                OutgoingDepartments = t.OutgoingDepartments.Select(o => new InstitutionalReportSnapshotQuery.DepartmentRow
                {
                    DepartmentId = o.DepartmentId,
                    DepartmentName = o.Department.Name
                }).ToList(),
                LastFollowUpDate = t.FollowUps.Any()
                    ? t.FollowUps.Max(f => f.CreatedAt > f.FollowUpDate ? f.CreatedAt : f.FollowUpDate)
                    : (DateTime?)null
            })
            .ToListAsync(ct);

        var today = DateTime.UtcNow.Date;
        return rows.Select(row => InstitutionalReportSnapshotQuery.MapRowToSnapshot(row, today)).ToList();
    }

    private static ExecutiveSummaryDto BuildExecutiveSummary(
        InstitutionalMetricsResult metrics,
        IReadOnlyList<TransactionReportSnapshot> snapshots)
    {
        var cards = new List<KpiCardDto>
        {
            new() { Key = "total", Title = "إجمالي المعاملات", Value = metrics.TotalTransactions.ToString("N0") },
            new() { Key = "closed", Title = "المعاملات المغلقة", Value = metrics.ClosedCount.ToString("N0") },
            new() { Key = "open", Title = "المعاملات المفتوحة", Value = metrics.OpenCount.ToString("N0") },
            new() { Key = "overdue", Title = "المعاملات المتأخرة", Value = metrics.OverdueCount.ToString("N0") },
            new() { Key = "joint", Title = "معاملات الإدارات المشتركة", Value = metrics.JointDepartmentCount.ToString("N0") },
            new() { Key = "partial", Title = "الردود الجزئية", Value = metrics.PartialResponseCount.ToString("N0") },
            new() { Key = "avgDays", Title = "متوسط مدة الإنجاز", Value = $"{metrics.AverageCompletionDays:N1} يوم" },
            new() { Key = "onTime", Title = "نسبة الإنجاز ضمن المهلة", Value = $"{metrics.OnTimeCompletionRate:N1}%" }
        };

        var topOverdueDept = snapshots.Where(s => s.IsOpen && s.IsOverdue)
            .GroupBy(s => s.ResponsibleDepartment)
            .OrderByDescending(g => g.Count())
            .FirstOrDefault()?.Key;

        var narrative = $"خلال الفترة المحددة، بلغ إجمالي المعاملات الفريدة {metrics.TotalTransactions:N0} معاملة، " +
                        $"منها {metrics.ClosedCount:N0} مغلقة و{metrics.OpenCount:N0} مفتوحة. " +
                        $"تضم المعاملات المفتوحة {metrics.OverdueCount:N0} معاملة متأخرة و{metrics.PartialResponseCount:N0} ردًا جزئيًا " +
                        $"و{metrics.JointDepartmentCount:N0} معاملة مشتركة بين الإدارات. " +
                        $"بلغ متوسط مدة الإنجاز للمعاملات المغلقة {metrics.AverageCompletionDays:N1} يومًا، " +
                        $"ونسبة الإنجاز ضمن المهلة {metrics.OnTimeCompletionRate:N1}%." +
                        (topOverdueDept != null ? $" أعلى تركز للتأخير في إدارة «{topOverdueDept}»." : string.Empty);

        return new ExecutiveSummaryDto { KpiCards = cards, ExecutiveNarrative = narrative };
    }

    private static List<ChartDto> BuildCharts(InstitutionalMetricsResult metrics, IReadOnlyList<TransactionReportSnapshot> snapshots)
    {
        var monthly = snapshots
            .GroupBy(s => new DateTime(s.IncomingDate.Year, s.IncomingDate.Month, 1, 0, 0, 0, DateTimeKind.Utc))
            .OrderBy(g => g.Key)
            .Select(g => new ChartSeriesPointDto { Label = g.Key.ToString("yyyy-MM"), Value = g.Count() })
            .ToList();

        var openClosed = new List<ChartSeriesPointDto>
        {
            new() { Label = "مفتوحة", Value = metrics.OpenCount },
            new() { Label = "مغلقة", Value = metrics.ClosedCount }
        };

        var stageGroups = snapshots.Where(s => s.IsOpen)
            .SelectMany(s => s.FollowUpStages)
            .GroupBy(s => s)
            .Select(g => new ChartSeriesPointDto
            {
                Label = InstitutionalReportMetricsCalculator.FollowUpStageLabel(g.Key),
                Value = g.Count()
            }).ToList();

        var priorityGroups = snapshots
            .GroupBy(s => s.Priority)
            .Select(g => new ChartSeriesPointDto { Label = PriorityLabel(g.Key), Value = g.Count() })
            .ToList();

        var topOverdueDepts = snapshots.Where(s => s.IsOpen && s.IsOverdue)
            .GroupBy(s => s.ResponsibleDepartment)
            .OrderByDescending(g => g.Count())
            .Take(8)
            .Select(g => new ChartSeriesPointDto { Label = g.Key, Value = g.Count() })
            .ToList();

        var avgByDept = snapshots.Where(s => s.IsClosed && s.ClosedAt.HasValue)
            .GroupBy(s => s.ResponsibleDepartment)
            .Select(g => new ChartSeriesPointDto
            {
                Label = g.Key,
                Value = (decimal)Math.Round(g.Average(x => (x.ClosedAt!.Value - x.IncomingDate).TotalDays), 1)
            })
            .OrderByDescending(x => x.Value)
            .Take(8)
            .ToList();

        return
        [
            new ChartDto { Key = "trend", Title = "الاتجاه العام للمعاملات خلال الفترة", ChartType = "line", Series = monthly },
            new ChartDto { Key = "openClosed", Title = "المعاملات المفتوحة مقابل المغلقة", ChartType = "bar", Series = openClosed },
            new ChartDto
            {
                Key = "stages",
                Title = "توزيع المعاملات المفتوحة حسب مرحلة المتابعة",
                ChartType = "donut",
                Series = stageGroups,
                Footnote = "قد تظهر المعاملة في أكثر من تصنيف متابعة فرعي."
            },
            new ChartDto { Key = "priority", Title = "توزيع المعاملات حسب الأولوية", ChartType = "donut", Series = priorityGroups },
            new ChartDto { Key = "topOverdueDepts", Title = "أعلى الإدارات في عدد المعاملات المتأخرة", ChartType = "bar", Series = topOverdueDepts },
            new ChartDto { Key = "avgByDept", Title = "متوسط مدة الإنجاز حسب الإدارة", ChartType = "bar", Series = avgByDept }
        ];
    }

    private List<DepartmentPerformanceRowDto> BuildDepartmentPerformance(IReadOnlyList<TransactionReportSnapshot> snapshots, DateTime today)
    {
        var staleThreshold = today.AddDays(-_ratingCriteria.CriticalStaleUpdateDaysThreshold);
        var deptMap = new Dictionary<int, (string Name, List<TransactionReportSnapshot> Items)>();

        foreach (var snapshot in snapshots)
        {
            var pairs = snapshot.AssignmentDepartmentIds.Count > 0
                ? snapshot.AssignmentDepartmentIds.Zip(snapshot.AssignmentDepartmentNames).ToList()
                : [(0, snapshot.ResponsibleDepartment)];

            foreach (var (deptId, deptName) in pairs)
            {
                if (!deptMap.TryGetValue(deptId, out var bucket))
                    bucket = (deptName, []);

                bucket.Items.Add(snapshot);
                deptMap[deptId] = bucket;
            }
        }

        return deptMap
            .Where(kv => kv.Key > 0 || kv.Value.Items.Count > 0)
            .Select(kv =>
            {
                var unique = kv.Value.Items.GroupBy(s => s.TransactionId).Select(g => g.First()).ToList();
                var closed = unique.Where(s => s.IsClosed).ToList();
                var open = unique.Where(s => s.IsOpen).ToList();
                var measurable = closed.Where(s => s.ResponseDueDate.HasValue && s.ClosedAt.HasValue).ToList();
                var onTime = measurable.Count(s => s.ClosedAt!.Value <= s.ResponseDueDate!.Value);
                var onTimeRate = measurable.Count == 0 ? 0 : Math.Round(onTime * 100.0 / measurable.Count, 1);
                var ratingMetrics = new InstitutionalReportMetricsCalculator.DepartmentPerformanceMetrics
                {
                    OnTimeCompletionRate = onTimeRate,
                    OverdueCount = open.Count(s => s.IsOverdue),
                    OldestOpenDays = open.Count == 0 ? 0 : open.Max(s => s.ElapsedDays),
                    PartialResponses = open.Count(s => s.IsPartialReply),
                    StaleUpdates = open.Count(s => (s.UpdatedAt ?? s.CreatedAt) < staleThreshold)
                };
                var rating = InstitutionalReportMetricsCalculator.RateDepartment(ratingMetrics, _ratingCriteria);
                return new DepartmentPerformanceRowDto
                {
                    DepartmentId = kv.Key,
                    DepartmentName = kv.Value.Name,
                    TotalTransactions = unique.Count,
                    ClosedCount = closed.Count,
                    OpenCount = open.Count,
                    WaitingForStatementCount = open.Count(s => s.IsWaitingForStatement),
                    OverdueCount = open.Count(s => s.IsOverdue),
                    JointDepartmentCount = unique.Count(s => s.IsJointDepartment),
                    AverageCompletionDays = closed.Count == 0 ? 0 :
                        Math.Round(closed.Average(s => (s.ClosedAt!.Value - s.IncomingDate).TotalDays), 1),
                    OnTimeCompletionRate = onTimeRate,
                    Rating = rating,
                    RatingLabel = InstitutionalReportMetricsCalculator.RatingLabel(rating)
                };
            })
            .OrderByDescending(r => r.TotalTransactions)
            .ToList();
    }

    private static List<RiskAlertRowDto> BuildRisks(IReadOnlyList<TransactionReportSnapshot> snapshots)
    {
        var seq = 1;
        var risks = new List<RiskAlertRowDto>();
        foreach (var s in snapshots.Where(s => s.IsOpen).OrderByDescending(s => s.ElapsedDays).Take(25))
        {
            if (s.IsOverdue)
                risks.Add(new RiskAlertRowDto
                {
                    Sequence = seq++,
                    Alert = $"معاملة متأخرة: {s.Subject}",
                    DepartmentName = s.ResponsibleDepartment,
                    Severity = RiskSeverity.High,
                    SeverityLabel = "مرتفع",
                    ElapsedDays = s.ElapsedDays,
                    SuggestedAction = "متابعة فورية وتحديد مسؤول الإنجاز"
                });
            if (s.IsPartialReply)
                risks.Add(new RiskAlertRowDto
                {
                    Sequence = seq++,
                    Alert = $"رد جزئي متوقف: {s.Subject}",
                    DepartmentName = s.ResponsibleDepartment,
                    Severity = RiskSeverity.Elevated,
                    SeverityLabel = "يحتاج متابعة",
                    ElapsedDays = s.ElapsedDays,
                    SuggestedAction = "استكمال ردود الإدارات المتبقية"
                });
        }
        return risks.Take(20).Select((r, i) => { r.Sequence = i + 1; return r; }).ToList();
    }

    private static List<RecommendationRowDto> BuildRecommendations(IReadOnlyList<TransactionReportSnapshot> snapshots, DateTime today)
    {
        var recs = new List<RecommendationRowDto>();
        var overdueDept = snapshots.Where(s => s.IsOverdue)
            .GroupBy(s => s.ResponsibleDepartment)
            .OrderByDescending(g => g.Count())
            .FirstOrDefault();
        if (overdueDept != null)
        {
            recs.Add(new RecommendationRowDto
            {
                Observation = $"تراكم {overdueDept.Count()} معاملة متأخرة في {overdueDept.Key}",
                RequiredAction = "عقد اجتماع متابعة أسبوعي وتحديد خطة إغلاق",
                ResponsibleDepartment = overdueDept.Key,
                Priority = "عالية",
                TargetDate = today.AddDays(7).ToString(IsoDateFormat, CultureInfo.InvariantCulture),
                Source = RecommendationSource.Automated,
                SourceLabel = "مولّد آليًا"
            });
        }
        return recs;
    }

    private static RiskSummaryCountersDto BuildRiskCounters(IReadOnlyList<TransactionReportSnapshot> snapshots, DateTime today)
    {
        var staleThreshold = today.AddDays(-14);
        return new RiskSummaryCountersDto
        {
            DepartmentsNeedingFollowUp = snapshots.Where(s => s.IsOpen && s.IsOverdue).Select(s => s.ResponsibleDepartment).Distinct().Count(),
            OpenJointDepartmentTransactions = snapshots.Count(s => s.IsOpen && s.IsJointDepartment),
            PartialResponses = snapshots.Count(s => s.IsOpen && s.IsPartialReply),
            TransactionsWithoutRecentUpdate = snapshots.Count(s => s.IsOpen && (s.UpdatedAt ?? s.CreatedAt) < staleThreshold),
            DataIntegrityIssues = 0
        };
    }

    private static List<TransactionDetailRowDto> BuildTransactionDetails(IReadOnlyList<TransactionReportSnapshot> snapshots) =>
        snapshots.Select((s, index) => new TransactionDetailRowDto
        {
            Sequence = index + 1,
            TransactionId = s.TransactionId,
            TrackingNumber = s.TrackingNumber,
            IncomingNumber = s.IncomingNumber,
            IncomingDate = s.IncomingDate,
            Subject = s.Subject,
            IncomingParty = s.IncomingParty,
            ResponsibleDepartment = s.ResponsibleDepartment,
            JointDepartments = string.Join("، ", s.AssignmentDepartmentNames.Concat(s.OutgoingDepartmentNames).Distinct()),
            Priority = PriorityLabel(s.Priority),
            Status = StatusLabel(s.Status),
            FollowUpStage = string.Join("، ", s.FollowUpStages.Select(InstitutionalReportMetricsCalculator.FollowUpStageLabel)),
            ElapsedDays = s.ElapsedDays,
            DueDate = s.ResponseDueDate?.ToString(IsoDateFormat, CultureInfo.InvariantCulture),
            LastActionDate = (s.UpdatedAt ?? s.LastFollowUpDate)?.ToString(IsoDateFormat, CultureInfo.InvariantCulture),
            ResponseState = ResolveResponseState(s),
            OutgoingNumber = s.OutgoingNumber,
            OutgoingDate = s.OutgoingDate?.ToString(IsoDateFormat, CultureInfo.InvariantCulture)
        }).ToList();

    private static string ResolveResponseState(TransactionReportSnapshot snapshot)
    {
        if (snapshot.ResponseCompleted)
            return "مكتمل";

        if (snapshot.RequiresResponse)
            return "بانتظار";

        return "—";
    }

    private static List<IntegrityWarningDto> ValidateIntegrity(InstitutionalMetricsResult metrics)
    {
        var warnings = new List<IntegrityWarningDto>();
        var bucketTotal = metrics.OpenCount + metrics.ClosedCount + metrics.CancelledCount + metrics.ArchivedCount;
        if (bucketTotal != metrics.TotalTransactions)
        {
            warnings.Add(new IntegrityWarningDto
            {
                Code = "TOTAL_MISMATCH",
                Message = "مجموع المفتوحة والمغلقة والملغاة والمؤرشفة لا يساوي إجمالي المعاملات الفريدة.",
                Severity = "critical"
            });
        }
        if (metrics.OverdueCount > metrics.OpenCount)
        {
            warnings.Add(new IntegrityWarningDto
            {
                Code = "OVERDUE_OUTSIDE_OPEN",
                Message = "عدد المتأخرة يتجاوز عدد المفتوحة.",
                Severity = "critical"
            });
        }
        return warnings;
    }

    private static void ValidateBuildRequest(ReportBuildRequestDto request)
    {
        if (request.SectionIds.Count == 0)
        {
            throw new FieldValidationException(new Dictionary<string, string>
            {
                ["sectionIds"] = "يجب تحديد قسم واحد على الأقل في التقرير.",
            });
        }

        if (request.Filters.DateFrom is DateTime from
            && request.Filters.DateTo is DateTime to
            && from.Date > to.Date)
        {
            throw new FieldValidationException(new Dictionary<string, string>
            {
                ["filters.dateFrom"] = "تاريخ البداية يجب أن يسبق أو يساوي تاريخ النهاية.",
                ["filters.dateTo"] = "تاريخ النهاية يجب أن يلي أو يساوي تاريخ البداية.",
            });
        }
    }

    private static DateTime ResolvePeriodStart<T>(
        DateTime? requested,
        IReadOnlyList<T> snapshots,
        Func<T, DateTime> selector,
        DateTime fallback)
    {
        if (requested.HasValue)
            return requested.Value;

        return snapshots.Count == 0 ? fallback : snapshots.Min(selector);
    }

    private static DateTime ResolvePeriodEnd<T>(
        DateTime? requested,
        IReadOnlyList<T> snapshots,
        Func<T, DateTime> selector,
        DateTime fallback)
    {
        if (requested.HasValue)
            return requested.Value;

        return snapshots.Count == 0 ? fallback : snapshots.Max(selector);
    }

    private static List<ReportSectionId> ResolveSections(ReportBuildRequestDto request)
    {
        if (request.SectionIds.Count > 0)
            return request.SectionIds.Distinct().ToList();

        return
        [
            ReportSectionId.Cover,
            ReportSectionId.ExecutiveSummary,
            ReportSectionId.IndicatorsDashboard,
            ReportSectionId.DepartmentPerformance,
            ReportSectionId.RisksAndAlerts,
            ReportSectionId.ExecutiveRecommendations,
            ReportSectionId.TransactionDetails,
            ReportSectionId.ReportMetadata
        ];
    }

    private static List<int> ResolveSelectedPages(ReportExportRequestDto request, RenderedReportManifestDto manifest)
    {
        var pages = request.ExportMode switch
        {
            ExportMode.CurrentPage when request.CurrentPageNumber.HasValue => [request.CurrentPageNumber.Value],
            ExportMode.SelectedPages when !string.IsNullOrWhiteSpace(request.PageRangeExpression)
                => ParsePageRangeOrThrow(request.PageRangeExpression, manifest.TotalPages),
            ExportMode.SelectedPages when request.SelectedPageNumbers.Count > 0 => request.SelectedPageNumbers.Distinct().OrderBy(p => p).ToList(),
            ExportMode.SelectedPages => throw new FieldValidationException(new Dictionary<string, string>
            {
                [SelectedPagesField] = "يجب تحديد الصفحات عبر التحديد اليدوي أو نطاق الصفحات."
            }),
            ExportMode.SelectedSections => manifest.Pages
                .Where(p => request.SelectedSectionIds.Contains(p.SectionId))
                .Select(p => p.OriginalPageNumber)
                .Distinct()
                .OrderBy(p => p)
                .ToList(),
            _ => manifest.Pages.Select(p => p.OriginalPageNumber).ToList()
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
                [SelectedPagesField] = "لا توجد صفحات صالحة ضمن الاختيار الحالي."
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
                ["pageRangeExpression"] = parsed.ErrorMessage ?? "صيغة الصفحات غير صالحة."
            });
        }

        if (parsed.PageNumbers.Count == 0)
        {
            throw new FieldValidationException(new Dictionary<string, string>
            {
                ["pageRangeExpression"] = "لم يتم العثور على صفحات صالحة ضمن التعبير المحدد."
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
            _ => $"{baseName}-FULL.{extension}"
        };
    }

    private static RenderedReportManifestDto EnrichManifest(
        RenderedReportManifestDto manifest,
        InstitutionalReportModel model,
        bool isSummaryOnly,
        DetailOverflowAction? overflowAction)
    {
        manifest.LoadedDetailRows = model.Transactions.Count;
        manifest.IsSummaryOnly = isSummaryOnly;
        manifest.OverflowAction = overflowAction;
        manifest.Stylesheet = InstitutionalReportStyles.BuildDocumentStylesheet();
        manifest.TemplateVersion = InstitutionalReportStyles.TemplateVersion;
        manifest.FileFingerprint = model.Metadata.FileFingerprint;
        return manifest;
    }

    private static string ReportTypeLabel(InstitutionalReportType type) => type switch
    {
        InstitutionalReportType.OverdueTransactions => "تقرير المعاملات المتأخرة",
        InstitutionalReportType.JointDepartmentTransactions => "تقرير معاملات الإدارات المشتركة",
        InstitutionalReportType.PartialResponses => "تقرير الإفادات والردود الجزئية",
        InstitutionalReportType.SingleTransaction => "تقرير معاملة واحدة",
        _ => "التقرير التنفيذي الشامل"
    };

    private static string PriorityLabel(Priority priority) => priority switch
    {
        Priority.Urgent => "عاجل",
        Priority.VeryUrgent => "عاجل جدًا",
        _ => "عادي"
    };

    private static string StatusLabel(TransactionStatus status) => status switch
    {
        TransactionStatus.Closed => "مغلقة",
        TransactionStatus.WaitingForReply => "بانتظار رد",
        TransactionStatus.PartiallyReplied => "رد جزئي",
        TransactionStatus.Overdue => "متأخرة",
        TransactionStatus.ReadyForResponse => "جاهزة للإفادة",
        _ => "مفتوحة"
    };

    private static ReportTemplateDto MapTemplate(ReportExportTemplate entity) => new()
    {
        Id = entity.Id,
        Name = entity.Name,
        ReportType = entity.ReportType,
        SectionIds = System.Text.Json.JsonSerializer.Deserialize<List<ReportSectionId>>(entity.SectionIdsJson) ?? [],
        DefaultFilters = System.Text.Json.JsonSerializer.Deserialize<ReportFiltersDto>(entity.DefaultFiltersJson) ?? new(),
        DefaultFormat = entity.DefaultFormat,
        PageNumberingMode = entity.PageNumberingMode,
        IncludePartialCover = entity.IncludePartialCover,
        IncludePartialManifest = entity.IncludePartialManifest
    };
}
