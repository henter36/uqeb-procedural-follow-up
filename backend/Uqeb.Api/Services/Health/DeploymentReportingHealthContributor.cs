using Microsoft.Extensions.Options;
using Uqeb.Api.Configuration;
using Uqeb.Api.Reporting.Operations;
using Uqeb.Api.Reporting.Services;

namespace Uqeb.Api.Services.Health;

public sealed record DeploymentReportingHealthCheck(
    string Name,
    string Status,
    string? Reason = null);

public sealed record DeploymentReportingHealthResult(
    bool FeatureEnabled,
    bool IsReady,
    IReadOnlyList<DeploymentReportingHealthCheck> Checks);

public interface IDeploymentReportingHealthContributor
{
    Task<DeploymentReportingHealthResult> EvaluateAsync(CancellationToken cancellationToken = default);
}

public sealed class DeploymentReportingHealthContributor : IDeploymentReportingHealthContributor
{
    private readonly FeatureFlagsSettings _featureFlags;
    private readonly IReportingChromiumProbe _chromiumProbe;
    private readonly IReportNumberSequenceSchemaProbe _sequenceSchemaProbe;

    public DeploymentReportingHealthContributor(
        IOptions<FeatureFlagsSettings> featureFlags,
        IReportingChromiumProbe chromiumProbe,
        IReportNumberSequenceSchemaProbe sequenceSchemaProbe)
    {
        _featureFlags = featureFlags.Value;
        _chromiumProbe = chromiumProbe;
        _sequenceSchemaProbe = sequenceSchemaProbe;
    }

    public async Task<DeploymentReportingHealthResult> EvaluateAsync(CancellationToken cancellationToken = default)
    {
        if (!_featureFlags.InstitutionalReports)
        {
            return new DeploymentReportingHealthResult(
                FeatureEnabled: false,
                IsReady: true,
                Checks:
                [
                    new DeploymentReportingHealthCheck("institutionalReporting", "not_applicable"),
                    new DeploymentReportingHealthCheck("playwrightChromium", "not_applicable"),
                    new DeploymentReportingHealthCheck("reportNumberSequence", "not_applicable"),
                ]);
        }

        var chromium = await _chromiumProbe.ProbeLaunchOnlyAsync(cancellationToken);
        var sequenceAvailable = await _sequenceSchemaProbe.IsTableAvailableAsync(cancellationToken);

        var chromiumStatus = chromium.LaunchSuccessful ? "pass" : "fail";
        var sequenceStatus = sequenceAvailable ? "pass" : "fail";
        var reportingStatus = chromium.LaunchSuccessful && sequenceAvailable ? "pass" : "fail";

        var checks = new List<DeploymentReportingHealthCheck>
        {
            new("playwrightChromium", chromiumStatus, chromium.Summary),
            new("reportNumberSequence", sequenceStatus,
                sequenceAvailable ? null : "ReportNumberSequences table is missing."),
            new("institutionalReporting", reportingStatus),
        };

        var isReady = checks.All(check =>
            check.Status is "pass" or "not_applicable");

        return new DeploymentReportingHealthResult(
            FeatureEnabled: true,
            IsReady: isReady,
            Checks: checks);
    }
}
