using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Uqeb.Api.Configuration;
using Uqeb.Api.Data;
using Uqeb.Api.DTOs.FollowUpPrint;
using Uqeb.Api.Models.Entities;

namespace Uqeb.Api.Services;

public interface IFollowUpLetterPrintRecordService
{
    Task<FollowUpLetterPrintRecordDto?> ConfirmPrintAsync(int recordId, ICurrentUserService currentUser, CancellationToken cancellationToken = default);
    Task<FollowUpLetterPrintRecordDto?> CancelRecordAsync(int recordId, string? reason, ICurrentUserService currentUser, CancellationToken cancellationToken = default);
    Task<FollowUpLetterPrintRecordDto?> LinkToFollowUpAsync(int recordId, int followUpId, ICurrentUserService currentUser, CancellationToken cancellationToken = default);
    Task<FollowUpPrintPendingSummaryDto> GetPendingSummaryAsync(CancellationToken cancellationToken = default);
    Task<List<FollowUpLetterPrintRecordDto>> GetPendingListAsync(int? page = null, int? pageSize = null, CancellationToken cancellationToken = default);
    Task<FollowUpLetterPrintRecordDto> ReprintAsync(int recordId, ICurrentUserService currentUser, CancellationToken cancellationToken = default);
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
        if (record == null || record.IsCancelled)
            return null;

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
            throw new InvalidOperationException("لا يمكن إلغاء سجل مطبوع مرتبط بتعقيب مسجل.");

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
        if (record == null || record.IsCancelled)
            return null;

        var followUp = await _db.FollowUps.AsNoTracking()
            .FirstOrDefaultAsync(f => f.Id == followUpId && f.TransactionId == record.TransactionId, cancellationToken);

        if (followUp == null)
            throw new InvalidOperationException("التعقيب غير موجود أو لا ينتمي إلى نفس المعاملة.");

        record.RegisteredFollowUpId = followUpId;
        record.RegisteredAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
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
        ICurrentUserService currentUser,
        CancellationToken cancellationToken = default)
    {
        var source = await _db.FollowUpLetterPrintRecords.AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == recordId, cancellationToken)
            ?? throw new InvalidOperationException("سجل الطباعة غير موجود.");

        if (source.IsCancelled)
            throw new InvalidOperationException("لا يمكن إعادة طباعة سجل ملغى.");

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

        _db.FollowUpLetterPrintRecords.Add(reprint);
        await _db.SaveChangesAsync(cancellationToken);
        await FollowUpPrintAuditWriter.LogReprintedAsync(_audit, currentUser.UserId, reprint.Id, reprint.TransactionId, $"sourceId={recordId}");
        return (await MapRecordAsync(reprint.Id, cancellationToken))!;
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
