using Microsoft.AspNetCore.Http;
using Uqeb.Api.DTOs.Security;
using Uqeb.Api.Services;

namespace Uqeb.Api.Tests;

internal sealed class NoOpSecurityAuditService : ISecurityAuditService
{
    public Task RecordLoginAttemptAsync(
        string? username,
        int? userId,
        bool succeeded,
        string? failureReason,
        HttpContext httpContext) => Task.CompletedTask;

    public Task EvaluateLoginRiskAsync(string? username, string? ipAddress, string? userAgent) =>
        Task.CompletedTask;

    public Task CreateSecurityAlertAsync(
        string type,
        string title,
        string message,
        string severity,
        string? username,
        string? ipAddress,
        string? userAgent) => Task.CompletedTask;

    public Task RecordUnauthorizedAccessAsync(HttpContext httpContext, int statusCode, string? reason) =>
        Task.CompletedTask;

    public Task<LoginAttemptsPageDto> GetRecentLoginAttemptsAsync(LoginAttemptFilterRequest filter) =>
        Task.FromResult(new LoginAttemptsPageDto());

    public Task<SecurityAlertsSummaryDto> GetSecurityAlertsAsync(SecurityAlertFilterRequest filter) =>
        Task.FromResult(new SecurityAlertsSummaryDto());

    public Task<bool> MarkAlertAsReadAsync(int id) => Task.FromResult(false);

    public Task<int> MarkAllAlertsAsReadAsync() => Task.FromResult(0);
}
