using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Uqeb.Api.Configuration;
using Uqeb.Api.Data;
using Uqeb.Api.DTOs.FollowUpPrint;
using Uqeb.Api.Exceptions;
using Uqeb.Api.Helpers;
using Uqeb.Api.Models.Entities;
using Uqeb.Api.Models.Enums;

namespace Uqeb.Api.Services;

public interface IFollowUpLetterPrintRecordService
{
    Task<FollowUpLetterPrintRecordDto?> ConfirmPrintAsync(int recordId, ICurrentUserService currentUser, CancellationToken cancellationToken = default);
    Task<FollowUpLetterPrintRecordDto?> CancelRecordAsync(int recordId, string? reason, ICurrentUserService currentUser, CancellationToken cancellationToken = default);
    Task<FollowUpLetterPrintRecordDto?> LinkToFollowUpAsync(int recordId, int followUpId, ICurrentUserService currentUser, CancellationToken cancellationToken = default);
    Task<FollowUpPrintPendingSummaryDto> GetPendingSummaryAsync(CancellationToken cancellationToken = default);
    Task<List<FollowUpLetterPrintRecordDto>> GetPendingListAsync(int? page = null, int? pageSize = null, CancellationToken cancellationToken = default);
    Task<FollowUpLetterPrintRecordDto> ReprintAsync(int recordId, string? idempotencyKey, ICurrentUserService currentUser, CancellationToken cancellationToken = default);
    Task<FollowUpLetterPrintRecordDto> RegisterDirectPrintRequestAsync(int transactionId, CreateDirectPrintRequest request, ICurrentUserService currentUser, CancellationToken cancellationToken = default);
}

public sealed class FollowUpLetterPrintRecordService : IFollowUpLetterPrintRecordService
{
    private readonly AppDbContext _db;
    private readonly IFollowUpLetterTimeZone _timeZone;
    private readonly FollowUpLettersOptions _options;
    private readonly IAuditService _audit;

    public FollowUpLetterPrintRecordService(
        AppDbContext db,
        IFollowUpLetterTimeZone timeZone,
        IOptions<FollowUpLettersOptions> options,
        IAuditService audit)
    {
        _db = db;
        _timeZone = timeZone;
        _options = options.Value;
        _audit = audit;
    }

    public async Task<FollowUpLetterPrintRecordDto?> ConfirmPrintAsync(
        int recordId,
        ICurrentUserService currentUser,
        CancellationToken cancellationToken = default)
    {
        var record = await _db.FollowUpLetterPrintRecords.FirstOrDefaultAsync(r => r.Id == recordId, cancellationToken);
        if (record == null)
            return null;
        if (record.IsCancelled)
            throw new FollowUpPrintConflictException("لا يمكن تأكيد سجل طباعة ملغى.");
        if (record.PrintConfirmedAt.HasValue)
            return await MapRecordAsync(recordId, cancellationToken);

        record.PrintConfirmedAt = DateTime.UtcNow;
        record.PrintConfirmedById = currentUser.UserId;
        await _db.SaveChangesAsync(cancellationToken);
        await FollowUpPrintAuditWriter.LogPrintConfirmedAsync(_audit, currentUser.UserId, recordId, record.TransactionId);
        return await MapRecordAsync(recordId, cancellationToken);
    }

    public async Task<FollowUpLetterPrintRecordDto?> CancelRecordAsync(
        int recordId,
        string? reason,
        ICurrentUserService currentUser,
        CancellationToken cancellationToken = default)
    {
        var record = await _db.FollowUpLetterPrintRecords.FirstOrDefaultAsync(r => r.Id == recordId, cancellationToken);
        if (record == null)
            return null;

        if (record.RegisteredFollowUpId.HasValue)
            throw new FollowUpPrintConflictException("لا يمكن إلغاء سجل مطبوع مرتبط بتعقيب مسجل.");
        if (record.IsCancelled)
            return await MapRecordAsync(recordId, cancellationToken);

        record.IsCancelled = true;
        record.CancelledAt = DateTime.UtcNow;
        record.CancelledById = currentUser.UserId;
        record.CancellationReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        await _db.SaveChangesAsync(cancellationToken);
        await FollowUpPrintAuditWriter.LogPrintCancelledAsync(_audit, currentUser.UserId, recordId, record.TransactionId, record.CancellationReason);
        return await MapRecordAsync(recordId, cancellationToken);
    }

