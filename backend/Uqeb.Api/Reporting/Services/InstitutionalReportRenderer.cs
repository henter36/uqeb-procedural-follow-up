using System.Globalization;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Uqeb.Api.Reporting.Assets;
using Uqeb.Api.Reporting.DTOs;
using Uqeb.Api.Reporting.Enums;
using Uqeb.Api.Reporting.Rendering;

namespace Uqeb.Api.Reporting.Services;

public sealed class InstitutionalReportRenderer
{
    private const string DateFormat = "yyyy-MM-dd";
    private const string DateTimeFormat = "yyyy-MM-dd HH:mm";
    private const string DefaultReportTitle = "تقرير المتابعة الإجرائية للمعاملات";
    private const string TransactionDetailsPageTitle = "المعاملات التفصيلية";
    private const string UndefinedDepartmentLabel = "غير محدد";

    private static readonly TimeSpan RegexMatchTimeout = TimeSpan.FromMilliseconds(250);

    private static readonly Regex JavascriptProtocolRegex = new(
        @"javascript\s*:",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled,
        RegexMatchTimeout);

    private static readonly Regex FileProtocolRegex = new(
        @"file\s*://",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled,
        RegexMatchTimeout);

    private static readonly Regex ExternalHttpUrlRegex = new(
        @"https?://[^\s<>&""']+",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled,
        RegexMatchTimeout);

    public RenderedReportManifestDto RenderManifest(
        InstitutionalReportModel model,
        IReadOnlyList<ReportSectionId> sections,
        bool includeTransactionDetails = true) =>
        RenderManifestCore(model, sections, includeTransactionDetails, measuredTransactionChunks: null);

    public RenderedReportManifestDto RenderManifestWithMeasuredTransactionPages(
        InstitutionalReportModel model,
        IReadOnlyList<ReportSectionId> sections,
        IReadOnlyList<IReadOnlyList<TransactionDetailRowDto>> measuredTransactionChunks,
        bool includeTransactionDetails = true) =>
        RenderManifestCore(model, sections, includeTransactionDetails, measuredTransactionChunks);

    private RenderedReportManifestDto RenderManifestCore(
        InstitutionalReportModel model,
        IReadOnlyList<ReportSectionId> sections,
        bool includeTransactionDetails,
        IReadOnlyList<IReadOnlyList<TransactionDetailRowDto>>? measuredTransactionChunks)
    {
        var pages = new List<RenderedReportPageDto>();
        var coverPageIndex = -1;

        foreach (var section in sections)
        {
            if (section == ReportSectionId.Cover)
                coverPageIndex = pages.Count;

            AppendSectionPages(model, section, includeTransactionDetails, pages, measuredTransactionChunks);
        }

        AppendDetailOverflowNoticeIfNeeded(model, includeTransactionDetails, pages);

        model.Metadata.TotalPages = pages.Count;

        var metadataPageIndex = pages.FindIndex(p => p.SectionId == ReportSectionId.ReportMetadata);
        if (metadataPageIndex >= 0)
        {
            var metadataPage = pages[metadataPageIndex];
            pages[metadataPageIndex] = metadataPage with
            {
                HtmlContent = WrapPage(
                    RenderMetadata(model),
                    new PageChromeOptions(
                        PageNumber: metadataPageIndex + 1,
                        TotalPages: pages.Count,
                        Partial: false,
                        Profile: InstitutionalReportPdfProfiles.GetByName(metadataPage.PdfProfileName),
                        SectionId: metadataPage.SectionId,
                        ReportTitle: model.Metadata.Title,
                        ReportId: model.Metadata.ReportNumber)),
            };
        }

        if (coverPageIndex >= 0)
        {
            var coverPage = pages[coverPageIndex];
            pages[coverPageIndex] = coverPage with
            {
                HtmlContent = WrapPage(
                    RenderCover(model),
                    new PageChromeOptions(
                        PageNumber: coverPageIndex + 1,
                        TotalPages: pages.Count,
                        Partial: false,
                        Profile: InstitutionalReportPdfProfiles.GetByName(coverPage.PdfProfileName),
                        SectionId: coverPage.SectionId,
                        ReportTitle: model.Metadata.Title,
                        ReportId: model.Metadata.ReportNumber)),
            };
        }

        return BuildManifestResult(model, pages);
    }

    private static void AppendSectionPages(
        InstitutionalReportModel model,
        ReportSectionId section,
        bool includeTransactionDetails,
        List<RenderedReportPageDto> pages,
        IReadOnlyList<IReadOnlyList<TransactionDetailRowDto>>? measuredTransactionChunks = null)
    {
        switch (section)
        {
            case ReportSectionId.Cover:
                pages.Add(MakePage(section, "الغلاف", string.Empty));
                break;
            case ReportSectionId.ExecutiveSummary:
                pages.Add(MakePage(section, "الملخص التنفيذي", RenderExecutiveSummary(model)));
                break;
            case ReportSectionId.KeyPerformanceIndicators:
                pages.Add(MakePage(section, "مؤشرات الأداء الرئيسية", RenderAnalyticalKpis(model)));
                break;
            case ReportSectionId.SignificantFindings:
                pages.Add(MakePage(section, "النتائج المهمة", RenderSignificantFindings(model)));
                break;
            case ReportSectionId.CriticalCases:
                pages.Add(MakePage(section, "الحالات الحرجة", RenderCriticalCases(model)));
                break;
            case ReportSectionId.TimeTrends:
                pages.Add(MakePage(section, "التحليل الزمني", RenderTimeTrends(model)));
                break;
            case ReportSectionId.IndicatorsDashboard:
                pages.Add(MakePage(section, "لوحة المؤشرات والاتجاهات", RenderCharts(model)));
                break;
            case ReportSectionId.DepartmentPerformance:
                pages.Add(MakePage(section, "أداء الإدارات", RenderDepartments(model)));
                break;
            case ReportSectionId.ExternalPartyAnalysis:
                pages.Add(MakePage(section, "تحليل الجهات الخارجية", RenderExternalParties(model)));
                break;
            case ReportSectionId.ClassificationAndPriorityAnalysis:
                pages.Add(MakePage(section, "تحليل التصنيفات والأولويات", RenderClassificationAndPriority(model)));
                break;
            case ReportSectionId.DelayAndBottleneckAnalysis:
                pages.Add(MakePage(section, "تحليل الاختناقات والتأخر", RenderBottlenecks(model)));
                break;
            case ReportSectionId.DataQuality:
                pages.Add(MakePage(section, "جودة البيانات", RenderDataQuality(model)));
                break;
            case ReportSectionId.RisksAndAlerts:
                pages.Add(MakePage(section, "المخاطر والتنبيهات", RenderRisks(model)));
                break;
            case ReportSectionId.ExecutiveRecommendations:
                pages.Add(MakePage(section, "التوصيات التنفيذية", RenderRecommendations(model)));
                break;
            case ReportSectionId.RecommendationsAndActionPlan:
                pages.Add(MakePage(section, "التوصيات وخطة الإجراءات", RenderActionPlan(model)));
                break;
            case ReportSectionId.TransactionDetails:
                AppendTransactionDetailPages(model, includeTransactionDetails, pages, measuredTransactionChunks);
                break;
            case ReportSectionId.Appendices:
                pages.Add(MakePage(section, "الجداول التفصيلية والملاحق", RenderAppendices(model)));
                break;
            case ReportSectionId.MethodologyAndDefinitions:
                pages.Add(MakePage(section, "المنهجية والتعريفات", RenderMethodology(model)));
                break;
            case ReportSectionId.ReportMetadata:
                pages.Add(MakePage(section, "بيانات التقرير والفلاتر", RenderMetadata(model)));
                break;
        }
    }

    // Fallback layout metrics for preview/HTML and other renderer-only manifests. Final PDF export
    // for the general TransactionDetails table replaces these pages with DOM-measured chunks before
    // rendering; DepartmentTransactions intentionally keeps the existing computed fallback path.
    private const decimal DepartmentTransactionsRowHeightMm = 11m;
    private const decimal DepartmentTransactionsHeaderReserveMm = 12m;
    private const decimal TransactionDetailsRowHeightMm = 14m;
    private const decimal TransactionDetailsHeaderReserveMm = 18m;

    private static void AppendTransactionDetailPages(
        InstitutionalReportModel model,
        bool includeTransactionDetails,
        List<RenderedReportPageDto> pages,
        IReadOnlyList<IReadOnlyList<TransactionDetailRowDto>>? measuredTransactionChunks = null)
    {
        if (!includeTransactionDetails)
            return;

        if (model.DetailRowsTruncated)
            pages.Add(MakePage(ReportSectionId.TransactionDetails, "تنبيه صفوف التفاصيل", RenderDetailTruncationNotice(model)));

        var isDepartmentTransactions = model.Metadata.ReportType == InstitutionalReportType.DepartmentTransactions;
        if (!isDepartmentTransactions && measuredTransactionChunks is not null)
        {
            AppendMeasuredTransactionDetailPages(model, measuredTransactionChunks, pages);
            return;
        }

        var profile = InstitutionalReportPdfProfiles.ForSection(ReportSectionId.TransactionDetails);
        var rowsPerPage = isDepartmentTransactions
            ? ComputeRowsPerPage(profile, DepartmentTransactionsRowHeightMm, DepartmentTransactionsHeaderReserveMm)
            : ComputeRowsPerPage(profile, TransactionDetailsRowHeightMm, TransactionDetailsHeaderReserveMm);

        var chunkIndex = 0;
        foreach (var chunk in model.Transactions.Chunk(rowsPerPage))
        {
            var html = isDepartmentTransactions
                ? RenderDepartmentTransactionDetails(model, chunk.ToList(), isFirstPage: chunkIndex == 0)
                : RenderTransactions(model, chunk.ToList(), isFirstPage: chunkIndex == 0);
            pages.Add(MakePage(ReportSectionId.TransactionDetails, TransactionDetailsPageTitle, html));
            chunkIndex++;
        }

        if (model.Transactions.Count == 0)
        {
            var emptyHtml = isDepartmentTransactions
                ? RenderDepartmentTransactionDetails(model, [], isFirstPage: true)
                : RenderTransactions(model, [], isFirstPage: true);
            pages.Add(MakePage(ReportSectionId.TransactionDetails, TransactionDetailsPageTitle, emptyHtml));
        }
    }

