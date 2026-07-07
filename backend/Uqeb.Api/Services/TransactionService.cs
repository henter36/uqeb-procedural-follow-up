using System.Runtime.ExceptionServices;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Uqeb.Api.Data;
using Uqeb.Api.DTOs.RecurringTemplates;
using Uqeb.Api.DTOs.Transactions;
using Uqeb.Api.Helpers;
using Uqeb.Api.Models.Entities;
using Uqeb.Api.Models.Enums;

namespace Uqeb.Api.Services;

public interface ITransactionService
{
    Task<PagedResult<TransactionListDto>> SearchAsync(TransactionSearchRequest request, ICurrentUserService currentUser);
    Task<TransactionDetailDto?> GetByIdAsync(int id, ICurrentUserService currentUser);
    Task<TransactionDetailDto?> GetBasicByIdAsync(int id, ICurrentUserService currentUser);
    Task<TransactionWorkspaceDto?> GetWorkspaceAsync(int id, ICurrentUserService currentUser);
    Task<List<AssignmentDto>?> GetAssignmentsAsync(int transactionId, ICurrentUserService currentUser);
    Task<List<FollowUpDto>?> GetFollowUpsAsync(int transactionId, ICurrentUserService currentUser);
    Task<TransactionDetailDto> CreateAsync(CreateTransactionRequest request, int userId);
    Task<TransactionDetailDto?> UpdateAsync(int id, UpdateTransactionRequest request, int userId, UserRole role);
    Task<bool> CancelAsync(int id, int userId, UserRole role);
    Task<bool> ArchiveAsync(int id, int userId, UserRole role);
    Task<bool> CloseAsync(int id, int userId, UserRole role);
    Task<TransactionDetailDto?> CompleteResponseAsync(int id, CompleteResponseRequest request, ICurrentUserService currentUser);
    Task<TransactionDetailDto?> EditResponseAsync(int id, CompleteResponseRequest request, ICurrentUserService currentUser);
    Task<List<FollowUpDepartmentOptionDto>?> GetFollowUpDepartmentsAsync(int transactionId, ICurrentUserService currentUser);
    Task<FollowUpDto> AddFollowUpAsync(int transactionId, CreateFollowUpRequest request, int userId);
    Task<FollowUpDto?> ReplyFollowUpAsync(int transactionId, int followUpId, ReplyFollowUpRequest request, int userId);
    Task<FollowUpDto?> EditFollowUpReplyAsync(int transactionId, int followUpId, ReplyFollowUpRequest request, ICurrentUserService currentUser);
    Task<AssignmentDto> AddAssignmentAsync(int transactionId, CreateAssignmentRequest request, int userId);
    Task<AssignmentDto?> ReplyAssignmentAsync(int transactionId, int assignmentId, ReplyAssignmentRequest request, ICurrentUserService currentUser);
    Task<AssignmentDto?> EditAssignmentReplyAsync(int transactionId, int assignmentId, ReplyAssignmentRequest request, ICurrentUserService currentUser);
    Task<AssignmentDto?> AdminEditAssignmentAsync(int transactionId, int assignmentId, AdminEditAssignmentRequest request, int userId);
    Task<TransactionDetailDto?> AdminEditTransactionDatesAsync(int transactionId, AdminEditTransactionDatesRequest request, int userId);
    Task<PagedResult<AuditLogDto>> GetAuditLogAsync(int transactionId, int page, int pageSize, ICurrentUserService currentUser);
    Task<TransactionDetailDto?> EnableRecurringAsync(int id, EnableRecurringForTransactionRequest request, int userId);
    Task<bool> CanAccessTransactionAsync(int transactionId, ICurrentUserService currentUser);
    Task<TransactionAdjacentDto?> GetAdjacentAsync(int id, ICurrentUserService currentUser);
}

public class TransactionService : ITransactionService
{
    private const string AssignmentEntityName = "Assignment";
    private const string AssignmentSourceName = "Assignment";
    private const string DepartmentResponseEntityName = "DepartmentResponse";
    private const string OutgoingDepartmentSourceName = "OutgoingDepartment";
    private const string OutgoingDepartmentsEntityName = "TransactionOutgoingDepartments";
    private const string TransactionEntityName = "Transaction";
    private const string ActiveStatusScope = "active";
    private const string ClosedStatusScope = "closed";
    private const string AllStatusScope = "all";
    private const string FutureEventDateMessage = "لا يمكن أن يكون التاريخ بعد تاريخ اليوم.";
    private static readonly UserRole[] DepartmentResponseReviewRoles =
        [UserRole.Admin, UserRole.Supervisor, UserRole.DataEntry];
    private static readonly AuditAction[] DepartmentResponseSufficientAuditActions =
    [
        AuditAction.DepartmentResponseCreated,
        AuditAction.DepartmentResponseUpdated,
        AuditAction.DepartmentResponseSubmitted,
        AuditAction.DepartmentResponseApproved,
    ];

    private readonly AppDbContext _db;
    private readonly IAuditService _audit;
    private readonly ITrackingNumberService _trackingNumbers;
    private readonly ICacheInvalidationService _cacheInvalidation;
    private readonly IRecurringTransactionTemplateService _recurringTemplates;

    private static readonly Dictionary<string, string> RecurringFieldKeyMap = new()
    {
        [nameof(CreateRecurringTemplateRequest.RecurrenceType)] = nameof(CreateTransactionRequest.RecurringRecurrenceType),
        [nameof(CreateRecurringTemplateRequest.StartDate)] = nameof(CreateTransactionRequest.RecurringStartDate),
        [nameof(CreateRecurringTemplateRequest.EndDate)] = nameof(CreateTransactionRequest.RecurringEndDate),
        [nameof(CreateRecurringTemplateRequest.DueDaysAfterPeriodEnd)] = nameof(CreateTransactionRequest.RecurringDueDaysAfterPeriodEnd),
        [nameof(CreateRecurringTemplateRequest.NextTransactionCreationMethod)] = nameof(CreateTransactionRequest.RecurringNextTransactionCreationMethod),
        [nameof(CreateRecurringTemplateRequest.DepartmentIds)] = nameof(CreateTransactionRequest.OutgoingDepartmentIds),
    };

    public TransactionService(
        AppDbContext db,
        IAuditService audit,
        ITrackingNumberService trackingNumbers,
        ICacheInvalidationService cacheInvalidation,
        IRecurringTransactionTemplateService recurringTemplates)
    {
        _db = db;
        _audit = audit;
        _trackingNumbers = trackingNumbers;
        _cacheInvalidation = cacheInvalidation;
        _recurringTemplates = recurringTemplates;
    }

    private static DateTime GetSaudiToday() => DateTime.UtcNow.AddHours(3).Date;

    private static bool IsFutureEventDate(DateTime value) => value.Date > GetSaudiToday();

    private static void ThrowFutureEventDateValidation(string fieldName) =>
        throw new FieldValidationException(new Dictionary<string, string>
        {
            [fieldName] = FutureEventDateMessage
        });

    private IQueryable<Transaction> BaseQuery() =>
        _db.Transactions
            .AsSplitQuery()
            .Include(t => t.CreatedBy)
            .Include(t => t.CategoryEntity)
            .Include(t => t.IncomingFromParty)
            .Include(t => t.IncomingFromDepartment)
            .Include(t => t.OutgoingDepartments).ThenInclude(o => o.Department)
            .Include(t => t.Assignments).ThenInclude(a => a.Department);

