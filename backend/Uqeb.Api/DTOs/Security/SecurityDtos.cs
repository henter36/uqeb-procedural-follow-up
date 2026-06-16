namespace Uqeb.Api.DTOs.Security;

public class LoginAttemptLogDto
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

public class SecurityAlertDto
{
    public int Id { get; set; }
    public string Type { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string Severity { get; set; } = string.Empty;
    public string? Username { get; set; }
    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }
    public bool IsRead { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? ReadAt { get; set; }
}

public class SecurityAlertsSummaryDto
{
    public int UnreadCount { get; set; }
    public List<SecurityAlertDto> Items { get; set; } = new();
    public int TotalCount { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
}

public class LoginAttemptsPageDto
{
    public List<LoginAttemptLogDto> Items { get; set; } = new();
    public int TotalCount { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
}

public class LoginAttemptFilterRequest
{
    public bool? Succeeded { get; set; }
    public string? Username { get; set; }
    public string? IpAddress { get; set; }
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 50;
}

public class SecurityAlertFilterRequest
{
    public bool? IsRead { get; set; }
    public string? Severity { get; set; }
    public string? Type { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 50;
}
