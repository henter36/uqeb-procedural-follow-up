namespace Uqeb.Api.Models.Enums;

public enum AuditAction
{
    Create = 1,
    Update = 2,
    StatusChange = 3,
    AddFollowUp = 4,
    AddAssignment = 5,
    RecordReply = 6,
    RecordResponse = 7,
    Close = 8,
    Cancel = 9,
    Archive = 10,
    CloseAttemptFailed = 11,
    CompleteResponse = 12,
    ResetPassword = 13,
    ExportReport = 14
}
