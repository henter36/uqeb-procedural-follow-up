using Uqeb.Api.Models.Enums;

namespace Uqeb.Api.Services;

public static class FollowUpPrintAuditEvents
{
    public const string JobQueued = "follow_up_print.job_queued";
    public const string JobStarted = "follow_up_print.job_started";
    public const string JobReady = "follow_up_print.job_ready";
    public const string JobCompleted = "follow_up_print.job_completed";
    public const string JobFailed = "follow_up_print.job_failed";
    public const string JobCancelled = "follow_up_print.job_cancelled";
    public const string JobLeaseRecovered = "follow_up_print.job_lease_recovered";
    public const string JobRetryRequested = "follow_up_print.job_retry_requested";
    public const string PartPrintRequested = "follow_up_print.part_print_requested";
    public const string DirectPrintRequested = "follow_up_print.direct_print_requested";
    public const string PrintConfirmed = "follow_up_print.print_confirmed";
    public const string PrintCancelled = "follow_up_print.print_cancelled";
    public const string Reprinted = "follow_up_print.reprinted";
    public const string LinkedToFollowUp = "follow_up_print.linked_to_follow_up";
}

public static class FollowUpPrintAuditWriter
{
    public const string JobResourceType = "FollowUpPrintJob";
    public const string PartResourceType = "FollowUpPrintJobPart";
    public const string PrintRecordResourceType = "FollowUpLetterPrintRecord";
    public const string LetterTemplateResourceType = "LetterTemplate";

    public static Task LogJobQueuedAsync(IAuditService audit, int userId, int jobId, string? detail = null) =>
        audit.LogAsync(userId, AuditAction.FollowUpPrintJobQueued, JobResourceType, jobId, null, null, Format(JobResourceType, jobId, FollowUpPrintAuditEvents.JobQueued, detail));

    public static Task LogJobStartedAsync(IAuditService audit, int userId, int jobId, string? detail = null) =>
        audit.LogAsync(userId, AuditAction.FollowUpPrintJobStarted, JobResourceType, jobId, null, null, Format(JobResourceType, jobId, FollowUpPrintAuditEvents.JobStarted, detail));

    public static Task LogJobReadyAsync(IAuditService audit, int userId, int jobId, string? detail = null) =>
        audit.LogAsync(userId, AuditAction.FollowUpPrintJobReady, JobResourceType, jobId, null, null, Format(JobResourceType, jobId, FollowUpPrintAuditEvents.JobReady, detail));

    public static Task LogJobCompletedAsync(IAuditService audit, int userId, int jobId, string? detail = null) =>
        audit.LogAsync(userId, AuditAction.FollowUpPrintJobCompleted, JobResourceType, jobId, null, null, Format(JobResourceType, jobId, FollowUpPrintAuditEvents.JobCompleted, detail));

    public static Task LogJobFailedAsync(IAuditService audit, int userId, int jobId, string? detail = null) =>
        audit.LogAsync(userId, AuditAction.FollowUpPrintJobFailed, JobResourceType, jobId, null, null, Format(JobResourceType, jobId, FollowUpPrintAuditEvents.JobFailed, detail));

    public static Task LogJobCancelledAsync(IAuditService audit, int userId, int jobId, string? detail = null) =>
        audit.LogAsync(userId, AuditAction.FollowUpPrintJobCancelled, JobResourceType, jobId, null, null, Format(JobResourceType, jobId, FollowUpPrintAuditEvents.JobCancelled, detail));

    public static Task LogJobLeaseRecoveredAsync(IAuditService audit, int userId, int jobId, string? detail = null) =>
        audit.LogAsync(userId, AuditAction.FollowUpPrintJobLeaseRecovered, JobResourceType, jobId, null, null, Format(JobResourceType, jobId, FollowUpPrintAuditEvents.JobLeaseRecovered, detail));

    public static Task LogJobRetryRequestedAsync(IAuditService audit, int userId, int jobId, string? detail = null) =>
        audit.LogAsync(userId, AuditAction.FollowUpPrintJobRetryRequested, JobResourceType, jobId, null, null, Format(JobResourceType, jobId, FollowUpPrintAuditEvents.JobRetryRequested, detail));

    public static Task LogPartPrintRequestedAsync(IAuditService audit, int userId, int partId, int jobId, string? detail = null) =>
        audit.LogAsync(userId, AuditAction.FollowUpLetterPrintRequested, PartResourceType, partId, null, null, Format(PartResourceType, partId, FollowUpPrintAuditEvents.PartPrintRequested, detail, jobId));

