using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Uqeb.Api.Reporting.DTOs;
using Uqeb.Api.Reporting.Enums;
using Uqeb.Api.Reporting.Services;

namespace Uqeb.Api.Reporting.Exporters;

public static class InstitutionalReportDocxExporter
{
    public static byte[] Export(
        InstitutionalReportModel model,
        RenderedReportManifestDto manifest,
        ReportExportRequestDto request)
    {
        using var stream = new MemoryStream();
        using (var doc = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document, true))
        {
            var mainPart = doc.AddMainDocumentPart();
            var body = new Body();
            mainPart.Document = new Document();
            mainPart.Document.AppendChild(body);

            AppendPartialExportNotice(body, request);
            foreach (var section in manifest.Pages.Select(p => p.SectionId).Distinct())
            {
                AppendSection(body, section, model);
                AppendSpacer(body);
            }

            mainPart.Document.Save();
        }

        return stream.ToArray();
    }

    private static void AppendPartialExportNotice(Body body, ReportExportRequestDto request)
    {
        if (request.ExportMode is not (ExportMode.SelectedPages or ExportMode.CurrentPage))
            return;

        AppendParagraph(
            body,
            "اختيار الصفحات الفعلية يطبق بدقة على PDF. في Word سيتم تصدير الأقسام المقابلة لأن توزيع الصفحات قد يختلف.",
            bold: true);
        AppendSpacer(body);
    }

    private static void AppendSection(Body body, ReportSectionId section, InstitutionalReportModel model)
    {
        switch (section)
        {
            case ReportSectionId.Cover:
                AppendCoverSection(body, model);
                break;
            case ReportSectionId.ExecutiveSummary:
                AppendExecutiveSummarySection(body, model);
                break;
            case ReportSectionId.KeyPerformanceIndicators:
                AppendKpisSection(body, model);
                break;
            case ReportSectionId.SignificantFindings:
                AppendFindingsSection(body, model);
                break;
            case ReportSectionId.CriticalCases:
                AppendCriticalCasesSection(body, model);
                break;
            case ReportSectionId.TimeTrends:
                AppendTimeTrendsSection(body, model);
                break;
            case ReportSectionId.DepartmentPerformance:
                AppendDepartmentPerformanceSection(body, model);
                break;
            case ReportSectionId.ExternalPartyAnalysis:
                AppendExternalPartiesSection(body, model);
                break;
            case ReportSectionId.ClassificationAndPriorityAnalysis:
                AppendClassificationSection(body, model);
                break;
            case ReportSectionId.DelayAndBottleneckAnalysis:
                AppendBottlenecksSection(body, model);
                break;
            case ReportSectionId.DataQuality:
                AppendDataQualitySection(body, model);
                break;
            case ReportSectionId.RisksAndAlerts:
                AppendRisksSection(body, model);
                break;
            case ReportSectionId.ExecutiveRecommendations:
                AppendRecommendationsSection(body, model);
                break;
            case ReportSectionId.RecommendationsAndActionPlan:
                AppendActionPlanSection(body, model);
                break;
            case ReportSectionId.TransactionDetails:
                AppendTransactionDetailsSection(body, model);
                break;
            case ReportSectionId.Appendices:
                AppendAppendicesSection(body, model);
                break;
            case ReportSectionId.MethodologyAndDefinitions:
                AppendMethodologySection(body, model);
                break;
            case ReportSectionId.ReportMetadata:
                AppendMetadataSection(body, model);
                break;
        }
    }

    private static void AppendCoverSection(Body body, InstitutionalReportModel model)
    {
        AppendHeading(body, model.Metadata.Title);
        AppendParagraph(body, $"الفترة من {model.Metadata.PeriodFrom:yyyy-MM-dd} إلى {model.Metadata.PeriodTo:yyyy-MM-dd}");
        AppendParagraph(body, $"رقم التقرير: {model.Metadata.ReportNumber}");
        AppendParagraph(body, $"نوع التقرير: {model.Metadata.ReportTypeName}");
        AppendParagraph(body, $"تاريخ الإصدار: {model.Metadata.IssueDate:yyyy-MM-dd}");
    }

    private static void AppendExecutiveSummarySection(Body body, InstitutionalReportModel model)
    {
        AppendHeading(body, "الملخص التنفيذي");
        foreach (var card in model.Summary.KpiCards)
            AppendParagraph(body, $"{card.Title}: {card.Value}");
        AppendHeading(body, "التقييم التنفيذي", 2);
        AppendParagraph(body, model.Summary.ExecutiveNarrative);
        foreach (var insight in model.Analysis.ExecutiveInsights)
            AppendParagraph(body, $"{SeverityLabel(insight.Severity)}: {insight.Text}");
    }

    private static void AppendKpisSection(Body body, InstitutionalReportModel model)
    {
        AppendHeading(body, "مؤشرات الأداء الرئيسية");
        foreach (var kpi in model.Analysis.Kpis)
            AppendParagraph(body, $"{kpi.Title}: {kpi.DisplayValue} — {kpi.Definition}");
    }

    private static void AppendFindingsSection(Body body, InstitutionalReportModel model)
    {
        AppendHeading(body, "النتائج المهمة");
        if (model.Analysis.Findings.Count == 0)
            AppendParagraph(body, "لا توجد نتائج مهمة وفق عتبات التحليل الحالية.");
        foreach (var finding in model.Analysis.Findings)
            AppendParagraph(body, $"{finding.Title}: {finding.Description} — الدليل: {finding.Evidence}");
    }

    private static void AppendCriticalCasesSection(Body body, InstitutionalReportModel model)
    {
        AppendHeading(body, "الحالات الحرجة");
        if (model.Analysis.CriticalCases.Count == 0)
            AppendParagraph(body, "لا توجد حالات حرجة وفق القواعد الحالية.");
        foreach (var item in model.Analysis.CriticalCases)
            AppendParagraph(body, $"{item.IncomingNumber} — {item.Subject} — {item.ReasonLabel} — {item.RequiredAction}");
    }

    private static void AppendTimeTrendsSection(Body body, InstitutionalReportModel model)
    {
        AppendHeading(body, "التحليل الزمني");
        foreach (var point in model.Analysis.TimeSeries)
            AppendParagraph(body, $"{point.PeriodLabel}: وارد {point.Incoming}، مغلق {point.Closed}، متأخر {point.Overdue}");

        var departmentPoints = model.Analysis.DepartmentTimeSeries;
        if (departmentPoints.Count == 0)
            return;

        var departmentGroups = DepartmentTimeSeriesRanking.RankDepartments(departmentPoints);
        var topDepartmentKeys = DepartmentTimeSeriesRanking.TopDepartmentKeys(departmentGroups);

        AppendHeading(body, "التحليل الزمني حسب الإدارة");
        if (topDepartmentKeys.Count < departmentGroups.Count)
        {
            AppendParagraph(
                body,
                $"تعرض هذه القائمة أعلى {DepartmentTimeSeriesRanking.TopDepartments} إدارات حسب المتأخرات ثم المفتوحة ثم الوارد؛ تصدير XLSX يشمل كل الإدارات.",
                bold: true);
        }
        foreach (var point in departmentPoints.Where(p => DepartmentTimeSeriesRanking.IsTopDepartment(p, topDepartmentKeys)))
        {
            var departmentName = DepartmentTimeSeriesRanking.NormalizeDepartmentName(point.DepartmentName);
            AppendParagraph(body, $"{point.PeriodLabel} — {departmentName}: وارد {point.IncomingCount}، مغلق {point.ClosedCount}، متأخر {point.OverdueCount}");
        }
    }

    private static void AppendDepartmentPerformanceSection(Body body, InstitutionalReportModel model)
    {
        AppendHeading(body, "أداء الإدارات");
        foreach (var row in model.DepartmentPerformance)
            AppendParagraph(body, $"{row.DepartmentName} — إجمالي {row.TotalTransactions} — التقييم {row.RatingLabel}");
    }

    private static void AppendExternalPartiesSection(Body body, InstitutionalReportModel model)
    {
        AppendHeading(body, "تحليل الجهات الخارجية");
        foreach (var party in model.Analysis.ExternalParties)
            AppendParagraph(body, $"{party.ExternalPartyName} — وارد {party.IncomingCount} — منتظر رد {party.PendingResponseCount} — متأخر {party.OverdueResponseCount}");
    }

    private static void AppendClassificationSection(Body body, InstitutionalReportModel model)
    {
        AppendHeading(body, "تحليل التصنيفات والأولويات");
        foreach (var category in model.Analysis.Categories)
            AppendParagraph(body, $"تصنيف {category.CategoryName}: إجمالي {category.TransactionCount}، متأخر {category.OverdueCount}");
        foreach (var priority in model.Analysis.Priorities)
            AppendParagraph(body, $"أولوية {priority.Priority}: إجمالي {priority.Count}، متأخر {priority.OverdueCount}");
    }

    private static void AppendBottlenecksSection(Body body, InstitutionalReportModel model)
    {
        AppendHeading(body, "تحليل الاختناقات والتأخر");
        foreach (var bottleneck in model.Analysis.Bottlenecks)
            AppendParagraph(body, $"{bottleneck.ReasonLabel}: {bottleneck.Count} ({bottleneck.SharePercent:N1}%)");
    }

    private static void AppendDataQualitySection(Body body, InstitutionalReportModel model)
    {
        AppendHeading(body, "جودة البيانات");
        AppendParagraph(body, $"نسبة اكتمال البيانات: {model.Analysis.DataCompletenessRate:N1}%");
        foreach (var issue in model.Analysis.DataQualityIssues)
            AppendParagraph(body, $"{issue.Label}: {issue.Count} ({issue.SharePercent:N1}%) — {issue.SuggestedCorrection}");
    }

    private static void AppendRisksSection(Body body, InstitutionalReportModel model)
    {
        AppendHeading(body, "المخاطر والتنبيهات");
        foreach (var risk in model.Risks)
            AppendParagraph(body, $"{risk.Sequence}. {risk.Alert} ({risk.DepartmentName})");
    }

    private static void AppendRecommendationsSection(Body body, InstitutionalReportModel model)
    {
        AppendHeading(body, "التوصيات التنفيذية");
        foreach (var rec in model.Recommendations)
            AppendParagraph(body, $"{rec.Observation} — {rec.RequiredAction} [{rec.SourceLabel}]");
    }

    private static void AppendActionPlanSection(Body body, InstitutionalReportModel model)
    {
        AppendHeading(body, "التوصيات وخطة الإجراءات");
        foreach (var rec in model.Analysis.Recommendations)
            AppendParagraph(body, $"{rec.Priority} — {rec.RecommendationText} — المسؤول: {rec.ResponsibleScope} — الحالة: {rec.Status}");
    }

    private static void AppendTransactionDetailsSection(Body body, InstitutionalReportModel model)
    {
        AppendHeading(body, "المعاملات التفصيلية");
        foreach (var tx in model.Transactions)
            AppendParagraph(body, $"{tx.Sequence}. {tx.IncomingNumber} — {tx.Subject}");
    }

    private static void AppendAppendicesSection(Body body, InstitutionalReportModel model)
    {
        AppendHeading(body, "الجداول التفصيلية والملاحق");
        AppendParagraph(body, $"صفوف التفاصيل المصدرة: {model.ExportedDetailRows:N0} من {model.TotalMatchedRows:N0}.");
    }

    private static void AppendMethodologySection(Body body, InstitutionalReportModel model)
    {
        var m = model.Analysis.Methodology;
        AppendHeading(body, "المنهجية والتعريفات");
        AppendParagraph(body, $"فترة البيانات: {m.DataPeriod}");
        AppendParagraph(body, $"أساس الفترة الزمنية: {m.PeriodBasis}");
        AppendParagraph(body, $"فترة المقارنة: {m.ComparisonPeriod}");
        AppendParagraph(body, $"الفلاتر: {m.Filters}");
        AppendParagraph(body, $"مصدر البيانات: {m.DataSource}");
        AppendParagraph(body, $"حدود الصفوف: {m.RowLimits}");
        AppendParagraph(body, $"إصدار الحساب: {m.CalculationVersion}");
        foreach (var item in m.DeferredMetrics)
            AppendParagraph(body, $"مؤجل: {item}");
    }

    private static void AppendMetadataSection(Body body, InstitutionalReportModel model)
    {
        AppendHeading(body, "بيانات التقرير والفلاتر");
        AppendParagraph(body, $"رقم التقرير: {model.Metadata.ReportNumber}");
    }

    private static void AppendHeading(Body body, string text, int level = 1)
    {
        var paragraph = CreateParagraph();
        var run = CreateRun();
        var runProperties = new RunProperties();
        runProperties.AppendChild(new Bold());
        runProperties.AppendChild(new RightToLeftText());
        runProperties.AppendChild(new FontSize { Val = level == 1 ? "32" : "26" });
        run.AppendChild(runProperties);
        run.AppendChild(new Text(text));
        paragraph.AppendChild(run);
        body.AppendChild(paragraph);
    }

    private static void AppendParagraph(Body body, string text, bool bold = false)
    {
        var paragraph = CreateParagraph();
        var run = CreateRun();
        var runProperties = new RunProperties();
        runProperties.AppendChild(new RightToLeftText());
        if (bold)
            runProperties.AppendChild(new Bold());
        run.AppendChild(runProperties);
        run.AppendChild(new Text(text) { Space = SpaceProcessingModeValues.Preserve });
        paragraph.AppendChild(run);
        body.AppendChild(paragraph);
    }

    private static void AppendSpacer(Body body)
    {
        var paragraph = CreateParagraph();
        var run = CreateRun();
        run.AppendChild(new Text(string.Empty));
        paragraph.AppendChild(run);
        body.AppendChild(paragraph);
    }

    private static Paragraph CreateParagraph()
    {
        var paragraph = new Paragraph();
        var properties = new ParagraphProperties();
        properties.AppendChild(new BiDi());
        properties.AppendChild(new Justification { Val = JustificationValues.Right });
        paragraph.AppendChild(properties);
        return paragraph;
    }

    private static Run CreateRun() => new();

    private static string SeverityLabel(AnalyticalSeverity severity) => severity switch
    {
        AnalyticalSeverity.Critical => "حرج",
        AnalyticalSeverity.High => "مرتفع",
        AnalyticalSeverity.Medium => "متوسط",
        AnalyticalSeverity.Low => "منخفض",
        _ => "—"
    };
}
