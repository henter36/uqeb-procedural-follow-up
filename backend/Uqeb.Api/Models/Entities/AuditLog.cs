using Uqeb.Api.Models.Enums;

namespace Uqeb.Api.Models.Entities;

public class AuditLog
{
    public int Id { get; set; }
    public int? TransactionId { get; set; }
    public int UserId { get; set; }
    public AuditAction Action { get; set; }
    public string? EntityName { get; set; }
    public int? EntityId { get; set; }
    public string? OldValue { get; set; }
    public string? NewValue { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public Transaction? Transaction { get; set; }
    public User User { get; set; } = null!;
}