    public static Task LogDirectPrintRequestedAsync(IAuditService audit, int userId, int recordId, int transactionId, string? detail = null) =>
        audit.LogAsync(userId, AuditAction.FollowUpLetterPrintRequested, PrintRecordResourceType, recordId, transactionId, null, Format(PrintRecordResourceType, recordId, FollowUpPrintAuditEvents.DirectPrintRequested, detail));

    public static Task LogPrintConfirmedAsync(IAuditService audit, int userId, int recordId, int transactionId, string? detail = null) =>
        audit.LogAsync(userId, AuditAction.FollowUpLetterPrintConfirmed, PrintRecordResourceType, recordId, transactionId, null, Format(PrintRecordResourceType, recordId, FollowUpPrintAuditEvents.PrintConfirmed, detail));

    public static Task LogPrintCancelledAsync(IAuditService audit, int userId, int recordId, int transactionId, string? detail = null) =>
        audit.LogAsync(userId, AuditAction.FollowUpLetterPrintCancelled, PrintRecordResourceType, recordId, transactionId, null, Format(PrintRecordResourceType, recordId, FollowUpPrintAuditEvents.PrintCancelled, detail));

    public static Task LogReprintedAsync(IAuditService audit, int userId, int recordId, int transactionId, string? detail = null) =>
        audit.LogAsync(userId, AuditAction.FollowUpLetterReprinted, PrintRecordResourceType, recordId, transactionId, null, Format(PrintRecordResourceType, recordId, FollowUpPrintAuditEvents.Reprinted, detail));

    public static Task LogLinkedToFollowUpAsync(IAuditService audit, int userId, int recordId, int transactionId, int followUpId) =>
        audit.LogAsync(userId, AuditAction.FollowUpPrintLinkedToRegisteredFollowUp, PrintRecordResourceType, recordId, transactionId, null, Format(PrintRecordResourceType, recordId, FollowUpPrintAuditEvents.LinkedToFollowUp, $"followUpId={followUpId}"));

    public static Task LogLetterTemplateCreatedAsync(IAuditService audit, int userId, int templateId, string? detail = null) =>
        audit.LogAsync(userId, AuditAction.LetterTemplateCreated, LetterTemplateResourceType, templateId, null, null, Format(LetterTemplateResourceType, templateId, "letter_template.created", detail));

    public static Task LogLetterTemplateUpdatedAsync(IAuditService audit, int userId, int templateId, string? detail = null) =>
        audit.LogAsync(userId, AuditAction.LetterTemplateUpdated, LetterTemplateResourceType, templateId, null, null, Format(LetterTemplateResourceType, templateId, "letter_template.updated", detail));

    public static Task LogLetterTemplateCopiedAsync(IAuditService audit, int userId, int templateId, string? detail = null) =>
        audit.LogAsync(userId, AuditAction.LetterTemplateCopied, LetterTemplateResourceType, templateId, null, null, Format(LetterTemplateResourceType, templateId, "letter_template.copied", detail));

    public static Task LogLetterTemplateActivatedAsync(IAuditService audit, int userId, int templateId) =>
        audit.LogAsync(userId, AuditAction.LetterTemplateActivated, LetterTemplateResourceType, templateId, null, null, Format(LetterTemplateResourceType, templateId, "letter_template.activated", null));

    public static Task LogLetterTemplateDeactivatedAsync(IAuditService audit, int userId, int templateId) =>
        audit.LogAsync(userId, AuditAction.LetterTemplateDeactivated, LetterTemplateResourceType, templateId, null, null, Format(LetterTemplateResourceType, templateId, "letter_template.deactivated", null));

    public static Task LogLetterTemplateDefaultChangedAsync(IAuditService audit, int userId, int templateId) =>
        audit.LogAsync(userId, AuditAction.LetterTemplateDefaultChanged, LetterTemplateResourceType, templateId, null, null, Format(LetterTemplateResourceType, templateId, "letter_template.default_changed", null));

    private static string Format(string resourceType, int resourceId, string eventName, string? detail, int? jobId = null)
    {
        var parts = new List<string> { $"event={eventName}", $"resourceType={resourceType}", $"resourceId={resourceId}" };
        if (jobId.HasValue)
            parts.Add($"jobId={jobId.Value}");
        if (!string.IsNullOrWhiteSpace(detail))
            parts.Add(detail);
        return string.Join(';', parts);
    }
}
