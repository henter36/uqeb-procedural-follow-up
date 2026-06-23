using System.Text;
using Uqeb.Api.Reporting.Assets;
using Uqeb.Api.Reporting.DTOs;
using Uqeb.Api.Reporting.Enums;

namespace Uqeb.Api.Reporting.Services;

public sealed class InstitutionalReportRenderer
{
    private const string Css = """
        @page { size: A4 portrait; margin: 18mm 14mm 20mm 14mm; }
        * { box-sizing: border-box; }
        body { margin: 0; font-family: 'Uqeb Report Arabic', 'IBM Plex Sans Arabic', 'Noto Sans Arabic', 'Cairo', Tahoma, sans-serif; color: #17211D; background: #fff; direction: rtl; }
        .report-page { width: 210mm; min-height: 297mm; padding: 14mm; page-break-after: always; position: relative; background: #fff; }
        .report-header { display: flex; justify-content: space-between; align-items: center; border-bottom: 2px solid #123F32; padding-bottom: 8px; margin-bottom: 16px; }
        .report-header .org { color: #123F32; font-weight: 700; font-size: 13px; }
        .report-header .meta { color: #2F6B58; font-size: 11px; text-align: left; }
        .report-footer { position: absolute; left: 14mm; right: 14mm; bottom: 10mm; border-top: 1px solid #D9E1DD; padding-top: 6px; display: flex; justify-content: space-between; font-size: 10px; color: #2F6B58; }
        .section-title { color: #123F32; font-size: 22px; font-weight: 700; margin: 0 0 14px; border-right: 4px solid #C5A253; padding-right: 10px; }
        .cover { display: grid; grid-template-columns: 1.1fr 0.9fr; gap: 20px; align-items: stretch; min-height: 250mm; }
        .cover-accent { background: linear-gradient(160deg, #123F32 0%, #2F6B58 100%); border-radius: 12px; padding: 24px; color: #fff; position: relative; overflow: hidden; }
        .cover-accent::after { content: ''; position: absolute; inset-inline-start: -40px; bottom: -40px; width: 180px; height: 180px; border: 3px solid #C5A253; border-radius: 50%; opacity: .35; }
        .cover-main { display: flex; flex-direction: column; justify-content: center; gap: 12px; padding: 20px 8px; }
        .cover-title { font-size: 34px; line-height: 1.3; color: #123F32; font-weight: 800; margin: 0; }
        .cover-period { font-size: 16px; color: #2F6B58; }
        .info-card { background: #EAF2EE; border: 1px solid #D9E1DD; border-radius: 10px; padding: 14px; }
        .info-card dt { color: #2F6B58; font-size: 12px; margin-bottom: 2px; }
        .info-card dd { margin: 0 0 10px; font-weight: 700; color: #123F32; }
        .kpi-grid { display: grid; grid-template-columns: repeat(4, 1fr); gap: 10px; }
        .kpi-card { background: #F4F6F5; border: 1px solid #D9E1DD; border-radius: 10px; padding: 12px; min-height: 88px; }
        .kpi-card .label { font-size: 12px; color: #2F6B58; margin-bottom: 6px; }
        .kpi-card .value { font-size: 24px; font-weight: 800; color: #123F32; }
        .kpi-card .delta { font-size: 11px; color: #C5A253; margin-top: 4px; }
        .narrative { background: #F4F6F5; border-radius: 10px; padding: 14px; line-height: 1.8; font-size: 13px; margin-top: 14px; }
        .charts-grid { display: grid; grid-template-columns: repeat(2, 1fr); gap: 12px; }
        .chart-card { border: 1px solid #D9E1DD; border-radius: 10px; padding: 10px; min-height: 180px; }
        .chart-title { font-size: 13px; font-weight: 700; color: #123F32; margin-bottom: 8px; }
        .chart-bars { display: flex; align-items: flex-end; gap: 6px; height: 120px; }
        .chart-bar { flex: 1; background: #2F6B58; border-radius: 4px 4px 0 0; min-width: 12px; position: relative; }
        .chart-bar.gold { background: #C5A253; }
        .chart-bar-label { font-size: 9px; text-align: center; margin-top: 4px; word-break: break-word; }
        .chart-footnote { font-size: 10px; color: #2F6B58; margin-top: 6px; }
        table.report-table { width: 100%; border-collapse: collapse; font-size: 11px; }
        table.report-table th { background: #123F32; color: #fff; padding: 8px 6px; text-align: right; }
        table.report-table td { border-bottom: 1px solid #D9E1DD; padding: 7px 6px; vertical-align: top; }
        table.report-table tr:nth-child(even) td { background: #F4F6F5; }
        table.report-table tfoot td { background: #123F32; color: #fff; font-weight: 700; }
        .rating-good { color: #123F32; font-weight: 700; }
        .rating-watch { color: #C5A253; font-weight: 700; }
        .rating-critical { color: #B42318; font-weight: 700; }
        .risk-grid { display: grid; grid-template-columns: 1fr 1fr; gap: 12px; }
        .counter-row { display: flex; gap: 8px; flex-wrap: wrap; margin-top: 10px; }
        .counter-pill { background: #EAF2EE; border: 1px solid #D9E1DD; border-radius: 999px; padding: 6px 10px; font-size: 11px; }
        .partial-note { background: #F4EBD7; border: 1px solid #C5A253; border-radius: 8px; padding: 10px; margin-bottom: 12px; font-size: 12px; }
        .qr-box { width: 88px; height: 88px; border: 1px dashed #C5A253; display: grid; place-items: center; font-size: 10px; color: #2F6B58; }
        """;

