using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Uqeb.Api.Data;
using Uqeb.Api.DTOs.RecurringTemplates;
using Uqeb.Api.Helpers;
using Uqeb.Api.Models.Entities;
using Uqeb.Api.Models.Enums;

namespace Uqeb.Api.Services;

public interface IRecurringTransactionTemplateService
{
    Task<List<RecurringTemplateListItemDto>> GetAllAsync();
    Task<RecurringTemplateDetailDto?> GetByIdAsync(int id);
    Task<RecurringTemplateDetailDto> CreateAsync(CreateRecurringTemplateRequest request, int userId);
    Task<RecurringTemplateDetailDto?> UpdateAsync(int id, CreateRecurringTemplateRequest request, int userId);
    Task<RecurringTemplateDetailDto?> PauseAsync(int id, int userId);
    Task<RecurringTemplateDetailDto?> ResumeAsync(int id, int userId);
    Task<RecurringTemplateDetailDto?> TerminateAsync(int id, TerminateRecurringTemplateRequest request, int userId);
    Task<GenerateRecurringTransactionResponse?> GenerateAsync(int id, GenerateRecurringTransactionRequest request, int userId);
    Task<List<RecurringTemplateTransactionItemDto>?> GetTransactionsAsync(int id);
}

public class RecurringTransactionTemplateService : IRecurringTransactionTemplateService
{
    private const string TemplateEntityName = "RecurringTransactionTemplate";

    private readonly AppDbContext _db;
    private readonly IAuditService _audit;
    private readonly ITrackingNumberService _trackingNumbers;

    public RecurringTransactionTemplateService(AppDbContext db, IAuditService audit, ITrackingNumberService trackingNumbers)
    {
        _db = db;
        _audit = audit;
        _trackingNumbers = trackingNumbers;
    }

