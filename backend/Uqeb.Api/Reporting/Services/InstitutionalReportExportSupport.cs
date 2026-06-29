using System.IO.Compression;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Uqeb.Api.Data;
using Uqeb.Api.DTOs.Reports;
using Uqeb.Api.Models.Entities;
using Uqeb.Api.Models.Enums;
using Uqeb.Api.Reporting.DTOs;
using Uqeb.Api.Reporting.Enums;

namespace Uqeb.Api.Reporting.Services;

internal static class InstitutionalReportQueryBuilder
{
    internal static IQueryable<Transaction> BuildFilteredQuery(
        AppDbContext db,
        ReportBuildRequestDto request,
        int userId,
        UserRole role,
        int? departmentId)
    {
        var legacyFilter = MapLegacyFilter(request.Filters);
        var query = db.Transactions.AsNoTracking();
        query = InstitutionalReportSnapshotQuery.ApplyInstitutionalFilter(query, request.Filters, legacyFilter);
        query = InstitutionalReportSnapshotQuery.ApplyReportTypeFilter(query, request.ReportType, request.SingleTransactionId);
        if (request.Filters.IncludeOverdue && request.ReportType != InstitutionalReportType.OverdueTransactions)
            query = InstitutionalReportOverdueQuery.ApplyOverdueFilter(query, ReportingTemporalCalculator.RiyadhBusinessDate());
        query = InstitutionalReportSnapshotQuery.ApplyAccessScopeFilter(query, role, departmentId);
        return query;
    }

    private static ReportFilterRequest MapLegacyFilter(ReportFiltersDto filters) => new()
    {
        DateFrom = filters.DateFrom,
        DateTo = filters.DateTo,
        CategoryId = filters.CategoryIds.FirstOrDefault(),
        DepartmentId = filters.DepartmentIds.FirstOrDefault(),
        IncomingPartyId = filters.PartyIds.FirstOrDefault(),
        Search = filters.Search
    };
}

internal static class InstitutionalReportZipExporter
{
    internal static byte[] CreateArchive(IReadOnlyDictionary<string, byte[]> entries)
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, content) in entries)
            {
                var entry = archive.CreateEntry(name, CompressionLevel.Optimal);
                using var entryStream = entry.Open();
                entryStream.Write(content);
            }
        }

        return stream.ToArray();
    }
}

internal static class InstitutionalReportAuditFormatter
{
    internal static string FormatExportAudit(
        ReportExportRequestDto request,
        int totalMatching,
        int detailLimit,
        DetailOverflowAction overflowAction,
        string? extra = null)
    {
        var builder = new StringBuilder();
        builder.Append($"format={request.ExportFormat}");
        builder.Append($";mode={request.ExportMode}");
        builder.Append($";totalMatching={totalMatching}");
        builder.Append($";detailLimit={detailLimit}");
        builder.Append($";overflowAction={overflowAction}");
        if (!string.IsNullOrWhiteSpace(extra))
            builder.Append(';').Append(extra);
        return builder.ToString();
    }
}