    private static void AppendMeasuredTransactionDetailPages(
        InstitutionalReportModel model,
        IReadOnlyList<IReadOnlyList<TransactionDetailRowDto>> measuredTransactionChunks,
        List<RenderedReportPageDto> pages)
    {
        var chunkIndex = 0;
        foreach (var chunk in measuredTransactionChunks.Where(chunk => chunk.Count > 0))
        {
            var html = RenderTransactions(model, chunk.ToList(), isFirstPage: chunkIndex == 0);
            pages.Add(MakePage(ReportSectionId.TransactionDetails, TransactionDetailsPageTitle, html));
            chunkIndex++;
        }

        if (chunkIndex == 0 && model.Transactions.Count == 0)
            pages.Add(MakePage(ReportSectionId.TransactionDetails, TransactionDetailsPageTitle, RenderTransactions(model, [], isFirstPage: true)));
    }

    /// <summary>
    /// Row capacity per PDF page, derived from real page-profile geometry (content height minus margins
    /// and a header reserve) divided by the table's own row height — not a fixed literal. Used for both
    /// the DepartmentTransactions and general TransactionDetails detail tables, each with their own
    /// row-height/header-reserve constants matching their respective table density in CSS.
    /// </summary>
    private static int ComputeRowsPerPage(PdfPageProfile profile, decimal rowHeightMm, decimal headerReserveMm)
    {
        var usableHeightMm = profile.HeightMm - profile.MarginTopMm - profile.MarginBottomMm - headerReserveMm;
        return Math.Max(1, (int)Math.Floor(usableHeightMm / rowHeightMm));
    }

    private static void AppendDetailOverflowNoticeIfNeeded(
        InstitutionalReportModel model,
        bool includeTransactionDetails,
        List<RenderedReportPageDto> pages)
    {
        if (!includeTransactionDetails && model.DetailRowsTruncated)
        {
            pages.Add(MakePage(
                ReportSectionId.ReportMetadata,
                "تنبيه حد صفوف التفاصيل",
                RenderDetailOverflowNotice(model)));
        }
    }

    private static RenderedReportManifestDto BuildManifestResult(
        InstitutionalReportModel model,
        List<RenderedReportPageDto> pages)
    {
        var total = pages.Count;
        var numberedPages = pages.Select((page, index) =>
        {
            var pageNumber = index + 1;
            return page with
            {
                OriginalPageNumber = pageNumber,
                RenderedPageNumber = pageNumber,
                HtmlContent = InjectFooter(
                    page.HtmlContent,
                    pageNumber,
                    total,
                    false,
                    model.Metadata.Title,
                    model.Metadata.ReportNumber)
            };
        }).ToList();

        return new RenderedReportManifestDto
        {
            ReportTitle = EffectiveReportTitle(model.Metadata.Title),
            ReportId = model.Metadata.ReportNumber,
            TotalPages = total,
            TotalMatchingTransactions = model.TotalMatchedRows,
            IncludedTransactionCount = model.ExportedDetailRows,
            DetailRowLimit = model.Metadata.DetailRowLimit,
            RequiresDetailOverflowAction = model.TotalMatchedRows > model.Metadata.DetailRowLimit,
            TotalMatchedRows = model.TotalMatchedRows,
            ExportedDetailRows = model.ExportedDetailRows,
            DetailRowsTruncated = model.DetailRowsTruncated,
            DetailPartsCount = model.DetailPartsCount,
            LoadedDetailRows = model.Transactions.Count,
            TemplateVersion = InstitutionalReportStyles.TemplateVersion,
            Analysis = model.Analysis,
            Pages = numberedPages
        };
    }

    public RenderedReportManifestDto RenderTransactionDetailsManifest(
        InstitutionalReportModel model,
        IReadOnlyList<TransactionDetailRowDto> rows,
        string partLabel)
    {
        var scoped = new InstitutionalReportModel
        {
            TotalMatchedRows = model.TotalMatchedRows,
            ExportedDetailRows = rows.Count,
            DetailRowsTruncated = model.DetailRowsTruncated,
            DetailPartsCount = model.DetailPartsCount,
            Metadata = new ReportMetadataDto
            {
                ReportNumber = $"{model.Metadata.ReportNumber}-{partLabel}",
                ReportType = model.Metadata.ReportType,
                ReportTypeName = model.Metadata.ReportTypeName,
                IssueDate = model.Metadata.IssueDate,
                PeriodFrom = model.Metadata.PeriodFrom,
                PeriodTo = model.Metadata.PeriodTo,
                Title = $"{model.Metadata.Title} — {partLabel}",
                Introduction = model.Metadata.Introduction,
                VerificationId = model.Metadata.VerificationId,
                GeneratedAt = model.Metadata.GeneratedAt,
                TotalMatchingTransactions = model.TotalMatchedRows,
                IncludedTransactionCount = rows.Count,
                DetailRowLimit = model.Metadata.DetailRowLimit,
            },
            Filters = model.Filters,
            Transactions = rows.ToList(),
            DetailSortByEffective = model.DetailSortByEffective,
            GroupDetailsByDepartmentEffective = model.GroupDetailsByDepartmentEffective,
            DetailRowsAreAdditive = model.DetailRowsAreAdditive,
            ComparisonUnavailableReason = model.ComparisonUnavailableReason,
            Analysis = model.Analysis,
        };

        return RenderManifest(scoped, [ReportSectionId.TransactionDetails]);
    }

    private static string RenderDetailOverflowNotice(InstitutionalReportModel model)
    {
        var partsNote = model.DetailPartsCount > 1
            ? $" سيتطلب التصدير الكامل للتفاصيل {model.DetailPartsCount:N0} ملفات PDF."
            : string.Empty;
        return $"""
        <h2 class="section-title">تنبيه حد صفوف التفاصيل</h2>
        <div class="partial-note">
          يتجاوز عدد المعاملات المطابقة للفلاتر الحد التشغيلي لصفوف التفاصيل في ملف PDF/DOCX واحد.
          تم إنشاء التقرير الملخص كاملاً من كامل النتائج ({model.TotalMatchedRows:N0} معاملة) دون تضمين جدول التفاصيل الكامل داخل هذا الملف.{partsNote}
        </div>
        <dl class="info-card">
          <dt>إجمالي المعاملات المطابقة</dt><dd>{model.TotalMatchedRows:N0}</dd>
          <dt>الحد التشغيلي لصفوف التفاصيل</dt><dd>{model.Metadata.DetailRowLimit:N0}</dd>
          <dt>صفوف التفاصيل المصدرة في هذا الملف</dt><dd>{model.ExportedDetailRows:N0}</dd>
        </dl>
        <p style="font-size:12px;color:#2F6B58;line-height:1.8;">
          لتصدير التفاصيل الكاملة: اختر تقسيم التفاصيل إلى عدة ملفات PDF، أو تصدير التفاصيل كاملة إلى Excel (XLSX).
        </p>
        """;
    }

    private static string RenderDetailTruncationNotice(InstitutionalReportModel model)
    {
        var partsNote = model.DetailPartsCount > 1
            ? $" عند التصدير الكامل ستُقسَّم التفاصيل إلى {model.DetailPartsCount:N0} ملفات PDF."
            : string.Empty;
        return $"""
        <h2 class="section-title">تنبيه صفوف التفاصيل</h2>
        <div class="partial-note">
          بطاقات KPI والرسوم والمؤشرات محسوبة من كامل النتائج ({model.TotalMatchedRows:N0} معاملة).
          جدول التفاصيل في هذا العرض يقتصر على {model.ExportedDetailRows:N0} صفًا فقط.{partsNote}
        </div>
        <dl class="info-card">
          <dt>إجمالي النتائج المطابقة</dt><dd>{model.TotalMatchedRows:N0}</dd>
          <dt>صفوف التفاصيل المعروضة/المصدرة هنا</dt><dd>{model.ExportedDetailRows:N0}</dd>
          <dt>الحد التشغيلي لكل ملف</dt><dd>{model.Metadata.DetailRowLimit:N0}</dd>
        </dl>
        """;
    }

