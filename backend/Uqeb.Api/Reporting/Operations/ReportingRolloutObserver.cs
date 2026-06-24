using Uqeb.Api.Reporting.Configuration;
using Uqeb.Api.Reporting.Services;

namespace Uqeb.Api.Reporting.Operations;

public interface IReportingRolloutObserver
{
    void RecordDecision(
        ReportingRolloutEnforcementMode enforcementMode,
        ReportingRolloutDecision decision,
        string? correlationId);
}

public sealed class ReportingRolloutObserver(
    IReportingMetrics metrics,
    ILogger<ReportingRolloutObserver> logger) : IReportingRolloutObserver
{
    public void RecordDecision(
        ReportingRolloutEnforcementMode enforcementMode,
        ReportingRolloutDecision decision,
        string? correlationId)
    {
        metrics.RecordRolloutDecision(enforcementMode, decision);

        var decisionLabel = decision.Allowed ? "would_allow" : "would_deny";
        var message = decision.Allowed
            ? "Reporting rollout would allow request"
            : "Reporting rollout would deny request";

        if (enforcementMode == ReportingRolloutEnforcementMode.ObserveOnly)
        {
            logger.LogInformation(
                ReportingEventIds.RolloutObserveOnlyEvaluated,
                "{Message}. CorrelationId: {CorrelationId}, Decision: {Decision}, MatchSource: {MatchSource}, EnforcementMode: {EnforcementMode}",
                message,
                correlationId,
                decisionLabel,
                decision.MatchSource,
                enforcementMode);
        }
        else
        {
            logger.LogDebug(
                ReportingEventIds.RolloutEnforcedEvaluated,
                "Reporting rollout evaluated. CorrelationId: {CorrelationId}, Decision: {Decision}, MatchSource: {MatchSource}, EnforcementMode: {EnforcementMode}",
                correlationId,
                decision.Allowed ? "allow" : "deny",
                decision.MatchSource,
                enforcementMode);
        }
    }
}
