using System.Globalization;
using Uqeb.Api.Helpers;
using Uqeb.Api.Models.Entities;
using Uqeb.Api.Models.Enums;
using Uqeb.Api.Configuration;
using Uqeb.Api.Models.Letters;

namespace Uqeb.Api.Services;

public sealed record FollowUpLetterTargetEntity(
    string Name,
    int? DepartmentId = null,
    int? ExternalPartyId = null);

public sealed class FollowUpLetterDocumentBuildRequest
{
    public required Transaction Transaction { get; init; }
    public required LetterTemplate Template { get; init; }
    public required FollowUpLetterTargetEntity Target { get; init; }
    public IReadOnlyList<Assignment> Assignments { get; init; } = [];
    public IReadOnlyList<FollowUp> FollowUps { get; init; } = [];
    public IReadOnlyList<FollowUpLetterTargetEntity> AllTargets { get; init; } = [];
    public string? BodyOverride { get; init; }
    public int? FollowUpSequenceOverride { get; init; }
    public int? ResponseDeadlineDays { get; init; }
    public string? PreparedBy { get; init; }
    public string? SenderDepartment { get; init; }
    public string? LogoPath { get; init; }
    public DateTime TodayLocal { get; init; }
    public string? SignatoryPosition { get; init; }
    public string? SignatoryRank { get; init; }
    public string? SignatoryNameOverride { get; init; }
}

public interface IFollowUpLetterDocumentBuilder
{
    FollowUpLetterDocumentModel Build(FollowUpLetterDocumentBuildRequest request);
}

public sealed class FollowUpLetterDocumentBuilder : IFollowUpLetterDocumentBuilder
{
    private readonly IFollowUpLetterTimeZone _timeZone;

    public FollowUpLetterDocumentBuilder(IFollowUpLetterTimeZone timeZone) =>
        _timeZone = timeZone;

    public FollowUpLetterDocumentModel Build(FollowUpLetterDocumentBuildRequest request)
    {
        var transaction = request.Transaction;
        var target = request.Target;
        var today = request.TodayLocal;
        var registeredFollowUpCount = request.FollowUps.Count;
        var sequence = request.FollowUpSequenceOverride
            ?? FollowUpSequenceCalculator.CalculateExpectedSequence(registeredFollowUpCount);
        var sequenceText = FollowUpSequenceCalculator.ToArabicText(sequence);

        var lastFollowUp = request.FollowUps
            .OrderByDescending(f => f.FollowUpDate)
            .ThenByDescending(f => f.Id)
            .FirstOrDefault();
        var openAssignment = request.Assignments
            .Where(a => a.Status is AssignmentStatus.Active)
            .OrderByDescending(a => a.AssignedDate)
            .ThenByDescending(a => a.Id)
            .FirstOrDefault();

        var incomingLocal = FollowUpLetterDateSemantics.ToBusinessDisplayDate(transaction.IncomingDate, _timeZone);
        var assignmentLocal = FollowUpLetterDateSemantics.ToBusinessDisplayDate(openAssignment?.AssignedDate, _timeZone);
        var followUpLocal = FollowUpLetterDateSemantics.ToBusinessDisplayDate(lastFollowUp?.FollowUpDate, _timeZone);
        var dueLocal = FollowUpLetterDateSemantics.ToBusinessDisplayDate(
            openAssignment?.DueDate ?? transaction.ResponseDueDate,
            _timeZone);

        int? daysOverdue = null;
        if (dueLocal.HasValue && dueLocal.Value.Date < today.Date)
            daysOverdue = (today.Date - dueLocal.Value.Date).Days;

        var targetEntities = JoinNames(request.AllTargets.Count > 0 ? request.AllTargets : [target]);
        var targetDepartments = JoinNames(
            (request.AllTargets.Count > 0 ? request.AllTargets : [target])
            .Where(t => t.DepartmentId.HasValue));

        var body = !string.IsNullOrWhiteSpace(request.BodyOverride)
            ? request.BodyOverride
            : FollowUpLetterVariableReplacer.Render(
                request.Template.Content,
                FollowUpLetterVariableReplacer.BuildValues(new FollowUpLetterRenderContext
                {
                    TransactionId = transaction.Id,
                    TransactionNumber = transaction.InternalTrackingNumber,
                    IncomingNumber = transaction.IncomingNumber,
                    IncomingDateLocal = incomingLocal,
                    Subject = transaction.Subject,
                    TargetEntity = target.Name,
                    TargetEntities = targetEntities,
                    TargetDepartments = targetDepartments,
                    AssignmentDateLocal = assignmentLocal,
                    DueDateLocal = dueLocal,
                    DaysOverdue = daysOverdue,
                    Priority = FormatPriority(transaction.Priority),
                    Category = transaction.Category ?? transaction.CategoryEntity?.Name,
                    TodayLocal = today,
                    SenderDepartment = request.SenderDepartment,
                    PreparedBy = request.PreparedBy,
                    FollowUpNumber = lastFollowUp?.FollowUpNumber,
                    FollowUpDateLocal = followUpLocal,
                    FollowUpSequence = sequence,
                    FollowUpSequenceText = sequenceText,
                    ResponseDeadlineDays = request.ResponseDeadlineDays ?? transaction.ResponseDueDays,
                }));

        return new FollowUpLetterDocumentModel
        {
            TransactionId = transaction.Id,
            TemplateId = request.Template.Id,
            LogoPath = OrganizationBrandingPaths.LogoApiUrl,
            OrganizationName = request.SenderDepartment ?? string.Empty,
            LetterNumber = transaction.IncomingNumber,
            GregorianDate = HijriDateFormatter.FormatGregorianArabic(today),
            HijriDate = HijriDateFormatter.Format(today) ?? string.Empty,
            Recipient = target.Name,
            Subject = transaction.Subject,
            Body = body,
            SenderDepartment = request.SenderDepartment ?? string.Empty,
            FollowUpSequence = sequence,
            FollowUpSequenceText = sequenceText,
            ResponseDeadlineDays = request.ResponseDeadlineDays ?? transaction.ResponseDueDays,
            Footer = string.Empty,
            Title = string.Empty,
            SignatoryName = string.IsNullOrWhiteSpace(request.SignatoryNameOverride) ? string.Empty : request.SignatoryNameOverride.Trim(),
            SignatoryTitle = string.IsNullOrWhiteSpace(request.SignatoryPosition) ? string.Empty : request.SignatoryPosition.Trim(),
            SignatoryRank = string.IsNullOrWhiteSpace(request.SignatoryRank) ? string.Empty : request.SignatoryRank.Trim(),
        };
    }

    private static string JoinNames(IEnumerable<FollowUpLetterTargetEntity> targets)
    {
        return string.Join("، ", targets
            .Select(t => t.Name)
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Distinct(StringComparer.OrdinalIgnoreCase));
    }

    private static string FormatPriority(Priority priority) => priority switch
    {
        Priority.VeryUrgent => "عاجل جدًا",
        Priority.Urgent => "عاجل",
        _ => "عادي",
    };
}