    public async Task<FollowUpLetterPrintRecordDto?> LinkToFollowUpAsync(
        int recordId,
        int followUpId,
        ICurrentUserService currentUser,
        CancellationToken cancellationToken = default)
    {
        var record = await _db.FollowUpLetterPrintRecords.FirstOrDefaultAsync(r => r.Id == recordId, cancellationToken);
        if (record == null)
            return null;
        if (record.IsCancelled)
            throw new FollowUpPrintConflictException("لا يمكن ربط سجل طباعة ملغى.");
        if (record.RegisteredFollowUpId == followUpId)
            return await MapRecordAsync(recordId, cancellationToken);
        if (record.RegisteredFollowUpId.HasValue)
            throw new FollowUpPrintConflictException("سجل الطباعة مرتبط بتعقيب آخر.");

        var alreadyLinked = await _db.FollowUpLetterPrintRecords
            .AsNoTracking()
            .AnyAsync(
                r => r.RegisteredFollowUpId == followUpId && r.Id != recordId,
                cancellationToken);

        if (alreadyLinked)
            throw new FollowUpPrintConflictException("التعقيب مرتبط بسجل طباعة آخر بالفعل.");

        var followUp = await _db.FollowUps.AsNoTracking()
            .FirstOrDefaultAsync(f => f.Id == followUpId && f.TransactionId == record.TransactionId, cancellationToken);

        if (followUp == null)
            throw new FollowUpPrintValidationException("التعقيب غير موجود أو لا ينتمي إلى نفس المعاملة.");

        record.RegisteredFollowUpId = followUpId;
        record.RegisteredAt = DateTime.UtcNow;
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (SqlExceptionHelper.IsDuplicateKey(ex, "IX_FollowUpLetterPrintRecords_RegisteredFollowUpId_Linked"))
        {
            throw new FollowUpPrintConflictException("التعقيب مرتبط بسجل طباعة آخر بالفعل.");
        }

        await FollowUpPrintAuditWriter.LogLinkedToFollowUpAsync(_audit, currentUser.UserId, recordId, record.TransactionId, followUpId);
        return await MapRecordAsync(recordId, cancellationToken);
    }

    public async Task<FollowUpPrintPendingSummaryDto> GetPendingSummaryAsync(CancellationToken cancellationToken = default)
    {
        var today = _timeZone.TodayDisplayDate;
        var exclusionCutoff = today.AddDays(-_options.PrintedLetterExclusionDays);

        var pending = await _db.FollowUpLetterPrintRecords.AsNoTracking()
            .Where(r => !r.IsCancelled && r.RegisteredFollowUpId == null)
            .Select(r => r.PrintRequestedAt)
            .ToListAsync(cancellationToken);

        var within = pending.Count(at => _timeZone.ToDisplayTime(at).Date >= exclusionCutoff.Date);
        return new FollowUpPrintPendingSummaryDto
        {
            Total = pending.Count,
            WithinExclusionDays = within,
            OlderThanExclusionDays = pending.Count - within,
        };
    }

    public async Task<List<FollowUpLetterPrintRecordDto>> GetPendingListAsync(
        int? page = null,
        int? pageSize = null,
        CancellationToken cancellationToken = default)
    {
        IQueryable<FollowUpLetterPrintRecord> query = _db.FollowUpLetterPrintRecords.AsNoTracking()
            .Where(r => !r.IsCancelled && r.RegisteredFollowUpId == null);

        query = query.OrderByDescending(r => r.PrintRequestedAt);

        if (page.HasValue && pageSize.HasValue)
        {
            var safePage = Math.Max(page.Value, 1);
            var safeSize = Math.Clamp(pageSize.Value, 1, 100);
            query = query.Skip((safePage - 1) * safeSize).Take(safeSize);
        }

        return await query.Select(MapExpr).ToListAsync(cancellationToken);
    }