    public RenderedReportManifestDto RenderManifest(InstitutionalReportModel model, IReadOnlyList<ReportSectionId> sections)
    {
        var pages = new List<RenderedReportPageDto>();
        var pageNo = 1;

        foreach (var section in sections)
        {
            switch (section)
            {
                case ReportSectionId.Cover:
                    pages.Add(MakePage(pageNo++, section, "الغلاف", RenderCover(model)));
                    break;
                case ReportSectionId.ExecutiveSummary:
                    pages.Add(MakePage(pageNo++, section, "الملخص التنفيذي", RenderExecutiveSummary(model)));
                    break;
                case ReportSectionId.IndicatorsDashboard:
                    pages.Add(MakePage(pageNo++, section, "لوحة المؤشرات والاتجاهات", RenderCharts(model)));
                    break;
                case ReportSectionId.DepartmentPerformance:
                    pages.Add(MakePage(pageNo++, section, "أداء الإدارات", RenderDepartments(model)));
                    break;
                case ReportSectionId.RisksAndAlerts:
                    pages.Add(MakePage(pageNo++, section, "المخاطر والتنبيهات", RenderRisks(model)));
                    break;
                case ReportSectionId.ExecutiveRecommendations:
                    pages.Add(MakePage(pageNo++, section, "التوصيات التنفيذية", RenderRecommendations(model)));
                    break;
                case ReportSectionId.TransactionDetails:
                    foreach (var chunk in model.Transactions.Chunk(18))
                        pages.Add(MakePage(pageNo++, section, "المعاملات التفصيلية", RenderTransactions(model, chunk.ToList())));
                    if (model.Transactions.Count == 0)
                        pages.Add(MakePage(pageNo++, section, "المعاملات التفصيلية", RenderTransactions(model, [])));
                    break;
                case ReportSectionId.ReportMetadata:
                    pages.Add(MakePage(pageNo++, section, "بيانات التقرير والفلاتر", RenderMetadata(model)));
                    break;
            }
        }

        var reportId = model.Metadata.ReportNumber;
        model.Metadata.TotalPages = pages.Count;

        return new RenderedReportManifestDto
        {
            ReportId = reportId,
            TotalPages = pages.Count,
            Pages = pages.Select((p, i) => p with
            {
                OriginalPageNumber = i + 1,
                RenderedPageNumber = i + 1
            }).ToList()
        };
    }

    public RenderedReportManifestDto BuildExportManifest(
        RenderedReportManifestDto source,
        IReadOnlyList<int> selectedOriginalPages,
        ReportExportRequestDto request)
    {
        var selected = source.Pages.Where(p => selectedOriginalPages.Contains(p.OriginalPageNumber)).ToList();
        var isPartial = selected.Count < source.Pages.Count;
        var pages = new List<RenderedReportPageDto>();

        if (isPartial && request.IncludePartialCover)
            pages.Add(CreatePartialCoverPage(source, request, selected));

        if (isPartial && request.IncludePartialManifest)
            pages.Add(CreatePartialManifestPage(source, request, selected));

        foreach (var page in selected)
            pages.Add(page);

        ApplyFinalPageNumbering(pages, request.PageNumberingMode, source.TotalPages, isPartial);

        return new RenderedReportManifestDto
        {
            ReportId = source.ReportId,
            TotalPages = pages.Count,
            Pages = pages,
            IsPartialExport = isPartial,
            PartialExportNote = isPartial ? "هذه نسخة جزئية من التقرير الأصلي." : null
        };
    }

