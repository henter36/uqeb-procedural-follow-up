using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Uqeb.Api.Configuration;
using Uqeb.Api.Models.Enums;
using Uqeb.Api.Reporting.Configuration;
using Uqeb.Api.Services;

namespace Uqeb.Api.Reporting.Services;

public interface IReportingRolloutService
{
    bool IsEnabledForUser(ICurrentUserService user);
    ReportingRolloutStatusDto GetStatus();
}

public sealed class ReportingRolloutStatusDto
{
    public bool GlobalFeatureEnabled { get; init; }
    public bool EmergencyDisable { get; init; }
    public int Percentage { get; init; }
    public IReadOnlyList<string> EnabledForRoles { get; init; } = [];
    public IReadOnlyList<int> EnabledForUserIds { get; init; } = [];
    public IReadOnlyList<int> EnabledForDepartments { get; init; } = [];
}

public sealed class ReportingRolloutService : IReportingRolloutService
{
    private readonly FeatureFlagsSettings _featureFlags;
    private readonly ReportingRolloutOptions _rollout;

    public ReportingRolloutService(
        IOptions<FeatureFlagsSettings> featureFlags,
        IOptions<ReportingRolloutOptions> rollout)
    {
        _featureFlags = featureFlags.Value;
        _rollout = rollout.Value;
    }

    public bool IsEnabledForUser(ICurrentUserService user)
    {
        if (_rollout.EmergencyDisable)
            return false;

        if (!_featureFlags.InstitutionalReports)
            return false;

        if (_rollout.EnabledForUserIds.Contains(user.UserId))
            return true;

        var roleName = user.Role.ToString();
        if (_rollout.EnabledForRoles.Any(r => string.Equals(r, roleName, StringComparison.OrdinalIgnoreCase)))
            return true;

        if (user.DepartmentId is int departmentId
            && _rollout.EnabledForDepartments.Contains(departmentId))
        {
            return true;
        }

        if (_rollout.Percentage > 0)
        {
            var bucket = ComputeStableBucket(user.UserId);
            return bucket < _rollout.Percentage;
        }

        return false;
    }

    public ReportingRolloutStatusDto GetStatus() => new()
    {
        GlobalFeatureEnabled = _featureFlags.InstitutionalReports,
        EmergencyDisable = _rollout.EmergencyDisable,
        Percentage = _rollout.Percentage,
        EnabledForRoles = _rollout.EnabledForRoles,
        EnabledForUserIds = _rollout.EnabledForUserIds,
        EnabledForDepartments = _rollout.EnabledForDepartments,
    };

    internal static int ComputeStableBucket(int userId)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"uqeb-reporting-rollout:{userId}"));
        return (int)(BitConverter.ToUInt32(hash, 0) % 100);
    }
}
