using Uqeb.Api.Models.Enums;

namespace Uqeb.Api.Services;

public interface IAuditService
{
    void TrackLog(int userId, AuditAction action, string? entityName, int? entityId, int? transactionId, string? oldValue, string? newValue);

    Task LogAsync(int userId, AuditAction action, string? entityName, int? entityId, int? transactionId, string? oldValue, string? newValue);
}
