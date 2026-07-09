using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Uqeb.Api.DTOs.Reports;
using Uqeb.Api.Models.Entities;
using Uqeb.Api.Models.Enums;
using Uqeb.Api.Helpers;
using Uqeb.Api.Reporting.DTOs;
using Uqeb.Api.Reporting.Enums;
using Uqeb.Api.Reporting.Models;

namespace Uqeb.Api.Reporting.Services;

internal static class InstitutionalReportSnapshotQuery
{
    internal sealed class SnapshotRow
    {
        public int Id { get; init; }
        public string InternalTrackingNumber { get; init; } = string.Empty;
        public string IncomingNumber { get; init; } = string.Empty;
        public DateTime IncomingDate { get; init; }
        public string Subject { get; init; } = string.Empty;
        public IncomingSourceType IncomingSourceType { get; init; }
        public string? IncomingFromRaw { get; init; }
        public string? IncomingDepartmentName { get; init; }
        public string? IncomingPartyName { get; init; }
        public string? CategoryEntityName { get; init; }
        public string? CategoryRaw { get; init; }
        public Priority Priority { get; init; }
        public TransactionStatus Status { get; init; }
        public bool RequiresResponse { get; init; }
        public bool ResponseCompleted { get; init; }
        public DateTime? ResponseCompletedDate { get; init; }
        public DateTime? ResponseDueDate { get; init; }
        public DateTime? ClosedAt { get; init; }
        public DateTime? UpdatedAt { get; init; }
        public DateTime CreatedAt { get; init; }
        public string? OutgoingNumber { get; init; }
        public DateTime? OutgoingDate { get; init; }
        public List<AssignmentRow> Assignments { get; init; } = [];
        public List<DepartmentRow> OutgoingDepartments { get; init; } = [];
        public DateTime? LastFollowUpDate { get; init; }
    }

    internal sealed class AssignmentRow
    {
        public int DepartmentId { get; init; }
        public string DepartmentName { get; init; } = string.Empty;
        public bool RequiresReply { get; init; }
        public ReplyStatus ReplyStatus { get; init; }
        public AssignmentStatus Status { get; init; }
        public DateTime? DueDate { get; init; }
        public DateTime? ReplyDate { get; init; }
    }

    internal sealed class DepartmentRow
    {
        public int DepartmentId { get; init; }
        public string DepartmentName { get; init; } = string.Empty;
    }

    internal static readonly Expression<Func<Transaction, SnapshotRow>> SnapshotRowProjection = t => new SnapshotRow
    {
        Id = t.Id,
        InternalTrackingNumber = t.InternalTrackingNumber,
        IncomingNumber = t.IncomingNumber,
        IncomingDate = t.IncomingDate,
        Subject = t.Subject,
        IncomingSourceType = t.IncomingSourceType,
        IncomingFromRaw = t.IncomingFrom,
        IncomingDepartmentName = t.IncomingFromDepartment != null ? t.IncomingFromDepartment.Name : null,
        IncomingPartyName = t.IncomingFromParty != null ? t.IncomingFromParty.Name : null,
        CategoryEntityName = t.CategoryEntity != null ? t.CategoryEntity.Name : null,
        CategoryRaw = t.Category,
        Priority = t.Priority,
        Status = t.Status,
        RequiresResponse = t.RequiresResponse,
        ResponseCompleted = t.ResponseCompleted,
        ResponseCompletedDate = t.ResponseCompletedDate,
        ResponseDueDate = t.ResponseDueDate,
        ClosedAt = t.ClosedAt,
        UpdatedAt = t.UpdatedAt,
        CreatedAt = t.CreatedAt,
        OutgoingNumber = t.OutgoingNumber,
        OutgoingDate = t.OutgoingDate,
        Assignments = t.Assignments.Select(a => new AssignmentRow
        {
            DepartmentId = a.DepartmentId,
            DepartmentName = a.Department != null ? a.Department.Name : string.Empty,
            RequiresReply = a.RequiresReply,
            ReplyStatus = a.ReplyStatus,
            Status = a.Status,
            DueDate = a.DueDate,
            ReplyDate = a.ReplyDate,
        }).ToList(),
        OutgoingDepartments = t.OutgoingDepartments.Select(o => new DepartmentRow
        {
            DepartmentId = o.DepartmentId,
            DepartmentName = o.Department.Name,
        }).ToList(),
        LastFollowUpDate = t.FollowUps.Any()
            ? t.FollowUps.Max(f => f.CreatedAt > f.FollowUpDate ? f.CreatedAt : f.FollowUpDate)
            : (DateTime?)null,
    };