    public RenderedReportManifestDto BuildExportManifest(
        RenderedReportManifestDto source,
        IReadOnlyList<int> selectedOriginalPages,
        ReportExportRequestDto request)
    {
        var selected = source.Pages.Where(p => selectedOriginalPages.Contains(p.OriginalPageNumber)).ToList();
        var isPartial = selected.Count < source.Pages.Count;
        var pages = new List<RenderedReportPageDto>();

        if (isPartial && request.IncludePartialCover == true)
            pages.Add(CreatePartialCoverPage(source, selected));

        if (isPartial && request.IncludePartialManifest == true)
            pages.Add(CreatePartialManifestPage(source, request, selected));

        foreach (var page in selected)
            pages.Add(page);

        ApplyFinalPageNumbering(
            pages,
            isPartial,
            EffectiveReportTitle(source.ReportTitle),
            source.ReportId);

        return new RenderedReportManifestDto
        {
            ReportTitle = EffectiveReportTitle(source.ReportTitle),
            ReportId = source.ReportId,
            TotalPages = pages.Count,
            TotalMatchingTransactions = source.TotalMatchingTransactions,
            IncludedTransactionCount = source.IncludedTransactionCount,
            DetailRowLimit = source.DetailRowLimit,
            RequiresDetailOverflowAction = source.RequiresDetailOverflowAction,
            TotalMatchedRows = source.TotalMatchedRows,
            ExportedDetailRows = source.ExportedDetailRows,
            DetailRowsTruncated = source.DetailRowsTruncated,
            DetailPartsCount = source.DetailPartsCount,
            LoadedDetailRows = source.LoadedDetailRows,
            CurrentPartNumber = source.CurrentPartNumber,
            RowsFrom = source.RowsFrom,
            RowsTo = source.RowsTo,
            IsSummaryOnly = source.IsSummaryOnly,
            OverflowAction = source.OverflowAction,
            Stylesheet = source.Stylesheet,
            TemplateVersion = source.TemplateVersion,
            FileFingerprint = source.FileFingerprint,
            Pages = pages,
            IsPartialExport = isPartial,
            PartialExportNote = isPartial ? "هذه نسخة جزئية من التقرير الأصلي." : null,
            Analysis = source.Analysis
        };
    }

    private static void ApplyFinalPageNumbering(
        List<RenderedReportPageDto> pages,
        bool isPartial,
        string reportTitle,
        string reportId)
    {
        for (var i = 0; i < pages.Count; i++)
        {
            var page = pages[i];
            var renderedNumber = i + 1;
            var footerTotal = pages.Count;

            pages[i] = page with
            {
                RenderedPageNumber = renderedNumber,
                HtmlContent = UpdateMetadataTotalPages(
                    InjectFooter(page.HtmlContent, renderedNumber, footerTotal, isPartial, reportTitle, reportId),
                    footerTotal)
            };
        }
    }

    public static string RenderHtmlDocument(RenderedReportManifestDto manifest)
    {
        var sb = new StringBuilder();
        sb.Append("<!DOCTYPE html><html lang='ar' dir='rtl'><head><meta charset='utf-8'><style>")
            .Append(InstitutionalReportStyles.BuildDocumentStylesheet())
            .Append("</style></head><body>");
        foreach (var page in manifest.Pages)
            sb.Append(page.HtmlContent);
        sb.Append("</body></html>");
        return sb.ToString();
    }

    private static RenderedReportPageDto MakePage(ReportSectionId section, string title, string innerHtml)
    {
        var profile = InstitutionalReportPdfProfiles.ForSection(section);
        return new RenderedReportPageDto
        {
            OriginalPageNumber = 0,
            RenderedPageNumber = 0,
            SectionId = section,
            SectionName = title,
            PageTitle = title,
            PdfProfileName = profile.Name,
            HtmlContent = WrapPage(
                innerHtml,
                new PageChromeOptions(
                    PageNumber: 1,
                    TotalPages: 1,
                    Partial: false,
                    Profile: profile,
                    SectionId: section,
                    ReportTitle: title,
                    ReportId: string.Empty))
        };
    }

    private static RenderedReportPageDto MakeSupplementaryPage(ReportSectionId section, string title, string innerHtml) =>
        new()
        {
            OriginalPageNumber = 0,
            RenderedPageNumber = 0,
            SectionId = section,
            SectionName = title,
            PageTitle = title,
            PdfProfileName = InstitutionalReportPdfProfiles.StandardPortrait.Name,
            HtmlContent = WrapPage(
                innerHtml,
                new PageChromeOptions(
                    PageNumber: 1,
                    TotalPages: 1,
                    Partial: true,
                    Profile: InstitutionalReportPdfProfiles.StandardPortrait,
                    SectionId: section,
                    ReportTitle: title,
                    ReportId: string.Empty))
        };

    private static string WrapPage(
        string content,
        PageChromeOptions options) =>
        $"""
        <section class="report-page report-page--{options.Profile.CssClass}" data-page="{options.PageNumber}" data-profile="{options.Profile.Name}" data-section="{Esc(options.ReportTitle)}" data-section-id="{options.SectionId}">
          {Header(options.Partial)}
          <main class="report-content">{content}</main>
          {BuildFooter(options.PageNumber, options.TotalPages, options.Partial, options.ReportTitle, options.ReportId)}
        </section>
        """;

    private sealed record PageChromeOptions(
        int PageNumber,
        int TotalPages,
        bool Partial,
        PdfPageProfile Profile,
        ReportSectionId SectionId,
        string ReportTitle,
        string ReportId);

    private static string Header(bool partial) =>
        $"""
        <header class="report-header">
          <div class="org">المتابعة الإجرائية</div>
          <div class="meta">{(partial ? "نسخة جزئية" : "تقرير مؤسسي")}</div>
        </header>
        """;

    private static string InjectFooter(
        string html,
        int pageNumber,
        int totalPages,
        bool partial,
        string reportTitle,
        string reportId)
    {
        var footer = BuildFooter(pageNumber, totalPages, partial, reportTitle, reportId);
        var footerStart = html.IndexOf("<footer class=\"report-footer", StringComparison.Ordinal);
        if (footerStart >= 0)
        {
            var footerEnd = html.IndexOf("</footer>", footerStart, StringComparison.Ordinal);
            if (footerEnd >= 0)
                return string.Concat(html.AsSpan(0, footerStart), footer, html.AsSpan(footerEnd + "</footer>".Length));
        }

        return html.Replace("</section>", $"{footer}</section>", StringComparison.Ordinal);
    }

    private static string BuildFooter(int pageNumber, int totalPages, bool partial, string reportTitle, string reportId)
    {
        var title = EffectiveReportTitle(reportTitle);
        var id = string.IsNullOrWhiteSpace(reportId)
            ? "—"
            : reportId;

        return $"""
        <footer class="report-footer">
          <span class="footer-title">{(partial ? "نسخة جزئية — " : string.Empty)}{Esc(title)}</span>
          <span class="footer-page">الصفحة {pageNumber} من {totalPages}</span>
          <span class="footer-id">{Esc(id)}</span>
        </footer>
        """;
    }

    private static string UpdateMetadataTotalPages(string html, int totalPages)
    {
        const string marker = "<dt>إجمالي الصفحات</dt><dd>";
        var start = html.IndexOf(marker, StringComparison.Ordinal);
        if (start < 0)
            return html;

        start += marker.Length;
        var end = html.IndexOf("</dd>", start, StringComparison.Ordinal);
        return end < 0
            ? html
            : string.Concat(html.AsSpan(0, start), totalPages.ToString("N0", CultureInfo.InvariantCulture), html.AsSpan(end));
    }

    private static string EffectiveReportTitle(string? reportTitle) =>
        string.IsNullOrWhiteSpace(reportTitle)
            ? DefaultReportTitle
            : reportTitle;

    private static string RenderCover(InstitutionalReportModel model)
    {
        var m = model.Metadata;
        return $"""
        <div class="cover">
          <div class="cover-main">
            <h1 class="cover-title">{Esc(m.Title)}</h1>
            <div class="cover-period">الفترة من {FormatDate(m.PeriodFrom)} إلى {FormatDate(m.PeriodTo)}</div>
            <dl class="info-card">
              <dt>رقم التقرير</dt><dd>{Esc(m.ReportNumber)}</dd>
              <dt>تاريخ الإصدار</dt><dd>{FormatDate(m.IssueDate)}</dd>
            </dl>
          </div>
        </div>
        """;
    }

    private static string RenderExecutiveSummary(InstitutionalReportModel model)
    {
        var cards = string.Join(string.Empty, model.Summary.KpiCards.Select(c =>
            $"""<div class="kpi-card"><div class="label">{Esc(c.Title)}</div><div class="value">{Esc(c.Value)}</div>{(c.Delta != null ? $"<div class='delta'>{Esc(c.Delta)}</div>" : string.Empty)}</div>"""));
        var insights = model.Analysis.ExecutiveInsights.Count == 0
            ? string.Empty
            : "<h3 style=\"color:#123F32;margin-top:16px;\">أبرز القراءات التحليلية</h3><ul class=\"insight-list\">"
              + string.Join(string.Empty, model.Analysis.ExecutiveInsights.Select(i => $"<li><strong>{Esc(SeverityLabel(i.Severity))}</strong> — {Esc(i.Text)}</li>"))
              + "</ul>";
        return $"""
        <h2 class="section-title">الملخص التنفيذي</h2>
        <div class="kpi-grid">{cards}</div>
        <h3 style="color:#123F32;margin-top:16px;">التقييم التنفيذي</h3>
        <div class="narrative">{Esc(model.Summary.ExecutiveNarrative)}</div>
        {insights}
        """;
    }

