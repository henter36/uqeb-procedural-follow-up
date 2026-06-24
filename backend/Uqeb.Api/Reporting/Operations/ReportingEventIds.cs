namespace Uqeb.Api.Reporting.Operations;

public static class ReportingEventIds
{
    public const int GenerationStarted = 4100;
    public const int ModelBuilt = 4101;
    public const int ExportStarted = 4102;
    public const int ExportCompleted = 4103;
    public const int ExportFailed = 4104;
    public const int ExportCancelled = 4105;
    public const int ExportRejected = 4106;
    public const int TempCleanupFailed = 4107;
    public const int ChromiumUnavailable = 4108;
    public const int RolloutObserveOnlyEvaluated = 4109;
    public const int RolloutEnforcedEvaluated = 4110;
}