    internal static IQueryable<Transaction> ApplyReportTypeFilter(
        IQueryable<Transaction> query,
        InstitutionalReportType reportType,
        int? singleTransactionId,
        DateTime? dateTo)
    {
        if (reportType == InstitutionalReportType.SingleTransaction)
        {
            if (!singleTransactionId.HasValue)
            {
                throw new FieldValidationException(new Dictionary<string, string>
                {
                    ["singleTransactionId"] = "يجب تحديد معاملة واحدة لتقرير المعاملة الواحدة."
                });
            }

            return query.Where(t => t.Id == singleTransactionId.Value);
        }

        if (reportType == InstitutionalReportType.OverdueTransactions)
        {
            return InstitutionalReportOverdueQuery.ApplyOverdueFilter(
                query,
                ResolveOverdueEvaluationDate(reportType, dateTo));
        }

        if (reportType == InstitutionalReportType.JointDepartmentTransactions)
        {
            return query.Where(t => t.Assignments.Count(a => a.Status == AssignmentStatus.Active) > 1
                || t.OutgoingDepartments.Count > 1);
        }

        if (reportType == InstitutionalReportType.PartialResponses)
        {
            return query.Where(t => t.Status == TransactionStatus.PartiallyReplied
                || (t.Assignments.Any(a => a.ReplyStatus == ReplyStatus.Replied)
                    && t.Assignments.Any(a => a.RequiresReply && a.ReplyStatus != ReplyStatus.Replied && a.Status == AssignmentStatus.Active)));
        }

        return query;
    }

    internal static IQueryable<Transaction> ApplyAccessScopeFilter(
        IQueryable<Transaction> query,
        UserRole role,
        int? departmentId)
    {
        if (role == UserRole.Admin)
            return query;

        if (departmentId is not int deptId)
        {
            throw new FieldValidationException(new Dictionary<string, string>
            {
                ["departmentId"] = "تعذر تحديد نطاق الإدارة للمستخدم الحالي."
            });
        }

        return query.Where(t =>
            t.Assignments.Any(a => a.DepartmentId == deptId)
            || t.OutgoingDepartments.Any(o => o.DepartmentId == deptId));
    }

    internal static IQueryable<Transaction> ApplyInstitutionalFilter(
        IQueryable<Transaction> query,
        ReportFiltersDto filters,
        ReportFilterRequest legacy,
        InstitutionalReportType reportType)
    {
        query = ApplyDateFilter(query, legacy, reportType);
        query = ApplyCategoryFilter(query, filters);
        query = ApplyDepartmentFilter(query, filters);
        query = ApplyPartyFilter(query, filters);
        query = ApplyPriorityFilter(query, filters);
        query = ApplyStatusFilter(query, filters);
        query = ApplySearchFilter(query, filters);
        return query;
    }

    private static IQueryable<Transaction> ApplyDateFilter(
        IQueryable<Transaction> query,
        ReportFilterRequest legacy,
        InstitutionalReportType reportType)
    {
        var evaluationDate = ResolveOverdueEvaluationDate(reportType, legacy.DateTo);
        if (legacy.DateTo.HasValue)
        {
            var toExclusive = legacy.DateTo.Value.Date.AddDays(1);
            query = query.Where(t => t.IncomingDate < toExclusive);
        }
        if (legacy.DateFrom.HasValue)
        {
            var from = legacy.DateFrom.Value.Date;
            var openAtPeriodEndExclusive = evaluationDate.Date.AddDays(1);
            query = query.Where(t =>
                t.IncomingDate >= from
                || (t.IncomingDate < from
                    && t.Status != TransactionStatus.Cancelled
                    && t.Status != TransactionStatus.Archived
                    && (t.ClosedAt == null && !t.ResponseCompletedDate.HasValue
                        || t.ClosedAt >= openAtPeriodEndExclusive
                        || t.ResponseCompletedDate >= openAtPeriodEndExclusive)));
        }
        return query;
    }

    /// <summary>The date "as of" which open/overdue reporting state is evaluated.</summary>
    internal static DateTime ResolveOverdueEvaluationDate(InstitutionalReportType reportType, DateTime? dateTo) =>
        dateTo?.Date ?? ReportingTemporalCalculator.RiyadhBusinessDate();

