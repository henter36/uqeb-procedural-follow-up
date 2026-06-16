using Microsoft.EntityFrameworkCore;
using Uqeb.Api.Data;
using Uqeb.Api.DTOs.Security;
using Uqeb.Api.Helpers;
using Uqeb.Api.Models.Entities;

namespace Uqeb.Api.Services;

public interface ISecurityAuditService
{
    Task RecordLoginAttemptAsync(string? username, int? userId, bool succeeded, string? failureReason, HttpContext httpContext);
    Task EvaluateLoginRiskAsync(string? username, string? ipAddress, string? userAgent);
    Task CreateSecurityAlertAsync(string type, string title, string message, string severity, string? username, string? ipAddress, string? userAgent);
    Task RecordUnauthorizedAccessAsync(HttpContext httpContext, int statusCode, string? reason);
    Task<LoginAttemptsPageDto> GetRecentLoginAttemptsAsync(LoginAttemptFilterRequest filter);
    Task<SecurityAlertsSummaryDto> GetSecurityAlertsAsync(SecurityAlertFilterRequest filter);
    Task<bool> MarkAlertAsReadAsync(int id);
    Task<int> MarkAllAlertsAsReadAsync();
}

public class SecurityAuditService : ISecurityAuditService
{
    private const int WindowMinutes = 10;
    private const int UsernameFailThreshold = 5;
    private const int IpFailThreshold = 10;
    private const int SprayUsernameThreshold = 5;
    private const int UnauthorizedThreshold = 5;

    private readonly AppDbContext _db;

    public SecurityAuditService(AppDbContext db) => _db = db;

    public async Task RecordLoginAttemptAsync(
        string? username,
        int? userId,
        bool succeeded,
        string? failureReason,
        HttpContext httpContext)
    {
        if (!userId.HasValue && succeeded && !string.IsNullOrWhiteSpace(username))
        {
            userId = await _db.Users.AsNoTracking()
                .Where(u => u.Username == username)
                .Select(u => (int?)u.Id)
                .FirstOrDefaultAsync();
        }

        var ip = HttpContextSecurityHelper.GetClientIpAddress(httpContext);
        var userAgent = HttpContextSecurityHelper.GetUserAgent(httpContext);
        var riskLevel = succeeded ? "low" : "medium";

        var log = new LoginAttemptLog
        {
            Username = string.IsNullOrWhiteSpace(username) ? null : username.Trim(),
            UserId = userId,
            IpAddress = ip,
            UserAgent = Truncate(userAgent, 512),
            Succeeded = succeeded,
            FailureReason = succeeded ? null : failureReason,
            RiskLevel = riskLevel,
            OccurredAt = DateTime.UtcNow
        };

        _db.LoginAttemptLogs.Add(log);
        await _db.SaveChangesAsync();

        if (!succeeded)
            await EvaluateLoginRiskAsync(log.Username, ip, userAgent);
    }

    public async Task EvaluateLoginRiskAsync(string? username, string? ipAddress, string? userAgent)
    {
        if (string.IsNullOrWhiteSpace(ipAddress))
            return;

        var since = DateTime.UtcNow.AddMinutes(-WindowMinutes);

        if (!string.IsNullOrWhiteSpace(username))
        {
            var userFails = await _db.LoginAttemptLogs.AsNoTracking()
                .CountAsync(l => !l.Succeeded
                    && l.Username == username
                    && l.FailureReason == "invalid_credentials"
                    && l.OccurredAt >= since);

            if (userFails >= UsernameFailThreshold
                && !await HasRecentAlertAsync("account_bruteforce", username, ipAddress, since))
            {
                await CreateSecurityAlertAsync(
                    "account_bruteforce",
                    "محاولات دخول متكررة فاشلة",
                    $"تم رصد {userFails} محاولات دخول فاشلة للمستخدم «{username}» خلال {WindowMinutes} دقائق.",
                    "high",
                    username,
                    ipAddress,
                    userAgent);
            }
        }

        var ipFails = await _db.LoginAttemptLogs.AsNoTracking()
            .CountAsync(l => !l.Succeeded
                && l.IpAddress == ipAddress
                && l.FailureReason == "invalid_credentials"
                && l.OccurredAt >= since);

        if (ipFails >= IpFailThreshold
            && !await HasRecentAlertAsync("failed_login_burst", null, ipAddress, since))
        {
            await CreateSecurityAlertAsync(
                "failed_login_burst",
                "ارتفاع محاولات الدخول الفاشلة",
                $"تم رصد {ipFails} محاولات دخول فاشلة من العنوان {ipAddress} خلال {WindowMinutes} دقائق.",
                "high",
                username,
                ipAddress,
                userAgent);
        }

        var distinctUsernames = await _db.LoginAttemptLogs.AsNoTracking()
            .Where(l => !l.Succeeded
                && l.IpAddress == ipAddress
                && l.FailureReason == "invalid_credentials"
                && l.OccurredAt >= since
                && l.Username != null)
            .Select(l => l.Username!)
            .Distinct()
            .CountAsync();

        if (distinctUsernames > SprayUsernameThreshold
            && !await HasRecentAlertAsync("ip_password_spray", null, ipAddress, since))
        {
            await CreateSecurityAlertAsync(
                "ip_password_spray",
                "محاولة تخمين أسماء مستخدمين",
                $"تم رصد {distinctUsernames} أسماء مستخدمين مختلفة من العنوان {ipAddress} خلال {WindowMinutes} دقائق.",
                "critical",
                username,
                ipAddress,
                userAgent);
        }
    }