    private static string RenderAnalyticalKpis(InstitutionalReportModel model)
    {
        var available = model.Analysis.Kpis.Where(k => k.IsAvailable).ToList();
        if (available.Count == 0)
            return """<h2 class="section-title">مؤشرات الأداء الرئيسية</h2><div class="empty-state">لا توجد مؤشرات قابلة للحساب ضمن البيانات الحالية.</div>""";

        var groups = available
            .GroupBy(KpiGroupLabel)
            .Select(group =>
            {
                var cards = string.Join(string.Empty, group.Select(k =>
                    $"""
                    <article class="analytical-kpi-card">
                      <div class="label">{Esc(k.Title)}</div>
                      <div class="value">{Esc(DisplayValue(k.DisplayValue))}</div>
                      <div class="meta">{Esc(k.Comparison.TrendDirection == TrendDirection.NotComparable ? "غير قابلة للمقارنة" : TrendLabel(k.Comparison.TrendDirection))}</div>
                      <p>{Esc(k.Definition)}</p>
                    </article>
                    """));
                return $"""<section class="kpi-section"><h3>{Esc(group.Key)}</h3><div class="analytical-kpi-grid">{cards}</div></section>""";
            });
        return $"""
        <h2 class="section-title">مؤشرات الأداء الرئيسية</h2>
        {string.Join(string.Empty, groups)}
        """;
    }

    private static string RenderSignificantFindings(InstitutionalReportModel model)
    {
        if (model.Analysis.Findings.Count == 0)
            return """<h2 class="section-title">النتائج المهمة</h2><div class="empty-state">لا توجد نتائج مهمة وفق عتبات التحليل الحالية.</div>""";

        var cards = string.Join(string.Empty, model.Analysis.Findings.Select(f =>
            $"""
            <article class="recommendation-card">
              <div class="priority">{Esc(SeverityLabel(f.Severity))}</div>
              <h3 style="margin:8px 0 6px;font-size:14px;color:var(--report-primary);">{Esc(f.Title)}</h3>
              <p style="margin:0 0 8px;">{Esc(f.Description)}</p>
              <div style="font-size:11px;color:var(--report-secondary);">الدليل: {Esc(f.Evidence)} — النطاق: {Esc(f.AffectedScope)}</div>
            </article>
            """));
        return $"""<h2 class="section-title">النتائج المهمة</h2>{cards}""";
    }

    private static string RenderCriticalCases(InstitutionalReportModel model)
    {
        if (model.Analysis.CriticalCases.Count == 0)
            return """<h2 class="section-title">الحالات الحرجة</h2><div class="empty-state">لا توجد حالات حرجة وفق القواعد الحالية.</div>""";

        var rows = string.Join(string.Empty, model.Analysis.CriticalCases.Select(c =>
            $"""
            <tr>
              <td class="cell--id">{Esc(c.IncomingNumber.Length == 0 ? c.TransactionId.ToString(CultureInfo.InvariantCulture) : c.IncomingNumber)}</td>
              <td class="cell--subject">{Esc(c.Subject)}</td>
              <td>{Esc(DisplayValue(c.ExternalParty))}</td><td>{Esc(NormalizeDepartmentName(c.Department))}</td><td>{Esc(DisplayValue(c.Priority))}</td>
              <td class="cell--number">{c.AgeDays}</td>
              <td>{Esc(DisplayValue(c.ReasonLabel))}</td><td>{Esc(DisplayValue(c.RequiredAction))}</td><td>{Esc(SeverityLabel(c.Severity))}</td>
            </tr>
            """));
        return $"""
        <h2 class="section-title">الحالات الحرجة</h2>
        <table class="report-table report-table--transactions">
          <thead><tr><th>رقم المعاملة</th><th>الموضوع</th><th>الجهة</th><th>الإدارة</th><th>الأولوية</th><th>العمر</th><th>سبب الخطورة</th><th>الإجراء المطلوب</th><th>مستوى الخطورة</th></tr></thead>
          <tbody>{rows}</tbody>
        </table>
        """;
    }

    private static string RenderTimeTrends(InstitutionalReportModel model)
    {
        if (model.Analysis.TimeSeries.Count == 0)
            return """<h2 class="section-title">التحليل الزمني</h2><div class="empty-state">لا توجد نقاط زمنية كافية للعرض.</div>""";

        var rows = string.Join(string.Empty, model.Analysis.TimeSeries.Select(p =>
            $"<tr><td>{Esc(p.PeriodLabel)}</td><td class=\"cell--number\">{p.Incoming}</td><td class=\"cell--number\">{p.Closed}</td><td class=\"cell--number\">{p.OpenBalance}</td><td class=\"cell--number\">{p.Overdue}</td><td class=\"cell--number\">{p.OnTimeRate:N1}%</td><td class=\"cell--number\">{p.BacklogGrowth}</td></tr>"));
        return $"""
        <h2 class="section-title">التحليل الزمني</h2>
        <table class="report-table">
          <thead><tr><th>الفترة</th><th>وارد</th><th>مغلق</th><th>رصيد مفتوح</th><th>متأخر</th><th>ضمن المهلة</th><th>تغير التراكم</th></tr></thead>
          <tbody>{rows}</tbody>
        </table>
        {RenderDepartmentTimeSeries(model)}
        """;
    }

    /// <summary>
    /// Caps the HTML/PDF view to the top departments (by Overdue, then Open, then Incoming
    /// totals across the whole window) so the report doesn't balloon with every department ×
    /// every period. XLSX export is not capped — see InstitutionalReportXlsxExporter.
    /// </summary>
    private static string RenderDepartmentTimeSeries(InstitutionalReportModel model)
    {
        var points = model.Analysis.DepartmentTimeSeries;
        if (points.Count == 0)
            return string.Empty;

        var departmentGroups = DepartmentTimeSeriesRanking.RankDepartments(points);
        var topDepartmentKeys = DepartmentTimeSeriesRanking.TopDepartmentKeys(departmentGroups);

        var visibleRows = points.Where(p => DepartmentTimeSeriesRanking.IsTopDepartment(p, topDepartmentKeys)).ToList();
        var truncationNote = topDepartmentKeys.Count < departmentGroups.Count
            ? $"""<div class="partial-note">تعرض هذه القائمة أعلى {DepartmentTimeSeriesRanking.TopDepartments} إدارات حسب المتأخرات ثم المفتوحة ثم الوارد؛ تصدير XLSX يشمل كل الإدارات.</div>"""
            : string.Empty;

        var rows = string.Join(string.Empty, visibleRows.Select(p =>
            $"<tr><td>{Esc(p.PeriodLabel)}</td><td class=\"cell--department\">{Esc(DepartmentTimeSeriesRanking.NormalizeDepartmentName(p.DepartmentName))}</td><td class=\"cell--number\">{p.IncomingCount}</td><td class=\"cell--number\">{p.ClosedCount}</td><td class=\"cell--number\">{p.OpenCount}</td><td class=\"cell--number\">{p.OverdueCount}</td><td class=\"cell--number\">{p.OnTimeCompletionRate:N1}%</td><td class=\"cell--number\">{p.AverageCompletionDays:N1}</td><td class=\"cell--number\">{p.PendingAssignments}</td></tr>"));

        return $"""
        <h2 class="section-title">التحليل الزمني حسب الإدارة</h2>
        {truncationNote}
        <table class="report-table">
          <thead><tr><th>الفترة</th><th>الإدارة</th><th>الوارد</th><th>المغلق</th><th>المفتوح</th><th>المتأخر</th><th>ضمن المهلة</th><th>متوسط الإنجاز</th><th>الإفادات المعلقة</th></tr></thead>
          <tbody>{rows}</tbody>
        </table>
        """;
    }

    private static string RenderCharts(InstitutionalReportModel model)
    {
        var limitedData = model.Charts.Count == 0 || model.Charts.Sum(c => c.Series.Count) < 3
            ? """<div class="partial-note">البيانات المتاحة محدودة، لذلك قد لا تعكس الرسوم اتجاهًا مستقرًا.</div>"""
            : string.Empty;
        var charts = string.Join(string.Empty, model.Charts.Select(chart =>
        {
            var max = chart.Series.DefaultIfEmpty(new ChartSeriesPointDto()).Max(s => s.Value);
            if (max <= 0) max = 1;
            var bars = string.Join(string.Empty, chart.Series.Select((s, i) =>
            {
                var height = Math.Max(8, (int)Math.Round(s.Value / max * 100));
                var cls = i % 2 == 0 ? "chart-bar" : "chart-bar gold";
                return $"""<div style="flex:1"><div class="{cls}" style="height:{height}px" title="{Esc(s.Label)}: {s.Value}"><span class="chart-value">{s.Value:N0}</span></div><div class="chart-bar-label">{Esc(s.Label)}</div></div>""";
            }));
            var footnote = chart.Footnote != null ? $"<div class='chart-footnote'>{Esc(chart.Footnote)}</div>" : string.Empty;
            return $"""<div class="chart-card"><div class="chart-title">{Esc(chart.Title)}</div><div class="chart-bars">{bars}</div>{footnote}</div>""";
        }));
        return $"""<h2 class="section-title">لوحة المؤشرات والاتجاهات</h2>{limitedData}<div class="charts-grid">{charts}</div>""";
    }

