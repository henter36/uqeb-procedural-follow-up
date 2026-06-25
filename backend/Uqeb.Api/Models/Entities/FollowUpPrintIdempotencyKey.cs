namespace Uqeb.Api.Models.Entities;

public class FollowUpPrintIdempotencyKey
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public string Key { get; set; } = string.Empty;
    public string Operation { get; set; } = string.Empty;
    public int? ResultId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public User User { get; set; } = null!;
}
