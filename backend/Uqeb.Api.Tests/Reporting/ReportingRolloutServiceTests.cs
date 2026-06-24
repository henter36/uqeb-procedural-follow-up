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
    public void IsEnabledForUser_ReturnsFalse_WhenEmergencyDisableIsTrue()
    {
        var service = CreateService(institutionalReports: true, emergencyDisable: true, roles: ["Admin"]);
        var user = new TestUser(UserRole.Admin, 1, null);
        Assert.False(service.IsEnabledForUser(user));
    }

    [Fact]
    public void IsEnabledForUser_AllowsExplicitUserId()
    {
        var service = CreateService(
            institutionalReports: true,
            emergencyDisable: false,
            userIds: [42]);
        Assert.True(service.IsEnabledForUser(new TestUser(UserRole.Supervisor, 42, 10)));
    }

    [Fact]
    public void IsEnabledForUser_UsesStablePercentageBucket()
    {
        var service = CreateService(
            institutionalReports: true,
            emergencyDisable: false,
            percentage: 50);
        var first = service.IsEnabledForUser(new TestUser(UserRole.Supervisor, 7, null));
        var second = service.IsEnabledForUser(new TestUser(UserRole.Supervisor, 7, null));
        Assert.Equal(first, second);
    }

    [Fact]
    public void IsEnabledForUser_DeniesByDefault_WhenNoAllowlistsConfigured()
    {
        var service = CreateService(institutionalReports: true, emergencyDisable: false);
        Assert.False(service.IsEnabledForUser(new TestUser(UserRole.Admin, 1, null)));
    }

    [Theory]
    [InlineData(true, true, 1, false)] // 1. EmergencyDisable blocks allowlisted user
    [InlineData(false, false, 1, false)] // 2. InstitutionalReports=false blocks allowlisted user
    [InlineData(false, true, 1, true)] // 3. Allowlisted user allowed
    [InlineData(false, true, 2, false)] // 4. Non-allowlisted user denied
    public void IsEnabledForUser_FollowsPhase1DecisionOrder(
        bool emergencyDisable,
        bool institutionalReports,
        int userId,
        bool expected)
    {
        var service = CreateService(
            institutionalReports: institutionalReports,
            emergencyDisable: emergencyDisable,
            userIds: [1],
            percentage: 0);

        Assert.Equal(expected, service.IsEnabledForUser(new TestUser(UserRole.Admin, userId, null)));
    }

    [Fact]
    public void IsEnabledForUser_Phase1AllowlistOnly_DeniesAdminRoleWithoutUserId()
    {
        var service = CreateService(
            institutionalReports: true,
            emergencyDisable: false,
            roles: [],
            userIds: [42],
            percentage: 0);

        Assert.False(service.IsEnabledForUser(new TestUser(UserRole.Admin, 1, null)));
        Assert.True(service.IsEnabledForUser(new TestUser(UserRole.Admin, 42, null)));
    }

    private static ReportingRolloutService CreateService(
        bool institutionalReports,
        bool emergencyDisable,
        IReadOnlyList<string>? roles = null,
        IReadOnlyList<int>? userIds = null,
        IReadOnlyList<int>? departmentIds = null,
        int percentage = 0)
    {
        return new ReportingRolloutService(
            Options.Create(new FeatureFlagsSettings { InstitutionalReports = institutionalReports }),
            Options.Create(new ReportingRolloutOptions
            {
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