    private static string RenderDepartments(InstitutionalReportModel model)
    {
        var rows = string.Join(string.Empty, model.DepartmentPerformance.Select(d =>
        {
            var ratingClass = d.Rating switch
            {
                DepartmentRatingLevel.Good => "rating-good",
                DepartmentRatingLevel.Critical => "rating-critical",
                _ => "rating-watch"
            };
            return $"""
            <tr>
              <td class="cell--department">{Esc(NormalizeDepartmentName(d.DepartmentName))}</td>
              <td class="cell--number">{d.TotalTransactions}</td><td class="cell--number">{d.ClosedCount}</td><td class="cell--number">{d.OpenCount}</td>
              <td class="cell--number">{d.OverdueCount}</td><td class="cell--number">{d.WaitingForStatementCount}</td><td class="cell--number">{d.OnTimeCompletionRate:N1}%</td>
              <td class="cell--rating {ratingClass}">{Esc(DisplayValue(d.RatingLabel))}</td>
            </tr>
            """;
        }));
        var undefinedDepartmentNote = model.DepartmentPerformance.Any(d => IsUndefinedDepartment(d.DepartmentName) || d.DepartmentId == 0)
            ? """<div class="partial-note">ملاحظة جودة بيانات: توجد معاملات بلا إدارة مختصة محددة. صف "غير محدد" يعرض حجم السجلات غير المصنفة ولا يُستخدم كتقييم تنفيذي لإدارة بعينها.</div>"""
            : string.Empty;
        var totals = model.DepartmentPerformance.Aggregate(
            (Total: 0, Closed: 0, Open: 0, Waiting: 0, Overdue: 0, Joint: 0),
            (acc, row) => (acc.Total + row.TotalTransactions, acc.Closed + row.ClosedCount, acc.Open + row.OpenCount,
                acc.Waiting + row.WaitingForStatementCount, acc.Overdue + row.OverdueCount, acc.Joint + row.JointDepartmentCount));
        var totalsNote = model.DepartmentTotalsAreAdditive
            ? $"الإجمالي — قابل للجمع (مجمَّع حسب {Esc(model.DepartmentAggregationDescription)})"
            : $"الإجمالي — ملاحظة: المجاميع قد تتجاوز إجمالي المعاملات ({Esc(model.DepartmentAggregationDescription)})";
        return $"""
        <h2 class="section-title">أداء الإدارات</h2>
        {undefinedDepartmentNote}
        <table class="report-table report-table--departments">
          <thead><tr>
            <th>الإدارة</th><th>إجمالي</th><th>مغلقة</th><th>مفتوحة</th><th>متأخرة</th>
            <th>بانتظار إفادة</th><th>ضمن المهلة</th><th>التقييم</th>
          </tr></thead>
          <tbody>{rows}
          <tr class="report-table__total-row">
            <td>{totalsNote}</td><td class="cell--number">{totals.Total}</td><td class="cell--number">{totals.Closed}</td><td class="cell--number">{totals.Open}</td>
            <td class="cell--number">{totals.Overdue}</td><td class="cell--number">{totals.Waiting}</td><td>—</td><td>—</td>
          </tr></tbody>
        </table>
        <p class="section-footnote">{(model.DepartmentTotalsAreAdditive
            ? "* تُحتسب كل معاملة تحت إدارتها المسؤولة فقط. تظهر تفاصيل الإدارات المشتركة في الجداول التفصيلية أو XLSX عند الحاجة."
            : $"* {Esc(model.DepartmentAggregationDescription)}")}</p>
        """;
    }

    private static string RenderExternalParties(InstitutionalReportModel model)
    {
        if (model.Analysis.ExternalParties.Count == 0)
            return """<h2 class="section-title">تحليل الجهات الخارجية</h2><div class="empty-state">لا توجد بيانات جهات خارجية ذات معنى ضمن نطاق التقرير.</div>""";

        var rows = string.Join(string.Empty, model.Analysis.ExternalParties.Select(p =>
            $"""
            <tr>
              <td>{Esc(p.ExternalPartyName)}</td><td class="cell--number">{p.IncomingCount}</td><td class="cell--number">{p.OutgoingCount}</td>
              <td class="cell--number">{p.PendingResponseCount}</td><td class="cell--number">{p.OverdueResponseCount}</td>
              <td class="cell--number">{p.AverageResponseDays:N1}</td><td class="cell--number">{p.MedianResponseDays:N1}</td>
              <td class="cell--number">{p.FollowUpCount}</td><td class="cell--number">{p.OldestPendingResponseDays}</td><td>{Esc(p.TopCategories)}</td>
            </tr>
            """));
        return $"""
        <h2 class="section-title">تحليل الجهات الخارجية</h2>
        <p class="section-subtitle">تُعرض هذه النتائج كمعاملات منتظرة من الجهة، ولا تنسب سبب التأخر للجهة دون قاعدة سببية صريحة.</p>
        <table class="report-table report-table--departments">
          <thead><tr><th>الجهة</th><th>وارد</th><th>صادر</th><th>منتظر رد</th><th>ردود متأخرة</th><th>متوسط الرد</th><th>وسيط الرد</th><th>متابعات</th><th>أقدم انتظار</th><th>أبرز التصنيفات</th></tr></thead>
          <tbody>{rows}</tbody>
        </table>
        """;
    }

    private static string RenderClassificationAndPriority(InstitutionalReportModel model)
    {
        var categoryRows = model.Analysis.Categories.Count == 0
            ? """<tr><td colspan="7">لا توجد بيانات تصنيفات.</td></tr>"""
            : string.Join(string.Empty, model.Analysis.Categories.Select(c =>
                $"<tr><td>{Esc(DisplayValue(c.CategoryName))}</td><td class=\"cell--number\">{c.TransactionCount}</td><td class=\"cell--number\">{c.OpenCount}</td><td class=\"cell--number\">{c.OverdueCount}</td><td class=\"cell--number\">{c.OnTimeCompletionRate:N1}%</td><td class=\"cell--number\">{c.AverageCompletionDays:N1}</td><td class=\"cell--number\">{c.PendingAssignments}</td></tr>"));
        var priorityRows = model.Analysis.Priorities.Count == 0
            ? """<tr><td colspan="6">لا توجد بيانات أولويات.</td></tr>"""
            : string.Join(string.Empty, model.Analysis.Priorities.Select(p =>
                $"<tr><td>{Esc(DisplayValue(p.Priority))}</td><td class=\"cell--number\">{p.Count}</td><td class=\"cell--number\">{p.OpenCount}</td><td class=\"cell--number\">{p.OverdueCount}</td><td class=\"cell--number\">{p.AverageAgeDays:N1}</td><td class=\"cell--number\">{p.OnTimeRate:N1}%</td></tr>"));
        return $"""
        <h2 class="section-title">تحليل التصنيفات والأولويات</h2>
        <h3>التصنيفات</h3>
        <table class="report-table">
          <thead><tr><th>التصنيف</th><th>الإجمالي</th><th>مفتوحة</th><th>متأخرة</th><th>ضمن المهلة</th><th>متوسط الإنجاز</th><th>إفادات معلقة</th></tr></thead>
          <tbody>{categoryRows}</tbody>
        </table>
        <h3>الأولويات</h3>
        <table class="report-table">
          <thead><tr><th>الأولوية</th><th>الإجمالي</th><th>مفتوحة</th><th>متأخرة</th><th>متوسط العمر</th><th>ضمن المهلة</th></tr></thead>
          <tbody>{priorityRows}</tbody>
        </table>
        """;
    }

    private static string RenderBottlenecks(InstitutionalReportModel model)
    {
        if (model.Analysis.Bottlenecks.Count == 0)
            return """<h2 class="section-title">تحليل الاختناقات والتأخر</h2><div class="empty-state">لا توجد معاملات مفتوحة كافية لتصنيف الاختناقات.</div>""";

        var rows = string.Join(string.Empty, model.Analysis.Bottlenecks.Select(b =>
            $"""
            <tr>
              <td>{Esc(b.ReasonLabel)}</td><td class="cell--number">{b.Count}</td><td class="cell--number">{b.SharePercent:N1}%</td>
              <td class="cell--number">{b.AverageDelayDays:N1}</td><td>{Esc(b.TopDepartments)}</td><td>{Esc(b.TopExternalParties)}</td>
            </tr>
            """));
        return $"""
        <h2 class="section-title">تحليل الاختناقات والتأخر</h2>
        <table class="report-table">
          <thead><tr><th>السبب المصنف</th><th>العدد</th><th>النسبة</th><th>متوسط الأيام</th><th>أبرز الإدارات</th><th>أبرز الجهات</th></tr></thead>
          <tbody>{rows}</tbody>
        </table>
        """;
    }

    private static string RenderDataQuality(InstitutionalReportModel model)
    {
        var rows = model.Analysis.DataQualityIssues.Count == 0
            ? """<tr><td colspan="6">لا توجد ملاحظات جودة بيانات وفق القواعد الحالية.</td></tr>"""
            : string.Join(string.Empty, model.Analysis.DataQualityIssues.Select(i =>
                $"<tr><td>{Esc(DisplayValue(i.Label))}</td><td class=\"cell--number\">{i.Count}</td><td class=\"cell--number\">{i.SharePercent:N1}%</td><td>{Esc(SeverityLabel(i.Severity))}</td><td>{Esc(DisplayValue(i.AffectedFields))}</td><td>{Esc(DisplayValue(i.SuggestedCorrection))}</td></tr>"));
        return $"""
        <h2 class="section-title">جودة البيانات</h2>
        <p class="section-subtitle">نسبة اكتمال البيانات: {model.Analysis.DataCompletenessRate:N1}% وفق الحقول المطلوبة والشرطية الموثقة.</p>
        <table class="report-table">
          <thead><tr><th>الملاحظة</th><th>العدد</th><th>النسبة</th><th>الخطورة</th><th>الحقول</th><th>التصحيح المقترح</th></tr></thead>
          <tbody>{rows}</tbody>
        </table>
        """;
    }

