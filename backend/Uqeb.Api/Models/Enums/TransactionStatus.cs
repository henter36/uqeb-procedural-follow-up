namespace Uqeb.Api.Models.Enums;

public enum TransactionStatus
{
    New = 1,
    InProgress = 2,
    Assigned = 3,
    WaitingForReply = 4,
    PartiallyReplied = 5,
    ReadyForResponse = 6,
    ResponseCompleted = 7,
    Closed = 8,
    Overdue = 9,
    Cancelled = 10,
    Archived = 11
}
