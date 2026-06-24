using Microsoft.EntityFrameworkCore;
using Uqeb.Api.Data;
using Uqeb.Api.Models.Enums;

namespace Uqeb.Api.Reporting.Operations;

public enum ReportingPhase1AdminUserResolutionStatus
{
    Success,
    NotFound,
    Duplicate,
    Inactive,
    NotAdmin,
}

public sealed record ReportingPhase1AdminUserResolution(
    ReportingPhase1AdminUserResolutionStatus Status,
    int? UserId = null,
    string? Detail = null);

public interface IReportingPhase1AdminUserResolver
{
    Task<ReportingPhase1AdminUserResolution> ResolveAsync(string adminUsername, CancellationToken cancellationToken = default);
}

public sealed class ReportingPhase1AdminUserResolver(AppDbContext db) : IReportingPhase1AdminUserResolver
{
    public async Task<ReportingPhase1AdminUserResolution> ResolveAsync(
        string adminUsername,
        CancellationToken cancellationToken = default)
    {
        var normalized = adminUsername.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return new ReportingPhase1AdminUserResolution(
                ReportingPhase1AdminUserResolutionStatus.NotFound,
                Detail: "Admin username is required.");
        }

        var users = await db.Users.AsNoTracking().ToListAsync(cancellationToken);
        var matches = users
            .Where(u => string.Equals(u.Username.Trim(), normalized, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (matches.Count == 0)
        {
            return new ReportingPhase1AdminUserResolution(
                ReportingPhase1AdminUserResolutionStatus.NotFound,
                Detail: "No user matches the admin username.");
        }

        if (matches.Count > 1)
        {
            return new ReportingPhase1AdminUserResolution(
                ReportingPhase1AdminUserResolutionStatus.Duplicate,
                Detail: "Multiple users share the admin username.");
        }

        var user = matches[0];
        if (!user.IsActive)
        {
            return new ReportingPhase1AdminUserResolution(
                ReportingPhase1AdminUserResolutionStatus.Inactive,
                Detail: "Admin account is inactive or locked.");
        }

        if (user.Role != UserRole.Admin)
        {
            return new ReportingPhase1AdminUserResolution(
                ReportingPhase1AdminUserResolutionStatus.NotAdmin,
                Detail: "User does not have the Admin role.");
        }

        return new ReportingPhase1AdminUserResolution(
            ReportingPhase1AdminUserResolutionStatus.Success,
            UserId: user.Id);
    }

    public static string MaskUserId(int userId)
    {
        var text = userId.ToString();
        if (text.Length <= 4)
            return new string('*', text.Length);

        return $"{text[..4]}…{text[^4..]}";
    }
}
