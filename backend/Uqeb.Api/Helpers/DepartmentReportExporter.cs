using ClosedXML.Excel;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using Uqeb.Api.DTOs.Reports;

namespace Uqeb.Api.Helpers;

public static class DepartmentReportExporter
{
    static DepartmentReportExporter()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public static byte[] ToExcel(List<DepartmentSummaryDto> data, DateTime? dateFrom, DateTime? dateTo)
    {
        using var workbook = new XLWorkbook();
        var ws = workbook.Worksheets.Add("تقرير الإدارات");
        ws.RightToLeft = true;

        ws.Cell(1, 1).Value = "تقرير الوارد والمغلق لكل إدارة";
        ws.Cell(2, 1).Value = $"الفترة: {FormatDate(dateFrom)} — {FormatDate(dateTo)}";
        ws.Cell(3, 1).Value = $"تاريخ التصدير: {DateTime.Now:yyyy-MM-dd HH:mm}";

        var headers = new[] { "الإدارة", "إجمالي الوارد", "مفتوح", "بانتظار رد", "متأخر", "مغلق", "نسبة الإغلاق %" };
        for (var c = 0; c < headers.Length; c++)
            ws.Cell(5, c + 1).Value = headers[c];

        for (var i = 0; i < data.Count; i++)
        {
            var row = i + 6;
            var d = data[i];
            ws.Cell(row, 1).Value = d.DepartmentName;
            ws.Cell(row, 2).Value = d.TotalIncoming;
            ws.Cell(row, 3).Value = d.OpenCount;
            ws.Cell(row, 4).Value = d.WaitingForReplyCount;
            ws.Cell(row, 5).Value = d.OverdueCount;
            ws.Cell(row, 6).Value = d.ClosedCount;
            ws.Cell(row, 7).Value = d.CloseRate;
        }

        ws.Columns().AdjustToContents();
        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    public static byte[] ToPdf(List<DepartmentSummaryDto> data, DateTime? dateFrom, DateTime? dateTo)
    {
        var fontFamily = GetArabicFontFamily();
        return Document.Create(doc =>
        {
            doc.Page(page =>
            {
                page.Size(PageSizes.A4.Landscape());
                page.Margin(30);
                page.ContentFromRightToLeft();
                page.DefaultTextStyle(x => x.FontFamily(fontFamily).FontSize(10));

                page.Content().Column(col =>
                {
                    col.Spacing(8);
                    col.Item().Text("تقرير الوارد والمغلق لكل إدارة").FontSize(16).Bold();
                    col.Item().Text($"الفترة: {FormatDate(dateFrom)} — {FormatDate(dateTo)}");
                    col.Item().Text($"تاريخ التصدير: {DateTime.Now:yyyy-MM-dd HH:mm}");
                    col.Item().PaddingTop(10).Table(table =>
                    {
                        table.ColumnsDefinition(c =>
                        {
                            c.RelativeColumn(2);
                            c.RelativeColumn();
                            c.RelativeColumn();
                            c.RelativeColumn();
                            c.RelativeColumn();
                            c.RelativeColumn();
                            c.RelativeColumn();
                        });

                        void HeaderCell(string text) =>
                            table.Cell().Background(Colors.Grey.Lighten2).Padding(4).Text(text).Bold();

                        HeaderCell("الإدارة");
                        HeaderCell("إجمالي الوارد");
                        HeaderCell("مفتوح");
                        HeaderCell("بانتظار رد");
                        HeaderCell("متأخر");
                        HeaderCell("مغلق");
                        HeaderCell("نسبة الإغلاق %");

                        foreach (var d in data)
                        {
                            table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten2).Padding(4).Text(d.DepartmentName);
                            table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten2).Padding(4).Text(d.TotalIncoming.ToString());
                            table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten2).Padding(4).Text(d.OpenCount.ToString());
                            table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten2).Padding(4).Text(d.WaitingForReplyCount.ToString());
                            table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten2).Padding(4).Text(d.OverdueCount.ToString());
                            table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten2).Padding(4).Text(d.ClosedCount.ToString());
                            table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten2).Padding(4).Text($"{d.CloseRate}%");
                        }
                    });
                });
            });
        }).GeneratePdf();
    }

    private static string FormatDate(DateTime? d) => d?.ToString("yyyy-MM-dd") ?? "—";

    private static string GetArabicFontFamily() => "Tahoma";
}
