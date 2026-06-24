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
    private const string StatusNotApplicable = "not_applicable";
    private const string StatusPass = "pass";
    private const string StatusFail = "fail";

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
                    new DeploymentReportingHealthCheck("institutionalReporting", StatusNotApplicable),
                    new DeploymentReportingHealthCheck("playwrightChromium", StatusNotApplicable),
                    new DeploymentReportingHealthCheck("reportNumberSequence", StatusNotApplicable),
                ]);
        }

        var chromium = await _chromiumProbe.ProbeLaunchOnlyAsync(cancellationToken);
        var sequenceAvailable = await _sequenceSchemaProbe.IsTableAvailableAsync(cancellationToken);

        var chromiumStatus = chromium.LaunchSuccessful ? StatusPass : StatusFail;
        var sequenceStatus = sequenceAvailable ? StatusPass : StatusFail;
        var reportingStatus = chromium.LaunchSuccessful && sequenceAvailable ? StatusPass : StatusFail;

        var checks = new List<DeploymentReportingHealthCheck>
        {
            new("playwrightChromium", chromiumStatus, chromium.Summary),
            new("reportNumberSequence", sequenceStatus,
                sequenceAvailable ? null : "ReportNumberSequences table is missing."),
            new("institutionalReporting", reportingStatus),
        };

        var isReady = checks.All(check =>
            check.Status is StatusPass or StatusNotApplicable);

        return new DeploymentReportingHealthResult(
            FeatureEnabled: true,
            IsReady: isReady,
            Checks: checks);
    }
}
