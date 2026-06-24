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
    ReportingRolloutDecision Evaluate(ICurrentUserService user);
    ReportingRolloutStatusDto GetStatus();
    ReportingRolloutAccessStatusDto GetAccessStatus(ICurrentUserService user);
}

public sealed class ReportingRolloutStatusDto
{
    public bool GlobalFeatureEnabled { get; init; }
    public ReportingRolloutEnforcementMode EnforcementMode { get; init; }
    public bool EmergencyDisable { get; init; }
    public int Percentage { get; init; }
    public int EnabledForUserIdsCount { get; init; }
    public int EnabledForRolesCount { get; init; }
    public int EnabledForDepartmentsCount { get; init; }
}

public sealed class ReportingRolloutUserDecisionDto
{
    public bool AllowedByRollout { get; init; }
    public bool Enforced { get; init; }
    public bool EffectiveAllowed { get; init; }
    public string Reason { get; init; } = string.Empty;
    public ReportingRolloutMatchSource MatchSource { get; init; }
}

public sealed class ReportingRolloutAccessStatusDto
{
    public bool FeatureEnabled { get; init; }
    public ReportingRolloutEnforcementMode EnforcementMode { get; init; }
    public bool EmergencyDisable { get; init; }
    public string EffectiveAccessMode { get; init; } = string.Empty;
    public ReportingRolloutUserDecisionDto? UserDecision { get; init; }
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

    public ReportingRolloutDecision Evaluate(ICurrentUserService user)
    {
        if (_rollout.EnabledForUserIds.Contains(user.UserId))
        {
            return new ReportingRolloutDecision(
                true,
                "UserAllowlist",
                ReportingRolloutMatchSource.UserAllowlist);
        }

        var roleName = user.Role.ToString();
        if (_rollout.EnabledForRoles.Any(r => string.Equals(r, roleName, StringComparison.OrdinalIgnoreCase)))
        {
            return new ReportingRolloutDecision(
                true,
                "RoleAllowlist",
                ReportingRolloutMatchSource.Role);
        }

        if (user.DepartmentId is int departmentId
            && _rollout.EnabledForDepartments.Contains(departmentId))
        {
            return new ReportingRolloutDecision(
                true,
                "DepartmentAllowlist",
                ReportingRolloutMatchSource.Department);
        }

        if (_rollout.Percentage > 0)
        {
            var bucket = ComputeStableBucket(user.UserId);
            if (bucket < _rollout.Percentage)
            {
                return new ReportingRolloutDecision(
                    true,
                    "PercentageBucket",
                    ReportingRolloutMatchSource.Percentage);
            }
        }

        return new ReportingRolloutDecision(
            false,
            "DefaultDeny",
            ReportingRolloutMatchSource.DefaultDeny);
    }

    public ReportingRolloutStatusDto GetStatus() => new()
    {
        GlobalFeatureEnabled = _featureFlags.InstitutionalReports,
        EnforcementMode = _rollout.EnforcementMode,
        EmergencyDisable = _rollout.EmergencyDisable,
        Percentage = _rollout.Percentage,
        EnabledForUserIdsCount = _rollout.EnabledForUserIds.Count,
        EnabledForRolesCount = _rollout.EnabledForRoles.Count,
        EnabledForDepartmentsCount = _rollout.EnabledForDepartments.Count,
    };

    public ReportingRolloutAccessStatusDto GetAccessStatus(ICurrentUserService user)
    {
        var featureEnabled = _featureFlags.InstitutionalReports;
        var decision = user.IsAuthenticated ? Evaluate(user) : null;
        var enforced = _rollout.EnforcementMode == ReportingRolloutEnforcementMode.Enforced;
        var effectiveAllowed = featureEnabled
                               && !_rollout.EmergencyDisable
                               && (decision is null
                                   || !enforced
                                   || decision.Allowed);

        return new ReportingRolloutAccessStatusDto
        {
            FeatureEnabled = featureEnabled,
            EnforcementMode = _rollout.EnforcementMode,
            EmergencyDisable = _rollout.EmergencyDisable,
            EffectiveAccessMode = ResolveEffectiveAccessMode(featureEnabled, effectiveAllowed),
            UserDecision = decision is null
                ? null
                : new ReportingRolloutUserDecisionDto
                {
                    AllowedByRollout = decision.Allowed,
                    Enforced = enforced,
                    EffectiveAllowed = effectiveAllowed,
                    Reason = decision.Reason,
                    MatchSource = decision.MatchSource,
                },
        };
    }

    internal static int ComputeStableBucket(int userId)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"uqeb-reporting-rollout:{userId}"));
        return (int)(BitConverter.ToUInt32(hash, 0) % 100);
    }

    private static string ResolveEffectiveAccessMode(bool featureEnabled, bool effectiveAllowed)
    {
        if (!featureEnabled)
            return "FeatureDisabled";

        return effectiveAllowed ? "AuthorizedUsersAllowed" : "RolloutDenied";
    }
}