    private static string RenderRisks(InstitutionalReportModel model)
    {
        var counters = model.RiskCounters;
        var groups = new (string Class, string Title, RiskSeverity MinSeverity)[]
        {
            ("critical", "حرج", RiskSeverity.Critical),
            ("high", "مرتفع", RiskSeverity.High),
            ("elevated", "متوسط", RiskSeverity.Elevated),
            ("info", "معلوماتي", RiskSeverity.Informational),
        };

        var groupedHtml = string.Join(string.Empty, groups.Select(group =>
        {
            var items = BuildUnifiedRiskAlerts(model).Where(r => r.Severity == group.MinSeverity).ToList();
            if (items.Count == 0)
            {
                return $"""
                <div class="risk-group {group.Class}">
                  <div class="risk-group-title">{group.Title}</div>
                  <div class="risk-empty">لا توجد تنبيهات في هذا المستوى.</div>
                </div>
                """;
            }

            var rows = string.Join(string.Empty, items.Select(r =>
                $"<tr><td>{r.Sequence}</td><td>{Esc(DisplayValue(r.Alert))}</td><td>{Esc(NormalizeDepartmentName(r.DepartmentName))}</td><td>{r.ElapsedDays}</td><td>{Esc(DisplayValue(r.SuggestedAction))}</td></tr>"));
            return $"""
            <div class="risk-group {group.Class}">
              <div class="risk-group-title">{group.Title}</div>
              <table class="report-table"><thead><tr><th>م</th><th>التنبيه</th><th>الإدارة</th><th>الأيام</th><th>الإجراء</th></tr></thead><tbody>{rows}</tbody></table>
            </div>
            """;
        }));

        return $"""
        <h2 class="section-title">المخاطر والتنبيهات</h2>
        <div class="risk-grid">{groupedHtml}</div>
        <div class="counter-row">
          <span class="counter-pill">إدارات تحتاج متابعة: {counters.DepartmentsNeedingFollowUp}</span>
          <span class="counter-pill">معاملات مشتركة مفتوحة: {counters.OpenJointDepartmentTransactions}</span>
          <span class="counter-pill">ردود جزئية: {counters.PartialResponses}</span>
          <span class="counter-pill">بلا تحديث: {counters.TransactionsWithoutRecentUpdate}</span>
        </div>
        """;
    }

    private static string RenderRecommendations(InstitutionalReportModel model)
    {
        if (model.Recommendations.Count == 0)
        {
            return """
            <h2 class="section-title">التوصيات التنفيذية</h2>
            <div class="empty-state">لا توجد توصيات مولّدة من نتائج هذا التقرير.</div>
            """;
        }

        var cards = string.Join(string.Empty, model.Recommendations.Select(r =>
            $"""
            <article class="recommendation-card">
              <div class="priority">{Esc(DisplayValue(r.Priority))}</div>
              <h3 style="margin:8px 0 6px;font-size:14px;color:var(--report-primary);">{Esc(r.Observation)}</h3>
              <p style="margin:0 0 8px;">{Esc(r.RequiredAction)}</p>
              <div style="font-size:11px;color:var(--report-secondary);">
                {Esc(RecommendationOwnerLabel(r.ResponsibleDepartment))} — المصدر: {Esc(DisplayValue(r.SourceLabel))} — الموعد: {Esc(r.TargetDate ?? "—")}
              </div>
            </article>
            """));
        return $"""<h2 class="section-title">التوصيات التنفيذية</h2>{cards}""";
    }

    private static string RenderActionPlan(InstitutionalReportModel model)
    {
        if (model.Analysis.Recommendations.Count == 0)
            return """<h2 class="section-title">التوصيات وخطة الإجراءات</h2><div class="empty-state">لا توجد توصيات تحليلية قابلة للتنفيذ.</div>""";

        var rows = string.Join(string.Empty, model.Analysis.Recommendations.Select(r =>
            $"""
            <tr>
              <td>{Esc(DisplayValue(r.Priority))}</td><td>{Esc(DisplayValue(r.SourceFindingCode))}</td><td>{Esc(DisplayValue(r.RecommendationText))}</td>
              <td>{Esc(ResponsibleScopeLabel(r.ResponsibleScope))}</td><td class="cell--number">{r.SuggestedDueDays}</td><td>{Esc(DisplayValue(r.Status))}</td>
            </tr>
            """));
        return $"""
        <h2 class="section-title">التوصيات وخطة الإجراءات</h2>
        <table class="report-table">
          <thead><tr><th>الأولوية</th><th>النتيجة</th><th>الإجراء</th><th>المسؤول</th><th>المدة المقترحة</th><th>الحالة</th></tr></thead>
          <tbody>{rows}</tbody>
        </table>
        """;
    }

    private static string RenderAppendices(InstitutionalReportModel model) =>
        $"""
        <h2 class="section-title">الجداول التفصيلية والملاحق</h2>
        <div class="info-card">
          <dt>صفوف التفاصيل المصدرة</dt><dd>{model.ExportedDetailRows:N0}</dd>
          <dt>إجمالي النتائج المطابقة</dt><dd>{model.TotalMatchedRows:N0}</dd>
          <dt>هل التفاصيل مقتطعة</dt><dd>{(model.DetailRowsTruncated ? "نعم" : "لا")}</dd>
        </div>
        <p class="section-subtitle">تظهر الجداول التفصيلية في قسم المعاملات التفصيلية وملفات XLSX حسب الصلاحيات وحدود الصفوف.</p>
        """;

    private static string RenderMethodology(InstitutionalReportModel model)
    {
        var m = model.Analysis.Methodology;
        var deferred = string.Join(string.Empty, m.DeferredMetrics.Select(item => $"<li>{Esc(DisplayValue(item))}</li>"));
        return $"""
        <h2 class="section-title">المنهجية والتعريفات</h2>
        <dl class="info-card">
          <dt>اسم التقرير</dt><dd>{Esc(m.ReportName)}</dd>
          <dt>إصدار التقرير</dt><dd>{Esc(m.ReportVersion)}</dd>
          <dt>وقت الإنشاء</dt><dd>{FormatDateTime(m.GeneratedAtUtc)}</dd>
          <dt>فترة البيانات</dt><dd>{Esc(m.DataPeriod)}</dd>
          <dt>أساس الفترة الزمنية</dt><dd>{Esc(DisplayValue(m.PeriodBasis))}</dd>
          <dt>فترة المقارنة</dt><dd>{Esc(m.ComparisonPeriod)}</dd>
          <dt>الفلاتر</dt><dd>{Esc(m.Filters)}</dd>
          <dt>مصدر البيانات</dt><dd>{Esc(DisplayValue(m.DataSource))}</dd>
          <dt>نمط البيانات</dt><dd>{Esc(DisplayValue(m.SnapshotMode))}</dd>
          <dt>حدود الصفوف</dt><dd>{Esc(DisplayValue(m.RowLimits))}</dd>
          <dt>إصدار الحساب</dt><dd>{Esc(m.CalculationVersion)}</dd>
          <dt>حالة الاعتماد</dt><dd>{Esc(DisplayValue(m.ApprovalStatus))}</dd>
        </dl>
        {(deferred.Length > 0 ? $"<h3>عناصر مؤجلة أو غير قابلة للحساب</h3><ul>{deferred}</ul>" : string.Empty)}
        """;
    }

    private static string RenderTransactions(InstitutionalReportModel model, List<TransactionDetailRowDto> rows, bool isFirstPage)
    {
        var body = string.Join(string.Empty, rows.Select(r =>
            $"<tr><td class=\"cell--id\">{Esc(r.TrackingNumber)}</td><td class=\"cell--id\">{Esc(r.IncomingNumber)}</td><td class=\"cell--date\">{FormatDate(r.IncomingDate)}</td><td class=\"cell--subject\">{Esc(r.Subject)}</td><td>{Esc(DisplayValue(r.IncomingParty))}</td><td>{Esc(NormalizeDepartmentName(r.ResponsibleDepartment))}</td><td>{Esc(DisplayValue(r.Status))}</td><td>{Esc(DisplayValue(r.FollowUpStage))}</td><td class=\"cell--number\">{r.ElapsedDays}</td><td class=\"cell--date\">{Esc(r.DueDate ?? "—")}</td><td>{Esc(DisplayValue(r.ResponseState))}</td></tr>"));
        var totalResults = model.TotalMatchedRows > 0 ? model.TotalMatchedRows : model.Transactions.Count;
        var pageNote = rows.Count < totalResults
            ? $" — عرض {rows.Count:N0} صف في هذه الصفحة من {model.ExportedDetailRows:N0} صفًا مصدَّرًا"
            : string.Empty;
        var truncationNote = isFirstPage && model.DetailRowsTruncated
            ? $"""<div class="partial-note">إجمالي النتائج المطابقة: {totalResults:N0} — صفوف التفاصيل في هذا الملف: {model.ExportedDetailRows:N0}</div>"""
            : string.Empty;
        return $"""
        <h2 class="section-title">المعاملات التفصيلية</h2>
        {truncationNote}
        <p class="section-subtitle">إجمالي النتائج: {totalResults:N0} معاملة{pageNote} — الفترة من {FormatDate(model.Metadata.PeriodFrom)} إلى {FormatDate(model.Metadata.PeriodTo)}</p>
        <table class="report-table report-table--transactions"><thead><tr>
          <th>رقم المعاملة</th><th>رقم الوارد</th><th>تاريخ الوارد</th><th>الموضوع</th><th>الجهة</th>
          <th>الإدارة</th><th>الحالة</th><th>مرحلة المتابعة</th><th>الأيام</th><th>المهلة</th><th>حالة الرد</th>
        </tr></thead><tbody>{body}</tbody></table>
        """;
    }