    private static void ApplyFinalPageNumbering(
        List<RenderedReportPageDto> pages,
        PageNumberingMode numberingMode,
        int originalTotalPages,
        bool isPartial)
    {
        for (var i = 0; i < pages.Count; i++)
        {
            var page = pages[i];
            var isSupplementary = page.SectionId is ReportSectionId.PartialCover or ReportSectionId.PartialManifest;

            int renderedNumber;
            int footerTotal;

            if (numberingMode == PageNumberingMode.Restart)
            {
                renderedNumber = i + 1;
                footerTotal = pages.Count;
            }
            else if (isSupplementary)
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
                HtmlContent = InjectFooter(page.HtmlContent, renderedNumber, footerTotal, isPartial)
            };
        }
    }

    public string RenderHtmlDocument(RenderedReportManifestDto manifest)
    {
        var sb = new StringBuilder();
        sb.Append("<!DOCTYPE html><html lang='ar' dir='rtl'><head><meta charset='utf-8'><style>")
            .Append(InstitutionalReportFontAssets.BuildFontFaceCss())
            .Append(Css)
            .Append("</style></head><body>");
        foreach (var page in manifest.Pages)
            sb.Append(page.HtmlContent);
        sb.Append("</body></html>");
        return sb.ToString();
    }

    private static RenderedReportPageDto MakePage(int pageNo, ReportSectionId section, string title, string innerHtml) =>
        new()
        {
            OriginalPageNumber = pageNo,
            RenderedPageNumber = pageNo,
            SectionId = section,
            SectionName = title,
            PageTitle = title,
            HtmlContent = WrapPage(innerHtml, pageNo, pageNo, false)
        };

    private static string WrapPage(string content, int pageNumber, int totalPages, bool partial) =>
        $"""
        <section class="report-page" data-page="{pageNumber}">
          {Header(partial)}
          <main>{content}</main>
          {BuildFooter(pageNumber, totalPages, partial)}
        </section>
        """;

    private static string Header(bool partial) =>
        $"""
        <header class="report-header">
          <div class="org">الهيئة العامة للمتابعة الإجرائية<br/>إدارة المتابعة والتقارير</div>
          <div class="meta">{(partial ? "نسخة جزئية" : "تقرير رسمي")}</div>
        </header>
        """;

    private static string InjectFooter(string html, int pageNumber, int totalPages, bool partial)
    {
        var footer = BuildFooter(pageNumber, totalPages, partial);
        var footerStart = html.IndexOf("<footer class=\"report-footer\">", StringComparison.Ordinal);
        if (footerStart >= 0)
        {
            var footerEnd = html.IndexOf("</footer>", footerStart, StringComparison.Ordinal);
            if (footerEnd >= 0)
                return string.Concat(html.AsSpan(0, footerStart), footer, html.AsSpan(footerEnd + "</footer>".Length));
        }

        return html.Replace("</section>", $"{footer}</section>", StringComparison.Ordinal);
    }

    private static string BuildFooter(int pageNumber, int totalPages, bool partial) =>
        $"<footer class=\"report-footer\"><span>{(partial ? "نسخة جزئية — " : string.Empty)}الصفحة {pageNumber} من {totalPages}</span><span>تقرير المتابعة الإجرائية للمعاملات</span></footer>";

    private static string RenderCover(InstitutionalReportModel model)
    {
        var m = model.Metadata;
        return $"""
        <div class="cover">
          <div class="cover-main">
            <h1 class="cover-title">{Esc(m.Title)}</h1>
            <div class="cover-period">الفترة من {m.PeriodFrom:yyyy-MM-dd} إلى {m.PeriodTo:yyyy-MM-dd}</div>
            <dl class="info-card">
              <dt>رقم التقرير</dt><dd>{Esc(m.ReportNumber)}</dd>
              <dt>نوع التقرير</dt><dd>{Esc(m.ReportTypeName)}</dd>
              <dt>تاريخ الإصدار</dt><dd>{m.IssueDate:yyyy-MM-dd}</dd>
            </dl>
            <div style="display:flex;gap:12px;align-items:center;margin-top:auto;">
              <div class="qr-box">QR<br/>{Esc(m.VerificationId)}</div>
              <div style="font-size:11px;color:#2F6B58;line-height:1.7;">
                معرف التحقق: {Esc(m.VerificationId)}<br/>
                وقت الإنشاء: {m.GeneratedAt:yyyy-MM-dd HH:mm}<br/>
                عدد الصفحات: {m.TotalPages}<br/>
                البصمة: {Esc(m.FileFingerprint ?? "—")}
              </div>
            </div>
          </div>
          <div class="cover-accent">
            <div style="font-size:14px;opacity:.9;">شعار الجهة</div>
            <div style="margin-top:24px;font-size:28px;font-weight:800;line-height:1.5;">تقرير مؤسسي<br/>للمتابعة الإجرائية</div>
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
              <td>{Esc(d.DepartmentName)}</td>
              <td>{d.TotalTransactions}</td><td>{d.ClosedCount}</td><td>{d.OpenCount}</td>
              <td>{d.WaitingForStatementCount}</td><td>{d.OverdueCount}</td><td>{d.JointDepartmentCount}</td>
              <td>{d.AverageCompletionDays:N1}</td><td>{d.OnTimeCompletionRate:N1}%</td>
              <td class="{ratingClass}">{Esc(d.RatingLabel)}</td>
            </tr>
            """;
        }));
        var totals = model.DepartmentPerformance.Aggregate(
            (Total: 0, Closed: 0, Open: 0, Waiting: 0, Overdue: 0, Joint: 0),
            (acc, row) => (acc.Total + row.TotalTransactions, acc.Closed + row.ClosedCount, acc.Open + row.OpenCount,
                acc.Waiting + row.WaitingForStatementCount, acc.Overdue + row.OverdueCount, acc.Joint + row.JointDepartmentCount));
        return $"""
        <h2 class="section-title">أداء الإدارات</h2>
        <table class="report-table">
          <thead><tr>
            <th>الإدارة</th><th>إجمالي</th><th>مغلقة</th><th>مفتوحة</th><th>بانتظار إفادة</th>
            <th>متأخرة</th><th>إدارات مشتركة</th><th>متوسط الإنجاز</th><th>ضمن المهلة</th><th>التقييم</th>
          </tr></thead>
          <tbody>{rows}</tbody>
          <tfoot><tr>
            <td>الإجمالي</td><td>{totals.Total}</td><td>{totals.Closed}</td><td>{totals.Open}</td>
            <td>{totals.Waiting}</td><td>{totals.Overdue}</td><td>{totals.Joint}</td>
            <td>—</td><td>—</td><td>—</td>
          </tr></tfoot>
        </table>
        """;
    }

    private static string RenderRisks(InstitutionalReportModel model)
    {
        var rows = string.Join(string.Empty, model.Risks.Select(r =>
            $"<tr><td>{r.Sequence}</td><td>{Esc(r.Alert)}</td><td>{Esc(r.DepartmentName)}</td><td>{Esc(r.SeverityLabel)}</td><td>{r.ElapsedDays}</td><td>{Esc(r.SuggestedAction)}</td></tr>"));
        var counters = model.RiskCounters;
        return $"""
        <h2 class="section-title">المخاطر والتنبيهات والتوصيات</h2>
        <div class="risk-grid">
          <div>
            <h3>جدول المخاطر والتنبيهات</h3>
            <table class="report-table"><thead><tr><th>م</th><th>التنبيه</th><th>الإدارة</th><th>الخطورة</th><th>الأيام</th><th>الإجراء</th></tr></thead><tbody>{rows}</tbody></table>
          </div>
          <div>
            <h3>ملخص المؤشرات</h3>
            <div class="counter-row">
              <span class="counter-pill">إدارات تحتاج متابعة: {counters.DepartmentsNeedingFollowUp}</span>
              <span class="counter-pill">معاملات مشتركة مفتوحة: {counters.OpenJointDepartmentTransactions}</span>
              <span class="counter-pill">ردود جزئية: {counters.PartialResponses}</span>
              <span class="counter-pill">بلا تحديث: {counters.TransactionsWithoutRecentUpdate}</span>
              <span class="counter-pill">اختبالات بيانات: {counters.DataIntegrityIssues}</span>
            </div>
          </div>
        </div>
        """;
    }

    private static string RenderRecommendations(InstitutionalReportModel model)
    {
        var rows = string.Join(string.Empty, model.Recommendations.Select(r =>
            $"<tr><td>{Esc(r.Observation)}</td><td>{Esc(r.RequiredAction)}</td><td>{Esc(r.ResponsibleDepartment)}</td><td>{Esc(r.Priority)}</td><td>{Esc(r.TargetDate ?? "—")}</td><td>{Esc(r.SourceLabel)}</td></tr>"));
        return $"""
        <h2 class="section-title">التوصيات التنفيذية</h2>
        <table class="report-table"><thead><tr><th>الملاحظة</th><th>الإجراء</th><th>الإدارة</th><th>الأولوية</th><th>الموعد</th><th>المصدر</th></tr></thead><tbody>{rows}</tbody></table>
        """;
    }

    private static string RenderTransactions(InstitutionalReportModel model, List<TransactionDetailRowDto> rows)
    {
        var body = string.Join(string.Empty, rows.Select(r =>
            $"<tr><td>{r.Sequence}</td><td>{Esc(r.TrackingNumber)}</td><td>{Esc(r.IncomingNumber)}</td><td>{r.IncomingDate:yyyy-MM-dd}</td><td>{Esc(r.Subject)}</td><td>{Esc(r.IncomingParty)}</td><td>{Esc(r.ResponsibleDepartment)}</td><td>{Esc(r.JointDepartments)}</td><td>{Esc(r.Priority)}</td><td>{Esc(r.Status)}</td><td>{Esc(r.FollowUpStage)}</td><td>{r.ElapsedDays}</td><td>{Esc(r.DueDate ?? "—")}</td><td>{Esc(r.LastActionDate ?? "—")}</td><td>{Esc(r.ResponseState)}</td></tr>"));
        return $"""
        <h2 class="section-title">المعاملات التفصيلية</h2>
        <p style="font-size:12px;color:#2F6B58;">إجمالي النتائج: {model.Transactions.Count:N0} معاملة — الفترة {model.Metadata.PeriodFrom:yyyy-MM-dd} إلى {model.Metadata.PeriodTo:yyyy-MM-dd}</p>
        <table class="report-table"><thead><tr>
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
        return $"""
        <h2 class="section-title">بيانات التقرير والفلاتر</h2>
        <dl class="info-card">
          <dt>رقم التقرير</dt><dd>{Esc(model.Metadata.ReportNumber)}</dd>
          <dt>نوع التقرير</dt><dd>{Esc(model.Metadata.ReportTypeName)}</dd>
          <dt>الفترة</dt><dd>{model.Metadata.PeriodFrom:yyyy-MM-dd} — {model.Metadata.PeriodTo:yyyy-MM-dd}</dd>
        </dl>
        {(warnings.Length > 0 ? $"<h3>تحذيرات سلامة البيانات</h3><ul>{warnings}</ul>" : string.Empty)}
        """;
    }

    private static RenderedReportPageDto CreatePartialCoverPage(RenderedReportManifestDto source, ReportExportRequestDto request, List<RenderedReportPageDto> selected) =>
        new()
        {
            OriginalPageNumber = 0,
            RenderedPageNumber = 0,
            SectionId = ReportSectionId.PartialCover,
            SectionName = "غلاف النسخة الجزئية",
            PageTitle = "غلاف النسخة الجزئية",
            HtmlContent = WrapPage($"""
                <div class="partial-note">نسخة جزئية من التقرير</div>
                <h2 class="section-title">غلاف النسخة الجزئية</h2>
                <dl class="info-card">
                  <dt>رقم التقرير الأصلي</dt><dd>{Esc(source.ReportId)}</dd>
                  <dt>الصفحات المضمنة</dt><dd>{string.Join(", ", selected.Select(p => p.OriginalPageNumber))}</dd>
                  <dt>تاريخ التصدير</dt><dd>{DateTime.UtcNow:yyyy-MM-dd}</dd>
                </dl>
                """, 0, 0, partial: true)
        };

    private static RenderedReportPageDto CreatePartialManifestPage(RenderedReportManifestDto source, ReportExportRequestDto request, List<RenderedReportPageDto> selected) =>
        new()
        {
            OriginalPageNumber = 0,
            RenderedPageNumber = 0,
            SectionId = ReportSectionId.PartialManifest,
            SectionName = "تعريف النسخة الجزئية",
            PageTitle = "تعريف النسخة الجزئية",
            HtmlContent = WrapPage($"""
                <h2 class="section-title">تعريف النسخة الجزئية</h2>
                <p>رقم التقرير الأصلي: {Esc(source.ReportId)}</p>
                <p>الصفحات الأصلية المختارة: {string.Join(", ", selected.Select(p => p.OriginalPageNumber))}</p>
                <p>الأقسام المضمنة: {string.Join("، ", selected.Select(p => p.SectionName).Distinct())}</p>
                {(string.IsNullOrWhiteSpace(request.Reason) ? string.Empty : $"<p>سبب الإنشاء: {Esc(request.Reason)}</p>")}
                """, 0, 0, partial: true)
        };

    private static string Esc(string? value) =>
        System.Net.WebUtility.HtmlEncode(value ?? string.Empty);
}