    public async Task<List<RecurringTemplateListItemDto>> GetAllAsync()
    {
        var templates = await _db.RecurringTransactionTemplates.AsNoTracking().ToListAsync();
        var countByTemplate = await _db.Transactions
            .AsNoTracking()
            .Where(t => t.RecurringTemplateId != null)
            .GroupBy(t => t.RecurringTemplateId!.Value)
            .Select(g => new { TemplateId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.TemplateId, g => g.Count);

        return templates
            .OrderByDescending(t => t.CreatedAt)
            .Select(t => MapToListItem(t, countByTemplate.GetValueOrDefault(t.Id)))
            .ToList();
    }

    public async Task<RecurringTemplateDetailDto?> GetByIdAsync(int id)
    {
        var template = await LoadWithDetailsAsync(id);
        if (template == null) return null;

        var count = await _db.Transactions.CountAsync(t => t.RecurringTemplateId == id);
        return MapToDetail(template, count);
    }

    public async Task<RecurringTemplateDetailDto> CreateAsync(CreateRecurringTemplateRequest request, int userId)
    {
        var errors = RecurringTemplateRequestValidator.Validate(request);
        if (errors.Count > 0)
            throw new FieldValidationException(errors);

        var template = new RecurringTransactionTemplate
        {
            Title = request.Title.Trim(),
            SubjectTemplate = request.SubjectTemplate.Trim(),
            RecurrenceType = Enum.Parse<RecurrenceType>(request.RecurrenceType!, true),
            Status = RecurringTemplateStatus.Active,
            StartDate = request.StartDate!.Value.Date,
            EndDate = request.EndDate?.Date,
            IncomingSourceType = Enum.Parse<IncomingSourceType>(request.IncomingSourceType!, true),
            IncomingFromPartyId = request.IncomingFromPartyId,
            IncomingFromDepartmentId = request.IncomingFromDepartmentId,
            CategoryId = request.CategoryId!.Value,
            Priority = Enum.Parse<Priority>(request.Priority!, true),
            ResponseType = Enum.Parse<ResponseType>(request.ResponseType!, true),
            RequiresResponse = request.RequiresResponse ?? false,
            DefaultRequiredAction = request.DefaultRequiredAction!.Trim(),
            DueDaysAfterPeriodEnd = request.DueDaysAfterPeriodEnd!.Value,
            DefaultReplyDueDays = request.DefaultReplyDueDays,
            Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim(),
            CreatedById = userId,
            CreatedAt = DateTime.UtcNow
        };

        var sortOrder = 0;
        foreach (var deptId in request.DepartmentIds!.Distinct())
            template.Departments.Add(new RecurringTransactionTemplateDepartment { DepartmentId = deptId, SortOrder = sortOrder++ });

        _db.RecurringTransactionTemplates.Add(template);

        await using var dbTransaction = await _db.Database.BeginTransactionAsync();
        try
        {
            await _db.SaveChangesAsync();

            _audit.TrackLog(userId, AuditAction.CreateRecurringTemplate, TemplateEntityName, template.Id, null, null,
                JsonSerializer.Serialize(new { template.Title, RecurrenceType = template.RecurrenceType.ToString() }));
            await _db.SaveChangesAsync();

            await dbTransaction.CommitAsync();
        }
        catch
        {
            await dbTransaction.RollbackAsync();
            throw;
        }

        return (await GetByIdAsync(template.Id))!;
    }

    public async Task<RecurringTemplateDetailDto?> UpdateAsync(int id, CreateRecurringTemplateRequest request, int userId)
    {
        var errors = RecurringTemplateRequestValidator.Validate(request);
        if (errors.Count > 0)
            throw new FieldValidationException(errors);

        var template = await _db.RecurringTransactionTemplates
            .Include(t => t.Departments)
            .FirstOrDefaultAsync(t => t.Id == id);
        if (template == null) return null;

        var oldValue = JsonSerializer.Serialize(new { template.Title, template.SubjectTemplate, template.DueDaysAfterPeriodEnd });

        template.Title = request.Title.Trim();
        template.SubjectTemplate = request.SubjectTemplate.Trim();
        template.RecurrenceType = Enum.Parse<RecurrenceType>(request.RecurrenceType!, true);
        template.StartDate = request.StartDate!.Value.Date;
        template.EndDate = request.EndDate?.Date;
        template.IncomingSourceType = Enum.Parse<IncomingSourceType>(request.IncomingSourceType!, true);
        template.IncomingFromPartyId = request.IncomingFromPartyId;
        template.IncomingFromDepartmentId = request.IncomingFromDepartmentId;
        template.CategoryId = request.CategoryId!.Value;
        template.Priority = Enum.Parse<Priority>(request.Priority!, true);
        template.ResponseType = Enum.Parse<ResponseType>(request.ResponseType!, true);
        template.RequiresResponse = request.RequiresResponse ?? false;
        template.DefaultRequiredAction = request.DefaultRequiredAction!.Trim();
        template.DueDaysAfterPeriodEnd = request.DueDaysAfterPeriodEnd!.Value;
        template.DefaultReplyDueDays = request.DefaultReplyDueDays;
        template.Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim();
        template.UpdatedAt = DateTime.UtcNow;

        UpdateTemplateDepartments(template, id, request.DepartmentIds!);

        _audit.TrackLog(userId, AuditAction.UpdateRecurringTemplate, TemplateEntityName, id, null, oldValue,
            JsonSerializer.Serialize(new { template.Title, template.SubjectTemplate, template.DueDaysAfterPeriodEnd }));
        await _db.SaveChangesAsync();

        return await GetByIdAsync(id);
    }

    private static void UpdateTemplateDepartments(RecurringTransactionTemplate template, int templateId, List<int> requestedDepartmentIds)
    {
        var newDeptIds = requestedDepartmentIds.Distinct().ToList();

        var toRemove = template.Departments
            .Where(d => !newDeptIds.Contains(d.DepartmentId))
            .ToList();
        foreach (var dept in toRemove)
            template.Departments.Remove(dept);

        for (var sortOrder = 0; sortOrder < newDeptIds.Count; sortOrder++)
        {
            var deptId = newDeptIds[sortOrder];
            var existingDept = template.Departments.FirstOrDefault(d => d.DepartmentId == deptId);
            if (existingDept is not null)
            {
                existingDept.SortOrder = sortOrder;
            }
            else
            {
                template.Departments.Add(new RecurringTransactionTemplateDepartment
                {
                    TemplateId = templateId,
                    DepartmentId = deptId,
                    SortOrder = sortOrder
                });
            }
        }
    }

    public async Task<RecurringTemplateDetailDto?> PauseAsync(int id, int userId)
    {
        var template = await _db.RecurringTransactionTemplates.FindAsync(id);
        if (template == null) return null;

        if (template.Status == RecurringTemplateStatus.Terminated)
            throw new InvalidOperationException("لا يمكن إيقاف قالب منتهٍ.");
        if (template.Status == RecurringTemplateStatus.Paused)
            throw new InvalidOperationException("القالب موقوف بالفعل.");

        var previousStatus = template.Status;
        template.Status = RecurringTemplateStatus.Paused;
        template.PausedAt = DateTime.UtcNow;
        template.PausedById = userId;
        template.UpdatedAt = DateTime.UtcNow;

        _audit.TrackLog(userId, AuditAction.PauseRecurringTemplate, TemplateEntityName, id, null,
            previousStatus.ToString(), RecurringTemplateStatus.Paused.ToString());
        await _db.SaveChangesAsync();

        return await GetByIdAsync(id);
    }

    public async Task<RecurringTemplateDetailDto?> ResumeAsync(int id, int userId)
    {
        var template = await _db.RecurringTransactionTemplates.FindAsync(id);
        if (template == null) return null;

        if (template.Status == RecurringTemplateStatus.Terminated)
            throw new InvalidOperationException("لا يمكن إعادة تفعيل قالب منتهٍ.");
        if (template.Status == RecurringTemplateStatus.Active)
            throw new InvalidOperationException("القالب نشط بالفعل.");

        template.Status = RecurringTemplateStatus.Active;
        template.ResumedAt = DateTime.UtcNow;
        template.ResumedById = userId;
        template.UpdatedAt = DateTime.UtcNow;

        _audit.TrackLog(userId, AuditAction.ResumeRecurringTemplate, TemplateEntityName, id, null,
            RecurringTemplateStatus.Paused.ToString(), RecurringTemplateStatus.Active.ToString());
        await _db.SaveChangesAsync();

        return await GetByIdAsync(id);
    }

    public async Task<RecurringTemplateDetailDto?> TerminateAsync(int id, TerminateRecurringTemplateRequest request, int userId)
    {
        if (string.IsNullOrWhiteSpace(request.Reason))
            throw new FieldValidationException(new Dictionary<string, string> { [nameof(request.Reason)] = "سبب الإنهاء مطلوب." });

        var template = await _db.RecurringTransactionTemplates.FindAsync(id);
        if (template == null) return null;

        if (template.Status == RecurringTemplateStatus.Terminated)
            throw new InvalidOperationException("القالب منتهٍ بالفعل.");

        var previousStatus = template.Status;
        template.Status = RecurringTemplateStatus.Terminated;
        template.TerminatedAt = DateTime.UtcNow;
        template.TerminatedById = userId;
        template.TerminationReason = request.Reason.Trim();
        template.UpdatedAt = DateTime.UtcNow;

        _audit.TrackLog(userId, AuditAction.TerminateRecurringTemplate, TemplateEntityName, id, null,
            previousStatus.ToString(),
            JsonSerializer.Serialize(new { Reason = template.TerminationReason }));
        await _db.SaveChangesAsync();

        return await GetByIdAsync(id);
    }

    public async Task<GenerateRecurringTransactionResponse?> GenerateAsync(int id, GenerateRecurringTransactionRequest request, int userId)
    {
        var template = await _db.RecurringTransactionTemplates
            .Include(t => t.Departments)
            .FirstOrDefaultAsync(t => t.Id == id);
        if (template == null) return null;

        EnsureTemplateCanGenerate(template);

        var period = RecurringPeriodCalculator.Compute(template.RecurrenceType, request.PeriodKey, template.DueDaysAfterPeriodEnd);
        EnsurePeriodWithinTemplateRange(template, period);
        await EnsurePeriodNotAlreadyGeneratedAsync(id, period.PeriodKey);

        var dateErrors = RecurringTemplateRequestValidator.ValidateGenerateRequestDates(request);
        if (dateErrors.Count > 0)
            throw new FieldValidationException(dateErrors);

        var incomingDate = request.IncomingDate!.Value.Date;
        var referralDate = request.ReferralDate!.Value.Date;
        var letterNumber = string.IsNullOrWhiteSpace(request.ReferralLetterNumber) ? null : request.ReferralLetterNumber.Trim();

        var transaction = BuildGeneratedTransaction(template, period, incomingDate, referralDate, letterNumber, userId);
        AddGeneratedAssignments(transaction, template, period, referralDate, letterNumber, userId);
        transaction.InternalTrackingNumber = await _trackingNumbers.GenerateNextAsync();

        await using var dbTransaction = await _db.Database.BeginTransactionAsync();
        try
        {
            _db.Transactions.Add(transaction);
            await _db.SaveChangesAsync();

            UpdateTemplateGenerationState(template, period);
            TrackGenerateAudit(userId, template, transaction, period);
            await _db.SaveChangesAsync();

            await dbTransaction.CommitAsync();
        }
        catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex))
        {
            await dbTransaction.RollbackAsync();
            var existingId = await FindExistingGeneratedTransactionIdAsync(id, period.PeriodKey);
            throw new RecurringTemplatePeriodAlreadyGeneratedException(existingId ?? 0);
        }
        catch
        {
            await dbTransaction.RollbackAsync();
            throw;
        }

