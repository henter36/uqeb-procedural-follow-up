using Microsoft.Extensions.Options;
using Uqeb.Api.Configuration;
using Uqeb.Api.Models.Enums;
using Uqeb.Api.Reporting.Configuration;
using Uqeb.Api.Reporting.Services;
using Uqeb.Api.Services;
using Xunit;

namespace Uqeb.Api.Tests.Reporting;

public class ReportingRolloutServiceTests
{
    [Fact]
    public void Evaluate_AllowsExplicitUserId()
    {
        var service = CreateService(userIds: [42]);
        var decision = service.Evaluate(new TestUser(UserRole.Supervisor, 42, 10));

        Assert.True(decision.Allowed);
        Assert.Equal(ReportingRolloutMatchSource.UserAllowlist, decision.MatchSource);
    }

    [Fact]
    public void Evaluate_UsesStablePercentageBucket()
    {
        var service = CreateService(percentage: 50);
        var first = service.Evaluate(new TestUser(UserRole.Supervisor, 7, null));
        var second = service.Evaluate(new TestUser(UserRole.Supervisor, 7, null));
        Assert.Equal(first.Allowed, second.Allowed);
    }

    [Fact]
    public void Evaluate_DeniesByDefault_WhenNoAllowlistsConfigured()
    {
        var service = CreateService();
        var decision = service.Evaluate(new TestUser(UserRole.Admin, 1, null));

        Assert.False(decision.Allowed);
        Assert.Equal(ReportingRolloutMatchSource.DefaultDeny, decision.MatchSource);
    }

    [Fact]
    public void Evaluate_Phase1AllowlistOnly_UsesUserIdNotRole()
    {
        var service = CreateService(userIds: [42]);

        Assert.False(service.Evaluate(new TestUser(UserRole.Admin, 1, null)).Allowed);
        Assert.True(service.Evaluate(new TestUser(UserRole.Admin, 42, null)).Allowed);
    }

    [Fact]
    public void GetAccessStatus_ObserveOnly_AllowsUnmatchedUserWhenFeatureEnabled()
    {
        var service = CreateService(
            institutionalReports: true,
            enforcementMode: ReportingRolloutEnforcementMode.ObserveOnly,
            userIds: [1]);

        var status = service.GetAccessStatus(new TestUser(UserRole.Admin, 2, null));

        Assert.True(status.FeatureEnabled);
        Assert.Equal(ReportingRolloutEnforcementMode.ObserveOnly, status.EnforcementMode);
        Assert.False(status.UserDecision!.AllowedByRollout);
        Assert.True(status.UserDecision.EffectiveAllowed);
        Assert.Equal("AuthorizedUsersAllowed", status.EffectiveAccessMode);
    }

    [Fact]
    public void GetAccessStatus_Enforced_DeniesUnmatchedUser()
    {
        var service = CreateService(
            institutionalReports: true,
            enforcementMode: ReportingRolloutEnforcementMode.Enforced,
            userIds: [1]);

        var status = service.GetAccessStatus(new TestUser(UserRole.Admin, 2, null));

        Assert.False(status.UserDecision!.EffectiveAllowed);
        Assert.Equal("RolloutDenied", status.EffectiveAccessMode);
    }

    [Fact]
    public void GetAccessStatus_DefaultFeatureFlag_AllowsWhenMissingFromConfiguration()
    {
        var service = new ReportingRolloutService(
            Options.Create(new FeatureFlagsSettings()),
            Options.Create(new ReportingRolloutOptions
            {
                EnforcementMode = ReportingRolloutEnforcementMode.ObserveOnly,
            }));

        var status = service.GetAccessStatus(new TestUser(UserRole.Admin, 2, null));

        Assert.True(status.FeatureEnabled);
        Assert.True(status.UserDecision!.EffectiveAllowed);
    }

    private static ReportingRolloutService CreateService(
        bool institutionalReports = true,
        ReportingRolloutEnforcementMode enforcementMode = ReportingRolloutEnforcementMode.ObserveOnly,
        bool emergencyDisable = false,
        IReadOnlyList<string>? roles = null,
        IReadOnlyList<int>? userIds = null,
        IReadOnlyList<int>? departmentIds = null,
        int percentage = 0)
    {
        return new ReportingRolloutService(
            Options.Create(new FeatureFlagsSettings { InstitutionalReports = institutionalReports }),
            Options.Create(new ReportingRolloutOptions
            {
                EnforcementMode = enforcementMode,
                EmergencyDisable = emergencyDisable,
                EnabledForRoles = roles?.ToList() ?? [],
                EnabledForUserIds = userIds?.ToList() ?? [],
                EnabledForDepartments = departmentIds?.ToList() ?? [],
                Percentage = percentage,
            }));
    }

    private sealed class TestUser(UserRole role, int userId, int? departmentId) : ICurrentUserService
    {
        public bool IsAuthenticated => true;
        public int UserId => userId;
        public string Username => "test";
        public UserRole Role => role;
        public int? DepartmentId => departmentId;
    }
}
