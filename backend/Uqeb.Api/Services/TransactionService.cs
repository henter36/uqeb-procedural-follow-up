using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Uqeb.Api.Data;
using Uqeb.Api.DTOs.Transactions;
using Uqeb.Api.Helpers;
using Uqeb.Api.Models.Entities;
using Uqeb.Api.Models.Enums;

namespace Uqeb.Api.Services;

public interface ITransactionService
{
    Task<PagedResult<TransactionListDto>> SearchAsync(TransactionSearchRequest request, ICurrentUserService currentUser);
    Task<TransactionDetailDto?> GetByIdAsync(int id, ICurrentUserService currentUser);
    Task<TransactionDetailDto> CreateAsync(CreateTransactionRequest request, int userId);
    Task<TransactionDetailDto?> UpdateAsync(int id, UpdateTransactionRequest request, int userId, UserRole role);
    Task<bool> CancelAsync(int id, int userId, UserRole role);
    Task<bool> ArchiveAsync(int id, int userId, UserRole role);
    Task<bool> CloseAsync(int id, int userId, UserRole role);
    Task<TransactionDetailDto?> CompleteResponseAsync(int id, CompleteResponseRequest request, ICurrentUserService currentUser);
    Task<List<FollowUpDepartmentOptionDto>?> GetFollowUpDepartmentsAsync(int transactionId, ICurrentUserService currentUser);
    Task<FollowUpDto> AddFollowUpAsync(int transactionId, CreateFollowUpRequest request, int userId);
    Task<FollowUpDto?> ReplyFollowUpAsync(int transactionId, int followUpId, ReplyFollowUpRequest request, int userId);
    Task<AssignmentDto> AddAssignmentAsync(int transactionId, CreateAssignmentRequest request, int userId);
    Task<AssignmentDto?> ReplyAssignmentAsync(int transactionId, int assignmentId, ReplyAssignmentRequest request, ICurrentUserService currentUser);
    Task<List<AuditLogDto>> GetAuditLogAsync(int transactionId, ICurrentUserService currentUser);
}

public class TransactionService : ITransactionService
{
    private readonly AppDbContext _db;
    private readonly IAuditService _audit;
    private readonly ITrackingNumberService _trackingNumbers;

    public TransactionService(AppDbContext db, IAuditService audit, ITrackingNumberService trackingNumbers)
    {
        _db = db;
        _audit = audit;
        _trackingNumbers = trackingNumbers;
    }

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
        var query = BaseQuery().AsQueryable();

        if (currentUser.Role == UserRole.DepartmentUser && currentUser.DepartmentId.HasValue)
        {
            var deptId = currentUser.DepartmentId.Value;
            query = query.Where(t => t.Assignments.Any(a => a.DepartmentId == deptId));
        }

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
        if (request.DateFrom.HasValue)
            query = query.Where(t => t.IncomingDate >= request.DateFrom);
        if (request.DateTo.HasValue)
            query = query.Where(t => t.IncomingDate <= request.DateTo);
        if (request.ResponseDueDateFrom.HasValue)
            query = query.Where(t => t.ResponseDueDate >= request.ResponseDueDateFrom);
        if (request.ResponseDueDateTo.HasValue)
            query = query.Where(t => t.ResponseDueDate <= request.ResponseDueDateTo);
        if (request.RequiresResponse == true)
            query = query.Where(t => t.RequiresResponse);
        if (request.ResponseCompleted == true)
            query = query.Where(t => t.ResponseCompleted);
        else if (request.ResponseCompleted == false)
            query = query.Where(t => t.RequiresResponse && !t.ResponseCompleted);
        if (request.ResponseOverdue == true)
            query = query.Where(t => t.RequiresResponse && !t.ResponseCompleted && t.ResponseDueDate < now);
        if (request.HasPendingAssignments == true)
            query = query.Where(t => t.Assignments.Any(a => a.RequiresReply && a.ReplyStatus != ReplyStatus.Replied && a.Status == AssignmentStatus.Active));
        if (request.HasPartialReplies == true)
            query = query.Where(t => t.Status == TransactionStatus.PartiallyReplied);
        if (request.OverdueOnly == true)
            query = query.Where(t =>
                (t.RequiresResponse && !t.ResponseCompleted && t.ResponseDueDate < now) ||
                t.Assignments.Any(a => a.RequiresReply && a.ReplyStatus != ReplyStatus.Replied && a.DueDate < now));