        return new GenerateRecurringTransactionResponse
        {
            TransactionId = transaction.Id,
            InternalTrackingNumber = transaction.InternalTrackingNumber,
            PeriodKey = period.PeriodKey,
            PeriodLabel = period.PeriodLabel,
            DueDate = period.DueDate
        };
    }

    private static void EnsureTemplateCanGenerate(RecurringTransactionTemplate template)
    {
        if (template.Status == RecurringTemplateStatus.Paused)
            throw new RecurringTemplatePausedException();
        if (template.Status == RecurringTemplateStatus.Terminated)
            throw new RecurringTemplateTerminatedException();
    }

    private static void EnsurePeriodWithinTemplateRange(RecurringTransactionTemplate template, RecurringPeriodCalculator.PeriodInfo period)
    {
        if (period.PeriodStart.Date < template.StartDate.Date)
            throw new RecurringTemplatePeriodOutOfRangeException();
        if (template.EndDate.HasValue && period.PeriodStart.Date > template.EndDate.Value.Date)
            throw new RecurringTemplatePeriodOutOfRangeException();
    }

    private async Task EnsurePeriodNotAlreadyGeneratedAsync(int templateId, string periodKey)
    {
        var existingId = await FindExistingGeneratedTransactionIdAsync(templateId, periodKey);
        if (existingId.HasValue)
            throw new RecurringTemplatePeriodAlreadyGeneratedException(existingId.Value);
    }

    private async Task<int?> FindExistingGeneratedTransactionIdAsync(int templateId, string periodKey) =>
        await _db.Transactions
            .AsNoTracking()
            .Where(t => t.RecurringTemplateId == templateId && t.RecurringPeriodKey == periodKey)
            .Select(t => (int?)t.Id)
            .FirstOrDefaultAsync();

    private static bool IsUniqueConstraintViolation(DbUpdateException ex) =>
        SqlExceptionHelper.IsDuplicateKey(ex, "IX_Transactions_RecurringTemplateId_RecurringPeriodKey");

    private static Transaction BuildGeneratedTransaction(
        RecurringTransactionTemplate template,
        RecurringPeriodCalculator.PeriodInfo period,
        DateTime incomingDate,
        DateTime referralDate,
        string? letterNumber,
        int userId) =>
        new()
        {
            IncomingNumber = $"REC-{template.Id}-{period.PeriodKey}",
            IncomingDate = incomingDate,
            Subject = $"{template.SubjectTemplate} - {period.PeriodLabel}",
            IncomingSourceType = template.IncomingSourceType,
            IncomingFromPartyId = template.IncomingFromPartyId,
            IncomingFromDepartmentId = template.IncomingFromDepartmentId,
            OutgoingNumber = letterNumber,
            OutgoingDate = letterNumber != null ? referralDate : null,
            RequiresResponse = template.RequiresResponse,
            ResponseType = template.ResponseType,
            ResponseDueDate = template.RequiresResponse ? period.DueDate : null,
            Priority = template.Priority,
            CategoryId = template.CategoryId,
            Notes = template.Notes,
            Status = TransactionStatus.New,
            CreatedById = userId,
            CreatedAt = DateTime.UtcNow,
            RecurringTemplateId = template.Id,
            RecurringPeriodKey = period.PeriodKey,
            RecurringPeriodLabel = period.PeriodLabel,
        };

    private static void AddGeneratedAssignments(
        Transaction transaction,
        RecurringTransactionTemplate template,
        RecurringPeriodCalculator.PeriodInfo period,
        DateTime referralDate,
        string? letterNumber,
        int userId)
    {
        var replyDueDays = template.RequiresResponse
            ? Math.Max(1, (int)(period.DueDate.Date - referralDate.Date).TotalDays)
            : (int?)null;

        foreach (var dept in template.Departments.OrderBy(d => d.SortOrder ?? int.MaxValue))
        {
            transaction.Assignments.Add(new Assignment
            {
                DepartmentId = dept.DepartmentId,
                AssignedDate = referralDate,
                LetterNumber = letterNumber,
                RequiredAction = template.DefaultRequiredAction,
                RequiresReply = template.RequiresResponse,
                ReplyDueDays = replyDueDays,
                DueDate = period.DueDate,
                ReplyStatus = ReplyStatus.Pending,
                Status = AssignmentStatus.Active,
                CreatedById = userId,
                CreatedAt = DateTime.UtcNow
            });
        }

        if (transaction.Assignments.Count > 0)
            transaction.Status = TransactionStatus.Assigned;
    }

    private static void UpdateTemplateGenerationState(RecurringTransactionTemplate template, RecurringPeriodCalculator.PeriodInfo period)
    {
        template.LastGeneratedPeriodKey = period.PeriodKey;
        template.UpdatedAt = DateTime.UtcNow;
    }

    private void TrackGenerateAudit(
        int userId,
        RecurringTransactionTemplate template,
        Transaction transaction,
        RecurringPeriodCalculator.PeriodInfo period)
    {
        _audit.TrackLog(userId, AuditAction.GenerateRecurringTransaction, TemplateEntityName, template.Id, transaction.Id,
            null,
            JsonSerializer.Serialize(new { TemplateId = template.Id, period.PeriodKey, period.PeriodLabel, TransactionId = transaction.Id }));
    }

    public async Task<List<RecurringTemplateTransactionItemDto>?> GetTransactionsAsync(int id)
    {
        var exists = await _db.RecurringTransactionTemplates.AnyAsync(t => t.Id == id);
        if (!exists) return null;

        return await _db.Transactions
            .AsNoTracking()
            .Where(t => t.RecurringTemplateId == id)
            .OrderByDescending(t => t.RecurringPeriodKey)
            .Select(t => new RecurringTemplateTransactionItemDto
            {
                TransactionId = t.Id,
                InternalTrackingNumber = t.InternalTrackingNumber,
                Subject = t.Subject,
                PeriodKey = t.RecurringPeriodKey ?? string.Empty,
                PeriodLabel = t.RecurringPeriodLabel ?? string.Empty,
                Status = t.Status.ToString(),
                IncomingDate = t.IncomingDate,
                DueDate = t.ResponseDueDate,
                ClosedAt = t.ClosedAt
            })
            .ToListAsync();
    }

    private Task<RecurringTransactionTemplate?> LoadWithDetailsAsync(int id) =>
        _db.RecurringTransactionTemplates
            .Include(t => t.Departments).ThenInclude(d => d.Department)
            .Include(t => t.CreatedBy)
            .Include(t => t.PausedBy)
            .Include(t => t.ResumedBy)
            .Include(t => t.TerminatedBy)
            .Include(t => t.IncomingFromParty)
            .Include(t => t.IncomingFromDepartment)
            .Include(t => t.CategoryEntity)
            .FirstOrDefaultAsync(t => t.Id == id);

    private static RecurringTemplateListItemDto MapToListItem(RecurringTransactionTemplate t, int generatedCount)
    {
        var nextPeriodKey = RecurringPeriodCalculator.GetNextPeriodKey(t.RecurrenceType, t.StartDate, t.LastGeneratedPeriodKey);
        return new RecurringTemplateListItemDto
        {
            Id = t.Id,
            Title = t.Title,
            RecurrenceType = t.RecurrenceType.ToString(),
            Status = t.Status.ToString(),
            StartDate = t.StartDate,
            EndDate = t.EndDate,
            NextPeriodKey = nextPeriodKey,
            NextPeriodLabel = RecurringPeriodCalculator.GetPeriodLabel(t.RecurrenceType, nextPeriodKey),
            LastGeneratedPeriodKey = t.LastGeneratedPeriodKey,
            LastGeneratedPeriodLabel = t.LastGeneratedPeriodKey != null
                ? RecurringPeriodCalculator.GetPeriodLabel(t.RecurrenceType, t.LastGeneratedPeriodKey)
                : null,
            GeneratedTransactionsCount = generatedCount
        };
    }

    private static RecurringTemplateDetailDto MapToDetail(RecurringTransactionTemplate t, int generatedCount)
    {
        var list = MapToListItem(t, generatedCount);
        return new RecurringTemplateDetailDto
        {
            Id = list.Id,
            Title = list.Title,
            RecurrenceType = list.RecurrenceType,
            Status = list.Status,
            StartDate = list.StartDate,
            EndDate = list.EndDate,
            NextPeriodKey = list.NextPeriodKey,
            NextPeriodLabel = list.NextPeriodLabel,
            LastGeneratedPeriodKey = list.LastGeneratedPeriodKey,
            LastGeneratedPeriodLabel = list.LastGeneratedPeriodLabel,
            GeneratedTransactionsCount = list.GeneratedTransactionsCount,
            SubjectTemplate = t.SubjectTemplate,
            IncomingSourceType = t.IncomingSourceType.ToString(),
            IncomingFromPartyId = t.IncomingFromPartyId,
            IncomingFromPartyName = t.IncomingFromParty?.Name,
            IncomingFromDepartmentId = t.IncomingFromDepartmentId,
            IncomingFromDepartmentName = t.IncomingFromDepartment?.Name,
            CategoryId = t.CategoryId,
            CategoryName = t.CategoryEntity?.Name,
            Priority = t.Priority.ToString(),
            ResponseType = t.ResponseType.ToString(),
            RequiresResponse = t.RequiresResponse,
            DefaultRequiredAction = t.DefaultRequiredAction,
            DueDaysAfterPeriodEnd = t.DueDaysAfterPeriodEnd,
            DefaultReplyDueDays = t.DefaultReplyDueDays,
            Notes = t.Notes,
            Departments = t.Departments
                .OrderBy(d => d.SortOrder ?? int.MaxValue)
                .Select(d => new RecurringTemplateDepartmentDto
                {
                    DepartmentId = d.DepartmentId,
                    DepartmentName = d.Department.Name,
                    SortOrder = d.SortOrder
                })
                .ToList(),
            CreatedByName = t.CreatedBy?.FullName ?? string.Empty,
            CreatedAt = t.CreatedAt,
            UpdatedAt = t.UpdatedAt,
            PausedAt = t.PausedAt,
            PausedByName = t.PausedBy?.FullName,
            ResumedAt = t.ResumedAt,
            ResumedByName = t.ResumedBy?.FullName,
            TerminatedAt = t.TerminatedAt,
            TerminatedByName = t.TerminatedBy?.FullName,
            TerminationReason = t.TerminationReason
        };
    }
}