    private static IQueryable<Transaction> ApplyCategoryFilter(IQueryable<Transaction> query, ReportFiltersDto filters)
    {
        if (filters.CategoryIds.Count == 0)
            return query;

        return query.Where(t => t.CategoryId.HasValue && filters.CategoryIds.Contains(t.CategoryId.Value));
    }

    private static IQueryable<Transaction> ApplyDepartmentFilter(IQueryable<Transaction> query, ReportFiltersDto filters)
    {
        if (filters.DepartmentIds.Count == 0)
            return query;

        return query.Where(t => t.Assignments.Any(a => filters.DepartmentIds.Contains(a.DepartmentId))
            || t.OutgoingDepartments.Any(o => filters.DepartmentIds.Contains(o.DepartmentId)));
    }

    private static IQueryable<Transaction> ApplyPartyFilter(IQueryable<Transaction> query, ReportFiltersDto filters)
    {
        if (filters.PartyIds.Count == 0)
            return query;

        return query.Where(t => t.IncomingFromPartyId.HasValue && filters.PartyIds.Contains(t.IncomingFromPartyId.Value));
    }

    private static IQueryable<Transaction> ApplyPriorityFilter(IQueryable<Transaction> query, ReportFiltersDto filters)
    {
        if (filters.Priorities.Count == 0)
            return query;

        var parsed = filters.Priorities
            .Select(p => Enum.TryParse<Priority>(p, true, out var pr) ? (Priority?)pr : null)
            .Where(p => p.HasValue)
            .Select(p => p!.Value)
            .ToList();

        return parsed.Count == 0 ? query : query.Where(t => parsed.Contains(t.Priority));
    }

    private static IQueryable<Transaction> ApplyStatusFilter(IQueryable<Transaction> query, ReportFiltersDto filters)
    {
        if (filters.Statuses.Count == 0)
            return query;

        var parsed = filters.Statuses
            .Select(s => Enum.TryParse<TransactionStatus>(s, true, out var st) ? (TransactionStatus?)st : null)
            .Where(s => s.HasValue)
            .Select(s => s!.Value)
            .ToList();

        return parsed.Count == 0 ? query : query.Where(t => parsed.Contains(t.Status));
    }

    private static IQueryable<Transaction> ApplySearchFilter(IQueryable<Transaction> query, ReportFiltersDto filters)
    {
        if (string.IsNullOrWhiteSpace(filters.Search))
            return query;

        var term = filters.Search.Trim();
        return query.Where(t => t.IncomingNumber.Contains(term)
            || t.InternalTrackingNumber.Contains(term)
            || t.Subject.Contains(term));
    }

    internal static string ResolveIncomingPartyDisplay(SnapshotRow row)
    {
        if (row.IncomingSourceType == IncomingSourceType.Internal)
            return row.IncomingDepartmentName ?? row.IncomingFromRaw ?? "—";

        return row.IncomingPartyName ?? row.IncomingFromRaw ?? "—";
    }

    internal static string? ResolveCategoryName(SnapshotRow row) =>
        row.CategoryEntityName ?? row.CategoryRaw;

