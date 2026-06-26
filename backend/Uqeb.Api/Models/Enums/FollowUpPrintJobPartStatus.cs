namespace Uqeb.Api.Models.Enums;

public enum FollowUpPrintJobPartStatus
{
    Pending = 1,
    Processing = 2,
    ReadyToPrint = 3,
    Printed = 4,
    Failed = 5,
    Cancelled = 6,
    PartiallyReady = 7,
}
