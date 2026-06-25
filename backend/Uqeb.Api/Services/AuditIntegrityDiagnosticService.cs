using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using Uqeb.Api.Data;
using Uqeb.Api.DTOs.Security;
using Uqeb.Api.Models.Entities;

namespace Uqeb.Api.Services;

public interface IAuditIntegrityDiagnosticService
{
    Task<AuditIntegrityDiagnosticReportDto> GetHistoricalReportAsync(CancellationToken cancellationToken = default);
}

public class AuditIntegrityDiagnosticService : IAuditIntegrityDiagnosticService
{
    private readonly AppDbContext _db;

    public AuditIntegrityDiagnosticService(AppDbContext db) => _db = db;

    public async Task<AuditIntegrityDiagnosticReportDto> GetHistoricalReportAsync(CancellationToken cancellationToken = default)
    {
        var audits = await _db.AuditLogs.AsNoTracking()
            .OrderByDescending(a => a.CreatedAt)
            .Take(5000)
            .ToListAsync(cancellationToken);

        var issues = new List<AuditIntegrityIssueDto>();
        var repairableAssignmentLinkCount = 0;
        var ambiguousCount = 0;
        var transactionIds = audits
            .Where(a => a.TransactionId.HasValue)
            .Select(a => a.TransactionId!.Value)
            .Distinct()
            .ToList();
        var assignmentsByTransaction = transactionIds.Count == 0
            ? new Dictionary<int, List<Assignment>>()
            : (await _db.Assignments.AsNoTracking()
                .Where(a => transactionIds.Contains(a.TransactionId))
                .ToListAsync(cancellationToken))
                .GroupBy(a => a.TransactionId)
                .ToDictionary(g => g.Key, g => g.ToList());

        foreach (var audit in audits)
        {
            if (audit.TransactionId == null)
            {
                issues.Add(ToIssue(audit, "missing_transaction_id", "Audit log has no TransactionId."));
                ambiguousCount++;
                continue;
            }

            if (audit.EntityId != null)
                continue;

            if (audit.EntityName == "TransactionOutgoingDepartments")
            {
                issues.Add(ToIssue(audit, "repairable_missing_entity_id", "Outgoing department audit can link to parent transaction."));
                repairableAssignmentLinkCount++;
                continue;
            }

            if (audit.EntityName == "Assignment" && TryReadDepartmentId(audit.NewValue, out var departmentId))
            {
                assignmentsByTransaction.TryGetValue(audit.TransactionId!.Value, out var assignmentsForTransaction);
                assignmentsForTransaction ??= new List<Assignment>();
                var matchingAssignments = assignmentsForTransaction
                    .Where(a => a.DepartmentId == departmentId)
                    .Select(a => a.Id)
                    .ToList();

                if (matchingAssignments.Count == 1)
                {
                    issues.Add(ToIssue(
                        audit,
                        "repairable_assignment_link",
                        $"Assignment audit can link to assignment #{matchingAssignments[0]} via departmentId {departmentId}."));
                    repairableAssignmentLinkCount++;
                }
                else
                {
                    issues.Add(ToIssue(
                        audit,
                        "ambiguous_assignment_link",
                        $"Found {matchingAssignments.Count} candidate assignments for departmentId {departmentId}."));
                    ambiguousCount++;
                }

                continue;
            }

            issues.Add(ToIssue(audit, "ambiguous_missing_entity_id", "Missing EntityId with no safe repair rule."));
            ambiguousCount++;
        }

        return new AuditIntegrityDiagnosticReportDto
        {
            TotalAuditsScanned = audits.Count,
            MissingTransactionIdCount = audits.Count(a => a.TransactionId == null),
            MissingEntityIdCount = audits.Count(a => a.EntityId == null),
            RepairableAssignmentLinkCount = repairableAssignmentLinkCount,
            AmbiguousCount = ambiguousCount,
            Issues = issues
        };
    }

    private static AuditIntegrityIssueDto ToIssue(AuditLog audit, string classification, string detail) =>
        new()
        {
            AuditLogId = audit.Id,
            Classification = classification,
            EntityName = audit.EntityName,
            EntityId = audit.EntityId,
            TransactionId = audit.TransactionId,
            Action = audit.Action.ToString(),
            CreatedAt = audit.CreatedAt,
            Detail = detail
        };

    private static bool TryReadDepartmentId(string? payload, out int departmentId)
    {
        departmentId = 0;
        if (string.IsNullOrWhiteSpace(payload))
            return false;

        try
        {
            using var document = JsonDocument.Parse(payload);
            if (document.RootElement.TryGetProperty("departmentId", out var property)
                && property.TryGetInt32(out departmentId))
            {
                return true;
            }
        }
        catch (JsonException)
        {
            return false;
        }

        return false;
    }
}