    public async Task CreateSecurityAlertAsync(
        string type,
        string title,
        string message,
        string severity,
        string? username,
        string? ipAddress,
        string? userAgent)
    {
        _db.SecurityAlerts.Add(new SecurityAlert
        {
            Type = type,
            Title = title,
            Message = message,
            Severity = severity,
            Username = username,
            IpAddress = ipAddress,
            UserAgent = Truncate(userAgent, 512),
            IsRead = false,
            CreatedAt = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();
    }

    public async Task RecordUnauthorizedAccessAsync(HttpContext httpContext, int statusCode, string? reason)
    {
        var path = httpContext.Request.Path.Value ?? string.Empty;
        if (path.StartsWith("/api/auth/login", StringComparison.OrdinalIgnoreCase))
            return;

        var ip = HttpContextSecurityHelper.GetClientIpAddress(httpContext);
        var userAgent = HttpContextSecurityHelper.GetUserAgent(httpContext);
        var userId = HttpContextSecurityHelper.GetUserId(httpContext);
        var username = httpContext.User.Identity?.Name;
        var failureReason = statusCode == 401 ? "unauthorized_access" : "forbidden_access";

        _db.LoginAttemptLogs.Add(new LoginAttemptLog
        {
            Username = username,
            UserId = userId,
            IpAddress = ip,
            UserAgent = Truncate(userAgent, 512),
            Succeeded = false,
            FailureReason = failureReason,
            RiskLevel = statusCode == 403 ? "high" : "medium",
            OccurredAt = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();

        if (statusCode == 403 && HttpContextSecurityHelper.IsAdminProbePath(httpContext.Request.Path)
            && !await HasRecentAlertAsync("admin_endpoint_probe", username, ip, DateTime.UtcNow.AddMinutes(-WindowMinutes)))
        {
            await CreateSecurityAlertAsync(
                "admin_endpoint_probe",
                "محاولة وصول غير مصرح لمسار إداري",
                $"محاولة وصول مرفوضة إلى {path} من العنوان {ip}.",
                "high",
                username,
                ip,
                userAgent);
            return;
        }

        var since = DateTime.UtcNow.AddMinutes(-WindowMinutes);
        var unauthorizedCount = await _db.LoginAttemptLogs.AsNoTracking()
            .CountAsync(l => l.IpAddress == ip
                && l.OccurredAt >= since
                && (l.FailureReason == "unauthorized_access" || l.FailureReason == "forbidden_access"));

        if (unauthorizedCount >= UnauthorizedThreshold
            && !await HasRecentAlertAsync("unauthorized_access", null, ip, since))
        {
            var severity = unauthorizedCount >= 10 ? "high" : "medium";
            await CreateSecurityAlertAsync(
                "unauthorized_access",
                "محاولات وصول غير مصرح بها",
                $"تم رصد {unauthorizedCount} محاولات وصول غير مصرح بها (401/403) من العنوان {ip} خلال {WindowMinutes} دقائق.",
                severity,
                username,
                ip,
                userAgent);
        }
    }

    public async Task<LoginAttemptsPageDto> GetRecentLoginAttemptsAsync(LoginAttemptFilterRequest filter)
    {
        var page = Math.Max(1, filter.Page);
        var pageSize = Math.Clamp(filter.PageSize, 1, 200);
        var query = _db.LoginAttemptLogs.AsNoTracking();

        if (filter.Succeeded.HasValue)
            query = query.Where(l => l.Succeeded == filter.Succeeded.Value);
        if (!string.IsNullOrWhiteSpace(filter.Username))
            query = query.Where(l => l.Username != null && l.Username.Contains(filter.Username));
        if (!string.IsNullOrWhiteSpace(filter.IpAddress))
            query = query.Where(l => l.IpAddress != null && l.IpAddress.Contains(filter.IpAddress));
        if (filter.From.HasValue)
            query = query.Where(l => l.OccurredAt >= filter.From.Value);
        if (filter.To.HasValue)
            query = query.Where(l => l.OccurredAt <= filter.To.Value);

        var total = await query.CountAsync();
        var items = await query
            .OrderByDescending(l => l.OccurredAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(l => new LoginAttemptLogDto
            {
                Id = l.Id,
                Username = l.Username,
                UserId = l.UserId,
                IpAddress = l.IpAddress,
                UserAgent = l.UserAgent,
                Succeeded = l.Succeeded,
                FailureReason = l.FailureReason,
                RiskLevel = l.RiskLevel,
                OccurredAt = l.OccurredAt
            })
            .ToListAsync();

        return new LoginAttemptsPageDto
        {
            Items = items,
            TotalCount = total,
            Page = page,
            PageSize = pageSize
        };
    }

    public async Task<SecurityAlertsSummaryDto> GetSecurityAlertsAsync(SecurityAlertFilterRequest filter)
    {
        var page = Math.Max(1, filter.Page);
        var pageSize = Math.Clamp(filter.PageSize, 1, 200);
        var query = _db.SecurityAlerts.AsNoTracking();

        if (filter.IsRead.HasValue)
            query = query.Where(a => a.IsRead == filter.IsRead.Value);
        if (!string.IsNullOrWhiteSpace(filter.Severity))
            query = query.Where(a => a.Severity == filter.Severity);
        if (!string.IsNullOrWhiteSpace(filter.Type))
            query = query.Where(a => a.Type == filter.Type);

        var unreadCount = await _db.SecurityAlerts.AsNoTracking().CountAsync(a => !a.IsRead);
        var total = await query.CountAsync();
        var items = await query
            .OrderByDescending(a => a.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(a => new SecurityAlertDto
            {
                Id = a.Id,
                Type = a.Type,
                Title = a.Title,
                Message = a.Message,
                Severity = a.Severity,
                Username = a.Username,
                IpAddress = a.IpAddress,
                UserAgent = a.UserAgent,
                IsRead = a.IsRead,
                CreatedAt = a.CreatedAt,
                ReadAt = a.ReadAt
            })
            .ToListAsync();

        return new SecurityAlertsSummaryDto
        {
            UnreadCount = unreadCount,
            Items = items,
            TotalCount = total,
            Page = page,
            PageSize = pageSize
        };
    }

    public async Task<bool> MarkAlertAsReadAsync(int id)
    {
        var alert = await _db.SecurityAlerts.FirstOrDefaultAsync(a => a.Id == id);
        if (alert == null) return false;
        if (!alert.IsRead)
        {
            alert.IsRead = true;
            alert.ReadAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
        }
        return true;
    }

    public async Task<int> MarkAllAlertsAsReadAsync()
    {
        var now = DateTime.UtcNow;
        var unread = await _db.SecurityAlerts.Where(a => !a.IsRead).ToListAsync();
        foreach (var alert in unread)
        {
            alert.IsRead = true;
            alert.ReadAt = now;
        }
        await _db.SaveChangesAsync();
        return unread.Count;
    }

    private async Task<bool> HasRecentAlertAsync(string type, string? username, string? ipAddress, DateTime since) =>
        await _db.SecurityAlerts.AsNoTracking().AnyAsync(a =>
            a.Type == type
            && a.CreatedAt >= since
            && (username == null || a.Username == username)
            && (ipAddress == null || a.IpAddress == ipAddress));

    private static string? Truncate(string? value, int maxLength)
    {
        if (string.IsNullOrEmpty(value)) return value;
        return value.Length <= maxLength ? value : value[..maxLength];
    }
}