        var total = await query.CountAsync();

        query = request.SortBy?.ToLower() switch
        {
            "incomingnumber" => request.SortDesc ? query.OrderByDescending(t => t.IncomingNumber) : query.OrderBy(t => t.IncomingNumber),
            "subject" => request.SortDesc ? query.OrderByDescending(t => t.Subject) : query.OrderBy(t => t.Subject),
            "status" => request.SortDesc ? query.OrderByDescending(t => t.Status) : query.OrderBy(t => t.Status),
            "createdat" => request.SortDesc ? query.OrderByDescending(t => t.CreatedAt) : query.OrderBy(t => t.CreatedAt),
            _ => request.SortDesc ? query.OrderByDescending(t => t.IncomingDate) : query.OrderBy(t => t.IncomingDate)
        };

        var items = await query.Skip((request.Page - 1) * request.PageSize).Take(request.PageSize).ToListAsync();

        return PagedResult<TransactionListDto>.Create(
            items.Select(t => MapToListDto(t, now)).ToList(),
            total,
            request.Page,
            request.PageSize);
    }

    public async Task<TransactionDetailDto?> GetByIdAsync(int id, ICurrentUserService currentUser)
    {
        var t = await _db.Transactions
            .AsSplitQuery()
            .Include(x => x.CreatedBy)
            .Include(x => x.CategoryEntity)
            .Include(x => x.IncomingFromParty)
            .Include(x => x.IncomingFromDepartment)
            .Include(x => x.OutgoingDepartments).ThenInclude(o => o.Department)
            .Include(x => x.FollowUps).ThenInclude(f => f.CreatedBy)
            .Include(x => x.FollowUps).ThenInclude(f => f.Departments).ThenInclude(d => d.Department)
            .Include(x => x.Assignments).ThenInclude(a => a.Department)
            .Include(x => x.Assignments).ThenInclude(a => a.CreatedBy)
            .Include(x => x.Attachments).ThenInclude(a => a.UploadedBy)
            .Include(x => x.AuditLogs).ThenInclude(a => a.User)
            .FirstOrDefaultAsync(x => x.Id == id);

        if (t == null) return null;
        if (!CanAccess(t, currentUser)) return null;

        await UpdateOverdueStatusesAsync(t);
        return MapToDetailDto(t);
    }

    public async Task<TransactionDetailDto> CreateAsync(CreateTransactionRequest request, int userId)
    {
        var validationErrors = TransactionRequestValidator.ValidateCreate(request);
        if (validationErrors.Count > 0)
            throw new FieldValidationException(validationErrors);

        if (await _db.Transactions.AnyAsync(t => t.IncomingNumber == request.IncomingNumber))
            throw new DuplicateIncomingNumberException();

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

        await ApplyIncomingSourceAsync(transaction, request.IncomingSourceType!, request.IncomingFromPartyId, request.IncomingFromDepartmentId);

        const int maxAttempts = 3;
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            transaction.InternalTrackingNumber = await _trackingNumbers.GenerateNextAsync();
            _db.Transactions.Add(transaction);
            try
            {
                await _db.SaveChangesAsync();
                break;
            }
            catch (DbUpdateException ex) when (SqlExceptionHelper.IsDuplicateKey(ex, "IX_Transactions_IncomingNumber"))
            {
                DetachTransaction(transaction);
                throw new DuplicateIncomingNumberException();
            }
            catch (DbUpdateException ex) when (SqlExceptionHelper.IsDuplicateKey(ex, "IX_Transactions_InternalTrackingNumber") && attempt < maxAttempts)
            {
                DetachTransaction(transaction);
            }
            catch (DbUpdateException ex) when (SqlExceptionHelper.IsDuplicateKey(ex, "IX_Transactions_InternalTrackingNumber"))
            {
                DetachTransaction(transaction);
                throw new DuplicateTrackingNumberException();
            }
        }

        await SyncOutgoingDepartmentsAsync(transaction.Id, request.OutgoingDepartmentIds ?? new List<int>(), userId);

        await _audit.LogAsync(userId, AuditAction.Create, "Transaction", transaction.Id, transaction.Id, null,
            JsonSerializer.Serialize(new { transaction.IncomingNumber, transaction.Subject }));

        return (await GetByIdAsync(transaction.Id, new SystemUser(userId)))!;
    }

    public async Task<TransactionDetailDto?> UpdateAsync(int id, UpdateTransactionRequest request, int userId, UserRole role)
    {
        var t = await _db.Transactions.Include(x => x.OutgoingDepartments).FirstOrDefaultAsync(x => x.Id == id);
        if (t == null) return null;

        var oldValues = JsonSerializer.Serialize(new
        {
            t.IncomingNumber, t.Subject, t.Status, t.CategoryId, t.Priority,
            OutgoingDepartmentIds = t.OutgoingDepartments.Select(o => o.DepartmentId).ToList(),
            t.ResponseDueDays, ResponseType = t.ResponseType.ToString()
        });

        if (!string.IsNullOrEmpty(request.IncomingNumber) && request.IncomingNumber != t.IncomingNumber)
        {
            if (await _db.Transactions.AnyAsync(x => x.IncomingNumber == request.IncomingNumber && x.Id != id))
                throw new InvalidOperationException("رقم الوارد موجود مسبقاً");
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
                await _audit.LogAsync(userId, AuditAction.Update, "Transaction", id, id, oldType.ToString(), newType.ToString());
        }
        if (request.ResponseDueDays.HasValue) t.ResponseDueDays = request.ResponseDueDays;
        if (request.ResponseCompleted.HasValue || request.ResponseCompletedDate.HasValue)
            throw new InvalidOperationException("يجب تسجيل الإفادة عبر إجراء «تسجيل الإفادة» وليس من شاشة التعديل");
        if (!string.IsNullOrEmpty(request.Status))
        {
            var newStatus = EnumHelper.ParseTransactionStatus(request.Status);
            if (newStatus == TransactionStatus.Closed && role != UserRole.Admin && role != UserRole.Supervisor)
                throw new UnauthorizedAccessException("لا تملك صلاحية إغلاق المعاملة");
            if (newStatus == TransactionStatus.Closed)
                await ValidateCanCloseAsync(t, userId);
            t.Status = newStatus;
        }
        if (!string.IsNullOrEmpty(request.Priority))
        {
            var oldPriority = t.Priority;
            t.Priority = EnumHelper.ParsePriority(request.Priority);
            if (oldPriority != t.Priority)
                await _audit.LogAsync(userId, AuditAction.Update, "Transaction", id, id, oldPriority.ToString(), t.Priority.ToString());
        }
        if (request.CategoryId.HasValue)
        {
            var oldCategoryId = t.CategoryId;
            t.CategoryId = request.CategoryId;
            if (oldCategoryId != request.CategoryId)
                await _audit.LogAsync(userId, AuditAction.Update, "Transaction", id, id, oldCategoryId?.ToString(), request.CategoryId.ToString());
        }
        if (request.Notes != null) t.Notes = request.Notes;

        WorkflowHelper.RecalculateResponseDueDate(t);

        if (request.OutgoingDepartmentIds != null)
            await SyncOutgoingDepartmentsAsync(id, request.OutgoingDepartmentIds, userId);

        t.UpdatedById = userId;
        t.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();
        await _audit.LogAsync(userId, AuditAction.Update, "Transaction", id, id, oldValues,
            JsonSerializer.Serialize(new { t.IncomingNumber, t.Subject, t.Status, t.CategoryId, t.ResponseDueDays }));

        return await GetByIdAsync(id, new SystemUser(userId));
    }

    public async Task<bool> CancelAsync(int id, int userId, UserRole role)
    {
        if (role != UserRole.Admin && role != UserRole.Supervisor)
            throw new UnauthorizedAccessException("لا تملك صلاحية الإلغاء");

        var t = await _db.Transactions.FindAsync(id);
        if (t == null) return false;

        t.Status = TransactionStatus.Cancelled;
        t.UpdatedById = userId;
        t.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        await _audit.LogAsync(userId, AuditAction.Cancel, "Transaction", id, id, null, "Cancelled");
        return true;
    }

    public async Task<bool> ArchiveAsync(int id, int userId, UserRole role)
    {
        if (role != UserRole.Admin && role != UserRole.Supervisor)
            throw new UnauthorizedAccessException("لا تملك صلاحية الأرشفة");

        var t = await _db.Transactions.FindAsync(id);
        if (t == null) return false;

        t.IsArchived = true;
        t.ArchivedAt = DateTime.UtcNow;
        t.Status = TransactionStatus.Archived;
        t.UpdatedById = userId;
        t.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        await _audit.LogAsync(userId, AuditAction.Archive, "Transaction", id, id, null, "Archived");
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
            await ValidateCanCloseAsync(t, userId);
        }
        catch (InvalidOperationException ex)
        {
            await _audit.LogAsync(userId, AuditAction.CloseAttemptFailed, "Transaction", id, id, null, ex.Message);
            throw;
        }

        t.Status = TransactionStatus.Closed;
        t.ClosedAt = DateTime.UtcNow;
        t.UpdatedById = userId;
        t.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        await _audit.LogAsync(userId, AuditAction.Close, "Transaction", id, id, null,
            JsonSerializer.Serialize(new { ClosedAt = t.ClosedAt }));
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

        if (request.ResponseDate == default)
            throw new InvalidOperationException("تاريخ الإفادة مطلوب");

        if (string.IsNullOrWhiteSpace(request.ResponseSummary))
            throw new InvalidOperationException("ملخص الإفادة مطلوب");

        var requiresOutgoing = t.ResponseType is ResponseType.External or ResponseType.Both;
        if (requiresOutgoing)
        {
            if (string.IsNullOrWhiteSpace(request.OutgoingNumber))
                throw new InvalidOperationException("رقم الصادر مطلوب لنوع الإفادة المحدد");
            if (!request.OutgoingDate.HasValue)
                throw new InvalidOperationException("تاريخ الصادر مطلوب لنوع الإفادة المحدد");
        }

        t.ResponseCompleted = true;
        t.ResponseCompletedDate = request.ResponseDate.Date;
        t.ResponseSummary = request.ResponseSummary.Trim();
        if (!string.IsNullOrWhiteSpace(request.OutgoingNumber))
            t.OutgoingNumber = request.OutgoingNumber.Trim();
        if (request.OutgoingDate.HasValue)
            t.OutgoingDate = request.OutgoingDate.Value.Date;
        t.Status = TransactionStatus.ResponseCompleted;
        t.UpdatedById = userId;
        t.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();
        await _audit.LogAsync(userId, AuditAction.CompleteResponse, "Transaction", id, id, null,
            JsonSerializer.Serialize(new
            {
                responseDate = t.ResponseCompletedDate,
                responseSummary = t.ResponseSummary,
                outgoingNumber = t.OutgoingNumber,
                outgoingDate = t.OutgoingDate
            }));

        return await GetByIdAsync(id, currentUser);
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

        var allowedIds = GetAllowedFollowUpDepartmentIds(t);
        var invalid = departmentIds.Where(id => !allowedIds.Contains(id)).ToList();
        if (invalid.Count > 0)
        {
            var invalidNames = await _db.Departments
                .Where(d => invalid.Contains(d.Id))
                .Select(d => d.Name)
                .ToListAsync();
            var msg = $"لا يمكن إرسال التعقيب لإدارات غير مرتبطة بالمعاملة: {string.Join("، ", invalidNames)}";
            await _audit.LogAsync(userId, AuditAction.AddFollowUp, "FollowUp", null, transactionId, null, msg);
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

        _db.FollowUps.Add(followUp);
        await _db.SaveChangesAsync();

        foreach (var deptId in departmentIds)
        {
            _db.FollowUpDepartments.Add(new FollowUpDepartment
            {
                FollowUpId = followUp.Id,
                DepartmentId = deptId,
                CreatedById = userId,
                CreatedAt = DateTime.UtcNow
            });
        }

        if (t.Status == TransactionStatus.New) t.Status = TransactionStatus.InProgress;
        await _db.SaveChangesAsync();

        await _audit.LogAsync(userId, AuditAction.AddFollowUp, "FollowUp", followUp.Id, transactionId, null,
            JsonSerializer.Serialize(new { departmentIds, deptNames, request.Notes }));

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
                Source = "OutgoingDepartment"
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
                Source = "Assignment"
            };
        }

        return options.Values.OrderBy(x => x.DepartmentName).ToList();
    }

    public async Task<FollowUpDto?> ReplyFollowUpAsync(int transactionId, int followUpId, ReplyFollowUpRequest request, int userId)
    {
        var followUp = await _db.FollowUps.Include(f => f.CreatedBy)
            .FirstOrDefaultAsync(f => f.Id == followUpId && f.TransactionId == transactionId);
        if (followUp == null) return null;

        followUp.ReplyStatus = ReplyStatus.Replied;
        followUp.ReplyDate = request.ReplyDate;
        followUp.ReplySummary = request.ReplySummary;
        await _db.SaveChangesAsync();
        await _audit.LogAsync(userId, AuditAction.RecordReply, "FollowUp", followUpId, transactionId, null, request.ReplySummary);
        return await MapFollowUpDtoAsync(followUpId);
    }

    public async Task<AssignmentDto> AddAssignmentAsync(int transactionId, CreateAssignmentRequest request, int userId)
    {
        var t = await _db.Transactions.FindAsync(transactionId)
            ?? throw new InvalidOperationException("المعاملة غير موجودة");

        var dept = await _db.Departments.FindAsync(request.DepartmentId)
            ?? throw new InvalidOperationException("الإدارة غير موجودة");

        var dueDate = WorkflowHelper.CalculateAssignmentDueDate(request.AssignedDate, request.ReplyDueDays, request.DueDate);

        var assignment = new Assignment
        {
            TransactionId = transactionId,
            DepartmentId = request.DepartmentId,
            AssignedDate = request.AssignedDate,
            RequiredAction = request.RequiredAction,
            RequiresReply = true,
            ReplyDueDays = request.ReplyDueDays,
            DueDate = dueDate,
            ReplyStatus = ReplyStatus.Pending,
            Status = AssignmentStatus.Active,
            CreatedById = userId,
            CreatedAt = DateTime.UtcNow
        };

        _db.Assignments.Add(assignment);
        t.Status = TransactionStatus.Assigned;
        await _db.SaveChangesAsync();

        await UpdateTransactionReplyStatusAsync(transactionId);
        await _audit.LogAsync(userId, AuditAction.AddAssignment, "Assignment", assignment.Id, transactionId, null,
            JsonSerializer.Serialize(new { dept.Name, dueDate, request.ReplyDueDays }));

        return MapAssignment(assignment, dept.Name, "");
    }

    public async Task<AssignmentDto?> ReplyAssignmentAsync(int transactionId, int assignmentId, ReplyAssignmentRequest request, ICurrentUserService currentUser)
    {
        var assignment = await _db.Assignments
            .Include(a => a.Department)
            .Include(a => a.CreatedBy)
            .FirstOrDefaultAsync(a => a.Id == assignmentId && a.TransactionId == transactionId);
        if (assignment == null) return null;

        if (currentUser.Role == UserRole.DepartmentUser && currentUser.DepartmentId != assignment.DepartmentId)
            throw new UnauthorizedAccessException("لا يمكنك الرد على تحويل ليس لإدارتك");

        assignment.ReplyStatus = ReplyStatus.Replied;
        assignment.ReplyDate = request.ReplyDate;
        assignment.ReplySummary = request.ReplySummary;
        assignment.Status = AssignmentStatus.Completed;
        await _db.SaveChangesAsync();
        await _audit.LogAsync(currentUser.UserId, AuditAction.RecordReply, "Assignment", assignmentId, transactionId, null, request.ReplySummary);

        await UpdateTransactionReplyStatusAsync(transactionId);
        return MapAssignment(assignment, assignment.Department.Name, assignment.CreatedBy.FullName);
    }

    public async Task<List<AuditLogDto>> GetAuditLogAsync(int transactionId, ICurrentUserService currentUser)
    {
        var t = await _db.Transactions.FindAsync(transactionId);
        if (t == null || !CanAccess(t, currentUser)) return new();

        return await _db.AuditLogs
            .Include(a => a.User)
            .Where(a => a.TransactionId == transactionId)
            .OrderByDescending(a => a.CreatedAt)
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
    }

    private static List<Assignment> GetPendingReplyAssignments(Transaction t) =>
        t.Assignments
            .Where(a => a.RequiresReply
                && a.Status == AssignmentStatus.Active
                && a.ReplyStatus is ReplyStatus.Pending or ReplyStatus.Overdue)
            .ToList();

    private async Task ValidateCanCloseAsync(Transaction t, int userId)
    {
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

    private async Task UpdateTransactionReplyStatusAsync(int transactionId)
    {
        var t = await _db.Transactions.Include(x => x.Assignments).FirstOrDefaultAsync(x => x.Id == transactionId);
        if (t == null) return;

        WorkflowHelper.UpdateTransactionStatusFromAssignments(t);
        await _db.SaveChangesAsync();
    }

    private async Task UpdateOverdueStatusesAsync(Transaction t)
    {
        var now = DateTime.UtcNow;
        var changed = false;
        foreach (var a in t.Assignments)
        {
            if (a.RequiresReply && a.ReplyStatus == ReplyStatus.Pending && a.DueDate < now)
            {
                a.ReplyStatus = ReplyStatus.Overdue;
                changed = true;
            }
        }
        if (changed)
        {
            WorkflowHelper.UpdateTransactionStatusFromAssignments(t);
            await _db.SaveChangesAsync();
        }
    }

    private async Task SyncOutgoingDepartmentsAsync(int transactionId, List<int> departmentIds, int userId)
    {
        var existing = await _db.TransactionOutgoingDepartments.Where(x => x.TransactionId == transactionId).ToListAsync();
        var newIds = departmentIds.Distinct().ToHashSet();

        _db.TransactionOutgoingDepartments.RemoveRange(existing.Where(e => !newIds.Contains(e.DepartmentId)));

        var existingIds = existing.Select(e => e.DepartmentId).ToHashSet();
        foreach (var departmentId in newIds.Where(id => !existingIds.Contains(id)))
        {
            _db.TransactionOutgoingDepartments.Add(new TransactionOutgoingDepartment
            {
                TransactionId = transactionId,
                DepartmentId = departmentId,
                CreatedById = userId,
                CreatedAt = DateTime.UtcNow
            });
        }

        await _db.SaveChangesAsync();
        await _audit.LogAsync(userId, AuditAction.Update, "TransactionOutgoingDepartments", transactionId, transactionId,
            JsonSerializer.Serialize(existing.Select(e => e.DepartmentId)),
            JsonSerializer.Serialize(newIds));
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

    private static bool CanAccess(Transaction t, ICurrentUserService user)
    {
        if (user.Role == UserRole.DepartmentUser && user.DepartmentId.HasValue)
            return t.Assignments.Any(a => a.DepartmentId == user.DepartmentId);
        return true;
    }

    private static TransactionListDto MapToListDto(Transaction t, DateTime now)
    {
        var dto = new TransactionListDto
        {
            Id = t.Id,
            InternalTrackingNumber = t.InternalTrackingNumber,
            IncomingNumber = t.IncomingNumber,
            IncomingDate = t.IncomingDate,
            Subject = t.Subject,
            IncomingFrom = ResolveIncomingFromName(t),
            IncomingSourceType = t.IncomingSourceType.ToString(),
            OutgoingNumber = t.OutgoingNumber,
            OutgoingDate = t.OutgoingDate,
            OutgoingDepartmentNames = t.OutgoingDepartments.Select(o => o.Department.Name).ToList(),
            OutgoingPartyNames = t.OutgoingDepartments.Select(o => o.Department.Name).ToList(),
            Status = t.Status.ToString(),
            Priority = t.Priority.ToString(),
            CategoryName = t.CategoryEntity?.Name ?? t.Category,
            RequiresResponse = t.RequiresResponse,
            ResponseCompleted = t.ResponseCompleted,
            ResponseDueDate = t.ResponseDueDate,
            IsResponseOverdue = WorkflowHelper.IsResponseOverdue(t, now),
            HasPendingAssignments = t.Assignments.Any(a => a.RequiresReply && a.ReplyStatus != ReplyStatus.Replied && a.Status == AssignmentStatus.Active),
            IsOverdue = WorkflowHelper.IsTransactionOverdue(t, now),
            IsArchived = t.IsArchived,
            CreatedByName = t.CreatedBy?.FullName ?? "",
            CreatedAt = t.CreatedAt
        };
        if (t.ResponseDueDate.HasValue && !t.ResponseCompleted)
            dto.DaysRemainingForResponse = (int)(t.ResponseDueDate.Value.Date - now.Date).TotalDays;
        return dto;
    }

    private static TransactionDetailDto MapToDetailDto(Transaction t)
    {
        var now = DateTime.UtcNow;
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
            IsResponseOverdue = WorkflowHelper.IsResponseOverdue(t, now),
            HasPendingAssignments = t.Assignments.Any(a => a.RequiresReply && a.ReplyStatus != ReplyStatus.Replied && a.Status == AssignmentStatus.Active),
            IsOverdue = WorkflowHelper.IsTransactionOverdue(t, now),
            IsArchived = t.IsArchived,
            CreatedByName = t.CreatedBy?.FullName ?? "",
            CreatedAt = t.CreatedAt,
            UpdatedAt = t.UpdatedAt,
            OutgoingDepartments = t.OutgoingDepartments.Select(o => new OutgoingDepartmentDto
            {
                Id = o.Id,
                DepartmentId = o.DepartmentId,
                DepartmentName = o.Department.Name
            }).ToList(),
            OutgoingDepartmentNames = t.OutgoingDepartments.Select(o => o.Department.Name).ToList(),
            OutgoingPartyNames = t.OutgoingDepartments.Select(o => o.Department.Name).ToList(),
            RepliedDepartmentNames = t.Assignments.Where(a => a.ReplyStatus == ReplyStatus.Replied).Select(a => a.Department.Name).ToList(),
            PendingDepartmentNames = t.Assignments.Where(a => a.RequiresReply && a.ReplyStatus != ReplyStatus.Replied && a.Status == AssignmentStatus.Active).Select(a => a.Department.Name).ToList(),
            FollowUps = t.FollowUps.OrderByDescending(f => f.CreatedAt).Select(f => MapFollowUp(f)).ToList(),
            Assignments = t.Assignments.Select(a => MapAssignment(a, a.Department?.Name ?? "", a.CreatedBy?.FullName ?? "")).ToList(),
            Attachments = t.Attachments.Select(a => new AttachmentDto
            {
                Id = a.Id,
                AttachmentType = a.AttachmentType,
                OriginalFileName = a.OriginalFileName,
                ContentType = a.ContentType,
                FileSize = a.FileSize,
                UploadedByName = a.UploadedBy?.FullName ?? "",
                UploadedAt = a.UploadedAt
            }).ToList(),
            AuditLogs = t.AuditLogs.OrderByDescending(a => a.CreatedAt).Select(a => new AuditLogDto
            {
                Id = a.Id,
                Action = a.Action.ToString(),
                EntityName = a.EntityName,
                EntityId = a.EntityId,
                OldValue = a.OldValue,
                NewValue = a.NewValue,
                UserName = a.User?.FullName ?? "",
                CreatedAt = a.CreatedAt
            }).ToList()
        };
        if (t.ResponseDueDate.HasValue && !t.ResponseCompleted)
            dto.DaysRemainingForResponse = (int)(t.ResponseDueDate.Value.Date - now.Date).TotalDays;
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

    private static AssignmentDto MapAssignment(Assignment a, string deptName, string createdByName)
    {
        var now = DateTime.UtcNow;
        return new AssignmentDto
        {
            Id = a.Id,
            DepartmentId = a.DepartmentId,
            DepartmentName = deptName,
            AssignedDate = a.AssignedDate,
            RequiredAction = a.RequiredAction,
            RequiresReply = a.RequiresReply,
            ReplyDueDays = a.ReplyDueDays,
            DueDate = a.DueDate,
            ReplyStatus = a.ReplyStatus.ToString(),
            ReplyDate = a.ReplyDate,
            ReplySummary = a.ReplySummary,
            Status = a.Status.ToString(),
            IsOverdue = WorkflowHelper.IsAssignmentOverdue(a, now),
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