    private static string RenderDepartmentTransactionDetails(
        InstitutionalReportModel model, List<TransactionDetailRowDto> rows, bool isFirstPage)
    {
        var body = string.Join(string.Empty, rows.Select(r =>
        {
            var matched = string.Join("<br/>", r.MatchedDepartments.Select(m =>
                $"{Esc(NormalizeDepartmentName(m.DepartmentName))} ({Esc(m.Relation)})"));
            return $"<tr><td class=\"cell--number\">{r.Sequence}</td><td class=\"cell--id\">{Esc(r.IncomingNumber)}</td><td class=\"cell--date\">{FormatDate(r.IncomingDate)}</td><td class=\"cell--subject\">{Esc(r.Subject)}</td><td>{Esc(DisplayValue(r.IncomingParty))}</td><td class=\"cell--relation\">{matched}</td><td>{Esc(DisplayValue(r.Status))}</td><td>{Esc(DisplayValue(r.Priority))}</td><td class=\"cell--date\">{Esc(r.DueDate ?? "—")}</td><td class=\"cell--date\">{Esc(r.LastActionDate ?? "—")}</td></tr>";
        }));
        var totalResults = model.TotalMatchedRows > 0 ? model.TotalMatchedRows : model.Transactions.Count;
        var pageNote = rows.Count < totalResults
            ? $" — عرض {rows.Count:N0} صف في هذه الصفحة من {model.ExportedDetailRows:N0} صفًا مصدَّرًا"
            : string.Empty;
        // Truncation/grouping notices only repeat on the first detail page: they add no new
        // information on continuation pages and their vertical space is better spent on rows.
        var truncationNote = isFirstPage && model.DetailRowsTruncated
            ? $"""<div class="partial-note">إجمالي النتائج المطابقة: {totalResults:N0} — صفوف التفاصيل في هذا الملف: {model.ExportedDetailRows:N0}</div>"""
            : string.Empty;
        var groupingNote = isFirstPage && model.GroupDetailsByDepartmentEffective
            ? """<div class="partial-note">التفاصيل مجمّعة حسب الإدارة: قد تظهر المعاملة المشتركة تحت أكثر من إدارة — هذا التجميع غير تراكمي (لا يُجمع عدد الصفوف كإجمالي معاملات).</div>"""
            : string.Empty;
        return $"""
        <h2 class="dept-transactions-page-title">المعاملات التفصيلية — تقرير معاملات إدارة</h2>
        {truncationNote}
        {groupingNote}
        <p class="dept-transactions-page-subtitle">إجمالي النتائج: {totalResults:N0} معاملة{pageNote} — الفترة من {FormatDate(model.Metadata.PeriodFrom)} إلى {FormatDate(model.Metadata.PeriodTo)}</p>
        <table class="report-table report-table--department-transactions"><thead><tr>
          <th>#</th><th>رقم الوارد</th><th>تاريخ الوارد</th><th>الموضوع</th><th>الجهة الوارد منها</th>
          <th>الإدارة/الإدارات المطابقة</th><th>الحالة</th><th>الأولوية</th><th>المهلة</th><th>آخر إجراء</th>
        </tr></thead><tbody>{body}</tbody></table>
        """;
    }

    private static string RenderMetadata(InstitutionalReportModel model)
    {
        var warnings = string.Join(string.Empty, model.IntegrityWarnings.Select(w =>
            $"<li><strong>{Esc(DisplayValue(w.Code))}</strong>: {Esc(DisplayValue(w.Message))}</li>"));
        var filterSummary = BuildFilterSummary(model.Filters);
        return $"""
        <h2 class="section-title">بيانات التقرير والفلاتر</h2>
        <dl class="info-card">
              <dt>رقم التقرير</dt><dd>{Esc(model.Metadata.ReportNumber)}</dd>
              <dt>نوع التقرير</dt><dd>{Esc(model.Metadata.ReportTypeName)}</dd>
              <dt>الفترة</dt><dd>من {FormatDate(model.Metadata.PeriodFrom)} إلى {FormatDate(model.Metadata.PeriodTo)}</dd>
          <dt>معرف التحقق</dt><dd>{Esc(model.Metadata.VerificationId)}</dd>
          <dt>وقت الإنشاء</dt><dd>{FormatDateTime(model.Metadata.GeneratedAt)}</dd>
          <dt>حالة التقرير</dt><dd>{Esc(DisplayValue(model.Analysis.Methodology.ApprovalStatus))}</dd>
          <dt>إجمالي النتائج المطابقة</dt><dd>{model.TotalMatchedRows:N0}</dd>
          <dt>صفوف التفاصيل المحمّلة</dt><dd>{model.Transactions.Count:N0}</dd>
          <dt>صفوف التفاصيل المصدرة</dt><dd>{model.ExportedDetailRows:N0}</dd>
          <dt>إجمالي الصفحات</dt><dd>{model.Metadata.TotalPages:N0}</dd>
          <dt>هل التفاصيل مقتطعة</dt><dd>{(model.DetailRowsTruncated ? "نعم" : "لا")}</dd>
          <dt>عدد أجزاء PDF</dt><dd>{(model.DetailPartsCount > 0 ? model.DetailPartsCount.ToString() : "—")}</dd>
          <dt>إصدار القالب</dt><dd>{Esc(InstitutionalReportStyles.TemplateVersion)}</dd>
          <dt>البصمة</dt><dd>{Esc(model.Metadata.FileFingerprint ?? "—")}</dd>
          <dt>الفلاتر</dt><dd>{Esc(filterSummary)}</dd>
          <dt>حالة المقارنة</dt><dd>{Esc(ComparisonStatusLabel(model))}</dd>
          <dt>ترتيب التفاصيل</dt><dd>{Esc(DetailSortByLabel(model.DetailSortByEffective))}</dd>
          <dt>تجميع التفاصيل حسب الإدارة</dt><dd>{(model.GroupDetailsByDepartmentEffective ? "نعم (غير تراكمي)" : "لا")}</dd>
        </dl>
        {(warnings.Length > 0 ? $"<h3>تحذيرات سلامة البيانات</h3><ul>{warnings}</ul>" : string.Empty)}
        """;
    }

    private static string BuildFilterSummary(ReportFiltersDto filters)
    {
        var parts = new List<string>();
        if (filters.DateFrom.HasValue || filters.DateTo.HasValue)
        {
            var from = filters.DateFrom.HasValue ? FormatDate(filters.DateFrom.Value) : "—";
            var to = filters.DateTo.HasValue ? FormatDate(filters.DateTo.Value) : "—";
            parts.Add($"التاريخ: من {from} إلى {to}");
        }
        if (filters.DepartmentIds.Count > 0)
            parts.Add($"إدارات: {filters.DepartmentIds.Count}");
        if (filters.PartyIds.Count > 0)
            parts.Add($"جهات: {filters.PartyIds.Count}");
        if (filters.CategoryIds.Count > 0)
            parts.Add($"تصنيفات: {filters.CategoryIds.Count}");
        if (filters.Priorities.Count > 0)
            parts.Add($"الأولويات: {string.Join("، ", filters.Priorities)}");
        if (filters.Statuses.Count > 0)
            parts.Add($"الحالات: {string.Join("، ", filters.Statuses)}");
        if (filters.IncludeOverdue)
            parts.Add("المتأخرة فقط");
        if (!string.IsNullOrWhiteSpace(filters.Search))
            parts.Add($"بحث: {filters.Search.Trim()}");
        return parts.Count == 0 ? "بدون فلاتر إضافية" : string.Join(" | ", parts);
    }

    private static string ComparisonStatusLabel(InstitutionalReportModel model)
    {
        if (model.ComparisonUnavailableReason is not null)
            return $"غير متاحة — {model.ComparisonUnavailableReason}";
        if (model.Analysis.ComparisonMode == ReportComparisonMode.None)
            return "غير مطبقة";
        return model.Analysis.ComparisonMode switch
        {
            ReportComparisonMode.PreviousEquivalentPeriod => "الفترة السابقة المكافئة",
            ReportComparisonMode.YearOverYear => "نفس الفترة من العام السابق",
            ReportComparisonMode.Custom => "فترة مخصصة",
            _ => "غير مطبقة"
        };
    }

    private static string DetailSortByLabel(ReportDetailSortBy sortBy) => sortBy switch
    {
        ReportDetailSortBy.IncomingDateDesc => "تاريخ الوارد (الأحدث أولاً)",
        ReportDetailSortBy.Department => "الإدارة",
        ReportDetailSortBy.Status => "الحالة",
        ReportDetailSortBy.Priority => "الأولوية",
        ReportDetailSortBy.DueDate => "المهلة",
        _ => "افتراضي"
    };

    private static List<RiskAlertRowDto> BuildUnifiedRiskAlerts(InstitutionalReportModel model)
    {
        var alerts = model.Risks.ToList();
        var existingCriticalAlerts = alerts
            .Where(r => r.Severity == RiskSeverity.Critical)
            .Select(r => r.Alert)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var c in model.Analysis.CriticalCases)
        {
            var alert = $"حالة حرجة: {c.Subject}";
            if (existingCriticalAlerts.Contains(alert))
                continue;

            alerts.Add(new RiskAlertRowDto
            {
                Sequence = alerts.Count + 1,
                Alert = alert,
                DepartmentName = c.Department,
                Severity = RiskSeverityFromAnalytical(c.Severity),
                SeverityLabel = SeverityLabel(c.Severity),
                ElapsedDays = c.AgeDays,
                SuggestedAction = string.IsNullOrWhiteSpace(c.RequiredAction)
                    ? "مراجعة الحالة وتوثيق الإجراء التالي."
                    : c.RequiredAction
            });
        }

