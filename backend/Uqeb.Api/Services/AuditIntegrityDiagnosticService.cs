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
    internal const int ScanLimit = 5000;
    internal const int TransactionIdBatchSize = 1000;

    private readonly AppDbContext _db;

    public AuditIntegrityDiagnosticService(AppDbContext db) => _db = db;

    public async Task<AuditIntegrityDiagnosticReportDto> GetHistoricalReportAsync(CancellationToken cancellationToken = default)
    {
        var totalAuditsAvailable = await _db.AuditLogs.AsNoTracking().CountAsync(cancellationToken);
        var audits = await LoadAuditsAsync(cancellationToken);
        var transactionIds = audits
            .Where(audit => audit.TransactionId.HasValue)
            .Select(audit => audit.TransactionId!.Value)
            .Distinct()
            .ToList();
        var assignmentsByTransaction = await LoadAssignmentsByTransactionAsync(transactionIds, cancellationToken);

        var issues = new List<AuditIntegrityIssueDto>();
        var repairableAssignmentLinkCount = 0;
        var repairableOutgoingDepartmentLinkCount = 0;
        var ambiguousCount = 0;

        foreach (var audit in audits)
        {
            ClassifyAudit(
                audit,
                assignmentsByTransaction,
                issues,
                ref repairableAssignmentLinkCount,
                ref repairableOutgoingDepartmentLinkCount,
                ref ambiguousCount);
        }

        return BuildReport(
            totalAuditsAvailable,
            audits,
            issues,
            repairableAssignmentLinkCount,
            repairableOutgoingDepartmentLinkCount,
            ambiguousCount);
    }

    private async Task<List<AuditLog>> LoadAuditsAsync(CancellationToken cancellationToken) =>
        await _db.AuditLogs.AsNoTracking()
            .OrderByDescending(audit => audit.CreatedAt)
            .ThenByDescending(audit => audit.Id)
            .Take(ScanLimit)
            .ToListAsync(cancellationToken);

    internal static int GetAssignmentBatchQueryCount(int transactionIdCount) =>
        transactionIdCount == 0
            ? 0
            : (int)Math.Ceiling(transactionIdCount / (double)TransactionIdBatchSize);

    internal async Task<Dictionary<int, List<Assignment>>> LoadAssignmentsByTransactionAsync(
        IReadOnlyCollection<int> transactionIds,
        CancellationToken cancellationToken)
    {
        if (transactionIds.Count == 0)
            return new Dictionary<int, List<Assignment>>();

        var assignmentsByTransaction = new Dictionary<int, List<Assignment>>();
        var transactionIdArray = transactionIds as int[] ?? transactionIds.ToArray();

        for (var offset = 0; offset < transactionIdArray.Length; offset += TransactionIdBatchSize)
        {
            var batch = transactionIdArray
                .Skip(offset)
                .Take(TransactionIdBatchSize)
                .ToArray();

            var batchAssignments = await _db.Assignments.AsNoTracking()
                .Where(assignment => batch.Contains(assignment.TransactionId))
                .ToListAsync(cancellationToken);

            foreach (var assignment in batchAssignments)
            {
                if (!assignmentsByTransaction.TryGetValue(assignment.TransactionId, out var assignmentsForTransaction))
                {
                    assignmentsForTransaction = new List<Assignment>();
                    assignmentsByTransaction[assignment.TransactionId] = assignmentsForTransaction;
                }

                assignmentsForTransaction.Add(assignment);
            }
        }

        return assignmentsByTransaction;
    }

    private static void ClassifyAudit(
        AuditLog audit,
        IReadOnlyDictionary<int, List<Assignment>> assignmentsByTransaction,
        ICollection<AuditIntegrityIssueDto> issues,
        ref int repairableAssignmentLinkCount,
        ref int repairableOutgoingDepartmentLinkCount,
        ref int ambiguousCount)
    {
        if (audit.TransactionId == null)
        {
            issues.Add(ToIssue(audit, "missing_transaction_id", "Audit log has no TransactionId."));
            ambiguousCount++;
            return;
        }

        if (audit.EntityId != null)
            return;

        if (audit.EntityName == "TransactionOutgoingDepartments")
        {
            issues.Add(ToIssue(audit, "repairable_missing_entity_id", "Outgoing department audit can link to parent transaction."));
            repairableOutgoingDepartmentLinkCount++;
            return;
        }

        if (audit.EntityName == "Assignment" && TryReadDepartmentId(audit.NewValue, out var departmentId))
        {
            ClassifyMissingAssignmentEntity(
                audit,
                departmentId,
                assignmentsByTransaction,
                issues,
                ref repairableAssignmentLinkCount,
                ref ambiguousCount);
            return;
        }

        issues.Add(ToIssue(audit, "ambiguous_missing_entity_id", "Missing EntityId with no safe repair rule."));
        ambiguousCount++;
    }

    private static void ClassifyMissingAssignmentEntity(
        AuditLog audit,
        int departmentId,
        IReadOnlyDictionary<int, List<Assignment>> assignmentsByTransaction,
        ICollection<AuditIntegrityIssueDto> issues,
        ref int repairableAssignmentLinkCount,
        ref int ambiguousCount)
    {
        assignmentsByTransaction.TryGetValue(audit.TransactionId!.Value, out var assignmentsForTransaction);
        assignmentsForTransaction ??= new List<Assignment>();
        var matchingAssignments = assignmentsForTransaction
            .Where(assignment => assignment.DepartmentId == departmentId)
            .Select(assignment => assignment.Id)
            .ToList();

        if (matchingAssignments.Count == 1)
        {
            issues.Add(ToIssue(
                audit,
                "repairable_assignment_link",
                $"Assignment audit can link to assignment #{matchingAssignments[0]} via departmentId {departmentId}."));
            repairableAssignmentLinkCount++;
            return;
        }

        issues.Add(ToIssue(
            audit,
            "ambiguous_assignment_link",
            $"Found {matchingAssignments.Count} candidate assignments for departmentId {departmentId}."));
        ambiguousCount++;
    }

    private static AuditIntegrityDiagnosticReportDto BuildReport(
        int totalAuditsAvailable,
        IReadOnlyList<AuditLog> audits,
        IReadOnlyList<AuditIntegrityIssueDto> issues,
        int repairableAssignmentLinkCount,
        int repairableOutgoingDepartmentLinkCount,
        int ambiguousCount)
    {
        var totalAuditsScanned = audits.Count;
        return new AuditIntegrityDiagnosticReportDto
        {
            ScanLimit = ScanLimit,
            TotalAuditsAvailable = totalAuditsAvailable,
            TotalAuditsScanned = totalAuditsScanned,
            IsTruncated = totalAuditsAvailable > totalAuditsScanned,
            NewestAuditAt = audits.Count == 0 ? null : audits[0].CreatedAt,
            OldestAuditAt = audits.Count == 0 ? null : audits[^1].CreatedAt,
            MissingTransactionIdCount = audits.Count(audit => audit.TransactionId == null),
            MissingEntityIdCount = audits.Count(audit => audit.EntityId == null),
            RepairableAssignmentLinkCount = repairableAssignmentLinkCount,
            RepairableOutgoingDepartmentLinkCount = repairableOutgoingDepartmentLinkCount,
            TotalRepairableLinkCount = repairableAssignmentLinkCount + repairableOutgoingDepartmentLinkCount,
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
