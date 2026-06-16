namespace Uqeb.Api.Models.Entities;

public class LoginAttemptLog
{
    public int Id { get; set; }
    public string? Username { get; set; }
    public int? UserId { get; set; }
    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }
    public bool Succeeded { get; set; }
    public string? FailureReason { get; set; }
    public string RiskLevel { get; set; } = "low";
    public DateTime OccurredAt { get; set; }
}
