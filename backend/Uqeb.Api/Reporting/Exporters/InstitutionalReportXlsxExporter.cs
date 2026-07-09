using ClosedXML.Excel;
using Uqeb.Api.Reporting.DTOs;
using Uqeb.Api.Reporting.Enums;
using Uqeb.Api.Reporting.Services;

namespace Uqeb.Api.Reporting.Exporters;

public static class InstitutionalReportXlsxExporter
{
    private const string SeverityHeader = "الخطورة";
    private const string DepartmentHeader = "الإدارة";
    private const string PriorityHeader = "الأولوية";
    private const string OnTimeHeader = "ضمن المهلة";
    private const string PartyHeader = "الجهة";

    public static byte[] Export(
        InstitutionalReportModel model,
        RenderedReportManifestDto manifest,
        ReportExportRequestDto request)
    {
        using var workbook = new XLWorkbook();

        if (ShouldInclude(manifest, ReportSectionId.ExecutiveSummary))
            AddSummarySheet(workbook, model);

        if (ShouldInclude(manifest, ReportSectionId.KeyPerformanceIndicators))
            AddKpisSheet(workbook, model);

        if (ShouldInclude(manifest, ReportSectionId.SignificantFindings))
            AddFindingsSheet(workbook, model);

        if (ShouldInclude(manifest, ReportSectionId.CriticalCases))
            AddCriticalCasesSheet(workbook, model);

        if (ShouldInclude(manifest, ReportSectionId.DepartmentPerformance))
            AddDepartmentsSheet(workbook, model);

        if (ShouldInclude(manifest, ReportSectionId.OutstandingAndImprovedDepartments))
            AddDepartmentRecognitionsSheet(workbook, model);

        if (ShouldInclude(manifest, ReportSectionId.TimeTrends))
            AddDepartmentTimeSeriesSheet(workbook, model);

        if (ShouldInclude(manifest, ReportSectionId.ExternalPartyAnalysis))
            AddExternalPartiesSheet(workbook, model);

        if (ShouldInclude(manifest, ReportSectionId.ClassificationAndPriorityAnalysis))
        {
            AddCategoriesSheet(workbook, model);
            AddPrioritiesSheet(workbook, model);
        }

        if (ShouldInclude(manifest, ReportSectionId.DelayAndBottleneckAnalysis))
            AddBottlenecksSheet(workbook, model);

        if (ShouldInclude(manifest, ReportSectionId.DataQuality))
            AddDataQualitySheet(workbook, model);

        if (ShouldInclude(manifest, ReportSectionId.RisksAndAlerts))
            AddRisksSheet(workbook, model);

        if (ShouldInclude(manifest, ReportSectionId.RecommendationsAndActionPlan))
            AddActionPlanSheet(workbook, model);

        AddTransactionDetailsSheetIfRequested(workbook, model, manifest);

        if (ShouldInclude(manifest, ReportSectionId.MethodologyAndDefinitions))
            AddMethodologySheet(workbook, model);

        AddMetadataSheet(workbook, model, manifest, request);

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private static bool ShouldInclude(RenderedReportManifestDto manifest, ReportSectionId section) =>
        manifest.Pages.Any(p => p.SectionId == section);

    private static void AddSummarySheet(XLWorkbook workbook, InstitutionalReportModel model)
    {
        var ws = workbook.Worksheets.Add("الملخص التنفيذي");
        ws.RightToLeft = true;
        ws.Cell(1, 1).Value = "الملخص التنفيذي";
        ws.Range(1, 1, 1, 3).Merge().Style.Font.Bold = true;
        var row = 3;
        foreach (var card in model.Summary.KpiCards)
        {
            ws.Cell(row, 1).Value = card.Title;
            ws.Cell(row, 2).Value = card.Value;
            row++;
        }
        ws.Cell(row + 1, 1).Value = model.Summary.ExecutiveNarrative;
        ws.Columns().AdjustToContents();
    }

    private static void AddKpisSheet(XLWorkbook workbook, InstitutionalReportModel model)
    {
        var ws = workbook.Worksheets.Add("KPIs");
        ws.RightToLeft = true;
        var headers = new[] { "المؤشر", "القيمة", "الوحدة", "الاتجاه", "التغير", "التعريف", "الصيغة", "الحقول" };
        WriteHeaders(ws, headers);
        var row = 2;
        foreach (var kpi in model.Analysis.Kpis)
        {
            ws.Cell(row, 1).Value = kpi.Title;
            ws.Cell(row, 2).Value = kpi.DisplayValue;
            ws.Cell(row, 3).Value = kpi.Unit;
            ws.Cell(row, 4).Value = kpi.Comparison.TrendDirection.ToString();
            ws.Cell(row, 5).Value = kpi.Comparison.ComparisonLabel ?? string.Empty;
            ws.Cell(row, 6).Value = kpi.Definition;
            ws.Cell(row, 7).Value = kpi.Formula;
            ws.Cell(row, 8).Value = kpi.FieldsUsed;
            row++;
        }
        FinishTable(ws, row - 1, headers.Length);
    }

    private static void AddFindingsSheet(XLWorkbook workbook, InstitutionalReportModel model)
    {
        var ws = workbook.Worksheets.Add("Findings");
        ws.RightToLeft = true;
        var headers = new[] { "الكود", "العنوان", "الوصف", "الدليل", SeverityHeader, "النطاق" };
        WriteHeaders(ws, headers);
        var row = 2;
        foreach (var finding in model.Analysis.Findings)
        {
            ws.Cell(row, 1).Value = finding.Code;
            ws.Cell(row, 2).Value = finding.Title;
            ws.Cell(row, 3).Value = finding.Description;
            ws.Cell(row, 4).Value = finding.Evidence;
            ws.Cell(row, 5).Value = finding.Severity.ToString();
            ws.Cell(row, 6).Value = finding.AffectedScope;
            row++;
        }
        FinishTable(ws, row - 1, headers.Length);
    }

    private static void AddCriticalCasesSheet(XLWorkbook workbook, InstitutionalReportModel model)
    {
        var ws = workbook.Worksheets.Add("Critical Cases");
        ws.RightToLeft = true;
        var headers = new[] { "المعرف", "رقم الوارد", "الموضوع", DepartmentHeader, PartyHeader, PriorityHeader, "العمر", "أيام التأخر", "السبب", "الإجراء", SeverityHeader };
        WriteHeaders(ws, headers);
        var row = 2;
        foreach (var item in model.Analysis.CriticalCases)
        {
            ws.Cell(row, 1).Value = item.TransactionId;
            ws.Cell(row, 2).Value = item.IncomingNumber;
            ws.Cell(row, 3).Value = item.Subject;
            ws.Cell(row, 4).Value = item.Department;
            ws.Cell(row, 5).Value = item.ExternalParty;
            ws.Cell(row, 6).Value = item.Priority;
            ws.Cell(row, 7).Value = item.AgeDays;
            ws.Cell(row, 8).Value = item.DaysOverdue;
            ws.Cell(row, 9).Value = item.ReasonLabel;
            ws.Cell(row, 10).Value = item.RequiredAction;
            ws.Cell(row, 11).Value = item.Severity.ToString();
            row++;
        }
        FinishTable(ws, row - 1, headers.Length);
    }

    private static void AddDepartmentsSheet(XLWorkbook workbook, InstitutionalReportModel model)
    {
        var ws = workbook.Worksheets.Add("أداء الإدارات");
        ws.RightToLeft = true;
        var headers = new[] { DepartmentHeader, "إجمالي", "مغلقة", "مفتوحة", "بانتظار إفادة", "متأخرة", "إدارات مشتركة", "متوسط الإنجاز", OnTimeHeader, "التقييم" };
        for (var i = 0; i < headers.Length; i++) ws.Cell(1, i + 1).Value = headers[i];
        ws.Range(1, 1, 1, headers.Length).Style.Fill.BackgroundColor = XLColor.FromHtml("#123F32");
        ws.Range(1, 1, 1, headers.Length).Style.Font.FontColor = XLColor.White;
        var row = 2;
        foreach (var d in model.DepartmentPerformance)
        {
            ws.Cell(row, 1).Value = d.DepartmentName;
            ws.Cell(row, 2).Value = d.TotalTransactions;
            ws.Cell(row, 3).Value = d.ClosedCount;
            ws.Cell(row, 4).Value = d.OpenCount;
            ws.Cell(row, 5).Value = d.WaitingForStatementCount;
            ws.Cell(row, 6).Value = d.OverdueCount;
            ws.Cell(row, 7).Value = d.JointDepartmentCount;
            ws.Cell(row, 8).Value = d.AverageCompletionDays;
            ws.Cell(row, 9).Value = d.OnTimeCompletionRate;
            ws.Cell(row, 10).Value = d.RatingLabel;
            row++;
        }
        ws.Range(1, 1, row - 1, headers.Length).SetAutoFilter();
        ws.SheetView.FreezeRows(1);
        ws.Columns().AdjustToContents();
        // Aggregation metadata note — written AFTER AdjustToContents so it is then re-sized below.
        var noteRow = row + 1;
        var noteCell = ws.Cell(noteRow, 1);
        if (model.DepartmentTotalsAreAdditive)
        {
            noteCell.Value = $"ملاحظة منهجية: {model.DepartmentAggregationDescription}";
            noteCell.Style.Font.Italic = true;
            noteCell.Style.Font.FontColor = XLColor.Gray;
        }
        else
        {
            noteCell.Value = $"تحذير: المجاميع غير قابلة للجمع — {model.DepartmentAggregationDescription}";
            noteCell.Style.Font.FontColor = XLColor.DarkRed;
        }
        noteCell.Style.Alignment.WrapText = true;
        ws.Column(1).AdjustToContents();
    }

    private static void AddDepartmentRecognitionsSheet(XLWorkbook workbook, InstitutionalReportModel model)
    {
        var ws = workbook.Worksheets.Add("الإدارات المتميزة والتحسن");
        ws.RightToLeft = true;
        var headers = new[]
        {
            "اسم الإدارة",
            "نوع التصنيف",
            "حجم المعاملات",
            "نسبة الإنجاز في الوقت",
            "عدد المتأخرات",
            "متوسط مدة المعالجة أو التأخير",
            "مقدار التحسن مقارنة بالفترة السابقة",
            "سبب التصنيف"
        };
        WriteHeaders(ws, headers);
        var row = 2;
        foreach (var item in model.Analysis.DepartmentRecognitions)
        {
            ws.Cell(row, 1).Value = item.DepartmentName;
            ws.Cell(row, 2).Value = item.RecognitionType;
            ws.Cell(row, 3).Value = item.TransactionCount;
            ws.Cell(row, 4).Value = item.OnTimeCompletionRate;
            ws.Cell(row, 5).Value = item.OverdueCount;
            ws.Cell(row, 6).Value = item.AverageCompletionDays;
            ws.Cell(row, 7).Value = DepartmentRecognitionFormatter.FormatImprovementValue(item);
            ws.Cell(row, 8).Value = item.Reason;
            row++;
        }
        FinishTable(ws, row - 1, headers.Length);
    }

    private static void AddExternalPartiesSheet(XLWorkbook workbook, InstitutionalReportModel model)
    {
        var ws = workbook.Worksheets.Add("External Parties");
        ws.RightToLeft = true;
        var headers = new[] { PartyHeader, "وارد", "صادر", "منتظر رد", "ردود متأخرة", "متوسط الرد", "وسيط الرد", "متابعات", "أقدم انتظار", "أبرز التصنيفات" };
        WriteHeaders(ws, headers);
        var row = 2;
        foreach (var party in model.Analysis.ExternalParties)
        {
            ws.Cell(row, 1).Value = party.ExternalPartyName;
            ws.Cell(row, 2).Value = party.IncomingCount;
            ws.Cell(row, 3).Value = party.OutgoingCount;
            ws.Cell(row, 4).Value = party.PendingResponseCount;
            ws.Cell(row, 5).Value = party.OverdueResponseCount;
            ws.Cell(row, 6).Value = party.AverageResponseDays;
            ws.Cell(row, 7).Value = party.MedianResponseDays;
            ws.Cell(row, 8).Value = party.FollowUpCount;
            ws.Cell(row, 9).Value = party.OldestPendingResponseDays;
            ws.Cell(row, 10).Value = party.TopCategories;
            row++;
        }
        FinishTable(ws, row - 1, headers.Length);
    }

    /// <summary>
    /// Unlike the HTML/PDF view (RenderDepartmentTimeSeries), this sheet exports the full,
    /// uncapped department × period breakdown — no top-10 department limit is applied here.
    /// </summary>
    private static void AddDepartmentTimeSeriesSheet(XLWorkbook workbook, InstitutionalReportModel model)
    {
        if (model.Analysis.DepartmentTimeSeries.Count == 0)
            return;

        var ws = workbook.Worksheets.Add("Department Time Series");
        ws.RightToLeft = true;
        var headers = new[] { "الفترة", DepartmentHeader, "الوارد", "المغلق", "المفتوح", "المتأخر", OnTimeHeader, "متوسط الإنجاز", "الإفادات المعلقة", "الردود الجزئية", "تغير التراكم" };
        WriteHeaders(ws, headers);
        var row = 2;
        foreach (var point in model.Analysis.DepartmentTimeSeries)
        {
            ws.Cell(row, 1).Value = point.PeriodLabel;
            ws.Cell(row, 2).Value = point.DepartmentName;
            ws.Cell(row, 3).Value = point.IncomingCount;
            ws.Cell(row, 4).Value = point.ClosedCount;
            ws.Cell(row, 5).Value = point.OpenCount;
            ws.Cell(row, 6).Value = point.OverdueCount;
            ws.Cell(row, 7).Value = point.OnTimeCompletionRate;
            ws.Cell(row, 8).Value = point.AverageCompletionDays;
            ws.Cell(row, 9).Value = point.PendingAssignments;
            ws.Cell(row, 10).Value = point.PartialReplies;
            ws.Cell(row, 11).Value = point.BacklogGrowth;
            row++;
        }
        FinishTable(ws, row - 1, headers.Length);
    }

    private static void AddCategoriesSheet(XLWorkbook workbook, InstitutionalReportModel model)
    {
        var ws = workbook.Worksheets.Add("Categories");
        ws.RightToLeft = true;
        var headers = new[] { "التصنيف", "الإجمالي", "مفتوحة", "متأخرة", OnTimeHeader, "متوسط الإنجاز", "إفادات معلقة" };
        WriteHeaders(ws, headers);
        var row = 2;
        foreach (var category in model.Analysis.Categories)
        {
            ws.Cell(row, 1).Value = category.CategoryName;
            ws.Cell(row, 2).Value = category.TransactionCount;
            ws.Cell(row, 3).Value = category.OpenCount;
            ws.Cell(row, 4).Value = category.OverdueCount;
            ws.Cell(row, 5).Value = category.OnTimeCompletionRate;
            ws.Cell(row, 6).Value = category.AverageCompletionDays;
            ws.Cell(row, 7).Value = category.PendingAssignments;
            row++;
        }
        FinishTable(ws, row - 1, headers.Length);
    }

    private static void AddPrioritiesSheet(XLWorkbook workbook, InstitutionalReportModel model)
    {
        var ws = workbook.Worksheets.Add("Priorities");
        ws.RightToLeft = true;
        var headers = new[] { PriorityHeader, "الإجمالي", "مفتوحة", "متأخرة", "متوسط العمر", OnTimeHeader };
        WriteHeaders(ws, headers);
        var row = 2;
        foreach (var priority in model.Analysis.Priorities)
        {
            ws.Cell(row, 1).Value = priority.Priority;
            ws.Cell(row, 2).Value = priority.Count;
            ws.Cell(row, 3).Value = priority.OpenCount;
            ws.Cell(row, 4).Value = priority.OverdueCount;
            ws.Cell(row, 5).Value = priority.AverageAgeDays;
            ws.Cell(row, 6).Value = priority.OnTimeRate;
            row++;
        }
        FinishTable(ws, row - 1, headers.Length);
    }

    private static void AddBottlenecksSheet(XLWorkbook workbook, InstitutionalReportModel model)
    {
        var ws = workbook.Worksheets.Add("Bottlenecks");
        ws.RightToLeft = true;
        var headers = new[] { "الكود", "السبب", "العدد", "النسبة", "متوسط الأيام", "أبرز الإدارات", "أبرز الجهات", "أمثلة" };
        WriteHeaders(ws, headers);
        var row = 2;
        foreach (var bottleneck in model.Analysis.Bottlenecks)
        {
            ws.Cell(row, 1).Value = bottleneck.ReasonCode;
            ws.Cell(row, 2).Value = bottleneck.ReasonLabel;
            ws.Cell(row, 3).Value = bottleneck.Count;
            ws.Cell(row, 4).Value = bottleneck.SharePercent;
            ws.Cell(row, 5).Value = bottleneck.AverageDelayDays;
            ws.Cell(row, 6).Value = bottleneck.TopDepartments;
            ws.Cell(row, 7).Value = bottleneck.TopExternalParties;
            ws.Cell(row, 8).Value = string.Join(", ", bottleneck.ExampleTransactionIds);
            row++;
        }
        FinishTable(ws, row - 1, headers.Length);
    }

    private static void AddDataQualitySheet(XLWorkbook workbook, InstitutionalReportModel model)
    {
        var ws = workbook.Worksheets.Add("Data Quality");
        ws.RightToLeft = true;
        var headers = new[] { "الكود", "الملاحظة", "العدد", "النسبة", SeverityHeader, "الحقول", "التصحيح" };
        WriteHeaders(ws, headers);
        var row = 2;
        foreach (var issue in model.Analysis.DataQualityIssues)
        {
            ws.Cell(row, 1).Value = issue.IssueCode;
            ws.Cell(row, 2).Value = issue.Label;
            ws.Cell(row, 3).Value = issue.Count;
            ws.Cell(row, 4).Value = issue.SharePercent;
            ws.Cell(row, 5).Value = issue.Severity.ToString();
            ws.Cell(row, 6).Value = issue.AffectedFields;
            ws.Cell(row, 7).Value = issue.SuggestedCorrection;
            row++;
        }
        FinishTable(ws, row - 1, headers.Length);
    }

    private static void AddRisksSheet(XLWorkbook workbook, InstitutionalReportModel model)
    {
        var ws = workbook.Worksheets.Add("المخاطر والتنبيهات");
        ws.RightToLeft = true;
        ws.Cell(1, 1).Value = "م";
        ws.Cell(1, 2).Value = "التنبيه";
        ws.Cell(1, 3).Value = DepartmentHeader;
        ws.Cell(1, 4).Value = SeverityHeader;
        ws.Cell(1, 5).Value = "الأيام";
        ws.Cell(1, 6).Value = "الإجراء";
        var row = 2;
        foreach (var risk in model.Risks)
        {
            ws.Cell(row, 1).Value = risk.Sequence;
            ws.Cell(row, 2).Value = risk.Alert;
            ws.Cell(row, 3).Value = risk.DepartmentName;
            ws.Cell(row, 4).Value = risk.SeverityLabel;
            ws.Cell(row, 5).Value = risk.ElapsedDays;
            ws.Cell(row, 6).Value = risk.SuggestedAction;
            row++;
        }
        ws.Columns().AdjustToContents();
    }

    private static void AddActionPlanSheet(XLWorkbook workbook, InstitutionalReportModel model)
    {
        var ws = workbook.Worksheets.Add("Recommendations");
        ws.RightToLeft = true;
        var headers = new[] { PriorityHeader, "النتيجة", "الإجراء", "المسؤول", "المدة المقترحة", "الحالة", "الدليل" };
        WriteHeaders(ws, headers);
        var row = 2;
        foreach (var recommendation in model.Analysis.Recommendations)
        {
            ws.Cell(row, 1).Value = recommendation.Priority;
            ws.Cell(row, 2).Value = recommendation.SourceFindingCode;
            ws.Cell(row, 3).Value = recommendation.RecommendationText;
            ws.Cell(row, 4).Value = recommendation.ResponsibleScope;
            ws.Cell(row, 5).Value = recommendation.SuggestedDueDays;
            ws.Cell(row, 6).Value = recommendation.Status;
            ws.Cell(row, 7).Value = recommendation.EvidenceSummary;
            row++;
        }
        FinishTable(ws, row - 1, headers.Length);
    }

    private static void AddTransactionDetailsSheetIfRequested(
        XLWorkbook workbook, InstitutionalReportModel model, RenderedReportManifestDto manifest)
    {
        if (!ShouldInclude(manifest, ReportSectionId.TransactionDetails))
            return;

        if (model.Metadata.ReportType == InstitutionalReportType.DepartmentTransactions)
            AddDepartmentTransactionsSheet(workbook, model);
        else
            AddTransactionsSheet(workbook, model);
    }

    private static void AddTransactionsSheet(XLWorkbook workbook, InstitutionalReportModel model)
    {
        var ws = workbook.Worksheets.Add("المعاملات التفصيلية");
        ws.RightToLeft = true;
        var headers = new[] { "م", "رقم الوارد", "تاريخ الوارد", "الموضوع", PartyHeader, DepartmentHeader, "الإدارات المشتركة", PriorityHeader, "الحالة", "مرحلة المتابعة", "الأيام", "المهلة", "آخر إجراء", "حالة الرد" };
        for (var i = 0; i < headers.Length; i++) ws.Cell(1, i + 1).Value = headers[i];
        var row = 2;
        foreach (var t in model.Transactions)
        {
            ws.Cell(row, 1).Value = t.Sequence;
            ws.Cell(row, 2).Value = t.IncomingNumber;
            ws.Cell(row, 3).Value = t.IncomingDate;
            ws.Cell(row, 4).Value = t.Subject;
            ws.Cell(row, 5).Value = t.IncomingParty;
            ws.Cell(row, 6).Value = t.ResponsibleDepartment;
            ws.Cell(row, 7).Value = t.JointDepartments;
            ws.Cell(row, 8).Value = t.Priority;
            ws.Cell(row, 9).Value = t.Status;
            ws.Cell(row, 10).Value = t.FollowUpStage;
            ws.Cell(row, 11).Value = t.ElapsedDays;
            ws.Cell(row, 12).Value = t.DueDate ?? string.Empty;
            ws.Cell(row, 13).Value = t.LastActionDate ?? string.Empty;
            ws.Cell(row, 14).Value = t.ResponseState;
            row++;
        }
        ws.Range(1, 1, row - 1, headers.Length).SetAutoFilter();
        ws.SheetView.FreezeRows(1);
        ws.Columns().AdjustToContents();
    }

    /// <summary>
    /// DepartmentTransactions only: a richer column set than the PDF/HTML table, including the
    /// matched-department/relation breakdown and full referral/outgoing department lists, per report
    /// requirement #9. The other 5 report types keep using AddTransactionsSheet unchanged.
    /// </summary>
    private static void AddDepartmentTransactionsSheet(XLWorkbook workbook, InstitutionalReportModel model)
    {
        var ws = workbook.Worksheets.Add("المعاملات التفصيلية");
        ws.RightToLeft = true;
        var headers = new[]
        {
            "م", "رقم الوارد", "تاريخ الوارد", "الموضوع", PartyHeader,
            "الإدارة/الإدارات المطابقة", "نوع العلاقة", "رقم الصادر", "تاريخ الصادر",
            "إدارات الإحالة (الكل)", "إدارات الصادر (الكل)", "الحالة", PriorityHeader, "المهلة", "آخر إجراء",
        };
        for (var i = 0; i < headers.Length; i++) ws.Cell(1, i + 1).Value = headers[i];
        var row = 2;
        foreach (var t in model.Transactions)
        {
            ws.Cell(row, 1).Value = t.Sequence;
            ws.Cell(row, 2).Value = t.IncomingNumber;
            ws.Cell(row, 3).Value = t.IncomingDate;
            ws.Cell(row, 4).Value = t.Subject;
            ws.Cell(row, 5).Value = t.IncomingParty;
            ws.Cell(row, 6).Value = string.Join("؛ ", t.MatchedDepartments.Select(m => m.DepartmentName));
            ws.Cell(row, 7).Value = string.Join("؛ ", t.MatchedDepartments.Select(m => m.Relation));
            ws.Cell(row, 8).Value = t.OutgoingNumber ?? string.Empty;
            ws.Cell(row, 9).Value = t.OutgoingDate ?? string.Empty;
            ws.Cell(row, 10).Value = string.Join("؛ ", t.AllAssignmentDepartments);
            ws.Cell(row, 11).Value = string.Join("؛ ", t.AllOutgoingDepartments);
            ws.Cell(row, 12).Value = t.Status;
            ws.Cell(row, 13).Value = t.Priority;
            ws.Cell(row, 14).Value = t.DueDate ?? string.Empty;
            ws.Cell(row, 15).Value = t.LastActionDate ?? string.Empty;
            row++;
        }
        ws.Range(1, 1, row - 1, headers.Length).SetAutoFilter();
        ws.SheetView.FreezeRows(1);
        ws.Columns().AdjustToContents();
    }

    private static void AddMetadataSheet(XLWorkbook workbook, InstitutionalReportModel model, RenderedReportManifestDto manifest, ReportExportRequestDto request)
    {
        var ws = workbook.Worksheets.Add("بيانات التقرير");
        ws.RightToLeft = true;
        ws.Cell(1, 1).Value = "رقم التقرير";
        ws.Cell(1, 2).Value = model.Metadata.ReportNumber;
        ws.Cell(2, 1).Value = "الفترة";
        ws.Cell(2, 2).Value = $"{model.Metadata.PeriodFrom:yyyy-MM-dd} — {model.Metadata.PeriodTo:yyyy-MM-dd}";
        ws.Cell(3, 1).Value = "صفحات PDF المختارة";
        ws.Cell(3, 2).Value = string.Join(", ", manifest.Pages.Select(p => p.OriginalPageNumber));
        if (request.ExportMode is ExportMode.SelectedPages or ExportMode.CurrentPage)
        {
            ws.Cell(4, 1).Value = "ملاحظة";
            ws.Cell(4, 2).Value = "اختيار الصفحات الفعلية يطبق بدقة على PDF. في Excel سيتم تصدير الأقسام المقابلة.";
        }
        ws.Columns().AdjustToContents();
    }

    private static void AddMethodologySheet(XLWorkbook workbook, InstitutionalReportModel model)
    {
        var ws = workbook.Worksheets.Add("Methodology");
        ws.RightToLeft = true;
        var m = model.Analysis.Methodology;
        var rows = new (string Label, string Value)[]
        {
            ("Report name", m.ReportName),
            ("Report version", m.ReportVersion),
            ("Generated at UTC", m.GeneratedAtUtc.ToString("yyyy-MM-dd HH:mm")),
            ("Data period", m.DataPeriod),
            ("Period basis", m.PeriodBasis),
            ("Comparison period", m.ComparisonPeriod),
            ("Filters", m.Filters),
            ("Data source", m.DataSource),
            ("Snapshot mode", m.SnapshotMode),
            ("Row limits", m.RowLimits),
            ("Calculation version", m.CalculationVersion),
            ("Approval status", m.ApprovalStatus),
            ("Deferred metrics", string.Join(" | ", m.DeferredMetrics))
        };
        var row = 1;
        foreach (var item in rows)
        {
            ws.Cell(row, 1).Value = item.Label;
            ws.Cell(row, 2).Value = item.Value;
            row++;
        }
        ws.Columns().AdjustToContents();
    }

    private static void WriteHeaders(IXLWorksheet ws, IReadOnlyList<string> headers)
    {
        for (var i = 0; i < headers.Count; i++)
            ws.Cell(1, i + 1).Value = headers[i];

        ws.Range(1, 1, 1, headers.Count).Style.Fill.BackgroundColor = XLColor.FromHtml("#123F32");
        ws.Range(1, 1, 1, headers.Count).Style.Font.FontColor = XLColor.White;
        ws.Range(1, 1, 1, headers.Count).Style.Font.Bold = true;
    }

    private static void FinishTable(IXLWorksheet ws, int lastRow, int lastColumn)
    {
        if (lastRow >= 1)
            ws.Range(1, 1, Math.Max(1, lastRow), lastColumn).SetAutoFilter();
        ws.SheetView.FreezeRows(1);
        ws.Columns().AdjustToContents();
    }
}