        return alerts
            .OrderByDescending(r => r.Severity)
            .ThenByDescending(r => r.ElapsedDays)
            .Select((r, index) =>
            {
                r.Sequence = index + 1;
                return r;
            })
            .ToList();
    }

    private static RiskSeverity RiskSeverityFromAnalytical(AnalyticalSeverity severity) => severity switch
    {
        AnalyticalSeverity.Critical => RiskSeverity.Critical,
        AnalyticalSeverity.High => RiskSeverity.High,
        AnalyticalSeverity.Medium => RiskSeverity.Elevated,
        _ => RiskSeverity.Informational
    };

    private static string KpiGroupLabel(AnalyticalKpiDto kpi)
    {
        var key = kpi.Key.ToLowerInvariant();
        if (key.Contains("total", StringComparison.Ordinal) || key.Contains("count", StringComparison.Ordinal))
            return "مؤشرات الحجم";
        if (key.Contains("closed", StringComparison.Ordinal) || key.Contains("completion", StringComparison.Ordinal) || key.Contains("ontime", StringComparison.Ordinal))
            return "مؤشرات الإنجاز";
        if (key.Contains("overdue", StringComparison.Ordinal) || key.Contains("delay", StringComparison.Ordinal) || key.Contains("backlog", StringComparison.Ordinal))
            return "مؤشرات التأخر";
        if (key.Contains("quality", StringComparison.Ordinal) || key.Contains("data", StringComparison.Ordinal))
            return "مؤشرات جودة البيانات";
        return "مؤشرات المتابعة";
    }

    private static bool IsUndefinedDepartment(string? value)
    {
        var normalized = (value ?? string.Empty).Trim();
        return normalized.Length == 0 || normalized is "—" or "-" or "غير محددة" or "غير محدد";
    }

    private static string NormalizeDepartmentName(string? value) =>
        IsUndefinedDepartment(value) ? UndefinedDepartmentLabel : DisplayValue(value);

    private static string ResponsibleScopeLabel(string? value) =>
        IsUndefinedDepartment(value) ? "مالك البيانات" : DisplayValue(value);

    private static string RecommendationOwnerLabel(string? value) =>
        IsUndefinedDepartment(value)
            ? "ملاحظة جودة بيانات: معاملات بلا إدارة مختصة"
            : $"الإدارة: {DisplayValue(value)}";

    private static string DisplayValue(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "—";

        var trimmed = value.Trim();
        var normalized = trimmed.Replace('_', ' ').ToUpperInvariant();
        return normalized switch
        {
            "DRAFT" => "مسودة",
            "APPROVED" => "معتمد",
            "FINAL" => "نهائي",
            "ARCHIVED" => "مؤرشف",
            "PROPOSED" => "مقترحة",
            "HIGH" => "عالية",
            "MEDIUM" => "متوسطة",
            "LOW" => "منخفضة",
            "CRITICAL" => "حرجة",
            "INFO" or "INFORMATIONAL" => "معلوماتية",
            "ISSUE QUALITY DATA" => "جودة البيانات",
            "EXTERNAL PENDING RESPONSES" => "معاملات منتظرة من جهة خارجية",
            "RESPONSES PENDING EXTERNAL" => "متابعة الردود الخارجية",
            "CASES CRITICAL" or "CRITICAL CASES" => "مراجعة الحالات الحرجة",
            "OVERDUE RATE INCREASED" => "ارتفاع نسبة التأخر",
            "BACKLOG INCREASED" => "زيادة الرصيد المفتوح",
            "DEPARTMENT BACKLOG CONCENTRATION" => "تركز الرصيد المفتوح لدى إدارة",
            "DATA QUALITY ISSUE" => "ملاحظة جودة بيانات",
            "MISSING DUE DATE" => "مهلة رد مفقودة",
            "RESPONSEDUEDATE" => "تاريخ مهلة الرد",
            "TRANSACTION.ID" => "معرف المعاملة",
            "LIVE QUERY" => "استعلام مباشر من قاعدة البيانات",
            "LIVE PREVIEW / GENERATED REPORT" => "معاينة مباشرة / تقرير مولد",
            _ when trimmed.StartsWith("DetailLimit=", StringComparison.OrdinalIgnoreCase)
                => $"حد صفوف التفاصيل = {trimmed["DetailLimit=".Length..]}",
            _ when trimmed.StartsWith("AverageFirstActionHours:", StringComparison.OrdinalIgnoreCase)
                => "متوسط ساعات أول إجراء: يحتاج حدث أول إجراء موثوق.",
            _ => trimmed
        };
    }

    private static string SeverityLabel(AnalyticalSeverity severity) => severity switch
    {
        AnalyticalSeverity.Critical => "حرج",
        AnalyticalSeverity.High => "مرتفع",
        AnalyticalSeverity.Medium => "متوسط",
        AnalyticalSeverity.Low => "منخفض",
        _ => "—"
    };

    private static string TrendLabel(TrendDirection direction) => direction switch
    {
        TrendDirection.Improved => "تحسن",
        TrendDirection.Declined => "تراجع",
        TrendDirection.Stable => "مستقر",
        TrendDirection.NotComparable => "غير قابل للمقارنة",
        _ => "—"
    };

    private static RenderedReportPageDto CreatePartialCoverPage(RenderedReportManifestDto source, List<RenderedReportPageDto> selected) =>
        new()
        {
            OriginalPageNumber = 0,
            RenderedPageNumber = 0,
            SectionId = ReportSectionId.PartialCover,
            SectionName = "غلاف النسخة الجزئية",
            PageTitle = "غلاف النسخة الجزئية",
            PdfProfileName = InstitutionalReportPdfProfiles.StandardPortrait.Name,
            HtmlContent = WrapPage($"""
                <div class="partial-note">نسخة جزئية من التقرير</div>
                <h2 class="section-title">غلاف النسخة الجزئية</h2>
                <dl class="info-card">
                  <dt>رقم التقرير الأصلي</dt><dd>{Esc(source.ReportId)}</dd>
                  <dt>الصفحات المضمنة</dt><dd>{string.Join(", ", selected.Select(p => p.OriginalPageNumber))}</dd>
                  <dt>تاريخ التصدير</dt><dd>{FormatDate(DateTime.UtcNow)}</dd>
                </dl>
                """,
                new PageChromeOptions(
                    PageNumber: 0,
                    TotalPages: 0,
                    Partial: true,
                    Profile: InstitutionalReportPdfProfiles.StandardPortrait,
                    SectionId: ReportSectionId.PartialCover,
                    ReportTitle: "غلاف النسخة الجزئية",
                    ReportId: source.ReportId))
        };

    private static RenderedReportPageDto CreatePartialManifestPage(RenderedReportManifestDto source, ReportExportRequestDto request, List<RenderedReportPageDto> selected) =>
        new()
        {
            OriginalPageNumber = 0,
            RenderedPageNumber = 0,
            SectionId = ReportSectionId.PartialManifest,
            SectionName = "تعريف النسخة الجزئية",
            PageTitle = "تعريف النسخة الجزئية",
            PdfProfileName = InstitutionalReportPdfProfiles.StandardPortrait.Name,
            HtmlContent = WrapPage($"""
                <h2 class="section-title">تعريف النسخة الجزئية</h2>
                <p>رقم التقرير الأصلي: {Esc(source.ReportId)}</p>
                <p>الصفحات الأصلية المختارة: {string.Join(", ", selected.Select(p => p.OriginalPageNumber))}</p>
                <p>الأقسام المضمنة: {string.Join("، ", selected.Select(p => p.SectionName).Distinct())}</p>
                {(string.IsNullOrWhiteSpace(request.Reason) ? string.Empty : $"<p>سبب الإنشاء: {Esc(request.Reason)}</p>")}
                """,
                new PageChromeOptions(
                    PageNumber: 0,
                    TotalPages: 0,
                    Partial: true,
                    Profile: InstitutionalReportPdfProfiles.StandardPortrait,
                    SectionId: ReportSectionId.PartialManifest,
                    ReportTitle: "تعريف النسخة الجزئية",
                    ReportId: source.ReportId))
        };

    private static string FormatDate(DateTime value) =>
        value.ToString(DateFormat, CultureInfo.InvariantCulture);

    private static string FormatDateTime(DateTime value) =>
        value.ToString(DateTimeFormat, CultureInfo.InvariantCulture);

    private static string Esc(string? value)
    {
        var encoded = WebUtility.HtmlEncode(value ?? string.Empty);
        if (encoded.Length == 0)
            return encoded;

        try
        {
            encoded = JavascriptProtocolRegex.Replace(encoded, "javascript&#58;");
            encoded = FileProtocolRegex.Replace(encoded, "file&#58;//");
            encoded = ExternalHttpUrlRegex.Replace(encoded, "[رابط خارجي]");
            return encoded;
        }
        catch (RegexMatchTimeoutException)
        {
            throw new InvalidOperationException(
                "تعذر معالجة نص التقرير بأمان بسبب تجاوز مهلة التحقق.");
        }
    }
}