    public async Task<FollowUpLetterPrintRecordDto> ReprintAsync(
        int recordId,
        string? idempotencyKey,
        ICurrentUserService currentUser,
        CancellationToken cancellationToken = default)
    {
        idempotencyKey = NormalizeIdempotencyKey(idempotencyKey, required: false);
        var requestHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes($"reprint:{recordId}")));
        if (!string.IsNullOrWhiteSpace(idempotencyKey))
        {
            var existing = await TryResolveExistingRecordAsync(
                currentUser.UserId,
                FollowUpPrintOperations.ReprintRecord,
                idempotencyKey,
                requestHash,
                cancellationToken);
            if (existing != null)
                return existing;
        }

        var source = await _db.FollowUpLetterPrintRecords.AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == recordId, cancellationToken)
            ?? throw new FollowUpPrintNotFoundException("سجل الطباعة غير موجود.");

        if (source.IsCancelled)
            throw new FollowUpPrintConflictException("لا يمكن إعادة طباعة سجل ملغى.");

        var now = DateTime.UtcNow;
        var reprint = new FollowUpLetterPrintRecord
        {
            TransactionId = source.TransactionId,
            TargetDepartmentId = source.TargetDepartmentId,
            TargetEntityId = source.TargetEntityId,
            TargetEntityNameSnapshot = source.TargetEntityNameSnapshot,
            TemplateId = source.TemplateId,
            FollowUpSequence = source.FollowUpSequence,
            ResponseDeadlineDays = source.ResponseDeadlineDays,
            PrintRequestedAt = now,
            PrintRequestedById = currentUser.UserId,
            ReprintOfId = source.Id,
            CreatedAt = now,
        };

        var transaction = await BeginTransactionIfRelationalAsync(cancellationToken);
        _db.FollowUpLetterPrintRecords.Add(reprint);
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
            if (!string.IsNullOrWhiteSpace(idempotencyKey))
            {
                _db.FollowUpPrintIdempotencyKeys.Add(new FollowUpPrintIdempotencyKey
                {
                    UserId = currentUser.UserId,
                    Key = idempotencyKey,
                    Operation = FollowUpPrintOperations.ReprintRecord,
                    RequestHash = requestHash,
                    ResultId = reprint.Id,
                    CreatedAt = now,
                });
                await _db.SaveChangesAsync(cancellationToken);
            }

            if (transaction != null)
                await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (SqlExceptionHelper.IsDuplicateKey(ex) && !string.IsNullOrWhiteSpace(idempotencyKey))
        {
            if (transaction != null)
                await transaction.RollbackAsync(cancellationToken);
            _db.ChangeTracker.Clear();

            var racedRecord = await TryResolveExistingRecordAsync(
                currentUser.UserId,
                FollowUpPrintOperations.ReprintRecord,
                idempotencyKey,
                requestHash,
                cancellationToken);
            if (racedRecord != null)
                return racedRecord;

            throw;
        }
        finally
        {
            if (transaction != null)
                await transaction.DisposeAsync();
        }
        await FollowUpPrintAuditWriter.LogReprintedAsync(_audit, currentUser.UserId, reprint.Id, reprint.TransactionId, $"sourceId={recordId}");
        return (await MapRecordAsync(reprint.Id, cancellationToken))!;
    }

    public Task<FollowUpLetterPrintRecordDto> ReprintAsync(
        int recordId,
        ICurrentUserService currentUser,
        CancellationToken cancellationToken = default) =>
        ReprintAsync(recordId, null, currentUser, cancellationToken);

    public async Task<FollowUpLetterPrintRecordDto> RegisterDirectPrintRequestAsync(
        int transactionId,
        CreateDirectPrintRequest request,
        ICurrentUserService currentUser,
        CancellationToken cancellationToken = default)
    {
        var idempotencyKey = NormalizeIdempotencyKey(request.IdempotencyKey, required: true)!;
        request.IdempotencyKey = idempotencyKey;

        var requestHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(System.Text.Json.JsonSerializer.Serialize(new
            {
                transactionId,
                request.TemplateId,
                request.TargetDepartmentId,
                request.TargetEntityId,
                targetEntityName = request.TargetEntityName?.Trim(),
                request.FollowUpSequence,
                request.ResponseDeadlineDays,
            }))));

        var existing = await TryResolveExistingRecordAsync(
            currentUser.UserId,
            FollowUpPrintOperations.DirectPrintRequest,
            idempotencyKey,
            requestHash,
            cancellationToken);
        if (existing != null)
            return existing;

        var transaction = await _db.Transactions.AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == transactionId, cancellationToken)
            ?? throw new FollowUpPrintNotFoundException("المعاملة غير موجودة.");

        var template = request.TemplateId.HasValue
            ? await _db.LetterTemplates.AsNoTracking().FirstOrDefaultAsync(
                t => t.Id == request.TemplateId.Value &&
                     t.TemplateType == LetterTemplateType.FollowUp &&
                     t.IsActive,
                cancellationToken)
            : await _db.LetterTemplates.AsNoTracking().FirstOrDefaultAsync(
                t => t.TemplateType == LetterTemplateType.FollowUp &&
                     t.IsDefault &&
                     t.IsActive,
                cancellationToken);
        if (template == null)
            throw new FollowUpPrintValidationException("قالب خطاب التعقيب غير موجود أو غير نشط.");

        var targetName = await ResolveDirectTargetNameAsync(request, cancellationToken);
        if (string.IsNullOrWhiteSpace(targetName))
            throw new FollowUpPrintValidationException("جهة الطباعة غير موجودة.");

        var now = DateTime.UtcNow;
        var record = new FollowUpLetterPrintRecord
        {
            TransactionId = transaction.Id,
            TargetDepartmentId = request.TargetDepartmentId,
            TargetEntityId = request.TargetEntityId,
            TargetEntityNameSnapshot = targetName,
            TemplateId = template.Id,
            FollowUpSequence = request.FollowUpSequence,
            ResponseDeadlineDays = request.ResponseDeadlineDays,
            PrintRequestedAt = now,
            PrintRequestedById = currentUser.UserId,
            CreatedAt = now,
        };

        var dbTransaction = await BeginTransactionIfRelationalAsync(cancellationToken);
        _db.FollowUpLetterPrintRecords.Add(record);
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
            _db.FollowUpPrintIdempotencyKeys.Add(new FollowUpPrintIdempotencyKey
            {
                UserId = currentUser.UserId,
                Key = idempotencyKey,
                Operation = FollowUpPrintOperations.DirectPrintRequest,
                RequestHash = requestHash,
                ResultId = record.Id,
                CreatedAt = now,
            });
            await _db.SaveChangesAsync(cancellationToken);
            if (dbTransaction != null)
                await dbTransaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (SqlExceptionHelper.IsDuplicateKey(ex))
        {
            if (dbTransaction != null)
                await dbTransaction.RollbackAsync(cancellationToken);
            _db.ChangeTracker.Clear();

            var racedRecord = await TryResolveExistingRecordAsync(
                currentUser.UserId,
                FollowUpPrintOperations.DirectPrintRequest,
                idempotencyKey,
                requestHash,
                cancellationToken);
            if (racedRecord != null)
                return racedRecord;

            throw;
        }
        finally
        {
            if (dbTransaction != null)
                await dbTransaction.DisposeAsync();
        }

        await FollowUpPrintAuditWriter.LogDirectPrintRequestedAsync(_audit, currentUser.UserId, record.Id, record.TransactionId, "direct-print-request");
        return (await MapRecordAsync(record.Id, cancellationToken))!;
    }

    private async Task<FollowUpLetterPrintRecordDto?> TryResolveExistingRecordAsync(
        int userId,
        string operation,
        string idempotencyKey,
        string requestHash,
        CancellationToken cancellationToken)
    {
        var existingKey = await _db.FollowUpPrintIdempotencyKeys.AsNoTracking()
            .FirstOrDefaultAsync(
                k => k.UserId == userId &&
                     k.Operation == operation &&
                     k.Key == idempotencyKey,
                cancellationToken);
        if (existingKey == null)
            return null;
        if (!string.Equals(existingKey.RequestHash, requestHash, StringComparison.OrdinalIgnoreCase))
            throw new FollowUpPrintConflictException("مفتاح idempotency مستخدم لطلب مختلف.");
        return existingKey.ResultId.HasValue
            ? await MapRecordAsync(existingKey.ResultId.Value, cancellationToken)
            : null;
    }

    private async Task<string?> ResolveDirectTargetNameAsync(CreateDirectPrintRequest request, CancellationToken cancellationToken)
    {
        if (request.TargetDepartmentId.HasValue)
            return await _db.Departments.AsNoTracking()
                .Where(d => d.Id == request.TargetDepartmentId.Value)
                .Select(d => d.Name)
                .FirstOrDefaultAsync(cancellationToken);

        if (request.TargetEntityId.HasValue)
            return await _db.ExternalParties.AsNoTracking()
                .Where(p => p.Id == request.TargetEntityId.Value)
                .Select(p => p.Name)
                .FirstOrDefaultAsync(cancellationToken);

        return string.IsNullOrWhiteSpace(request.TargetEntityName)
            ? null
            : request.TargetEntityName.Trim();
    }

    private async Task<Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction?> BeginTransactionIfRelationalAsync(CancellationToken cancellationToken) =>
        _db.Database.IsRelational()
            ? await _db.Database.BeginTransactionAsync(cancellationToken)
            : null;

    private static string? NormalizeIdempotencyKey(string? value, bool required)
    {
        var key = value?.Trim();
        if (string.IsNullOrWhiteSpace(key))
        {
            if (required)
                throw new FollowUpPrintValidationException("مفتاح idempotency مطلوب.");
            return null;
        }
        if (key.Length > 128)
            throw new FollowUpPrintValidationException("مفتاح idempotency يجب ألا يتجاوز 128 حرفًا.");
        return key;
    }

    private async Task<FollowUpLetterPrintRecordDto?> MapRecordAsync(int recordId, CancellationToken cancellationToken) =>
        await _db.FollowUpLetterPrintRecords.AsNoTracking()
            .Where(r => r.Id == recordId)
            .Select(MapExpr)
            .FirstOrDefaultAsync(cancellationToken);

    private static readonly System.Linq.Expressions.Expression<Func<FollowUpLetterPrintRecord, FollowUpLetterPrintRecordDto>> MapExpr = r => new FollowUpLetterPrintRecordDto
    {
        Id = r.Id,
        TransactionId = r.TransactionId,
        IncomingNumber = r.Transaction.IncomingNumber,
        Subject = r.Transaction.Subject,
        TargetDepartmentId = r.TargetDepartmentId,
        TargetEntityId = r.TargetEntityId,
        TargetEntityNameSnapshot = r.TargetEntityNameSnapshot,
        TemplateId = r.TemplateId,
        FollowUpSequence = r.FollowUpSequence,
        ResponseDeadlineDays = r.ResponseDeadlineDays,
        PrintRequestedAt = r.PrintRequestedAt,
        PrintConfirmedAt = r.PrintConfirmedAt,
        RegisteredFollowUpId = r.RegisteredFollowUpId,
        IsCancelled = r.IsCancelled,
        ReprintOfId = r.ReprintOfId,
    };
}
