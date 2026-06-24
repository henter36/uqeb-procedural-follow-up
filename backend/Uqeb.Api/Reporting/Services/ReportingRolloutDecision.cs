using Uqeb.Api.Reporting.Configuration;

namespace Uqeb.Api.Reporting.Services;

public sealed record ReportingRolloutDecision(
    bool Allowed,
    string Reason,
    ReportingRolloutMatchSource MatchSource);
