using Uqeb.Api.Data;
using Uqeb.Api.Models.Entities;
using Uqeb.Api.Models.Enums;

namespace Uqeb.Api.Services;

public class AuditService : IAuditService
{
    private readonly AppDbContext _db;

    public AuditService(AppDbContext db) => _db = db;

    public AuditLog TrackLog(int userId, AuditAction action, string? entityName, int? entityId, int? transactionId, string? oldValue, string? newValue)
    {
        var auditLog = new AuditLog
        {
            UserId = userId,
            Action = action,
            EntityName = entityName,
            EntityId = entityId,
            TransactionId = transactionId,
            OldValue = oldValue,
            NewValue = newValue,
            CreatedAt = DateTime.UtcNow
        };
        _db.AuditLogs.Add(auditLog);
        return auditLog;
    }

    public async Task LogAsync(int userId, AuditAction action, string? entityName, int? entityId, int? transactionId, string? oldValue, string? newValue)
    {
        TrackLog(userId, action, entityName, entityId, transactionId, oldValue, newValue);
        await _db.SaveChangesAsync();
    }
}