    public async Task<PagedResult<TransactionListDto>> SearchAsync(TransactionSearchRequest request, ICurrentUserService currentUser)
    {
        var now = DateTime.UtcNow;
        var query = ApplySearchFilters(_db.Transactions.AsNoTracking(), request, currentUser, now);
        var sortBy = TransactionSearchPagination.NormalizeSortBy(request.SortBy);
        var pageSize = request.PageSize > 0 ? request.PageSize : 20;

        if (TransactionSearchPagination.IsCursorMode(request.PaginationMode))
        {
            IQueryable<Transaction> orderedQuery = TransactionSearchPagination.ApplySort(query, sortBy, request.SortDesc);

            if (!string.IsNullOrWhiteSpace(request.Cursor))
            {
                var cursorPayload = TransactionSearchCursorCodec.Decode(request.Cursor);
                TransactionSearchPagination.EnsureCursorMatchesRequest(cursorPayload, sortBy, request.SortDesc);
                orderedQuery = TransactionSearchPagination.ApplyKeysetFilter(
                    orderedQuery,
                    sortBy,
                    request.SortDesc,
                    cursorPayload);
            }

            var fetchSize = pageSize + 1;
            var rows = await ProjectSearchRows(orderedQuery, now).Take(fetchSize).ToListAsync();
            var hasNextPage = rows.Count > pageSize;
            if (hasNextPage)
                rows = rows.Take(pageSize).ToList();

            int? totalCount = null;
            if (request.IncludeTotalCount)
                totalCount = await query.CountAsync();

            var items = await MapSearchRowsToDtosAsync(rows, now);
            string? nextCursor = null;
            if (hasNextPage && rows.Count > 0)
            {
                var lastRow = rows[^1];
                nextCursor = TransactionSearchCursorCodec.Encode(
                    TransactionSearchPagination.BuildCursorPayload(sortBy, request.SortDesc, lastRow));
            }

            return PagedResult<TransactionListDto>.CreateCursor(
                items,
                pageSize,
                nextCursor,
                totalCount,
                request.IncludeTotalCount);
        }

        var total = await query.CountAsync();
        var orderedOffset = TransactionSearchPagination.ApplySort(query, sortBy, request.SortDesc);
        var offsetRows = await ProjectSearchRows(orderedOffset, now)
            .Skip((request.Page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        var offsetItems = await MapSearchRowsToDtosAsync(offsetRows, now);
        return PagedResult<TransactionListDto>.Create(offsetItems, total, request.Page, pageSize);
    }

    private IQueryable<Transaction> ApplySearchFilters(
        IQueryable<Transaction> query,
        TransactionSearchRequest request,
        ICurrentUserService currentUser,
        DateTime now)
    {
        query = ApplyDepartmentUserScope(query, currentUser);
        query = ApplyStatusScopeFilter(query, request.StatusScope);
        query = ApplyTextAndReferenceFilters(query, request);
        query = ApplyDateRangeFilters(query, request);
        query = ApplyStatusAndAssignmentFilters(query, request, now);
        query = ApplyRecurringFilters(query, request);
        return query;
    }

    private static IQueryable<Transaction> ApplyRecurringFilters(
        IQueryable<Transaction> query, TransactionSearchRequest request)
    {
        if (request.IsRecurring == true)
            query = query.Where(t => t.RecurringTemplateId != null);
        else if (request.IsRecurring == false)
            query = query.Where(t => t.RecurringTemplateId == null);

        if (!string.IsNullOrWhiteSpace(request.RecurringRecurrenceType) &&
            Enum.TryParse<RecurrenceType>(request.RecurringRecurrenceType, true, out var recurrenceType))
            query = query.Where(t => t.RecurringTemplate != null && t.RecurringTemplate.RecurrenceType == recurrenceType);

        if (!string.IsNullOrWhiteSpace(request.RecurringTemplateStatus) &&
            Enum.TryParse<RecurringTemplateStatus>(request.RecurringTemplateStatus, true, out var templateStatus))
            query = query.Where(t => t.RecurringTemplate != null && t.RecurringTemplate.Status == templateStatus);

        return query;
    }

    private static IQueryable<Transaction> ApplyStatusScopeFilter(
        IQueryable<Transaction> query, string? statusScope)
    {
        var normalized = string.IsNullOrWhiteSpace(statusScope)
            ? ActiveStatusScope
            : statusScope.Trim().ToLowerInvariant();

        return normalized switch
        {
            ActiveStatusScope => query.Where(t =>
                t.Status != TransactionStatus.Closed &&
                t.Status != TransactionStatus.Cancelled &&
                t.Status != TransactionStatus.Archived),
            ClosedStatusScope => query.Where(t => t.Status == TransactionStatus.Closed),
            AllStatusScope => query,
            _ => throw new InvalidOperationException("نطاق حالة المعاملات غير صالح.")
        };
    }

    private IQueryable<Transaction> ApplyDepartmentUserScope(
        IQueryable<Transaction> query, ICurrentUserService currentUser)
    {
        if (currentUser.Role != UserRole.DepartmentUser)
            return query;
        var deptId = RequireDepartmentUserDepartmentId(currentUser);
        return query.Where(t =>
            t.Assignments.Any(a => a.DepartmentId == deptId &&
                a.RequiresReply &&
                a.Status != AssignmentStatus.Cancelled) ||
            _db.DepartmentResponses.Any(r => r.TransactionId == t.Id && r.DepartmentId == deptId));
    }

    private static IQueryable<Transaction> ApplyTextAndReferenceFilters(
        IQueryable<Transaction> query, TransactionSearchRequest request)
    {
        if (!string.IsNullOrWhiteSpace(request.IncomingNumber))
            query = query.Where(t => t.IncomingNumber.Contains(request.IncomingNumber));
        if (!string.IsNullOrWhiteSpace(request.OutgoingNumber))
            query = query.Where(t => t.OutgoingNumber != null && t.OutgoingNumber.Contains(request.OutgoingNumber));
        if (!string.IsNullOrWhiteSpace(request.Subject))
            query = query.Where(t => t.Subject.Contains(request.Subject));
        if (!string.IsNullOrWhiteSpace(request.IncomingSourceType)
            && Enum.TryParse<IncomingSourceType>(request.IncomingSourceType, true, out var incomingSourceType))
            query = query.Where(t => t.IncomingSourceType == incomingSourceType);
        if (request.IncomingFromPartyId.HasValue)
            query = query.Where(t => t.IncomingFromPartyId == request.IncomingFromPartyId);
        if (request.IncomingFromDepartmentId.HasValue)
            query = query.Where(t => t.IncomingFromDepartmentId == request.IncomingFromDepartmentId);
        if (!string.IsNullOrWhiteSpace(request.Status) && Enum.TryParse<TransactionStatus>(request.Status, out var status))
            query = query.Where(t => t.Status == status);
        if (request.DepartmentId.HasValue)
            query = query.Where(t => t.Assignments.Any(a => a.DepartmentId == request.DepartmentId));
        if (request.CategoryId.HasValue)
            query = query.Where(t => t.CategoryId == request.CategoryId);
        if (request.OutgoingPartyId.HasValue)
            query = query.Where(t => t.OutgoingParties.Any(o => o.ExternalPartyId == request.OutgoingPartyId));
        if (request.OutgoingDepartmentId.HasValue)
            query = query.Where(t => t.OutgoingDepartments.Any(o => o.DepartmentId == request.OutgoingDepartmentId));
        return query;
    }

    private static IQueryable<Transaction> ApplyDateRangeFilters(
        IQueryable<Transaction> query, TransactionSearchRequest request)
    {
        if (request.DateFrom.HasValue)
            query = query.Where(t => t.IncomingDate >= request.DateFrom);
        if (request.DateTo.HasValue)
            query = query.Where(t => t.IncomingDate <= request.DateTo);
        if (request.ResponseDueDateFrom.HasValue)
            query = query.Where(t => t.ResponseDueDate >= request.ResponseDueDateFrom);
        if (request.ResponseDueDateTo.HasValue)
            query = query.Where(t => t.ResponseDueDate <= request.ResponseDueDateTo);
        return query;
    }

    private static IQueryable<Transaction> ApplyStatusAndAssignmentFilters(
        IQueryable<Transaction> query, TransactionSearchRequest request, DateTime now)
    {
        var today = now.Date;
        query = ApplyResponseStatusFilters(query, request, today);
        query = ApplyAssignmentStatusFilters(query, request, today);
        return query;
    }

    private static IQueryable<Transaction> ApplyResponseStatusFilters(
        IQueryable<Transaction> query, TransactionSearchRequest request, DateTime today)
    {
        if (request.RequiresResponse == true)
            query = query.Where(t => t.RequiresResponse);
        if (request.ResponseCompleted == true)
            query = query.Where(t => t.ResponseCompleted);
        else if (request.ResponseCompleted == false)
            query = query.Where(t => t.RequiresResponse && !t.ResponseCompleted);
        if (request.ResponseOverdue == true)
            query = query.Where(t => t.RequiresResponse && t.ResponseDueDate.HasValue &&
                ((!t.ResponseCompleted && t.Status != TransactionStatus.Closed && t.ResponseDueDate.Value.Date < today) ||
                 ((t.ResponseCompleted || t.Status == TransactionStatus.Closed) &&
                  ((t.ClosedAt.HasValue && t.ClosedAt.Value.Date > t.ResponseDueDate.Value.Date) ||
                   (!t.ClosedAt.HasValue && t.ResponseCompletedDate.HasValue && t.ResponseCompletedDate.Value.Date > t.ResponseDueDate.Value.Date)))));
        return query;
    }

    private static IQueryable<Transaction> ApplyAssignmentStatusFilters(
        IQueryable<Transaction> query, TransactionSearchRequest request, DateTime today)
    {
        if (request.HasPendingAssignments == true)
            query = query.Where(t => t.Assignments.Any(a =>
                a.RequiresReply && a.ReplyStatus != ReplyStatus.Replied && a.Status == AssignmentStatus.Active));
        if (request.HasPartialReplies == true)
            query = query.Where(t => t.Status == TransactionStatus.PartiallyReplied);
        if (request.OverdueOnly == true)
            query = ApplyOverdueOnlyFilter(query, today);
        return query;
    }

    private static IQueryable<Transaction> ApplyOverdueOnlyFilter(IQueryable<Transaction> query, DateTime today) =>
        query.Where(t =>
            (t.RequiresResponse && t.ResponseDueDate.HasValue &&
                ((!t.ResponseCompleted && t.Status != TransactionStatus.Closed && t.ResponseDueDate.Value.Date < today) ||
                 ((t.ResponseCompleted || t.Status == TransactionStatus.Closed) &&
                  ((t.ClosedAt.HasValue && t.ClosedAt.Value.Date > t.ResponseDueDate.Value.Date) ||
                   (!t.ClosedAt.HasValue && t.ResponseCompletedDate.HasValue && t.ResponseCompletedDate.Value.Date > t.ResponseDueDate.Value.Date))))) ||
            t.Assignments.Any(a => a.RequiresReply && a.DueDate.HasValue &&
                ((a.ReplyStatus != ReplyStatus.Replied
                  && a.Status == AssignmentStatus.Active
                  && a.DueDate.Value.Date < today) ||
                 (a.ReplyStatus == ReplyStatus.Replied
                  && a.ReplyDate.HasValue
                  && a.ReplyDate.Value.Date > a.DueDate.Value.Date))));

    private static IQueryable<TransactionSearchRow> ProjectSearchRows(IQueryable<Transaction> ordered, DateTime now)
    {
        var today = now.Date;
        return ordered
            .Select(t => new
            {
                Transaction = t,
                IncomingFromPartyName = t.IncomingFromParty != null ? t.IncomingFromParty.Name : null,
                IncomingFromDepartmentName = t.IncomingFromDepartment != null ? t.IncomingFromDepartment.Name : null,
                CategoryName = t.CategoryEntity != null ? t.CategoryEntity.Name : t.Category,
                CreatedByName = t.CreatedBy != null ? t.CreatedBy.FullName : "",
                RecurrenceType = t.RecurringTemplate != null ? t.RecurringTemplate.RecurrenceType : (RecurrenceType?)null,
                HasPendingAssignment = t.Assignments.Any(a =>
                    a.RequiresReply &&
                    a.ReplyStatus != ReplyStatus.Replied &&
                    a.Status == AssignmentStatus.Active),
                // Replied vs not-replied are mutually exclusive, so the open-and-pending
                // vs completed-and-late cases branch on reply status instead of an OR.
                HasOverdueAssignment = t.Assignments.Any(a =>
                    a.RequiresReply &&
                    a.DueDate.HasValue &&
                    (a.ReplyStatus == ReplyStatus.Replied
                        ? a.ReplyDate.HasValue && a.ReplyDate.Value.Date > a.DueDate.Value.Date
                        : a.Status == AssignmentStatus.Active && a.DueDate.Value.Date < today)),
                // Completed-or-closed vs still open are mutually exclusive, and within the
                // completed branch, having a ClosedAt vs not is also mutually exclusive, so
                // both branch on those conditions instead of combining via OR.
                IsResponseOverdue =
                    t.RequiresResponse &&
                    t.ResponseDueDate.HasValue &&
                    (t.ResponseCompleted || t.Status == TransactionStatus.Closed
                        ? (t.ClosedAt.HasValue
                            ? t.ClosedAt.Value.Date > t.ResponseDueDate.Value.Date
                            : t.ResponseCompletedDate.HasValue && t.ResponseCompletedDate.Value.Date > t.ResponseDueDate.Value.Date)
                        : t.ResponseDueDate.Value.Date < today)
            })
            .Select(x => new TransactionSearchRow(
                x.Transaction.Id,
                x.Transaction.InternalTrackingNumber,
                x.Transaction.IncomingNumber,
                x.Transaction.IncomingDate,
                x.Transaction.Subject,
                x.Transaction.IncomingFrom,
                x.Transaction.IncomingSourceType,
                x.IncomingFromPartyName,
                x.IncomingFromDepartmentName,
                x.Transaction.OutgoingNumber,
                x.Transaction.OutgoingDate,
                x.Transaction.Status,
                x.Transaction.Priority,
                x.CategoryName,
                x.Transaction.RequiresResponse,
                x.Transaction.ResponseCompleted,
                x.Transaction.ResponseCompletedDate,
                x.Transaction.ResponseDueDays,
                x.Transaction.ResponseDueDate,
                x.Transaction.ClosedAt,
                x.Transaction.IsArchived,
                x.CreatedByName,
                x.Transaction.CreatedAt,
                x.HasPendingAssignment,
                x.IsResponseOverdue,
                x.IsResponseOverdue || x.HasOverdueAssignment,
                x.Transaction.RecurringTemplateId,
                x.Transaction.RecurringPeriodLabel,
                x.RecurrenceType));
    }

    private async Task<List<TransactionListDto>> MapSearchRowsToDtosAsync(List<TransactionSearchRow> rows, DateTime now)
    {
        var pageIds = rows.Select(r => r.Id).ToList();
        var deptNames = pageIds.Count == 0
            ? new List<(int TransactionId, string Name)>()
            : (await _db.TransactionOutgoingDepartments.AsNoTracking()
                .Where(l => pageIds.Contains(l.TransactionId))
                .Select(l => new { l.TransactionId, Name = l.Department.Name })
                .ToListAsync())
                .Select(x => (x.TransactionId, x.Name))
                .ToList();

        var deptLookup = deptNames
            .GroupBy(x => x.TransactionId)
            .ToDictionary(g => g.Key, g => g.Select(x => x.Name).ToList());

        var lastFollowUpLookup = pageIds.Count == 0
            ? new Dictionary<int, DateTime?>()
            : (await _db.FollowUps.AsNoTracking()
                .Where(f => pageIds.Contains(f.TransactionId))
                .GroupBy(f => f.TransactionId)
                .Select(g => new
                {
                    TransactionId = g.Key,
                    LastDate = g.Max(f => f.CreatedAt > f.FollowUpDate ? f.CreatedAt : f.FollowUpDate)
                })
                .ToListAsync())
                .ToDictionary(x => x.TransactionId, x => (DateTime?)x.LastDate);

        return rows.Select(r =>
        {
            var names = deptLookup.GetValueOrDefault(r.Id) ?? new List<string>();
            var dto = new TransactionListDto
            {
                Id = r.Id,
                InternalTrackingNumber = r.InternalTrackingNumber,
                IncomingNumber = r.IncomingNumber,
                IncomingDate = r.IncomingDate,
                Subject = r.Subject,
                IncomingFrom = r.IncomingSourceType switch
                {
                    IncomingSourceType.Internal => r.IncomingFromDepartmentName ?? r.IncomingFrom,
                    IncomingSourceType.External => r.IncomingFromPartyName ?? r.IncomingFrom,
                    _ => r.IncomingFrom
                },
                IncomingSourceType = r.IncomingSourceType.ToString(),
                OutgoingNumber = r.OutgoingNumber,
                OutgoingDate = r.OutgoingDate,
                OutgoingDepartmentNames = names,
                OutgoingPartyNames = names,
                Status = r.Status.ToString(),
                Priority = r.Priority.ToString(),
                CategoryName = r.CategoryName,
                RequiresResponse = r.RequiresResponse,
                ResponseCompleted = r.ResponseCompleted,
                ResponseDueDate = r.ResponseDueDate,
                IsResponseOverdue = r.IsResponseOverdue,
                HasPendingAssignments = r.HasPendingAssignments,
                IsOverdue = r.IsOverdue,
                IsArchived = r.IsArchived,
                CreatedByName = r.CreatedByName,
                CreatedAt = r.CreatedAt,
                RecurringTemplateId = r.RecurringTemplateId,
                RecurringPeriodLabel = r.RecurringPeriodLabel,
                RecurringRecurrenceType = r.RecurringRecurrenceType?.ToString()
            };
            var lastFollowUp = lastFollowUpLookup.GetValueOrDefault(r.Id);
            TransactionTimelineHelper.ApplyTo(dto, TransactionTimelineHelper.Compute(new TransactionTimelineHelper.TimelineComputationInput
            {
                IncomingDate = r.IncomingDate,
                ResponseDueDate = r.ResponseDueDate,
                ResponseDueDays = r.ResponseDueDays,
                RequiresResponse = r.RequiresResponse,
                ResponseCompleted = r.ResponseCompleted,
                ResponseCompletedDate = r.ResponseCompletedDate,
                Status = r.Status,
                ClosedAt = r.ClosedAt,
                LastFollowUpDate = lastFollowUp?.Date,
                Today = now.Date
            }));
            return dto;
        }).ToList();
    }

    public Task<TransactionDetailDto?> GetByIdAsync(int id, ICurrentUserService currentUser) =>
        GetBasicByIdAsync(id, currentUser);

    public async Task<TransactionDetailDto?> GetBasicByIdAsync(int id, ICurrentUserService currentUser)
    {
        var t = await _db.Transactions
            .AsNoTracking()
            .Include(x => x.CreatedBy)
            .Include(x => x.CategoryEntity)
            .Include(x => x.IncomingFromParty)
            .Include(x => x.IncomingFromDepartment)
            .Include(x => x.OutgoingDepartments).ThenInclude(o => o.Department)
            .Include(x => x.RecurringTemplate)
            .FirstOrDefaultAsync(x => x.Id == id);

        if (t == null) return null;
        if (!await CanAccessTransactionAsync(id, currentUser)) return null;

        var assignmentRows = await (
            from a in _db.Assignments.AsNoTracking()
            join d in _db.Departments.AsNoTracking()
                on a.DepartmentId equals d.Id
                into departmentGroup
            from d in departmentGroup.DefaultIfEmpty()
            where a.TransactionId == id
            select new AssignmentSummaryRow(
                d != null ? d.Name : "إدارة غير معروفة",
                a.ReplyStatus,
                a.RequiresReply,
                a.Status,
                a.DueDate))
            .ToListAsync();

        var dto = MapToBasicDetailDto(t, assignmentRows, DateTime.UtcNow);
        var lastFollowUp = await _db.FollowUps.AsNoTracking()
            .Where(f => f.TransactionId == id)
            .Select(f => f.CreatedAt > f.FollowUpDate ? f.CreatedAt : f.FollowUpDate)
            .OrderByDescending(d => d)
            .FirstOrDefaultAsync();
        TransactionTimelineHelper.ApplyForTransaction(dto, t, DateTime.UtcNow, lastFollowUp == default ? null : lastFollowUp);
        dto.Assignments = new();
        dto.FollowUps = new();
        dto.Attachments = new();
        dto.AuditLogs = new();
        return dto;
    }

    public async Task<TransactionWorkspaceDto?> GetWorkspaceAsync(int id, ICurrentUserService currentUser)
    {
        var transaction = await GetBasicByIdAsync(id, currentUser);
        if (transaction == null) return null;

        var entity = await _db.Transactions.AsNoTracking().FirstOrDefaultAsync(t => t.Id == id);
        if (entity == null) return null;

        var now = DateTime.UtcNow;
        var assignments = await GetAssignmentsAsync(id, currentUser) ?? new();
        var followUps = await GetFollowUpsAsync(id, currentUser) ?? new();
        var attachments = await _db.Attachments.AsNoTracking()
            .Where(a => a.TransactionId == id)
            .OrderByDescending(a => a.UploadedAt)
            .Select(a => new AttachmentDto
            {
                Id = a.Id,
                AttachmentType = a.AttachmentType,
                OriginalFileName = a.OriginalFileName,
                ContentType = a.ContentType,
                FileSize = a.FileSize,
                UploadedByName = a.UploadedBy != null ? a.UploadedBy.FullName : "",
                UploadedAt = a.UploadedAt
            })
            .ToListAsync();

        return new TransactionWorkspaceDto
        {
            Transaction = transaction,
            Assignments = assignments,
            FollowUps = followUps,
            Attachments = attachments,
            TemporalFacts = TransactionWorkspaceHelper.BuildTemporalFacts(entity, assignments, now),
            AllowedActions = TransactionWorkspaceHelper.BuildAllowedActions(transaction, currentUser)
        };
    }

    public async Task<List<AssignmentDto>?> GetAssignmentsAsync(int transactionId, ICurrentUserService currentUser)
    {
        if (!await CanAccessTransactionAsync(transactionId, currentUser)) return null;

        var departmentId = currentUser.Role == UserRole.DepartmentUser
            ? RequireDepartmentUserDepartmentId(currentUser)
            : (int?)null;
        var isAdmin = currentUser.Role == UserRole.Admin;
        var now = DateTime.UtcNow;

        var incomingDate = await _db.Transactions.AsNoTracking()
            .Where(t => t.Id == transactionId)
            .Select(t => (DateTime?)t.IncomingDate)
            .FirstOrDefaultAsync();

        var query = _db.Assignments.AsNoTracking()
            .Where(a => a.TransactionId == transactionId);
        if (departmentId.HasValue)
            query = query.Where(a => a.DepartmentId == departmentId.Value && a.Status != AssignmentStatus.Cancelled);

        var rows = await (
            from a in query
            join d in _db.Departments.AsNoTracking()
                on a.DepartmentId equals d.Id
                into departmentGroup
            from d in departmentGroup.DefaultIfEmpty()
            join u in _db.Users.AsNoTracking()
                on a.CreatedById equals u.Id
                into createdByGroup
            from u in createdByGroup.DefaultIfEmpty()
            orderby a.AssignedDate descending
            select new
            {
                a.Id,
                a.DepartmentId,
                DepartmentName = d != null ? d.Name : "إدارة غير معروفة",
                a.LetterNumber,
                a.AssignedDate,
                a.RequiredAction,
                a.RequiresReply,
                a.ReplyDueDays,
                a.DueDate,
                a.ReplyStatus,
                a.ReplyDate,
                a.ReplySummary,
                a.Status,
                CreatedByName = u != null ? u.FullName : "",
                a.CreatedAt,
                // The assignment's own ReplyDate is the authoritative completion date
                // (set once ApproveAsync clamps/records it); DepartmentResponse rows can be
                // drafts, rejected, or resubmitted, so joining on them directly for the date
                // both used the wrong field and risked duplicate/non-deterministic rows.
                // Only an Approved response is a "completed" one — surfacing a Draft/Submitted/
                // Rejected/ReturnedForCorrection id here would let a row that isn't actually
                // finished look editable via the admin-edit-response endpoint.
                DepartmentResponseId = _db.DepartmentResponses
                    .Where(dr => dr.TransactionId == a.TransactionId
                        && dr.DepartmentId == a.DepartmentId
                        && dr.Status == DepartmentResponseStatus.Approved)
                    .OrderByDescending(dr => dr.Id)
                    .Select(dr => (int?)dr.Id)
                    .FirstOrDefault()
            }
        ).ToListAsync();

        return rows.Select(r =>
        {
            int? completionDays = null;
            if (r.ReplyDate.HasValue && incomingDate.HasValue)
                completionDays = Math.Max(0, (r.ReplyDate.Value.Date - incomingDate.Value.Date).Days);

            return new AssignmentDto
            {
                Id = r.Id,
                DepartmentId = r.DepartmentId,
                DepartmentName = r.DepartmentName,
                LetterNumber = r.LetterNumber,
                AssignedDate = r.AssignedDate,
                RequiredAction = r.RequiredAction,
                RequiresReply = r.RequiresReply,
                ReplyDueDays = r.ReplyDueDays,
                DueDate = r.DueDate,
                ReplyStatus = r.ReplyStatus.ToString(),
                ReplyDate = r.ReplyDate,
                ReplySummary = r.ReplySummary,
                Status = r.Status.ToString(),
                IsOverdue = TransactionTemporalCalculator.IsAssignmentOverdue(
                    r.ReplyStatus, r.RequiresReply, r.Status, r.DueDate, now),
                DepartmentResponseId = r.DepartmentResponseId,
                ResponseDate = r.ReplyDate,
                DepartmentCompletionDays = completionDays,
                CanAdminEdit = isAdmin,
                CreatedByName = r.CreatedByName,
                CreatedAt = r.CreatedAt
            };
        }).ToList();
    }

    public async Task<List<FollowUpDto>?> GetFollowUpsAsync(int transactionId, ICurrentUserService currentUser)
    {
        if (!await CanAccessTransactionAsync(transactionId, currentUser)) return null;

        var departmentId = currentUser.Role == UserRole.DepartmentUser
            ? RequireDepartmentUserDepartmentId(currentUser)
            : (int?)null;
        var query = _db.FollowUps.AsNoTracking()
            .Where(f => f.TransactionId == transactionId);
        if (departmentId.HasValue)
            query = query.Where(f => f.Departments.Any(d => d.DepartmentId == departmentId.Value));

        return await query
            .OrderByDescending(f => f.CreatedAt)
            .Select(f => new FollowUpDto
            {
                Id = f.Id,
                FollowUpNumber = f.FollowUpNumber,
                FollowUpDate = f.FollowUpDate,
                SentTo = f.SentTo,
                Recipients = f.Recipients.Select(r => new FollowUpRecipientDto
                {
                    Id = r.Id,
                    ExternalPartyId = r.ExternalPartyId,
                    PartyName = r.ExternalParty != null ? r.ExternalParty.Name : ""
                }).ToList(),
                Departments = f.Departments.Select(d => new FollowUpDepartmentDto
                {
                    Id = d.Id,
                    DepartmentId = d.DepartmentId,
                    DepartmentName = d.Department != null ? d.Department.Name : ""
                }).ToList(),
                Notes = f.Notes,
                RequiresReply = f.RequiresReply,
                ReplyStatus = f.ReplyStatus.ToString(),
                ReplyDate = f.ReplyDate,
                ReplySummary = f.ReplySummary,
                CreatedByName = f.CreatedBy != null ? f.CreatedBy.FullName : "",
                CreatedAt = f.CreatedAt
            })
            .ToListAsync();
    }

    public async Task<TransactionDetailDto> CreateAsync(CreateTransactionRequest request, int userId)
    {
        var validationErrors = TransactionRequestValidator.ValidateCreate(request);
        if (validationErrors.Count > 0)
            throw new FieldValidationException(validationErrors);

        CreateRecurringTemplateRequest? recurringTemplateRequest = null;
        if (request.EnableRecurringFollowUp == true)
        {
            recurringTemplateRequest = BuildRecurringTemplateRequestFromTransaction(request);
            var recurringErrors = RecurringTemplateRequestValidator.Validate(recurringTemplateRequest);
            if (recurringErrors.Count > 0)
                throw new FieldValidationException(RemapRecurringFieldErrors(recurringErrors));
        }

        if (await _db.Transactions.AnyAsync(t => t.IncomingNumber == request.IncomingNumber))
            throw new DuplicateIncomingNumberException();

        var transaction = BuildNewTransaction(request, userId);
        await ApplyIncomingSourceAsync(
            transaction,
            request.IncomingSourceType!,
            request.IncomingFromPartyId,
            request.IncomingFromDepartmentId);
        await PersistNewTransactionAsync(
            transaction,
            request.OutgoingDepartmentIds ?? new List<int>(),
            userId);

        if (recurringTemplateRequest != null)
            await _recurringTemplates.EnableForTransactionAsync(transaction, recurringTemplateRequest, userId);

        _cacheInvalidation.InvalidateOnTransactionChange();
        return (await GetByIdAsync(transaction.Id, new SystemUser(userId)))!;
    }

    public async Task<TransactionDetailDto?> EnableRecurringAsync(int id, EnableRecurringForTransactionRequest request, int userId)
    {
        var transaction = await _db.Transactions.FirstOrDefaultAsync(t => t.Id == id);
        if (transaction == null) return null;

        var templateRequest = new CreateRecurringTemplateRequest
        {
            Title = transaction.Subject,
            SubjectTemplate = transaction.Subject,
            RecurrenceType = request.RecurrenceType,
            StartDate = transaction.IncomingDate.Date,
            EndDate = null,
            IncomingSourceType = transaction.IncomingSourceType.ToString(),
            IncomingFromPartyId = transaction.IncomingFromPartyId,
            IncomingFromDepartmentId = transaction.IncomingFromDepartmentId,
            CategoryId = transaction.CategoryId,
            Priority = transaction.Priority.ToString(),
            ResponseType = transaction.ResponseType.ToString(),
            RequiresResponse = transaction.RequiresResponse,
            DefaultRequiredAction = $"متابعة: {transaction.Subject}",
            DueDaysAfterPeriodEnd = 0,
            DefaultReplyDueDays = null,
            Notes = null,
            DepartmentIds = await _db.TransactionOutgoingDepartments
                .Where(o => o.TransactionId == id)
                .Select(o => o.DepartmentId)
                .ToListAsync(),
            NextTransactionCreationMethod = request.NextTransactionCreationMethod
        };

        await _recurringTemplates.EnableForTransactionAsync(transaction, templateRequest, userId);

        _cacheInvalidation.InvalidateOnTransactionChange();
        return await GetByIdAsync(id, new SystemUser(userId));
    }

    private static CreateRecurringTemplateRequest BuildRecurringTemplateRequestFromTransaction(CreateTransactionRequest request)
    {
        var subject = request.Subject.Trim();
        var incomingSourceType = request.IncomingSourceType ?? "External";
        var responseType = request.ResponseType ?? "External";

        return new CreateRecurringTemplateRequest
        {
            Title = subject,
            SubjectTemplate = subject,
            RecurrenceType = request.RecurringRecurrenceType,
            StartDate = request.IncomingDate.Date,
            EndDate = null,
            IncomingSourceType = incomingSourceType,
            IncomingFromPartyId = request.IncomingFromPartyId,
            IncomingFromDepartmentId = request.IncomingFromDepartmentId,
            CategoryId = request.CategoryId,
            Priority = request.Priority,
            ResponseType = responseType,
            RequiresResponse = request.RequiresResponse ?? (EnumHelper.ParseResponseType(responseType) != ResponseType.None),
            DefaultRequiredAction = $"متابعة: {subject}",
            DueDaysAfterPeriodEnd = 0,
            DefaultReplyDueDays = null,
            Notes = null,
            DepartmentIds = request.OutgoingDepartmentIds,
            NextTransactionCreationMethod = request.RecurringNextTransactionCreationMethod
        };
    }

    private static Dictionary<string, string> RemapRecurringFieldErrors(Dictionary<string, string> errors) =>
        errors.ToDictionary(e => RecurringFieldKeyMap.GetValueOrDefault(e.Key, e.Key), e => e.Value);

    private static Transaction BuildNewTransaction(CreateTransactionRequest request, int userId)
    {
        var hasOutgoing = TransactionRequestValidator.HasAnyOutgoingData(request);
        var responseType = EnumHelper.ParseResponseType(request.ResponseType ?? "External");
        var requiresResponse = responseType != ResponseType.None;

        var transaction = new Transaction
        {
            IncomingNumber = request.IncomingNumber.Trim(),
            IncomingDate = request.IncomingDate,
            Subject = request.Subject.Trim(),
            OutgoingNumber = hasOutgoing ? request.OutgoingNumber?.Trim() : null,
            OutgoingDate = hasOutgoing ? request.OutgoingDate : null,
            RequiresResponse = requiresResponse,
            ResponseType = responseType,
            ResponseDueDays = requiresResponse ? request.ResponseDueDays : null,
            Priority = EnumHelper.ParsePriority(request.Priority),
            CategoryId = request.CategoryId,
            Notes = request.Notes,
            Status = TransactionStatus.New,
            CreatedById = userId,
            CreatedAt = DateTime.UtcNow
        };

        WorkflowHelper.RecalculateResponseDueDate(transaction);
        return transaction;
    }

    private async Task PersistNewTransactionAsync(
        Transaction transaction,
        IReadOnlyCollection<int> outgoingDepartmentIds,
        int userId,
        CancellationToken cancellationToken = default)
    {
        await using var dbTransaction = await _db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            await ExecuteCreatePersistenceAttemptsAsync(
                transaction,
                outgoingDepartmentIds,
                userId,
                cancellationToken);
            await dbTransaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await dbTransaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    private async Task ExecuteCreatePersistenceAttemptsAsync(
        Transaction transaction,
        IReadOnlyCollection<int> outgoingDepartmentIds,
        int userId,
        CancellationToken cancellationToken)
    {
        const int maxAttempts = 3;
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            var pendingAudits = new List<AuditLog>();
            ResetCreatePersistenceState(transaction);

            transaction.InternalTrackingNumber = await _trackingNumbers.GenerateNextAsync(cancellationToken);
            _db.Transactions.Add(transaction);

            await SyncOutgoingDepartmentsAsync(
                transaction,
                outgoingDepartmentIds.ToList(),
                userId,
                pendingAudits);

            try
            {
                await _db.SaveChangesAsync(cancellationToken);
                BackfillAuditLogsForTransaction(transaction, pendingAudits);
                foreach (var pendingAudit in pendingAudits)
                    _db.AuditLogs.Add(pendingAudit);
                break;
            }
            catch (DbUpdateException ex)
            {
                HandleCreatePersistenceFailure(ex, transaction, pendingAudits, attempt, maxAttempts);
            }
        }

        _audit.TrackLog(
            userId,
            AuditAction.Create,
            TransactionEntityName,
            transaction.Id,
            transaction.Id,
            null,
            JsonSerializer.Serialize(new { transaction.IncomingNumber, transaction.Subject }));
        await _db.SaveChangesAsync(cancellationToken);
    }

    private void HandleCreatePersistenceFailure(
        DbUpdateException exception,
        Transaction transaction,
        IReadOnlyList<AuditLog> pendingAudits,
        int attempt,
        int maxAttempts)
    {
        if (SqlExceptionHelper.IsDuplicateKey(exception, "IX_Transactions_IncomingNumber"))
        {
            ResetCreatePersistenceState(transaction, pendingAudits);
            DetachTransaction(transaction);
            throw new DuplicateIncomingNumberException();
        }

        if (SqlExceptionHelper.IsDuplicateKey(exception, "IX_Transactions_InternalTrackingNumber") && attempt < maxAttempts)
        {
            ResetCreatePersistenceState(transaction, pendingAudits);
            return;
        }

        if (SqlExceptionHelper.IsDuplicateKey(exception, "IX_Transactions_InternalTrackingNumber"))
        {
            ResetCreatePersistenceState(transaction, pendingAudits);
            throw new DuplicateTrackingNumberException();
        }

        ExceptionDispatchInfo.Capture(exception).Throw();
    }

    public async Task<TransactionDetailDto?> UpdateAsync(int id, UpdateTransactionRequest request, int userId, UserRole role)
    {
        var t = await _db.Transactions.Include(x => x.OutgoingDepartments).FirstOrDefaultAsync(x => x.Id == id);
        if (t == null) return null;

        var validationErrors = TransactionRequestValidator.ValidateUpdate(
            request,
            t.IncomingDate,
            t.OutgoingDate,
            t.OutgoingNumber,
            t.OutgoingDepartments.Select(o => o.DepartmentId).ToList());
        if (validationErrors.Count > 0)
            throw new FieldValidationException(validationErrors);

        if (request.IncomingDate.HasValue)
            await EnsureIncomingDateDoesNotFollowExistingTimelineAsync(id, incomingDateSpecified: true, request.IncomingDate.Value.Date);

        var oldValues = JsonSerializer.Serialize(new
        {
            t.IncomingNumber,
            t.Subject,
            t.Status,
            t.CategoryId,
            t.Priority,
            OutgoingDepartmentIds = t.OutgoingDepartments.Select(o => o.DepartmentId).ToList(),
            t.ResponseDueDays,
            ResponseType = t.ResponseType.ToString()
        });

        if (!string.IsNullOrEmpty(request.IncomingNumber) && request.IncomingNumber != t.IncomingNumber)
        {
            if (await _db.Transactions.AnyAsync(x => x.IncomingNumber == request.IncomingNumber && x.Id != id))
                throw new InvalidOperationException("رقم المعاملة موجود مسبقاً");
            t.IncomingNumber = request.IncomingNumber;
        }

        if (request.IncomingDate.HasValue) t.IncomingDate = request.IncomingDate.Value;
        if (!string.IsNullOrEmpty(request.Subject)) t.Subject = request.Subject;
        if (!string.IsNullOrEmpty(request.IncomingSourceType))
            await ApplyIncomingSourceAsync(t, request.IncomingSourceType, request.IncomingFromPartyId, request.IncomingFromDepartmentId);
        if (request.OutgoingNumber != null) t.OutgoingNumber = request.OutgoingNumber;
        if (request.OutgoingDate.HasValue) t.OutgoingDate = request.OutgoingDate;
        if (request.RequiresResponse.HasValue)
        {
            t.RequiresResponse = request.RequiresResponse.Value;
            if (!t.RequiresResponse)
            {
                t.ResponseType = ResponseType.None;
                t.ResponseDueDays = null;
            }
        }
        if (!string.IsNullOrEmpty(request.ResponseType))
        {
            var newType = EnumHelper.ParseResponseType(request.ResponseType);
            if (t.RequiresResponse && newType == ResponseType.None)
                throw new InvalidOperationException("نوع الإفادة مطلوب عند تفعيل مطلوب إفادة");
            var oldType = t.ResponseType;
            t.ResponseType = newType;
            if (oldType != newType)
                _audit.TrackLog(userId, AuditAction.Update, TransactionEntityName, id, id, oldType.ToString(), newType.ToString());
        }
        if (request.ResponseDueDays.HasValue) t.ResponseDueDays = request.ResponseDueDays;
        if (request.ResponseCompleted.HasValue || request.ResponseCompletedDate.HasValue)
            throw new InvalidOperationException("يجب تسجيل الإفادة عبر إجراء «تسجيل الإفادة» وليس من شاشة التعديل");
        bool isClosing = false;
        if (!string.IsNullOrEmpty(request.Status))
        {
            var newStatus = EnumHelper.ParseTransactionStatus(request.Status);
            if (newStatus == TransactionStatus.Closed && role != UserRole.Admin && role != UserRole.Supervisor)
                throw new UnauthorizedAccessException("لا تملك صلاحية إغلاق المعاملة");
            isClosing = newStatus == TransactionStatus.Closed;
            t.Status = newStatus;
        }
        if (!string.IsNullOrEmpty(request.Priority))
        {
            var oldPriority = t.Priority;
            t.Priority = EnumHelper.ParsePriority(request.Priority);
            if (oldPriority != t.Priority)
                _audit.TrackLog(userId, AuditAction.Update, TransactionEntityName, id, id, oldPriority.ToString(), t.Priority.ToString());
        }
        if (request.CategoryId.HasValue)
        {
            var oldCategoryId = t.CategoryId;
            t.CategoryId = request.CategoryId;
            if (oldCategoryId != request.CategoryId)
                _audit.TrackLog(userId, AuditAction.Update, TransactionEntityName, id, id, oldCategoryId?.ToString(), request.CategoryId.ToString());
        }
        if (request.Notes != null) t.Notes = request.Notes;

        WorkflowHelper.RecalculateResponseDueDate(t);

        await using var dbTransaction = await _db.Database.BeginTransactionAsync();
        try
        {
            if (isClosing)
                await ValidateCanCloseAsync(t, userId);

            if (request.OutgoingDepartmentIds != null)
                await SyncOutgoingDepartmentsAsync(t, request.OutgoingDepartmentIds, userId);

            t.UpdatedById = userId;
            t.UpdatedAt = DateTime.UtcNow;

            _audit.TrackLog(userId, AuditAction.Update, TransactionEntityName, id, id, oldValues,
                JsonSerializer.Serialize(new { t.IncomingNumber, t.Subject, t.Status, t.CategoryId, t.ResponseDueDays }));

            await _db.SaveChangesAsync();
            await dbTransaction.CommitAsync();
        }
        catch
        {
            await dbTransaction.RollbackAsync();
            throw;
        }

        _cacheInvalidation.InvalidateOnTransactionChange();
        return await GetByIdAsync(id, new SystemUser(userId));
    }

    public async Task<bool> CancelAsync(int id, int userId, UserRole role)
    {
        if (role != UserRole.Admin && role != UserRole.Supervisor)
            throw new UnauthorizedAccessException("لا تملك صلاحية الإلغاء");

        var t = await _db.Transactions.FindAsync(id);
        if (t == null) return false;

        await CommitWorkflowMutationAsync(() =>
        {
            t.Status = TransactionStatus.Cancelled;
            t.UpdatedById = userId;
            t.UpdatedAt = DateTime.UtcNow;
            _audit.TrackLog(userId, AuditAction.Cancel, TransactionEntityName, id, id, null, "Cancelled");
            return Task.CompletedTask;
        });
        return true;
    }

    public async Task<bool> ArchiveAsync(int id, int userId, UserRole role)
    {
        if (role != UserRole.Admin && role != UserRole.Supervisor)
            throw new UnauthorizedAccessException("لا تملك صلاحية الأرشفة");

        var t = await _db.Transactions.FindAsync(id);
        if (t == null) return false;

        await CommitWorkflowMutationAsync(() =>
        {
            t.IsArchived = true;
            t.ArchivedAt = DateTime.UtcNow;
            t.Status = TransactionStatus.Archived;
            t.UpdatedById = userId;
            t.UpdatedAt = DateTime.UtcNow;
            _audit.TrackLog(userId, AuditAction.Archive, TransactionEntityName, id, id, null, "Archived");
            return Task.CompletedTask;
        });
        return true;
    }

    public async Task<bool> CloseAsync(int id, int userId, UserRole role)
    {
        if (role != UserRole.Admin && role != UserRole.Supervisor)
            throw new UnauthorizedAccessException("لا تملك صلاحية إغلاق المعاملة");

        var t = await _db.Transactions.Include(x => x.Assignments).ThenInclude(a => a.Department).FirstOrDefaultAsync(x => x.Id == id);
        if (t == null) return false;

        try
        {
            await CommitWorkflowMutationAsync(async () =>
            {
                await ValidateCanCloseAsync(t, userId);
                t.Status = TransactionStatus.Closed;
                t.ClosedAt = DateTime.UtcNow;
                t.UpdatedById = userId;
                t.UpdatedAt = DateTime.UtcNow;
                _audit.TrackLog(userId, AuditAction.Close, TransactionEntityName, id, id, null,
                    JsonSerializer.Serialize(new { ClosedAt = t.ClosedAt }));
            });
        }
        catch (InvalidOperationException ex)
        {
            await _audit.LogAsync(userId, AuditAction.CloseAttemptFailed, TransactionEntityName, id, id, null, ex.Message);
            throw;
        }
        return true;
    }

    public async Task<TransactionDetailDto?> CompleteResponseAsync(int id, CompleteResponseRequest request, ICurrentUserService currentUser)
    {
        if (currentUser.Role != UserRole.Admin && currentUser.Role != UserRole.Supervisor)
            throw new UnauthorizedAccessException("لا تملك صلاحية تسجيل الإفادة");

        var userId = currentUser.UserId;
        var t = await _db.Transactions
            .Include(x => x.Assignments).ThenInclude(a => a.Department)
            .FirstOrDefaultAsync(x => x.Id == id);
        if (t == null) return null;

        if (t.Status == TransactionStatus.Closed || t.Status == TransactionStatus.Cancelled || t.Status == TransactionStatus.Archived)
            throw new InvalidOperationException("لا يمكن تسجيل الإفادة لمعاملة مغلقة أو ملغاة");

        if (!t.RequiresResponse && t.ResponseType == ResponseType.None)
            throw new InvalidOperationException("هذه المعاملة لا تتطلب إفادة");

        if (t.ResponseCompleted)
            throw new InvalidOperationException("تم تسجيل الإفادة مسبقًا.");

        var pending = GetPendingReplyAssignments(t);
        if (pending.Count > 0)
        {
            var names = string.Join("، ", pending.Select(a => a.Department.Name));
            throw new InvalidOperationException($"لا يمكن تسجيل الإفادة قبل اكتمال رد جميع الإدارات: {names}");
        }

        var requiresOutgoing = t.ResponseType is ResponseType.External or ResponseType.Both;
        ValidateResponseFields(t, request, requiresOutgoing);

        await CommitWorkflowMutationAsync(() =>
        {
            t.ResponseCompleted = true;
            t.ResponseCompletedDate = request.ResponseDate.Date;
            t.ResponseSummary = request.ResponseSummary.Trim();
            ApplyOutgoingFields(t, request, requiresOutgoing);
            t.Status = TransactionStatus.ResponseCompleted;
            t.UpdatedById = userId;
            t.UpdatedAt = DateTime.UtcNow;
            _audit.TrackLog(userId, AuditAction.CompleteResponse, TransactionEntityName, id, id, null,
                JsonSerializer.Serialize(new
                {
                    responseDate = t.ResponseCompletedDate,
                    responseSummary = t.ResponseSummary,
                    outgoingNumber = t.OutgoingNumber,
                    outgoingDate = t.OutgoingDate
                }));
            return Task.CompletedTask;
        });

        return await GetByIdAsync(id, currentUser);
    }

    public async Task<TransactionDetailDto?> EditResponseAsync(int id, CompleteResponseRequest request, ICurrentUserService currentUser)
    {
        if (currentUser.Role != UserRole.Admin && currentUser.Role != UserRole.Supervisor)
            throw new UnauthorizedAccessException("لا تملك صلاحية تعديل الإفادة");

        var userId = currentUser.UserId;
        var t = await _db.Transactions.FirstOrDefaultAsync(x => x.Id == id);
        if (t == null) return null;

        if (t.Status == TransactionStatus.Closed || t.Status == TransactionStatus.Cancelled || t.Status == TransactionStatus.Archived)
            throw new InvalidOperationException("لا يمكن تعديل إفادة معاملة مغلقة أو ملغاة أو مؤرشفة");

        if (!t.ResponseCompleted)
            throw new InvalidOperationException("لم يتم تسجيل الإفادة بعد، لا يمكن تعديلها.");

        var requiresOutgoing = t.ResponseType is ResponseType.External or ResponseType.Both;
        ValidateResponseFields(t, request, requiresOutgoing);

        var oldValue = JsonSerializer.Serialize(new
        {
            responseDate = t.ResponseCompletedDate,
            responseSummary = t.ResponseSummary,
            outgoingNumber = t.OutgoingNumber,
            outgoingDate = t.OutgoingDate
        });

        await CommitWorkflowMutationAsync(() =>
        {
            t.ResponseCompletedDate = request.ResponseDate.Date;
            t.ResponseSummary = request.ResponseSummary.Trim();
            ApplyOutgoingFields(t, request, requiresOutgoing);
            t.UpdatedById = userId;
            t.UpdatedAt = DateTime.UtcNow;
            _audit.TrackLog(userId, AuditAction.EditResponse, TransactionEntityName, id, id, oldValue,
                JsonSerializer.Serialize(new
                {
                    responseDate = t.ResponseCompletedDate,
                    responseSummary = t.ResponseSummary,
                    outgoingNumber = t.OutgoingNumber,
                    outgoingDate = t.OutgoingDate
                }));
            return Task.CompletedTask;
        });

        return await GetByIdAsync(id, currentUser);
    }

    private static void ValidateResponseFields(Transaction t, CompleteResponseRequest request, bool requiresOutgoing)
    {
        if (request.ResponseDate == default)
            throw new InvalidOperationException("تاريخ الإفادة مطلوب");
        if (IsFutureEventDate(request.ResponseDate))
            throw new InvalidOperationException(FutureEventDateMessage);
        if (request.ResponseDate.Date < t.IncomingDate.Date)
            throw new InvalidOperationException("تاريخ الإفادة لا يمكن أن يسبق تاريخ الوارد.");

        if (string.IsNullOrWhiteSpace(request.ResponseSummary))
            throw new InvalidOperationException("ملخص الإفادة مطلوب");

        if (!requiresOutgoing) return;

        if (string.IsNullOrWhiteSpace(request.OutgoingNumber))
            throw new InvalidOperationException("رقم الصادر مطلوب لنوع الإفادة المحدد");
        if (!request.OutgoingDate.HasValue)
            throw new InvalidOperationException("تاريخ الصادر مطلوب لنوع الإفادة المحدد");
        if (IsFutureEventDate(request.OutgoingDate.Value))
            throw new InvalidOperationException(FutureEventDateMessage);
        if (request.OutgoingDate.Value.Date < t.IncomingDate.Date)
            throw new InvalidOperationException("تاريخ الصادر لا يمكن أن يسبق تاريخ الوارد.");
    }

    private static void ApplyOutgoingFields(Transaction t, CompleteResponseRequest request, bool requiresOutgoing)
    {
        t.OutgoingNumber = requiresOutgoing ? request.OutgoingNumber!.Trim() : null;
        t.OutgoingDate = requiresOutgoing ? request.OutgoingDate!.Value.Date : null;
    }

    public async Task<List<FollowUpDepartmentOptionDto>?> GetFollowUpDepartmentsAsync(int transactionId, ICurrentUserService currentUser)
    {
        var t = await _db.Transactions
            .Include(x => x.OutgoingDepartments).ThenInclude(o => o.Department)
            .Include(x => x.Assignments).ThenInclude(a => a.Department)
            .FirstOrDefaultAsync(x => x.Id == transactionId);

        if (t == null || !CanAccess(t, currentUser)) return null;

        return BuildFollowUpDepartmentOptions(t);
    }

    public async Task<FollowUpDto> AddFollowUpAsync(int transactionId, CreateFollowUpRequest request, int userId)
    {
        var t = await _db.Transactions
            .Include(x => x.OutgoingDepartments)
            .Include(x => x.Assignments)
            .FirstOrDefaultAsync(x => x.Id == transactionId)
            ?? throw new InvalidOperationException("المعاملة غير موجودة");

        var departmentIds = request.DepartmentIds?.Distinct().ToList() ?? new List<int>();
        if (departmentIds.Count == 0)
            throw new InvalidOperationException("يجب اختيار إدارة واحدة على الأقل لإرسال التعقيب.");
        if (IsFutureEventDate(request.FollowUpDate))
            ThrowFutureEventDateValidation(nameof(CreateFollowUpRequest.FollowUpDate));
        if (request.FollowUpDate.Date < t.IncomingDate.Date)
            throw new InvalidOperationException("تاريخ التعقيب لا يمكن أن يسبق تاريخ الوارد.");

        var allowedIds = GetAllowedFollowUpDepartmentIds(t);
        var invalid = departmentIds.Where(id => !allowedIds.Contains(id)).ToList();
        if (invalid.Count > 0)
        {
            var invalidNames = await _db.Departments
                .Where(d => invalid.Contains(d.Id))
                .Select(d => d.Name)
                .ToListAsync();
            var msg = $"لا يمكن إرسال التعقيب لإدارات غير مرتبطة بالمعاملة: {string.Join("، ", invalidNames)}";
            throw new InvalidOperationException(msg);
        }

        var deptNames = await _db.Departments
            .Where(d => departmentIds.Contains(d.Id))
            .Select(d => d.Name)
            .ToListAsync();

        var followUp = new FollowUp
        {
            TransactionId = transactionId,
            FollowUpNumber = request.FollowUpNumber,
            FollowUpDate = request.FollowUpDate,
            SentTo = string.Join("، ", deptNames),
            Notes = request.Notes,
            RequiresReply = true,
            ReplyStatus = ReplyStatus.Pending,
            CreatedById = userId,
            CreatedAt = DateTime.UtcNow
        };

        foreach (var deptId in departmentIds)
        {
            followUp.Departments.Add(new FollowUpDepartment
            {
                DepartmentId = deptId,
                CreatedById = userId,
                CreatedAt = DateTime.UtcNow
            });
        }

        await CommitWorkflowMutationAsync(
            () =>
            {
                _db.FollowUps.Add(followUp);
                if (t.Status == TransactionStatus.New)
                    t.Status = TransactionStatus.InProgress;
                return Task.CompletedTask;
            },
            () =>
            {
                _audit.TrackLog(userId, AuditAction.AddFollowUp, "FollowUp", followUp.Id, transactionId, null,
                    JsonSerializer.Serialize(new { departmentIds, deptNames, request.Notes }));
                return Task.CompletedTask;
            });

        return await MapFollowUpDtoAsync(followUp.Id);
    }

    private static HashSet<int> GetAllowedFollowUpDepartmentIds(Transaction t)
    {
        var ids = t.OutgoingDepartments.Select(o => o.DepartmentId)
            .Concat(t.Assignments.Select(a => a.DepartmentId))
            .ToHashSet();
        return ids;
    }

    private static List<FollowUpDepartmentOptionDto> BuildFollowUpDepartmentOptions(Transaction t)
    {
        var outgoingIds = t.OutgoingDepartments.Select(o => o.DepartmentId).ToHashSet();
        var hasOutgoing = outgoingIds.Count > 0;
        var options = new Dictionary<int, FollowUpDepartmentOptionDto>();

        foreach (var od in t.OutgoingDepartments)
        {
            options[od.DepartmentId] = new FollowUpDepartmentOptionDto
            {
                DepartmentId = od.DepartmentId,
                DepartmentName = od.Department.Name,
                IsDefaultSelected = true,
                Source = OutgoingDepartmentSourceName
            };
        }

        foreach (var a in t.Assignments)
        {
            if (options.ContainsKey(a.DepartmentId))
                continue;

            options[a.DepartmentId] = new FollowUpDepartmentOptionDto
            {
                DepartmentId = a.DepartmentId,
                DepartmentName = a.Department.Name,
                IsDefaultSelected = !hasOutgoing,
                Source = AssignmentSourceName
            };
        }

        return options.Values.OrderBy(x => x.DepartmentName).ToList();
    }

    public async Task<FollowUpDto?> ReplyFollowUpAsync(int transactionId, int followUpId, ReplyFollowUpRequest request, int userId)
    {
        var followUp = await _db.FollowUps
            .Include(f => f.CreatedBy)
            .Include(f => f.Transaction)
            .FirstOrDefaultAsync(f => f.Id == followUpId && f.TransactionId == transactionId);
        if (followUp == null) return null;
        if (IsFutureEventDate(request.ReplyDate))
            ThrowFutureEventDateValidation(nameof(ReplyFollowUpRequest.ReplyDate));
        if (request.ReplyDate.Date < followUp.Transaction.IncomingDate.Date)
            throw new InvalidOperationException("تاريخ رد التعقيب لا يمكن أن يسبق تاريخ الوارد.");
        if (request.ReplyDate.Date < followUp.FollowUpDate.Date)
            throw new InvalidOperationException("تاريخ رد التعقيب لا يمكن أن يسبق تاريخ التعقيب.");

        await CommitWorkflowMutationAsync(
            () =>
            {
                followUp.ReplyStatus = ReplyStatus.Replied;
                followUp.ReplyDate = request.ReplyDate;
                followUp.ReplySummary = request.ReplySummary;
                return Task.CompletedTask;
            },
            () =>
            {
                _audit.TrackLog(userId, AuditAction.RecordReply, "FollowUp", followUpId, transactionId, null, request.ReplySummary);
                return Task.CompletedTask;
            });

        return await MapFollowUpDtoAsync(followUpId);
    }

    public async Task<FollowUpDto?> EditFollowUpReplyAsync(int transactionId, int followUpId, ReplyFollowUpRequest request, ICurrentUserService currentUser)
    {
        if (currentUser.Role != UserRole.Admin && currentUser.Role != UserRole.Supervisor)
            throw new UnauthorizedAccessException("لا تملك صلاحية تعديل رد التعقيب");

        var followUp = await _db.FollowUps
            .Include(f => f.Transaction)
            .FirstOrDefaultAsync(f => f.Id == followUpId && f.TransactionId == transactionId);
        if (followUp == null) return null;

        if (followUp.Transaction.Status is TransactionStatus.Closed or TransactionStatus.Cancelled or TransactionStatus.Archived)
            throw new InvalidOperationException("لا يمكن تعديل رد تعقيب لمعاملة مغلقة أو ملغاة أو مؤرشفة.");

        if (followUp.ReplyStatus != ReplyStatus.Replied)
            throw new InvalidOperationException("لم يتم تسجيل رد لهذا التعقيب بعد، لا يمكن تعديله.");

        if (request.ReplyDate == default)
            throw new FieldValidationException(new Dictionary<string, string>
            {
                [nameof(ReplyFollowUpRequest.ReplyDate)] = "تاريخ رد التعقيب مطلوب."
            });
        if (IsFutureEventDate(request.ReplyDate))
            ThrowFutureEventDateValidation(nameof(ReplyFollowUpRequest.ReplyDate));
        if (request.ReplyDate.Date < followUp.Transaction.IncomingDate.Date)
            throw new InvalidOperationException("تاريخ رد التعقيب لا يمكن أن يسبق تاريخ الوارد.");
        if (request.ReplyDate.Date < followUp.FollowUpDate.Date)
            throw new InvalidOperationException("تاريخ رد التعقيب لا يمكن أن يسبق تاريخ التعقيب.");
        if (string.IsNullOrWhiteSpace(request.ReplySummary))
            throw new FieldValidationException(new Dictionary<string, string>
            {
                [nameof(ReplyFollowUpRequest.ReplySummary)] = "ملخص الرد مطلوب."
            });

        var oldValue = JsonSerializer.Serialize(new { followUp.ReplyDate, followUp.ReplySummary });

        await CommitWorkflowMutationAsync(
            () =>
            {
                followUp.ReplyDate = request.ReplyDate;
                followUp.ReplySummary = request.ReplySummary;
                return Task.CompletedTask;
            },
            () =>
            {
                _audit.TrackLog(currentUser.UserId, AuditAction.EditFollowUpReply, "FollowUp", followUpId, transactionId, oldValue,
                    JsonSerializer.Serialize(new { ReplyDate = request.ReplyDate, request.ReplySummary }));
                return Task.CompletedTask;
            });

        return await MapFollowUpDtoAsync(followUpId);
    }

    public async Task<AssignmentDto> AddAssignmentAsync(int transactionId, CreateAssignmentRequest request, int userId)
    {
        if (request is null)
            throw new InvalidOperationException("بيانات طلب الإحالة مطلوبة.");

        var t = await _db.Transactions.Include(x => x.Assignments).FirstOrDefaultAsync(x => x.Id == transactionId)
            ?? throw new InvalidOperationException("المعاملة غير موجودة");

        var dept = await _db.Departments.FindAsync(request.DepartmentId)
            ?? throw new InvalidOperationException("الإدارة غير موجودة");

        if (request.AssignedDate == default)
            throw new FieldValidationException(new Dictionary<string, string>
            {
                [nameof(CreateAssignmentRequest.AssignedDate)] = "تاريخ الإحالة مطلوب."
            });

        var assignedDate = NormalizeDateOnlyUtc(request.AssignedDate);
        if (IsFutureEventDate(assignedDate))
            ThrowFutureEventDateValidation(nameof(CreateAssignmentRequest.AssignedDate));
        var requestedDueDate = request.DueDate.HasValue
            ? NormalizeDateOnlyUtc(request.DueDate.Value)
            : (DateTime?)null;
        var replyDueDays = request.ReplyDueDays ?? WorkflowHelper.CalculateAssignmentDueDays(assignedDate, requestedDueDate);
        var dueDate = WorkflowHelper.CalculateAssignmentDueDate(assignedDate, replyDueDays, requestedDueDate);

        if (assignedDate.Date < t.IncomingDate.Date)
            throw new InvalidOperationException("تاريخ الإحالة لا يمكن أن يسبق تاريخ الوارد.");

        if (replyDueDays.HasValue && replyDueDays.Value < 0)
            throw new InvalidOperationException("عدد أيام الرد لا يمكن أن يكون سالبًا.");

        if (dueDate.HasValue && dueDate.Value.Date < assignedDate.Date)
            throw new InvalidOperationException("تاريخ استحقاق الإدارة لا يمكن أن يسبق تاريخ الإحالة.");

        var assignment = new Assignment
        {
            TransactionId = transactionId,
            DepartmentId = request.DepartmentId,
            AssignedDate = assignedDate,
            LetterNumber = string.IsNullOrWhiteSpace(request.LetterNumber) ? null : request.LetterNumber.Trim(),
            RequiredAction = request.RequiredAction,
            RequiresReply = true,
            ReplyDueDays = replyDueDays,
            DueDate = dueDate,
            ReplyStatus = ReplyStatus.Pending,
            Status = AssignmentStatus.Active,
            CreatedById = userId,
            CreatedAt = DateTime.UtcNow
        };

        await CommitWorkflowMutationAsync(
            () =>
            {
                t.Assignments.Add(assignment);
                _db.Assignments.Add(assignment);
                t.Status = TransactionStatus.Assigned;
                ApplyTransactionReplyStatus(t);
                return Task.CompletedTask;
            },
            () =>
            {
                _audit.TrackLog(userId, AuditAction.AddAssignment, AssignmentEntityName, assignment.Id, transactionId, null,
                    JsonSerializer.Serialize(new { dept.Name, dueDate, request.ReplyDueDays }));
                return Task.CompletedTask;
            });

        return MapAssignment(assignment, dept.Name, "");
    }

    public async Task<AssignmentDto?> ReplyAssignmentAsync(int transactionId, int assignmentId, ReplyAssignmentRequest request, ICurrentUserService currentUser)
    {
        if (request is null)
            throw new FieldValidationException(new Dictionary<string, string>
            {
                [nameof(ReplyAssignmentRequest)] = "بيانات طلب الرد مطلوبة."
            });

        if (request.ReplyDate == default)
            throw new FieldValidationException(new Dictionary<string, string>
            {
                [nameof(ReplyAssignmentRequest.ReplyDate)] = "تاريخ إنجاز الإدارة مطلوب."
            });
        if (IsFutureEventDate(request.ReplyDate))
            ThrowFutureEventDateValidation(nameof(ReplyAssignmentRequest.ReplyDate));

        var assignment = await _db.Assignments
            .Include(a => a.Department)
            .Include(a => a.CreatedBy)
            .Include(a => a.Transaction).ThenInclude(t => t.Assignments)
            .FirstOrDefaultAsync(a => a.Id == assignmentId && a.TransactionId == transactionId);
        if (assignment == null) return null;

        if (currentUser.Role == UserRole.DepartmentUser)
            throw new UnauthorizedAccessException("لا تملك صلاحية تسجيل رد على الاحالة. استخدم مسار إفادات الإدارة.");
        if (request.ReplyDate.Date < assignment.Transaction.IncomingDate.Date)
            throw new InvalidOperationException("تاريخ إنجاز الإدارة لا يمكن أن يسبق تاريخ الوارد.");
        if (request.ReplyDate.Date < assignment.AssignedDate.Date)
            throw new InvalidOperationException("تاريخ إنجاز الإدارة لا يمكن أن يسبق تاريخ الإحالة.");

        // Recording a reply directly here (outside the department-user submission/approval
        // workflow) must still leave behind an Approved DepartmentResponse row. Without one,
        // the assignment shows as "Replied" with no editable DepartmentResponseId, so admins
        // can never correct it later via the admin-edit-response endpoint.
        var existingResponseId = await _db.DepartmentResponses
            .Where(dr => dr.TransactionId == transactionId
                && dr.DepartmentId == assignment.DepartmentId
                && dr.Status == DepartmentResponseStatus.Approved)
            .Select(dr => (int?)dr.Id)
            .FirstOrDefaultAsync();

        DepartmentResponse? departmentResponse = null;

        await CommitWorkflowMutationAsync(
            () =>
            {
                assignment.ReplyStatus = ReplyStatus.Replied;
                assignment.ReplyDate = request.ReplyDate.Date;
                assignment.ReplySummary = request.ReplySummary;
                assignment.Status = AssignmentStatus.Completed;
                ApplyTransactionReplyStatus(assignment.Transaction);

                if (existingResponseId == null)
                {
                    // SubmittedAt/ReviewedAt/CreatedAt are technical system timestamps for this
                    // synthesized Approved record, not the operational completion date — that is
                    // ResponseDate, sourced from the admin-entered ReplyDate.
                    departmentResponse = new DepartmentResponse
                    {
                        TransactionId = transactionId,
                        DepartmentId = assignment.DepartmentId,
                        ResponseText = request.ReplySummary,
                        Status = DepartmentResponseStatus.Approved,
                        SubmittedByUserId = currentUser.UserId,
                        SubmittedAt = DateTime.UtcNow,
                        ReviewedByUserId = currentUser.UserId,
                        ReviewedAt = DateTime.UtcNow,
                        CreatedAt = DateTime.UtcNow,
                        ResponseDate = request.ReplyDate.Date,
                    };
                    _db.DepartmentResponses.Add(departmentResponse);
                }

                return Task.CompletedTask;
            },
            () =>
            {
                _audit.TrackLog(currentUser.UserId, AuditAction.RecordReply, AssignmentEntityName, assignmentId, transactionId, null, request.ReplySummary);
                return Task.CompletedTask;
            });

        return MapAssignment(
            assignment,
            assignment.Department.Name,
            assignment.CreatedBy.FullName,
            departmentResponseId: departmentResponse?.Id ?? existingResponseId,
            responseDate: assignment.ReplyDate,
            canAdminEdit: currentUser.Role == UserRole.Admin);
    }

    public async Task<AssignmentDto?> EditAssignmentReplyAsync(int transactionId, int assignmentId, ReplyAssignmentRequest request, ICurrentUserService currentUser)
    {
        // Edits the assignment's own reply fields directly by assignmentId. A reply can be
        // recorded either through ReplyAssignmentAsync or via an approved DepartmentResponse;
        // in both cases the assignment's ReplyDate/ReplySummary are the source of truth, so
        // this must not require (or create) a DepartmentResponse to work.
        if (currentUser.Role != UserRole.Admin && currentUser.Role != UserRole.Supervisor)
            throw new UnauthorizedAccessException("لا تملك صلاحية تعديل إفادة الإحالة");

        var assignment = await _db.Assignments
            .Include(a => a.Department)
            .Include(a => a.CreatedBy)
            .Include(a => a.Transaction)
            .FirstOrDefaultAsync(a => a.Id == assignmentId && a.TransactionId == transactionId);
        if (assignment == null) return null;

        if (assignment.Transaction.Status is TransactionStatus.Closed or TransactionStatus.Cancelled or TransactionStatus.Archived)
            throw new InvalidOperationException("لا يمكن تعديل إفادة الإحالة لمعاملة مغلقة أو ملغاة أو مؤرشفة.");

        if (assignment.ReplyStatus != ReplyStatus.Replied)
            throw new InvalidOperationException("لم يتم تسجيل إفادة لهذه الإحالة بعد، لا يمكن تعديلها.");

        if (request.ReplyDate == default)
            throw new FieldValidationException(new Dictionary<string, string>
            {
                [nameof(ReplyAssignmentRequest.ReplyDate)] = "تاريخ إنجاز الإدارة مطلوب."
            });
        if (IsFutureEventDate(request.ReplyDate))
            ThrowFutureEventDateValidation(nameof(ReplyAssignmentRequest.ReplyDate));
        if (request.ReplyDate.Date < assignment.Transaction.IncomingDate.Date)
            throw new InvalidOperationException("تاريخ إنجاز الإدارة لا يمكن أن يسبق تاريخ الوارد.");
        if (request.ReplyDate.Date < assignment.AssignedDate.Date)
            throw new InvalidOperationException("تاريخ إنجاز الإدارة لا يمكن أن يسبق تاريخ الإحالة.");
        if (string.IsNullOrWhiteSpace(request.ReplySummary))
            throw new FieldValidationException(new Dictionary<string, string>
            {
                [nameof(ReplyAssignmentRequest.ReplySummary)] = "ملخص الإفادة مطلوب."
            });

        var oldValue = JsonSerializer.Serialize(new { assignment.ReplyDate, assignment.ReplySummary });

        await CommitWorkflowMutationAsync(
            () =>
            {
                assignment.ReplyDate = request.ReplyDate;
                assignment.ReplySummary = request.ReplySummary;
                return Task.CompletedTask;
            },
            () =>
            {
                _audit.TrackLog(currentUser.UserId, AuditAction.EditAssignmentReply, AssignmentEntityName, assignmentId, transactionId, oldValue,
                    JsonSerializer.Serialize(new { ReplyDate = request.ReplyDate, request.ReplySummary }));
                return Task.CompletedTask;
            });

        // Optional sync: if this assignment happens to have an approved DepartmentResponse,
        // surface its id in the returned DTO — but it is never required for the edit itself.
        var existingResponseId = await _db.DepartmentResponses
            .Where(dr => dr.TransactionId == transactionId
                && dr.DepartmentId == assignment.DepartmentId
                && dr.Status == DepartmentResponseStatus.Approved)
            .Select(dr => (int?)dr.Id)
            .FirstOrDefaultAsync();

        return MapAssignment(
            assignment,
            assignment.Department.Name,
            assignment.CreatedBy.FullName,
            departmentResponseId: existingResponseId,
            responseDate: assignment.ReplyDate,
            canAdminEdit: currentUser.Role == UserRole.Admin);
    }

    public async Task<AssignmentDto?> AdminEditAssignmentAsync(
        int transactionId, int assignmentId, AdminEditAssignmentRequest request, int userId)
    {
        if (request is null)
            throw new InvalidOperationException("بيانات طلب التعديل مطلوبة.");

        var assignment = await _db.Assignments
            .Include(a => a.Department)
            .Include(a => a.CreatedBy)
            .Include(a => a.Transaction)
            .FirstOrDefaultAsync(a => a.Id == assignmentId && a.TransactionId == transactionId);
        if (assignment == null) return null;

        var oldSnapshot = JsonSerializer.Serialize(new
        {
            assignment.LetterNumber,
            assignment.AssignedDate,
            assignment.RequiredAction,
            assignment.ReplyDueDays,
            assignment.DueDate
        });

        var resolvedDates = ResolveAdminEditAssignmentDates(assignment, request);
        ValidateAdminEditAssignmentDates(assignment, resolvedDates.AssignedDate, resolvedDates.ReplyDueDays, resolvedDates.DueDate);
        ApplyAdminEditAssignmentChanges(assignment, request, resolvedDates.AssignedDate, resolvedDates.ReplyDueDays, resolvedDates.DueDate);

        var newSnapshot = JsonSerializer.Serialize(new
        {
            assignment.LetterNumber,
            assignment.AssignedDate,
            assignment.RequiredAction,
            assignment.ReplyDueDays,
            assignment.DueDate
        });

        await CommitWorkflowMutationAsync(
            () => Task.CompletedTask,
            () =>
            {
                _audit.TrackLog(userId, AuditAction.AdminEditAssignment, AssignmentEntityName, assignmentId, transactionId, oldSnapshot, newSnapshot);
                return Task.CompletedTask;
            });

        return MapAssignment(assignment, assignment.Department.Name, assignment.CreatedBy?.FullName ?? "", canAdminEdit: true);
    }

    public async Task<TransactionDetailDto?> AdminEditTransactionDatesAsync(
        int transactionId, AdminEditTransactionDatesRequest request, int userId)
    {
        if (request is null)
            throw new InvalidOperationException("بيانات طلب التعديل مطلوبة.");

        var t = await _db.Transactions
            .Include(x => x.CreatedBy)
            .Include(x => x.CategoryEntity)
            .Include(x => x.IncomingFromParty)
            .Include(x => x.IncomingFromDepartment)
            .Include(x => x.OutgoingDepartments).ThenInclude(o => o.Department)
            .FirstOrDefaultAsync(x => x.Id == transactionId);
        if (t == null) return null;

        var oldSnapshot = JsonSerializer.Serialize(new
        {
            t.IncomingDate,
            t.ResponseDueDays,
            t.ResponseDueDate,
            ClosedAt = t.ClosedAt,
            Reason = request.Reason
        });

        var resolvedDates = ResolveAdminEditTransactionDates(t, request);
        ValidateAdminEditTransactionDateOrder(
            resolvedDates.IncomingDate,
            resolvedDates.ResponseDueDays,
            resolvedDates.ResponseDueDate,
            resolvedDates.ClosedAt,
            t.RequiresResponse,
            t.ResponseCompletedDate);
        await EnsureIncomingDateDoesNotFollowExistingTimelineAsync(
            transactionId,
            request.IsIncomingDateSpecified && request.IncomingDate.HasValue,
            resolvedDates.IncomingDate,
            resolvedDates.ClosedAt);
        ApplyAdminEditTransactionDates(
            t,
            request,
            resolvedDates.IncomingDate,
            resolvedDates.ResponseDueDays,
            resolvedDates.ResponseDueDate,
            resolvedDates.ClosedAt);

        var newSnapshot = JsonSerializer.Serialize(new
        {
            t.IncomingDate,
            t.ResponseDueDays,
            t.ResponseDueDate,
            ClosedAt = t.ClosedAt,
            Reason = request.Reason
        });

        await CommitWorkflowMutationAsync(
            () => Task.CompletedTask,
            () =>
            {
                _audit.TrackLog(userId, AuditAction.AdminEditTransactionDates, TransactionEntityName, transactionId, transactionId, oldSnapshot, newSnapshot);
                return Task.CompletedTask;
            });

        var assignmentRows = await _db.Assignments.AsNoTracking()
            .Where(a => a.TransactionId == transactionId)
            .Select(a => new AssignmentSummaryRow(a.Department.Name, a.ReplyStatus, a.RequiresReply, a.Status, a.DueDate))
            .ToListAsync();

        return MapToBasicDetailDto(t, assignmentRows, DateTime.UtcNow);
    }

    private static (DateTime AssignedDate, int? ReplyDueDays, DateTime? DueDate) ResolveAdminEditAssignmentDates(
        Assignment assignment,
        AdminEditAssignmentRequest request)
    {
        var assignedDate = request.AssignedDate.HasValue
            ? NormalizeDateOnlyUtc(request.AssignedDate.Value)
            : assignment.AssignedDate;
        var replyDueDays = request.ReplyDueDays ?? assignment.ReplyDueDays;
        DateTime? dueDate;

        if (request.DueDate.HasValue)
        {
            dueDate = NormalizeDateOnlyUtc(request.DueDate.Value);
            replyDueDays = WorkflowHelper.CalculateAssignmentDueDays(assignedDate, dueDate);
        }
        else
        {
            dueDate = WorkflowHelper.CalculateAssignmentDueDate(assignedDate, replyDueDays, assignment.DueDate);
        }

        return (assignedDate, replyDueDays, dueDate);
    }

    private static void ValidateAdminEditAssignmentDates(
        Assignment assignment,
        DateTime assignedDate,
        int? replyDueDays,
        DateTime? dueDate)
    {
        if (IsFutureEventDate(assignedDate))
            throw new InvalidOperationException(FutureEventDateMessage);
        if (assignedDate.Date < assignment.Transaction.IncomingDate.Date)
            throw new InvalidOperationException("تاريخ الإحالة لا يمكن أن يسبق تاريخ الوارد.");
        if (dueDate.HasValue && dueDate.Value.Date < assignedDate.Date)
            throw new InvalidOperationException("تاريخ استحقاق الإدارة لا يمكن أن يسبق تاريخ الإحالة.");
        if (replyDueDays.HasValue && replyDueDays.Value < 0)
            throw new InvalidOperationException("عدد أيام الرد لا يمكن أن يكون سالبًا.");
    }

    private static void ApplyAdminEditAssignmentChanges(
        Assignment assignment,
        AdminEditAssignmentRequest request,
        DateTime assignedDate,
        int? replyDueDays,
        DateTime? dueDate)
    {
        if (request.AssignedDate.HasValue)
            assignment.AssignedDate = assignedDate;
        if (request.DueDate.HasValue || request.ReplyDueDays.HasValue || request.AssignedDate.HasValue)
            assignment.DueDate = dueDate;
        if (request.IsLetterNumberSpecified)
            assignment.LetterNumber = string.IsNullOrWhiteSpace(request.LetterNumber) ? null : request.LetterNumber.Trim();
        if (request.RequiredAction != null)
            assignment.RequiredAction = string.IsNullOrWhiteSpace(request.RequiredAction) ? null : request.RequiredAction.Trim();
        if (request.ReplyDueDays.HasValue || request.DueDate.HasValue || request.AssignedDate.HasValue)
            assignment.ReplyDueDays = replyDueDays;
    }

    private static (DateTime IncomingDate, int? ResponseDueDays, DateTime? ResponseDueDate, DateTime? ClosedAt) ResolveAdminEditTransactionDates(
        Transaction transaction,
        AdminEditTransactionDatesRequest request)
    {
        var incomingDate = request.IncomingDate.HasValue
            ? NormalizeDateOnlyUtc(request.IncomingDate.Value)
            : transaction.IncomingDate;
        var responseDueDays = request.IsResponseDueDaysSpecified
            ? request.ResponseDueDays
            : transaction.ResponseDueDays;
        DateTime? responseDueDate;

        if (request.IsResponseDueDateSpecified)
        {
            responseDueDate = request.ResponseDueDate.HasValue
                ? NormalizeDateOnlyUtc(request.ResponseDueDate.Value)
                : null;
            responseDueDays = WorkflowHelper.CalculateResponseDueDays(incomingDate, responseDueDate);
        }
        else
        {
            responseDueDate = transaction.RequiresResponse
                ? WorkflowHelper.CalculateResponseDueDate(incomingDate, responseDueDays)
                : null;
        }

        // Must key off IsClosedAtSpecified (not ClosedAt.HasValue): an explicit
        // { closedAt: null } to clear the close date is indistinguishable from "field
        // omitted" if we only check HasValue, silently reverting the clear to the old value.
        DateTime? closedAt;
        if (request.IsClosedAtSpecified)
        {
            closedAt = request.ClosedAt.HasValue
                ? NormalizeDateOnlyUtc(request.ClosedAt.Value)
                : null;
        }
        else
        {
            closedAt = transaction.ClosedAt;
        }

        return (incomingDate, responseDueDays, responseDueDate, closedAt);
    }

    private static void ValidateAdminEditTransactionDateOrder(
        DateTime incomingDate,
        int? responseDueDays,
        DateTime? responseDueDate,
        DateTime? closedAt,
        bool requiresResponse,
        DateTime? responseCompletedDate)
    {
        if (IsFutureEventDate(incomingDate) || (closedAt.HasValue && IsFutureEventDate(closedAt.Value)))
            throw new InvalidOperationException(FutureEventDateMessage);
        if (responseDueDate.HasValue && responseDueDate.Value.Date < incomingDate.Date)
            throw new InvalidOperationException("تاريخ استحقاق المعاملة لا يمكن أن يسبق تاريخ الوارد.");
        if (closedAt.HasValue && closedAt.Value.Date < incomingDate.Date)
            throw new InvalidOperationException("تاريخ إغلاق المعاملة لا يمكن أن يسبق تاريخ الوارد.");
        if (requiresResponse && responseCompletedDate.HasValue && closedAt.HasValue
            && closedAt.Value.Date < responseCompletedDate.Value.Date)
            throw new InvalidOperationException("تاريخ إغلاق المعاملة لا يمكن أن يسبق تاريخ الإفادة.");
        if (responseDueDays.HasValue && responseDueDays.Value < 0)
            throw new InvalidOperationException("عدد أيام الرد لا يمكن أن يكون سالبًا.");
    }

    private async Task EnsureIncomingDateDoesNotFollowExistingTimelineAsync(
        int transactionId,
        bool incomingDateSpecified,
        DateTime incomingDate,
        DateTime? resolvedClosedAt = null)
    {
        if (!incomingDateSpecified)
            return;

        EnsureClosedAtDoesNotPrecedeIncoming(incomingDate, resolvedClosedAt);

        if (await HasEarlierTransactionEventAsync(transactionId, incomingDate, resolvedClosedAt))
            throw new InvalidOperationException("تاريخ الوارد لا يمكن أن يكون بعد تاريخ صادر أو إفادة أو إغلاق قائم.");

        if (await HasEarlierAssignmentEventAsync(transactionId, incomingDate))
            throw new InvalidOperationException("تاريخ الوارد لا يمكن أن يكون بعد تاريخ أي إحالة قائمة.");

        if (await HasEarlierFollowUpEventAsync(transactionId, incomingDate))
            throw new InvalidOperationException("تاريخ الوارد لا يمكن أن يكون بعد تاريخ تعقيب أو رد تعقيب قائم.");

        if (await HasEarlierDepartmentResponseEventAsync(transactionId, incomingDate))
            throw new InvalidOperationException("تاريخ الوارد لا يمكن أن يكون بعد تاريخ إنجاز إدارة قائم.");
    }

    private static void EnsureClosedAtDoesNotPrecedeIncoming(DateTime incomingDate, DateTime? resolvedClosedAt)
    {
        if (resolvedClosedAt.HasValue && resolvedClosedAt.Value.Date < incomingDate.Date)
            throw new InvalidOperationException("تاريخ الوارد لا يمكن أن يكون بعد تاريخ إغلاق المعاملة.");
    }

    private Task<bool> HasEarlierTransactionEventAsync(int transactionId, DateTime incomingDate, DateTime? resolvedClosedAt) =>
        _db.Transactions
            .AsNoTracking()
            .AnyAsync(t => t.Id == transactionId &&
                ((t.OutgoingDate.HasValue && t.OutgoingDate.Value.Date < incomingDate.Date) ||
                 (t.ResponseCompletedDate.HasValue && t.ResponseCompletedDate.Value.Date < incomingDate.Date) ||
                 (!resolvedClosedAt.HasValue && t.ClosedAt.HasValue && t.ClosedAt.Value.Date < incomingDate.Date)));

    private Task<bool> HasEarlierAssignmentEventAsync(int transactionId, DateTime incomingDate) =>
        _db.Assignments
            .AsNoTracking()
            .AnyAsync(a => a.TransactionId == transactionId && a.AssignedDate.Date < incomingDate.Date);

    private Task<bool> HasEarlierFollowUpEventAsync(int transactionId, DateTime incomingDate) =>
        _db.FollowUps
            .AsNoTracking()
            .AnyAsync(f => f.TransactionId == transactionId &&
                (f.FollowUpDate.Date < incomingDate.Date ||
                 (f.ReplyDate.HasValue && f.ReplyDate.Value.Date < incomingDate.Date)));

    private Task<bool> HasEarlierDepartmentResponseEventAsync(int transactionId, DateTime incomingDate) =>
        _db.DepartmentResponses
            .AsNoTracking()
            .AnyAsync(r => r.TransactionId == transactionId &&
                r.SubmittedAt.HasValue &&
                r.SubmittedAt.Value.Date < incomingDate.Date);

    private static void ApplyAdminEditTransactionDates(
        Transaction transaction,
        AdminEditTransactionDatesRequest request,
        DateTime incomingDate,
        int? responseDueDays,
        DateTime? responseDueDate,
        DateTime? closedAt)
    {
        if (request.IsIncomingDateSpecified && request.IncomingDate.HasValue)
            transaction.IncomingDate = incomingDate;
        if (request.IsResponseDueDaysSpecified || request.IsResponseDueDateSpecified || request.IsIncomingDateSpecified)
            transaction.ResponseDueDays = responseDueDays;
        if (request.IsResponseDueDateSpecified || request.IsResponseDueDaysSpecified || request.IsIncomingDateSpecified)
            transaction.ResponseDueDate = responseDueDate;
        if (request.IsClosedAtSpecified)
            transaction.ClosedAt = closedAt;
    }

    public async Task<PagedResult<AuditLogDto>> GetAuditLogAsync(
        int transactionId, int page, int pageSize, ICurrentUserService currentUser)
    {
        if (!await CanAccessTransactionAsync(transactionId, currentUser))
            return PagedResult<AuditLogDto>.Create(new(), 0, page, pageSize);

        page = Math.Max(1, page);
        pageSize = pageSize <= 0 ? 50 : Math.Min(pageSize, 100);

        var query = _db.AuditLogs.AsNoTracking().Where(a => a.TransactionId == transactionId);
        var total = await query.CountAsync();

        var items = await query
            .OrderByDescending(a => a.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(a => new AuditLogDto
            {
                Id = a.Id,
                Action = a.Action.ToString(),
                EntityName = a.EntityName,
                EntityId = a.EntityId,
                OldValue = a.OldValue,
                NewValue = a.NewValue,
                UserName = a.User.FullName,
                CreatedAt = a.CreatedAt
            })
            .ToListAsync();

        return PagedResult<AuditLogDto>.Create(items, total, page, pageSize);
    }

    private static List<Assignment> GetPendingReplyAssignments(Transaction t) =>
        t.Assignments
            .Where(a => a.RequiresReply
                && a.Status == AssignmentStatus.Active
                && a.ReplyStatus is ReplyStatus.Pending or ReplyStatus.Overdue)
            .ToList();

    private async Task ValidateCanCloseAsync(Transaction t, int userId)
    {
        if (t.RequiresResponse)
        {
            var departmentResponseFailures = await GetDepartmentResponseClosureFailuresAsync(t.Id);
            if (departmentResponseFailures.Count > 0)
                throw new InvalidOperationException(
                    $"لا يمكن إغلاق المعاملة: {string.Join("؛ ", departmentResponseFailures)}");
        }

        var pending = await _db.Assignments
            .Include(a => a.Department)
            .Where(a => a.TransactionId == t.Id
                && a.RequiresReply
                && a.Status == AssignmentStatus.Active
                && (a.ReplyStatus == ReplyStatus.Pending || a.ReplyStatus == ReplyStatus.Overdue))
            .ToListAsync();

        if (pending.Count > 0)
        {
            var names = string.Join("، ", pending.Select(a => a.Department.Name));
            throw new InvalidOperationException($"لا يمكن إغلاق المعاملة لوجود إدارات لم تؤكد الرد: {names}");
        }

        if (t.RequiresResponse && (!t.ResponseCompleted || !t.ResponseCompletedDate.HasValue))
            throw new InvalidOperationException("لا يمكن إغلاق المعاملة قبل تسجيل الإفادة.");
    }

    private async Task<List<string>> GetDepartmentResponseClosureFailuresAsync(int transactionId)
    {
        var requiredDepartments = await _db.Assignments
            .AsNoTracking()
            .Where(a => a.TransactionId == transactionId &&
                a.RequiresReply &&
                a.Status == AssignmentStatus.Active)
            .Select(a => new { a.DepartmentId, a.Department.Name })
            .Distinct()
            .ToListAsync();

        if (requiredDepartments.Count == 0)
            return [];

        var requiredDepartmentIds = requiredDepartments.Select(d => d.DepartmentId).ToHashSet();
        var responses = await _db.DepartmentResponses
            .AsNoTracking()
            .Include(r => r.SubmittedBy)
            .Include(r => r.ReviewedBy)
            .Where(r => r.TransactionId == transactionId &&
                requiredDepartmentIds.Contains(r.DepartmentId))
            .ToListAsync();
        var hasDifferentDepartmentResponse = await _db.DepartmentResponses
            .AsNoTracking()
            .AnyAsync(r => r.TransactionId == transactionId &&
                !requiredDepartmentIds.Contains(r.DepartmentId));

        var responseByDepartment = responses
            .GroupBy(r => r.DepartmentId)
            .ToDictionary(
                group => group.Key,
                group => group.OrderByDescending(r => r.UpdatedAt ?? r.CreatedAt).First());
        var privilegedAuditResponseIds = await GetPrivilegedDepartmentResponseAuditIdsAsync(
            transactionId,
            responseByDepartment.Values.Select(r => r.Id).ToList());

        return requiredDepartments
            .Select(d => GetDepartmentResponseClosureFailure(
                d.Name,
                responseByDepartment.GetValueOrDefault(d.DepartmentId),
                privilegedAuditResponseIds,
                hasDifferentDepartmentResponse))
            .Where(failure => !string.IsNullOrWhiteSpace(failure))
            .Select(failure => failure!)
            .OrderBy(name => name)
            .ToList();
    }

    private async Task<HashSet<int>> GetPrivilegedDepartmentResponseAuditIdsAsync(
        int transactionId,
        List<int> responseIds)
    {
        if (responseIds.Count == 0)
            return [];

        var auditResponseIds = await _db.AuditLogs
            .AsNoTracking()
            .Where(a => a.TransactionId == transactionId
                && a.EntityName == DepartmentResponseEntityName
                && a.EntityId.HasValue
                && responseIds.Contains(a.EntityId.Value)
                && DepartmentResponseSufficientAuditActions.Contains(a.Action)
                && DepartmentResponseReviewRoles.Contains(a.User.Role))
            .Select(a => a.EntityId!.Value)
            .Distinct()
            .ToListAsync();

        return auditResponseIds.ToHashSet();
    }

    private static string? GetDepartmentResponseClosureFailure(
        string departmentName,
        DepartmentResponse? response,
        HashSet<int> privilegedAuditResponseIds,
        bool hasDifferentDepartmentResponse)
    {
        if (response == null)
        {
            var reason = hasDifferentDepartmentResponse
                ? "الإفادة المسجلة لا تخص الإدارة المطلوبة."
                : "لا توجد إفادة مسجلة من الإدارة المطلوبة.";
            return $"{departmentName}: {reason}";
        }

        if (IsDepartmentResponseSufficientForClosure(
            response,
            privilegedAuditResponseIds.Contains(response.Id)))
            return null;

        return $"{departmentName}: {GetInsufficientDepartmentResponseReason(response.Status)}";
    }

    private static bool IsDepartmentResponseSufficientForClosure(
        DepartmentResponse response,
        bool hasPrivilegedAuditAction) =>
        response.Status == DepartmentResponseStatus.Approved ||
        hasPrivilegedAuditAction && IsPrivilegedUnapprovedResponseStatus(response.Status) ||
        IsReviewRole(response.SubmittedBy?.Role) ||
        IsApprovedByReviewRole(response);

    private static bool IsPrivilegedUnapprovedResponseStatus(DepartmentResponseStatus status) =>
        status is DepartmentResponseStatus.Draft or DepartmentResponseStatus.SubmittedForReview;

    private static bool IsApprovedByReviewRole(DepartmentResponse response) =>
        response.Status == DepartmentResponseStatus.Approved &&
        IsReviewRole(response.ReviewedBy?.Role);

    private static bool IsReviewRole(UserRole? role) =>
        role.HasValue && DepartmentResponseReviewRoles.Contains(role.Value);

    private static string GetInsufficientDepartmentResponseReason(DepartmentResponseStatus status) =>
        status switch
        {
            DepartmentResponseStatus.Draft => "توجد إفادة محفوظة كمسودة ولم تُرسل أو تُعتمد بعد.",
            DepartmentResponseStatus.SubmittedForReview => "توجد إفادة مرسلة للمراجعة لكنها لم تُعتمد بعد.",
            DepartmentResponseStatus.ReturnedForCorrection => "الإفادة معادة للتعديل ولم تُعتمد بعد.",
            DepartmentResponseStatus.Rejected => "الإفادة مرفوضة ولم تُعتمد بعد.",
            _ => "لا يمكن إغلاق المعاملة لوجود إفادة غير معتمدة."
        };

    private static void ApplyTransactionReplyStatus(Transaction t) =>
        WorkflowHelper.UpdateTransactionStatusFromAssignments(t);

    private async Task CommitWorkflowMutationAsync(Func<Task> mutateAsync, Func<Task>? afterFirstSaveAsync = null)
    {
        await using var dbTransaction = await _db.Database.BeginTransactionAsync();
        try
        {
            await mutateAsync();
            await _db.SaveChangesAsync();
            if (afterFirstSaveAsync != null)
            {
                await afterFirstSaveAsync();
                await _db.SaveChangesAsync();
            }

            await dbTransaction.CommitAsync();
        }
        catch
        {
            await dbTransaction.RollbackAsync();
            throw;
        }

        _cacheInvalidation.InvalidateOnTransactionChange();
    }

    private async Task SyncOutgoingDepartmentsAsync(
        Transaction transaction,
        List<int> departmentIds,
        int userId,
        ICollection<AuditLog>? deferredCreateAudits = null)
    {
        var existing = transaction.Id > 0
            ? await _db.TransactionOutgoingDepartments.Where(x => x.TransactionId == transaction.Id).ToListAsync()
            : transaction.OutgoingDepartments.ToList();
        var newIds = departmentIds.Distinct().ToHashSet();
        var removedIds = existing.Where(e => !newIds.Contains(e.DepartmentId)).Select(e => e.DepartmentId).ToList();

        _db.TransactionOutgoingDepartments.RemoveRange(existing.Where(e => !newIds.Contains(e.DepartmentId)));

        var existingIds = existing.Select(e => e.DepartmentId).ToHashSet();
        var addedIds = newIds.Where(id => !existingIds.Contains(id)).ToList();
        foreach (var departmentId in addedIds)
        {
            var outgoing = new TransactionOutgoingDepartment
            {
                DepartmentId = departmentId,
                CreatedById = userId,
                CreatedAt = DateTime.UtcNow
            };

            if (transaction.Id > 0)
            {
                outgoing.TransactionId = transaction.Id;
                _db.TransactionOutgoingDepartments.Add(outgoing);
            }
            else
            {
                transaction.OutgoingDepartments.Add(outgoing);
            }
        }

        await SyncAssignmentsFromOutgoingDepartmentsAsync(transaction, addedIds, removedIds, userId, deferredCreateAudits);
        TrackAuditLog(
            deferredCreateAudits,
            new AuditLogDraft(
                userId,
                AuditAction.Update,
                OutgoingDepartmentsEntityName,
                transaction.Id > 0 ? transaction.Id : null,
                transaction.Id > 0 ? transaction.Id : null,
                JsonSerializer.Serialize(existing.Select(e => e.DepartmentId)),
                JsonSerializer.Serialize(newIds)));
    }

    private async Task SyncAssignmentsFromOutgoingDepartmentsAsync(
        Transaction transaction,
        IReadOnlyList<int> addedDepartmentIds,
        IReadOnlyList<int> removedDepartmentIds,
        int userId,
        ICollection<AuditLog>? deferredCreateAudits = null)
    {
        if (addedDepartmentIds.Count == 0 && removedDepartmentIds.Count == 0)
            return;

        // The referral date should reflect when the transaction was actually routed out
        // (OutgoingDate) rather than when it first arrived (IncomingDate), whenever an
        // outgoing date has been recorded.
        var assignedDate = transaction.OutgoingDate ?? transaction.IncomingDate;
        var dueDate = transaction.ResponseDueDate;
        var assignmentsChanged = false;

        List<Assignment> existingAssignments;
        if (transaction.Id > 0)
        {
            existingAssignments = await _db.Assignments
                .Where(a => a.TransactionId == transaction.Id)
                .ToListAsync();
        }
        else
        {
            existingAssignments = transaction.Assignments.ToList();
        }

        var departmentIdsForNames = addedDepartmentIds
            .Concat(removedDepartmentIds)
            .Distinct()
            .ToList();
        var departmentNames = departmentIdsForNames.Count == 0
            ? new Dictionary<int, string>()
            : await _db.Departments
                .Where(d => departmentIdsForNames.Contains(d.Id))
                .ToDictionaryAsync(d => d.Id, d => d.Name);

        foreach (var departmentId in addedDepartmentIds.Distinct())
        {
            var existing = existingAssignments.FirstOrDefault(a => a.DepartmentId == departmentId);
            if (existing != null)
            {
                if (existing.Status == AssignmentStatus.Active
                    || existing.ReplyStatus == ReplyStatus.Replied
                    || existing.Status == AssignmentStatus.Completed)
                    continue;

                if (existing.Status == AssignmentStatus.Cancelled)
                {
                    existing.Status = AssignmentStatus.Active;
                    existing.ReplyStatus = ReplyStatus.Pending;
                    existing.ReplyDate = null;
                    existing.ReplySummary = null;
                    existing.AssignedDate = assignedDate;
                    existing.DueDate = dueDate;
                    existing.ReplyDueDays = transaction.ResponseDueDays;
                    assignmentsChanged = true;

                    var reactivatedDeptName = departmentNames.GetValueOrDefault(departmentId, departmentId.ToString());
                    TrackAuditLog(
                        deferredCreateAudits,
                        new AuditLogDraft(
                            userId,
                            AuditAction.StatusChange,
                            AssignmentEntityName,
                            existing.Id,
                            transaction.Id > 0 ? transaction.Id : null,
                            AssignmentStatus.Cancelled.ToString(),
                            JsonSerializer.Serialize(new { existing.Status, deptName = reactivatedDeptName, source = "OutgoingDepartment", reactivated = true })));
                }
                continue;
            }

            var deptNameNew = departmentNames.GetValueOrDefault(departmentId, departmentId.ToString());
            var assignment = new Assignment
            {
                DepartmentId = departmentId,
                AssignedDate = assignedDate,
                RequiredAction = "متابعة - صادر لها",
                RequiresReply = true,
                ReplyDueDays = transaction.ResponseDueDays,
                DueDate = dueDate,
                ReplyStatus = ReplyStatus.Pending,
                Status = AssignmentStatus.Active,
                CreatedById = userId,
                CreatedAt = DateTime.UtcNow
            };

            if (transaction.Id > 0)
            {
                assignment.TransactionId = transaction.Id;
                _db.Assignments.Add(assignment);
            }
            else
            {
                assignment.Transaction = transaction;
                transaction.Assignments.Add(assignment);
            }

            existingAssignments.Add(assignment);
            assignmentsChanged = true;

            TrackAuditLog(
                deferredCreateAudits,
                new AuditLogDraft(
                    userId,
                    AuditAction.AddAssignment,
                    AssignmentEntityName,
                    null,
                    transaction.Id > 0 ? transaction.Id : null,
                    null,
                    JsonSerializer.Serialize(new
                    {
                        deptName = deptNameNew,
                        departmentId,
                        dueDate,
                        source = "OutgoingDepartment",
                        autoCreated = true
                    })));
        }

        if (removedDepartmentIds.Count > 0)
        {
            var toCancel = transaction.Id > 0
                ? await _db.Assignments
                    .Include(a => a.Department)
                    .Where(a => a.TransactionId == transaction.Id
                        && removedDepartmentIds.Contains(a.DepartmentId)
                        && a.Status == AssignmentStatus.Active
                        && a.ReplyStatus != ReplyStatus.Replied
                        && a.ReplySummary == null)
                    .ToListAsync()
                : existingAssignments
                    .Where(a => removedDepartmentIds.Contains(a.DepartmentId)
                        && a.Status == AssignmentStatus.Active
                        && a.ReplyStatus != ReplyStatus.Replied
                        && a.ReplySummary == null)
                    .ToList();

            foreach (var assignment in toCancel)
            {
                assignment.Status = AssignmentStatus.Cancelled;
                var departmentName = assignment.Department?.Name
                    ?? departmentNames.GetValueOrDefault(assignment.DepartmentId, assignment.DepartmentId.ToString());
                TrackAuditLog(
                    deferredCreateAudits,
                    new AuditLogDraft(
                        userId,
                        AuditAction.StatusChange,
                        AssignmentEntityName,
                        assignment.Id,
                        transaction.Id > 0 ? transaction.Id : null,
                        AssignmentStatus.Active.ToString(),
                        JsonSerializer.Serialize(new
                        {
                            assignment.Status,
                            departmentName,
                            reason = "RemovedFromOutgoingDepartments"
                        })));
            }

            if (toCancel.Count > 0)
                assignmentsChanged = true;
        }

        if (assignmentsChanged || addedDepartmentIds.Count > 0 || removedDepartmentIds.Count > 0)
        {
            if (transaction.Id > 0 && !_db.Entry(transaction).Collection(x => x.Assignments).IsLoaded)
                await _db.Entry(transaction).Collection(x => x.Assignments).LoadAsync();

            WorkflowHelper.UpdateTransactionStatusFromAssignments(transaction);
        }
    }

    private sealed record AuditLogDraft(
        int UserId,
        AuditAction Action,
        string? EntityName,
        int? EntityId,
        int? TransactionId,
        string? OldValue,
        string? NewValue);

    private void TrackAuditLog(ICollection<AuditLog>? deferredCreateAudits, AuditLogDraft draft)
    {
        var audit = _audit.TrackLog(
            draft.UserId,
            draft.Action,
            draft.EntityName,
            draft.EntityId,
            draft.TransactionId,
            draft.OldValue,
            draft.NewValue);
        if (deferredCreateAudits is null)
            return;

        var entry = _db.Entry(audit);
        if (entry.State != EntityState.Detached)
            entry.State = EntityState.Detached;

        deferredCreateAudits.Add(audit);
    }

    private static void BackfillAuditLogsForTransaction(Transaction transaction, IReadOnlyList<AuditLog> audits)
    {
        foreach (var audit in audits)
        {
            audit.TransactionId ??= transaction.Id;

            if (audit.EntityId is null)
                audit.EntityId = ResolveAuditEntityId(transaction, audit);
        }
    }

    private static int? ResolveAuditEntityId(Transaction transaction, AuditLog audit) =>
        audit.EntityName switch
        {
            OutgoingDepartmentsEntityName => transaction.Id,
            AssignmentEntityName => ResolveAssignmentAuditEntityId(transaction, audit.NewValue),
            _ => null
        };

    private static int? ResolveAssignmentAuditEntityId(Transaction transaction, string? newValue)
    {
        var departmentId = TryReadDepartmentIdFromAuditPayload(newValue);
        if (!departmentId.HasValue)
            return null;

        var assignment = transaction.Assignments.FirstOrDefault(a => a.DepartmentId == departmentId.Value);
        return assignment?.Id > 0 ? assignment.Id : null;
    }

    private static int? TryReadDepartmentIdFromAuditPayload(string? payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
            return null;

        try
        {
            using var document = JsonDocument.Parse(payload);
            if (document.RootElement.TryGetProperty("departmentId", out var departmentIdProperty)
                && departmentIdProperty.TryGetInt32(out var departmentId))
            {
                return departmentId;
            }
        }
        catch (JsonException)
        {
            return null;
        }

        return null;
    }

    private void ResetCreatePersistenceState(Transaction transaction, IReadOnlyList<AuditLog>? ownedPendingAudits = null)
    {
        DetachTransaction(transaction);

        foreach (var outgoing in transaction.OutgoingDepartments.ToList())
        {
            var entry = _db.Entry(outgoing);
            if (entry.State != EntityState.Detached)
                entry.State = EntityState.Detached;
        }
        transaction.OutgoingDepartments.Clear();

        foreach (var assignment in transaction.Assignments.ToList())
        {
            var entry = _db.Entry(assignment);
            if (entry.State != EntityState.Detached)
                entry.State = EntityState.Detached;
        }
        transaction.Assignments.Clear();

        if (ownedPendingAudits is null)
            return;

        foreach (var audit in ownedPendingAudits)
        {
            var auditEntry = _db.Entry(audit);
            if (auditEntry.State != EntityState.Detached)
                auditEntry.State = EntityState.Detached;
        }
    }

    private void DetachTransaction(Transaction transaction)
    {
        var entry = _db.Entry(transaction);
        if (entry.State != EntityState.Detached)
            entry.State = EntityState.Detached;
    }

    private async Task ApplyIncomingSourceAsync(
        Transaction transaction,
        string incomingSourceType,
        int? incomingFromPartyId,
        int? incomingFromDepartmentId)
    {
        var errors = TransactionRequestValidator.ValidateIncomingSource(
            incomingSourceType, incomingFromPartyId, incomingFromDepartmentId);
        if (errors.Count > 0)
            throw new FieldValidationException(errors);

        var sourceType = EnumHelper.ParseIncomingSourceType(incomingSourceType);
        transaction.IncomingSourceType = sourceType;

        if (sourceType == IncomingSourceType.External)
        {
            var party = await _db.ExternalParties.FindAsync(incomingFromPartyId!.Value)
                ?? throw new FieldValidationException(new Dictionary<string, string>
                {
                    [nameof(CreateTransactionRequest.IncomingFromPartyId)] = "الجهة الخارجية غير موجودة"
                });
            transaction.IncomingFromPartyId = incomingFromPartyId;
            transaction.IncomingFromDepartmentId = null;
            transaction.IncomingFrom = party.Name;
        }
        else
        {
            var dept = await _db.Departments.FindAsync(incomingFromDepartmentId!.Value)
                ?? throw new FieldValidationException(new Dictionary<string, string>
                {
                    [nameof(CreateTransactionRequest.IncomingFromDepartmentId)] = "الإدارة غير موجودة"
                });
            transaction.IncomingFromDepartmentId = incomingFromDepartmentId;
            transaction.IncomingFromPartyId = null;
            transaction.IncomingFrom = dept.Name;
        }
    }

    private static string? ResolveIncomingFromName(Transaction t) =>
        t.IncomingSourceType switch
        {
            IncomingSourceType.Internal => t.IncomingFromDepartment?.Name ?? t.IncomingFrom,
            IncomingSourceType.External => t.IncomingFromParty?.Name ?? t.IncomingFrom,
            _ => t.IncomingFrom
        };

    public async Task<bool> CanAccessTransactionAsync(int transactionId, ICurrentUserService currentUser)
    {
        if (currentUser.Role != UserRole.DepartmentUser)
            return await _db.Transactions.AsNoTracking().AnyAsync(t => t.Id == transactionId);

        var deptId = RequireDepartmentUserDepartmentId(currentUser);

        return await _db.Transactions.AsNoTracking()
            .AnyAsync(t => t.Id == transactionId &&
                (t.Assignments.Any(a => a.DepartmentId == deptId &&
                    a.RequiresReply &&
                    a.Status != AssignmentStatus.Cancelled) ||
                    _db.DepartmentResponses.Any(r => r.TransactionId == t.Id && r.DepartmentId == deptId)));
    }

    // Adjacent transaction ids for the detail page's previous/next navigation. Ordered by
    // (IncomingDate, Id) ascending, the same stable ordering as the transaction list's default
    // sort; scoped with the same department-user visibility rule as SearchAsync so a user never
    // navigates to a transaction they could not otherwise open.
    public async Task<TransactionAdjacentDto?> GetAdjacentAsync(int id, ICurrentUserService currentUser)
    {
        var current = await _db.Transactions.AsNoTracking()
            .Where(t => t.Id == id)
            .Select(t => new { t.IncomingDate })
            .FirstOrDefaultAsync();
        if (current == null) return null;

        var scoped = ApplyDepartmentUserScope(_db.Transactions.AsNoTracking(), currentUser);

        var previousId = await scoped
            .Where(t => t.IncomingDate < current.IncomingDate
                || (t.IncomingDate == current.IncomingDate && t.Id < id))
            .OrderByDescending(t => t.IncomingDate)
            .ThenByDescending(t => t.Id)
            .Select(t => (int?)t.Id)
            .FirstOrDefaultAsync();

        var nextId = await scoped
            .Where(t => t.IncomingDate > current.IncomingDate
                || (t.IncomingDate == current.IncomingDate && t.Id > id))
            .OrderBy(t => t.IncomingDate)
            .ThenBy(t => t.Id)
            .Select(t => (int?)t.Id)
            .FirstOrDefaultAsync();

        return new TransactionAdjacentDto { PreviousId = previousId, NextId = nextId };
    }

    private static bool CanAccess(Transaction t, ICurrentUserService user)
    {
        if (user.Role == UserRole.DepartmentUser)
        {
            var deptId = RequireDepartmentUserDepartmentId(user);
            return t.Assignments.Any(a => a.DepartmentId == deptId &&
                a.RequiresReply &&
                a.Status != AssignmentStatus.Cancelled);
        }
        return true;
    }

    private static int RequireDepartmentUserDepartmentId(ICurrentUserService user) =>
        user.DepartmentId
            ?? throw new UnauthorizedAccessException("المستخدم غير مرتبط بإدارة.");

    private sealed record AssignmentSummaryRow(
        string DepartmentName,
        ReplyStatus ReplyStatus,
        bool RequiresReply,
        AssignmentStatus Status,
        DateTime? DueDate);

    private static TransactionDetailDto MapToBasicDetailDto(
        Transaction t,
        IReadOnlyList<AssignmentSummaryRow> assignmentRows,
        DateTime now)
    {
        var replied = assignmentRows.Where(a => a.ReplyStatus == ReplyStatus.Replied).Select(a => a.DepartmentName).ToList();
        var pending = assignmentRows.Where(a => a.RequiresReply && a.ReplyStatus != ReplyStatus.Replied && a.Status == AssignmentStatus.Active)
            .Select(a => a.DepartmentName).ToList();
        var hasPending = pending.Count > 0;
        var summaryFacts = assignmentRows
            .Select(a => new TransactionTemporalCalculator.AssignmentSummaryFacts(
                a.ReplyStatus, a.RequiresReply, a.Status, a.DueDate))
            .ToList();

        var dto = new TransactionDetailDto
        {
            Id = t.Id,
            InternalTrackingNumber = t.InternalTrackingNumber,
            IncomingNumber = t.IncomingNumber,
            IncomingDate = t.IncomingDate,
            Subject = t.Subject,
            IncomingFrom = ResolveIncomingFromName(t),
            IncomingSourceType = t.IncomingSourceType.ToString(),
            IncomingFromPartyId = t.IncomingFromPartyId,
            IncomingFromDepartmentId = t.IncomingFromDepartmentId,
            OutgoingNumber = t.OutgoingNumber,
            OutgoingDate = t.OutgoingDate,
            OutgoingTo = t.OutgoingTo,
            Status = t.Status.ToString(),
            Priority = t.Priority.ToString(),
            CategoryId = t.CategoryId,
            CategoryName = t.CategoryEntity?.Name ?? t.Category,
            Category = t.Category,
            RequiresResponse = t.RequiresResponse,
            ResponseCompleted = t.ResponseCompleted,
            ResponseType = t.ResponseType.ToString(),
            ResponseDueDays = t.ResponseDueDays,
            ResponseDueDate = t.ResponseDueDate,
            ResponseCompletedDate = t.ResponseCompletedDate,
            ResponseSummary = t.ResponseSummary,
            Notes = t.Notes,
            IsResponseOverdue = TransactionTemporalCalculator.IsResponseOverdue(t, now),
            HasPendingAssignments = hasPending,
            IsOverdue = TransactionTemporalCalculator.IsOverdue(t, summaryFacts, now),
            IsArchived = t.IsArchived,
            CreatedByName = t.CreatedBy?.FullName ?? "",
            CreatedAt = t.CreatedAt,
            UpdatedAt = t.UpdatedAt,
            RecurringTemplateId = t.RecurringTemplateId,
            RecurringTemplateTitle = t.RecurringTemplate?.Title,
            RecurringPeriodKey = t.RecurringPeriodKey,
            RecurringPeriodLabel = t.RecurringPeriodLabel,
            OutgoingDepartments = t.OutgoingDepartments.Select(o => new OutgoingDepartmentDto
            {
                Id = o.Id,
                DepartmentId = o.DepartmentId,
                DepartmentName = o.Department.Name
            }).ToList(),
            OutgoingDepartmentNames = t.OutgoingDepartments.Select(o => o.Department.Name).ToList(),
            OutgoingPartyNames = t.OutgoingDepartments.Select(o => o.Department.Name).ToList(),
            RepliedDepartmentNames = replied,
            PendingDepartmentNames = pending,
            FollowUps = new(),
            Assignments = new(),
            Attachments = new(),
            AuditLogs = new()
        };
        TransactionTimelineHelper.ApplyForTransaction(dto, t, now);
        return dto;
    }

    private static FollowUpDto MapFollowUp(FollowUp f) => new()
    {
        Id = f.Id,
        FollowUpNumber = f.FollowUpNumber,
        FollowUpDate = f.FollowUpDate,
        SentTo = f.SentTo,
        Recipients = f.Recipients.Select(r => new FollowUpRecipientDto
        {
            Id = r.Id,
            ExternalPartyId = r.ExternalPartyId,
            PartyName = r.ExternalParty?.Name ?? ""
        }).ToList(),
        Departments = f.Departments.Select(d => new FollowUpDepartmentDto
        {
            Id = d.Id,
            DepartmentId = d.DepartmentId,
            DepartmentName = d.Department?.Name ?? ""
        }).ToList(),
        Notes = f.Notes,
        RequiresReply = f.RequiresReply,
        ReplyStatus = f.ReplyStatus.ToString(),
        ReplyDate = f.ReplyDate,
        ReplySummary = f.ReplySummary,
        CreatedByName = f.CreatedBy?.FullName ?? "",
        CreatedAt = f.CreatedAt
    };

    private async Task<FollowUpDto> MapFollowUpDtoAsync(int followUpId)
    {
        var f = await _db.FollowUps
            .Include(x => x.CreatedBy)
            .Include(x => x.Departments).ThenInclude(d => d.Department)
            .FirstAsync(x => x.Id == followUpId);
        return MapFollowUp(f);
    }

    private static DateTime NormalizeDateOnlyUtc(DateTime value) =>
        DateTime.SpecifyKind(value.Date, DateTimeKind.Utc);

    private static AssignmentDto MapAssignment(
        Assignment a,
        string deptName,
        string createdByName,
        int? departmentResponseId = null,
        DateTime? responseDate = null,
        int? departmentCompletionDays = null,
        bool canAdminEdit = false)
    {
        var now = DateTime.UtcNow;
        return new AssignmentDto
        {
            Id = a.Id,
            DepartmentId = a.DepartmentId,
            DepartmentName = deptName,
            LetterNumber = a.LetterNumber,
            AssignedDate = a.AssignedDate,
            RequiredAction = a.RequiredAction,
            RequiresReply = a.RequiresReply,
            ReplyDueDays = a.ReplyDueDays,
            DueDate = a.DueDate,
            ReplyStatus = a.ReplyStatus.ToString(),
            ReplyDate = a.ReplyDate,
            ReplySummary = a.ReplySummary,
            Status = a.Status.ToString(),
            IsOverdue = TransactionTemporalCalculator.IsAssignmentOverdue(a, now),
            DepartmentResponseId = departmentResponseId,
            ResponseDate = responseDate,
            DepartmentCompletionDays = departmentCompletionDays,
            CanAdminEdit = canAdminEdit,
            CreatedByName = createdByName,
            CreatedAt = a.CreatedAt
        };
    }
}

internal class SystemUser : ICurrentUserService
{
    private readonly int _id;
    public SystemUser(int id) => _id = id;
    public int UserId => _id;
    public string Username => "system";
    public UserRole Role => UserRole.Admin;
    public int? DepartmentId => null;
    public bool IsAuthenticated => true;
}
