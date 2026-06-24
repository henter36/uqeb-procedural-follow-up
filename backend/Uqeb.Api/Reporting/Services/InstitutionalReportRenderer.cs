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
    private const int TransactionRowsPerPdfPage = 6;

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
        bool includeTransactionDetails = true)
    {
        var pages = new List<RenderedReportPageDto>();
        var coverPageIndex = -1;

        foreach (var section in sections)
        {
            if (section == ReportSectionId.Cover)
                coverPageIndex = pages.Count;

            AppendSectionPages(model, section, includeTransactionDetails, pages);
        }

        AppendDetailOverflowNoticeIfNeeded(model, includeTransactionDetails, pages);

        model.Metadata.TotalPages = pages.Count;

        if (coverPageIndex >= 0)
        {
            var coverPage = pages[coverPageIndex];
            pages[coverPageIndex] = coverPage with
            {
                HtmlContent = WrapPage(
                    RenderCover(model),
                    pageNumber: coverPageIndex + 1,
                    totalPages: pages.Count,
                    partial: false,
                    profile: InstitutionalReportPdfProfiles.GetByName(coverPage.PdfProfileName),
                    reportTitle: model.Metadata.Title,
                    reportId: model.Metadata.ReportNumber),
            };
        }

        return BuildManifestResult(model, pages);
    }

    private static void AppendSectionPages(
        InstitutionalReportModel model,
        ReportSectionId section,
        bool includeTransactionDetails,
        List<RenderedReportPageDto> pages)
    {
        switch (section)
        {
            case ReportSectionId.Cover:
                pages.Add(MakePage(section, "الغلاف", string.Empty));
                break;
            case ReportSectionId.ExecutiveSummary:
                pages.Add(MakePage(section, "الملخص التنفيذي", RenderExecutiveSummary(model)));
                break;
            case ReportSectionId.IndicatorsDashboard:
                pages.Add(MakePage(section, "لوحة المؤشرات والاتجاهات", RenderCharts(model)));
                break;
            case ReportSectionId.DepartmentPerformance:
                pages.Add(MakePage(section, "أداء الإدارات", RenderDepartments(model)));
                break;
            case ReportSectionId.RisksAndAlerts:
                pages.Add(MakePage(section, "المخاطر والتنبيهات", RenderRisks(model)));
                break;
            case ReportSectionId.ExecutiveRecommendations:
                pages.Add(MakePage(section, "التوصيات التنفيذية", RenderRecommendations(model)));
                break;
            case ReportSectionId.TransactionDetails:
                AppendTransactionDetailPages(model, includeTransactionDetails, pages);
                break;
            case ReportSectionId.ReportMetadata:
                pages.Add(MakePage(section, "بيانات التقرير والفلاتر", RenderMetadata(model)));
                break;
        }
    }

    private static void AppendTransactionDetailPages(
        InstitutionalReportModel model,
        bool includeTransactionDetails,
        List<RenderedReportPageDto> pages)
    {
        if (!includeTransactionDetails)
            return;

        if (model.DetailRowsTruncated)
            pages.Add(MakePage(ReportSectionId.TransactionDetails, "تنبيه صفوف التفاصيل", RenderDetailTruncationNotice(model)));

        foreach (var chunk in model.Transactions.Chunk(TransactionRowsPerPdfPage))
            pages.Add(MakePage(ReportSectionId.TransactionDetails, "المعاملات التفصيلية", RenderTransactions(model, chunk.ToList())));

        if (model.Transactions.Count == 0)
            pages.Add(MakePage(ReportSectionId.TransactionDetails, "المعاملات التفصيلية", RenderTransactions(model, [])));
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
            request.PageNumberingMode ?? PageNumberingMode.Restart,
            source.TotalPages,
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
            PartialExportNote = isPartial ? "هذه نسخة جزئية من التقرير الأصلي." : null
        };
    }

    private static void ApplyFinalPageNumbering(
        List<RenderedReportPageDto> pages,
        PageNumberingMode numberingMode,
        int originalTotalPages,
        bool isPartial,
        string reportTitle,
        string reportId)
    {
        for (var i = 0; i < pages.Count; i++)
        {
            var page = pages[i];
            var isSupplementary = page.SectionId is ReportSectionId.PartialCover or ReportSectionId.PartialManifest;

            int renderedNumber;
            int footerTotal;

            if (numberingMode == PageNumberingMode.Restart || isSupplementary)
            {
                renderedNumber = i + 1;
                footerTotal = pages.Count;
            }
            else
            {
                renderedNumber = page.OriginalPageNumber;
                footerTotal = originalTotalPages;
            }

            pages[i] = page with
            {
                RenderedPageNumber = renderedNumber,
                HtmlContent = InjectFooter(page.HtmlContent, renderedNumber, footerTotal, isPartial, reportTitle, reportId)
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
            HtmlContent = WrapPage(innerHtml, 1, 1, false, profile, title, string.Empty)
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
                1,
                1,
                partial: true,
                profile: InstitutionalReportPdfProfiles.StandardPortrait,
                reportTitle: title,
                reportId: string.Empty)
        };

    private static string WrapPage(
        string content,
        int pageNumber,
        int totalPages,
        bool partial,
        PdfPageProfile profile,
        string reportTitle,
        string reportId) =>
        $"""
        <section class="report-page report-page--{profile.CssClass}" data-page="{pageNumber}" data-profile="{profile.Name}" data-section="{Esc(reportTitle)}">
          {Header(partial)}
          <main class="report-content">{content}</main>
          {BuildFooter(pageNumber, totalPages, partial, reportTitle, reportId)}
        </section>
        """;

    private static string Header(bool partial) =>
        $"""
        <header class="report-header">
          <div class="org">المتابعة الإجرائية<br/>إدارة المتابعة والتقارير</div>
          <div class="meta">{(partial ? "نسخة جزئية" : "تقرير رسمي")}</div>
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

    private static string EffectiveReportTitle(string? reportTitle) =>
        string.IsNullOrWhiteSpace(reportTitle)
            ? DefaultReportTitle
            : reportTitle;

    private static string RenderCover(InstitutionalReportModel model)
    {
        var m = model.Metadata;
        var partialBadge = model.DetailRowsTruncated
            ? """<span class="cover-badge">نسخة ملخصة — التفاصيل مقتطعة</span>"""
            : string.Empty;
        var scopeNote = string.IsNullOrWhiteSpace(m.DepartmentName)
            ? string.Empty
            : $"""<dt>النطاق</dt><dd>{Esc(m.DepartmentName)}</dd>""";
        var confidentiality = string.IsNullOrWhiteSpace(m.ConfidentialityLabel)
            ? string.Empty
            : $"""<dt>مستوى السرية</dt><dd>{Esc(m.ConfidentialityLabel)}</dd>""";
        var totalPages = Math.Max(1, m.TotalPages);
        return $"""
        <div class="cover">
          <div class="cover-main">
            <div style="font-size:13px;color:var(--report-secondary);font-weight:700;">{Esc(m.OrganizationName)}</div>
            <h1 class="cover-title">{Esc(m.Title)}</h1>
            <div class="cover-period">الفترة من {FormatDate(m.PeriodFrom)} إلى {FormatDate(m.PeriodTo)}</div>
            {partialBadge}
            <dl class="info-card">
              <dt>رقم التقرير</dt><dd>{Esc(m.ReportNumber)}</dd>
              <dt>نوع التقرير</dt><dd>{Esc(m.ReportTypeName)}</dd>
              <dt>تاريخ الإصدار</dt><dd>{FormatDate(m.IssueDate)}</dd>
              {scopeNote}
              {confidentiality}
              <dt>إجمالي الصفحات</dt><dd>{totalPages}</dd>
            </dl>
            <div style="display:flex;gap:12px;align-items:center;margin-top:auto;">
              <div class="qr-box" aria-hidden="true">QR<br/>{Esc(m.VerificationId)}</div>
              <div style="font-size:11px;color:var(--report-secondary);line-height:1.7;">
                معرف التحقق: {Esc(m.VerificationId)}<br/>
                وقت الإنشاء: {FormatDateTime(m.GeneratedAt)}<br/>
                إجمالي النتائج المطابقة: {model.TotalMatchedRows:N0}<br/>
                البصمة: {Esc(m.FileFingerprint ?? "—")}
              </div>
            </div>
          </div>
          <div class="cover-accent">
            <div style="font-size:14px;opacity:.9;">المتابعة الإجرائية</div>
            <div style="margin-top:24px;font-size:28px;font-weight:800;line-height:1.5;">تقرير<br/>المتابعة الإجرائية</div>
          </div>
        </div>
        """;
    }

    private static string RenderExecutiveSummary(InstitutionalReportModel model)
    {
        var cards = string.Join(string.Empty, model.Summary.KpiCards.Select(c =>
            $"""<div class="kpi-card"><div class="label">{Esc(c.Title)}</div><div class="value">{Esc(c.Value)}</div>{(c.Delta != null ? $"<div class='delta'>{Esc(c.Delta)}</div>" : string.Empty)}</div>"""));
        return $"""
        <h2 class="section-title">الملخص التنفيذي</h2>
        <div class="kpi-grid">{cards}</div>
        <h3 style="color:#123F32;margin-top:16px;">التقييم التنفيذي</h3>
        <div class="narrative">{Esc(model.Summary.ExecutiveNarrative)}</div>
        """;
    }

    private static string RenderCharts(InstitutionalReportModel model)
    {
        var charts = string.Join(string.Empty, model.Charts.Select(chart =>
        {
            var max = chart.Series.DefaultIfEmpty(new ChartSeriesPointDto()).Max(s => s.Value);
            if (max <= 0) max = 1;
            var bars = string.Join(string.Empty, chart.Series.Select((s, i) =>
            {
                var height = Math.Max(8, (int)Math.Round(s.Value / max * 100));
                var cls = i % 2 == 0 ? "chart-bar" : "chart-bar gold";
                return $"""<div style="flex:1"><div class="{cls}" style="height:{height}px" title="{Esc(s.Label)}: {s.Value}"></div><div class="chart-bar-label">{Esc(s.Label)}</div></div>""";
            }));
            var footnote = chart.Footnote != null ? $"<div class='chart-footnote'>{Esc(chart.Footnote)}</div>" : string.Empty;
            return $"""<div class="chart-card"><div class="chart-title">{Esc(chart.Title)}</div><div class="chart-bars">{bars}</div>{footnote}</div>""";
        }));
        return $"""<h2 class="section-title">لوحة المؤشرات والاتجاهات</h2><div class="charts-grid">{charts}</div>""";
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
              <td class="cell--department">{Esc(d.DepartmentName)}</td>
              <td class="cell--number">{d.TotalTransactions}</td><td class="cell--number">{d.ClosedCount}</td><td class="cell--number">{d.OpenCount}</td>
              <td class="cell--number">{d.WaitingForStatementCount}</td><td class="cell--number">{d.OverdueCount}</td><td class="cell--number">{d.JointDepartmentCount}</td>
              <td class="cell--number">{d.AverageCompletionDays:N1}</td><td class="cell--number">{d.OnTimeCompletionRate:N1}%</td>
              <td class="cell--rating {ratingClass}">{Esc(d.RatingLabel)}</td>
            </tr>
            """;
        }));
        var totals = model.DepartmentPerformance.Aggregate(
            (Total: 0, Closed: 0, Open: 0, Waiting: 0, Overdue: 0, Joint: 0),
            (acc, row) => (acc.Total + row.TotalTransactions, acc.Closed + row.ClosedCount, acc.Open + row.OpenCount,
                acc.Waiting + row.WaitingForStatementCount, acc.Overdue + row.OverdueCount, acc.Joint + row.JointDepartmentCount));
        return $"""
        <h2 class="section-title">أداء الإدارات</h2>
        <table class="report-table report-table--departments">
          <thead><tr>
            <th>الإدارة</th><th>إجمالي</th><th>مغلقة</th><th>مفتوحة</th><th>بانتظار إفادة</th>
            <th>متأخرة</th><th>إدارات مشتركة</th><th>متوسط الإنجاز</th><th>ضمن المهلة</th><th>التقييم</th>
          </tr></thead>
          <tbody>{rows}
          <tr class="report-table__total-row">
            <td>الإجمالي</td><td class="cell--number">{totals.Total}</td><td class="cell--number">{totals.Closed}</td><td class="cell--number">{totals.Open}</td>
            <td class="cell--number">{totals.Waiting}</td><td class="cell--number">{totals.Overdue}</td><td class="cell--number">{totals.Joint}</td>
            <td>—</td><td>—</td><td>—</td>
          </tr></tbody>
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
            var items = model.Risks.Where(r => r.Severity == group.MinSeverity).ToList();
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
                $"<tr><td>{r.Sequence}</td><td>{Esc(r.Alert)}</td><td>{Esc(r.DepartmentName)}</td><td>{r.ElapsedDays}</td><td>{Esc(r.SuggestedAction)}</td></tr>"));
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
              <div class="priority">{Esc(r.Priority)}</div>
              <h3 style="margin:8px 0 6px;font-size:14px;color:var(--report-primary);">{Esc(r.Observation)}</h3>
              <p style="margin:0 0 8px;">{Esc(r.RequiredAction)}</p>
              <div style="font-size:11px;color:var(--report-secondary);">
                الإدارة: {Esc(r.ResponsibleDepartment)} — المصدر: {Esc(r.SourceLabel)} — الموعد: {Esc(r.TargetDate ?? "—")}
              </div>
            </article>
            """));
        return $"""<h2 class="section-title">التوصيات التنفيذية</h2>{cards}""";
    }

    private static string RenderTransactions(InstitutionalReportModel model, List<TransactionDetailRowDto> rows)
    {
        var body = string.Join(string.Empty, rows.Select(r =>
            $"<tr><td class=\"cell--number\">{r.Sequence}</td><td class=\"cell--id\">{Esc(r.TrackingNumber)}</td><td class=\"cell--id\">{Esc(r.IncomingNumber)}</td><td class=\"cell--date\">{FormatDate(r.IncomingDate)}</td><td class=\"cell--subject\">{Esc(r.Subject)}</td><td>{Esc(r.IncomingParty)}</td><td>{Esc(r.ResponsibleDepartment)}</td><td>{Esc(r.JointDepartments)}</td><td>{Esc(r.Priority)}</td><td>{Esc(r.Status)}</td><td>{Esc(r.FollowUpStage)}</td><td class=\"cell--number\">{r.ElapsedDays}</td><td class=\"cell--date\">{Esc(r.DueDate ?? "—")}</td><td class=\"cell--date\">{Esc(r.LastActionDate ?? "—")}</td><td>{Esc(r.ResponseState)}</td></tr>"));
        var totalResults = model.TotalMatchedRows > 0 ? model.TotalMatchedRows : model.Transactions.Count;
        var pageNote = rows.Count < totalResults
            ? $" — عرض {rows.Count:N0} صف في هذه الصفحة من {model.ExportedDetailRows:N0} صفًا مصدَّرًا"
            : string.Empty;
        var truncationNote = model.DetailRowsTruncated
            ? $"""<div class="partial-note">إجمالي النتائج المطابقة: {totalResults:N0} — صفوف التفاصيل في هذا الملف: {model.ExportedDetailRows:N0}</div>"""
            : string.Empty;
        return $"""
        <h2 class="section-title">المعاملات التفصيلية</h2>
        {truncationNote}
        <p class="section-subtitle">إجمالي النتائج: {totalResults:N0} معاملة{pageNote} — الفترة من {FormatDate(model.Metadata.PeriodFrom)} إلى {FormatDate(model.Metadata.PeriodTo)}</p>
        <table class="report-table report-table--transactions"><thead><tr>
          <th>م</th><th>رقم المعاملة</th><th>رقم الوارد</th><th>تاريخ الوارد</th><th>الموضوع</th><th>الجهة</th>
          <th>الإدارة المختصة</th><th>الإدارات المشتركة</th><th>الأولوية</th><th>الحالة</th><th>مرحلة المتابعة</th>
          <th>الأيام</th><th>المهلة</th><th>آخر إجراء</th><th>حالة الرد</th>
        </tr></thead><tbody>{body}</tbody></table>
        """;
    }

    private static string RenderMetadata(InstitutionalReportModel model)
    {
        var warnings = string.Join(string.Empty, model.IntegrityWarnings.Select(w =>
            $"<li><strong>{Esc(w.Code)}</strong>: {Esc(w.Message)}</li>"));
        var filterSummary = BuildFilterSummary(model.Filters);
        return $"""
        <h2 class="section-title">بيانات التقرير والفلاتر</h2>
        <dl class="info-card">
          <dt>رقم التقرير</dt><dd>{Esc(model.Metadata.ReportNumber)}</dd>
          <dt>نوع التقرير</dt><dd>{Esc(model.Metadata.ReportTypeName)}</dd>
          <dt>الفترة</dt><dd>من {FormatDate(model.Metadata.PeriodFrom)} إلى {FormatDate(model.Metadata.PeriodTo)}</dd>
          <dt>تاريخ الإنشاء</dt><dd>{FormatDateTime(model.Metadata.GeneratedAt)}</dd>
          <dt>إجمالي النتائج المطابقة</dt><dd>{model.TotalMatchedRows:N0}</dd>
          <dt>صفوف التفاصيل المحمّلة</dt><dd>{model.Transactions.Count:N0}</dd>
          <dt>صفوف التفاصيل المصدرة</dt><dd>{model.ExportedDetailRows:N0}</dd>
          <dt>هل التفاصيل مقتطعة</dt><dd>{(model.DetailRowsTruncated ? "نعم" : "لا")}</dd>
          <dt>عدد أجزاء PDF</dt><dd>{(model.DetailPartsCount > 0 ? model.DetailPartsCount.ToString() : "—")}</dd>
          <dt>إصدار القالب</dt><dd>{Esc(InstitutionalReportStyles.TemplateVersion)}</dd>
          <dt>البصمة</dt><dd>{Esc(model.Metadata.FileFingerprint ?? "—")}</dd>
          <dt>الفلاتر</dt><dd>{Esc(filterSummary)}</dd>
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
        if (!string.IsNullOrWhiteSpace(filters.Search))
            parts.Add($"بحث: {filters.Search.Trim()}");
        return parts.Count == 0 ? "بدون فلاتر إضافية" : string.Join(" | ", parts);
    }

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
                0,
                0,
                partial: true,
                profile: InstitutionalReportPdfProfiles.StandardPortrait,
                reportTitle: "غلاف النسخة الجزئية",
                reportId: source.ReportId)
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
                0,
                0,
                partial: true,
                profile: InstitutionalReportPdfProfiles.StandardPortrait,
                reportTitle: "تعريف النسخة الجزئية",
                reportId: source.ReportId)
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
