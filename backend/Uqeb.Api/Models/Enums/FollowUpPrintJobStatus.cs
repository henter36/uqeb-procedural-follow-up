namespace Uqeb.Api.Models.Enums;

public enum FollowUpPrintJobStatus
{
    Queued = 1,
    Claimed = 2,
    Processing = 3,
    ReadyToPrint = 4,
    PartiallyPrinted = 5,
    Completed = 6,
    Failed = 7,
    Cancelled = 8,
    Expired = 9
}