    internal static TransactionReportSnapshot MapRowToSnapshot(SnapshotRow row, DateTime today)
    {
        var activeAssignments = row.Assignments.Where(a => a.Status == AssignmentStatus.Active).ToList();
        var uniqueDepartments = BuildDepartmentPairs(activeAssignments);
        var assignmentDeptIds = uniqueDepartments.Select(x => x.DepartmentId).ToList();
        var assignmentDeptNames = uniqueDepartments.Select(x => x.DepartmentName).ToList();
        var responsible = assignmentDeptNames.FirstOrDefault() ?? "—";

        var uniqueOutgoingDepartments = row.OutgoingDepartments
            .GroupBy(o => o.DepartmentId)
            .Select(g => g.First())
            .ToList();

        var pendingReplyAssignments = activeAssignments
            .Where(a => a.RequiresReply && a.ReplyStatus != ReplyStatus.Replied)
            .ToList();
        var pendingReplyDueDates = pendingReplyAssignments
            .Where(a => a.DueDate.HasValue)
            .Select(a => a.DueDate!.Value)
            .ToList();

        // Required-reply assignments across ALL statuses except Cancelled (not just Active),
        // since a replied assignment transitions to Completed and would otherwise be invisible
        // to the "did every required referral get a reply" check.
        var requiredReplySignals = WorkflowHelper.BuildRequiredReplySignals(
            row.Assignments, a => a.RequiresReply, a => a.Status, a => a.ReplyStatus, a => a.ReplyDate);
        var proceduralCompletionDate = WorkflowHelper.ResolveProceduralCompletionDateFromRequiredReplies(
            requiredReplySignals, row.ResponseCompletedDate?.Date)?.Date;
        var isProcedurallyComplete = requiredReplySignals.Count > 0 && proceduralCompletionDate.HasValue;
        var completionDate = proceduralCompletionDate
            ?? (row.ResponseCompleted ? row.ResponseCompletedDate?.Date : null)
            ?? row.ClosedAt?.Date;
        var isClosedAsOf = completionDate.HasValue && completionDate.Value.Date <= today.Date;
        var isTerminalWithoutDate = row.Status is TransactionStatus.Cancelled or TransactionStatus.Archived;
        var isOpenAsOf = row.IncomingDate.Date <= today.Date && !isClosedAsOf && !isTerminalWithoutDate;

        var snapshot = new TransactionReportSnapshot
        {
            TransactionId = row.Id,
            TrackingNumber = row.InternalTrackingNumber,
            IncomingNumber = row.IncomingNumber,
            IncomingDate = row.IncomingDate.Date,
            Subject = row.Subject,
            IncomingParty = ResolveIncomingPartyDisplay(row),
            CategoryName = ResolveCategoryName(row),
            Priority = row.Priority,
            Status = row.Status,
            RequiresResponse = row.RequiresResponse,
            ResponseCompleted = row.ResponseCompleted,
            ResponseCompletedDate = row.ResponseCompletedDate?.Date,
            ResponseDueDate = row.ResponseDueDate?.Date,
            ClosedAt = row.ClosedAt?.Date,
            UpdatedAt = row.UpdatedAt?.Date,
            CreatedAt = row.CreatedAt,
            OutgoingNumber = row.OutgoingNumber,
            OutgoingDate = row.OutgoingDate?.Date,
            ResponsibleDepartment = responsible,
            ResponsibleDepartmentId = assignmentDeptIds.FirstOrDefault(),
            AssignmentDepartmentIds = assignmentDeptIds,
            AssignmentDepartmentNames = assignmentDeptNames,
            OutgoingDepartmentIds = uniqueOutgoingDepartments.Select(o => o.DepartmentId).ToList(),
            OutgoingDepartmentNames = uniqueOutgoingDepartments.Select(o => o.DepartmentName).ToList(),
            ActiveAssignmentCount = activeAssignments.Count,
            RepliedAssignmentCount = activeAssignments.Count(a => a.ReplyStatus == ReplyStatus.Replied),
            PendingReplyAssignmentCount = pendingReplyAssignments.Count,
            LastFollowUpDate = row.LastFollowUpDate?.Date,
            EarliestPendingReplyDueDate = pendingReplyDueDates.Count > 0 ? pendingReplyDueDates.Min() : null,
            ProceduralCompletionDateForReporting = proceduralCompletionDate,
            IsProcedurallyCompleteForReporting = isProcedurallyComplete,
            IsClosed = isClosedAsOf,
            IsOpen = isOpenAsOf,
            ElapsedDays = Math.Max(0, (today - row.IncomingDate.Date).Days)
        };

        snapshot.IsOverdue = InstitutionalReportMetricsCalculator.IsOverdue(snapshot, today);
        snapshot.IsOpenOverdue = InstitutionalReportMetricsCalculator.IsOpenOverdue(snapshot, today);
        snapshot.IsCompletedLate = InstitutionalReportMetricsCalculator.IsCompletedLate(snapshot);
        snapshot.IsWaitingForStatement = InstitutionalReportMetricsCalculator.IsWaitingForStatement(snapshot);
        snapshot.IsPartialReply = InstitutionalReportMetricsCalculator.IsPartialReply(snapshot);
        snapshot.IsJointDepartment = InstitutionalReportMetricsCalculator.IsJointDepartment(snapshot);
        snapshot.FollowUpStages = InstitutionalReportMetricsCalculator.ResolveFollowUpStages(snapshot, today);
        return snapshot;
    }

    internal static List<DepartmentRow> BuildDepartmentPairs(IEnumerable<AssignmentRow> activeAssignments) =>
        activeAssignments
            .Select(a => new DepartmentRow { DepartmentId = a.DepartmentId, DepartmentName = a.DepartmentName })
            .GroupBy(x => x.DepartmentId)
            .Select(g => g.First())
            .ToList();
}
