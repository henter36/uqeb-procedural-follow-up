using ClosedXML.Excel;
using Uqeb.Api.Reporting.DTOs;
using Uqeb.Api.Reporting.Enums;

namespace Uqeb.Api.Reporting.Exporters;

public sealed class InstitutionalReportXlsxExporter
{
    public byte[] Export(InstitutionalReportModel model, RenderedReportManifestDto manifest, ReportExportRequestDto request)
    {
        using var workbook = new XLWorkbook();

        if (ShouldInclude(manifest, ReportSectionId.ExecutiveSummary))
            AddSummarySheet(workbook, model);

        if (ShouldInclude(manifest, ReportSectionId.DepartmentPerformance))
            AddDepartmentsSheet(workbook, model);

        if (ShouldInclude(manifest, ReportSectionId.RisksAndAlerts))
            AddRisksSheet(workbook, model);

        if (ShouldInclude(manifest, ReportSectionId.TransactionDetails))
            AddTransactionsSheet(workbook, model);

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

    private static void AddDepartmentsSheet(XLWorkbook workbook, InstitutionalReportModel model)
    {
        var ws = workbook.Worksheets.Add("أداء الإدارات");
        ws.RightToLeft = true;
        var headers = new[] { "الإدارة", "إجمالي", "مغلقة", "مفتوحة", "بانتظار إفادة", "متأخرة", "إدارات مشتركة", "متوسط الإنجاز", "ضمن المهلة", "التقييم" };
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
    }

    private static void AddRisksSheet(XLWorkbook workbook, InstitutionalReportModel model)
    {
        var ws = workbook.Worksheets.Add("المخاطر والتنبيهات");
        ws.RightToLeft = true;
        ws.Cell(1, 1).Value = "م";
        ws.Cell(1, 2).Value = "التنبيه";
        ws.Cell(1, 3).Value = "الإدارة";
        ws.Cell(1, 4).Value = "الخطورة";
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

    private static void AddTransactionsSheet(XLWorkbook workbook, InstitutionalReportModel model)
    {
        var ws = workbook.Worksheets.Add("المعاملات التفصيلية");
        ws.RightToLeft = true;
        var headers = new[] { "م", "رقم المعاملة", "رقم الوارد", "تاريخ الوارد", "الموضوع", "الجهة", "الإدارة", "الإدارات المشتركة", "الأولوية", "الحالة", "مرحلة المتابعة", "الأيام", "المهلة", "آخر إجراء", "حالة الرد" };
        for (var i = 0; i < headers.Length; i++) ws.Cell(1, i + 1).Value = headers[i];
        var row = 2;
        foreach (var t in model.Transactions)
        {
            ws.Cell(row, 1).Value = t.Sequence;
            ws.Cell(row, 2).Value = t.TrackingNumber;
            ws.Cell(row, 3).Value = t.IncomingNumber;
            ws.Cell(row, 4).Value = t.IncomingDate;
            ws.Cell(row, 5).Value = t.Subject;
            ws.Cell(row, 6).Value = t.IncomingParty;
            ws.Cell(row, 7).Value = t.ResponsibleDepartment;
            ws.Cell(row, 8).Value = t.JointDepartments;
            ws.Cell(row, 9).Value = t.Priority;
            ws.Cell(row, 10).Value = t.Status;
            ws.Cell(row, 11).Value = t.FollowUpStage;
            ws.Cell(row, 12).Value = t.ElapsedDays;
            ws.Cell(row, 13).Value = t.DueDate ?? string.Empty;
            ws.Cell(row, 14).Value = t.LastActionDate ?? string.Empty;
            ws.Cell(row, 15).Value = t.ResponseState;
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
}
