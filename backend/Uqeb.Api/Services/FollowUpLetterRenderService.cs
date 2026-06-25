using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Uqeb.Api.Configuration;
using Uqeb.Api.Data;
using Uqeb.Api.DTOs.LetterTemplates;
using Uqeb.Api.Helpers;
using Uqeb.Api.Models.Entities;
using Uqeb.Api.Models.Enums;
using Uqeb.Api.Models.Letters;

namespace Uqeb.Api.Services;

public interface IFollowUpLetterRenderService
{
    Task<FollowUpLetterPreviewDto?> RenderFollowUpLetterAsync(
        int transactionId,
        string? targetEntity,
        string? optionalEditedContent,
        ICurrentUserService currentUser,
        int? templateId = null,
        int? responseDeadlineDays = null,
        CancellationToken cancellationToken = default);

    Task<FollowUpLetterDocumentModel?> BuildDocumentAsync(
        int transactionId,
        FollowUpLetterTargetEntity target,
        ICurrentUserService currentUser,
        int? templateId = null,
        string? bodyOverride = null,
        int? followUpSequenceOverride = null,
        int? responseDeadlineDays = null,
        CancellationToken cancellationToken = default);

    Task<byte[]?> GeneratePdfAsync(
        int transactionId,
        string? targetEntity,
        string? optionalEditedContent,
        ICurrentUserService currentUser,
        int? templateId = null,
        CancellationToken cancellationToken = default);

    Task<string?> GeneratePrintViewHtmlAsync(
        int transactionId,
        string? targetEntity,
        string? optionalEditedContent,
        ICurrentUserService currentUser,
        int? templateId = null,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<FollowUpLetterTargetEntity>> ResolveTargetEntitiesAsync(
        int transactionId,
        string? targetEntityHint = null,
        CancellationToken cancellationToken = default);

    Task<bool> CanAccessTransactionAsync(int transactionId, ICurrentUserService currentUser, CancellationToken cancellationToken = default);

    Task<LetterTemplate> GetOrCreateDefaultTemplateAsync(CancellationToken cancellationToken = default);
}

public sealed class FollowUpLetterRenderService : IFollowUpLetterRenderService
{
    public const string FollowUpTemplateCode = "follow_up_letter";

    public static readonly string DefaultFollowUpContent =
        "السلام عليكم ورحمة الله وبركاته،،\n\n" +
        "إشارةً إلى المعاملة رقم {IncomingNumber} وتاريخ {IncomingDate} بشأن: {Subject}\n\n" +
        "والمحالة إلى: {TargetEntity}\n\n" +
        "نأمل سرعة الإفادة عما تم حيال الموضوع، وتزويدنا بما لديكم من مرئيات أو إجراءات أو مستندات ذات علاقة، وذلك خلال المدة النظامية، مع التأكيد على أهمية استكمال اللازم والرفع بما يتم حيالها.\n\n" +
        "والسلام عليكم ورحمة الله وبركاته،،";

    private readonly AppDbContext _db;
    private readonly IFollowUpLetterDocumentBuilder _documentBuilder;
    private readonly IFollowUpLetterPdfExporter _pdfExporter;
    private readonly IFollowUpLetterTimeZone _timeZone;
    private readonly OrganizationBrandingOptions _branding;

    public FollowUpLetterRenderService(
        AppDbContext db,
        IFollowUpLetterDocumentBuilder documentBuilder,
        IFollowUpLetterPdfExporter pdfExporter,
        IFollowUpLetterTimeZone timeZone,
        IOptions<OrganizationBrandingOptions> branding)
    {
        _db = db;
        _documentBuilder = documentBuilder;
        _pdfExporter = pdfExporter;
        _timeZone = timeZone;
        _branding = branding.Value;
    }

    public async Task<FollowUpLetterPreviewDto?> RenderFollowUpLetterAsync(
        int transactionId,
        string? targetEntity,
        string? optionalEditedContent,
        ICurrentUserService currentUser,
        int? templateId = null,
        int? responseDeadlineDays = null,
        CancellationToken cancellationToken = default)
    {
        var targets = await ResolveTargetEntitiesAsync(transactionId, targetEntity, cancellationToken);
        if (targets.Count == 0)
            return null;

        if (!await CanAccessTransactionAsync(transactionId, currentUser, cancellationToken))
            return null;

        var selectedTarget = targets[0];
        var document = await BuildDocumentAsync(
            transactionId,
            selectedTarget,
            currentUser,
            templateId,
            optionalEditedContent,
            responseDeadlineDays: responseDeadlineDays,
            cancellationToken: cancellationToken);

        if (document == null)
            return null;

        return new FollowUpLetterPreviewDto
        {
            Content = document.Body,
            TargetEntity = selectedTarget.Name,
        };
    }

    public async Task<FollowUpLetterDocumentModel?> BuildDocumentAsync(
        int transactionId,
        FollowUpLetterTargetEntity target,
        ICurrentUserService currentUser,
        int? templateId = null,
        string? bodyOverride = null,
        int? followUpSequenceOverride = null,
        int? responseDeadlineDays = null,
        CancellationToken cancellationToken = default)
    {
        if (!await CanAccessTransactionAsync(transactionId, currentUser, cancellationToken))
            return null;

        var bundle = await LoadTransactionBundleAsync(transactionId, cancellationToken);
        if (bundle == null)
            return null;

        var template = templateId.HasValue
            ? await _db.LetterTemplates.AsNoTracking().FirstOrDefaultAsync(t => t.Id == templateId.Value, cancellationToken)
            : await GetDefaultTemplateReadOnlyAsync(cancellationToken);

        if (template == null)
            return null;

        var allTargets = await ResolveTargetEntitiesAsync(transactionId, cancellationToken: cancellationToken);
        var senderDepartment = await ResolveSenderDepartmentAsync(currentUser, cancellationToken);
        var preparedBy = await ResolvePreparedByAsync(currentUser, cancellationToken);

        return _documentBuilder.Build(new FollowUpLetterDocumentBuildRequest
        {
            Transaction = bundle.Transaction,
            Template = template,
            Target = target,
            Assignments = bundle.Assignments,
            FollowUps = bundle.FollowUps,
            AllTargets = allTargets,
            BodyOverride = bodyOverride,
            FollowUpSequenceOverride = followUpSequenceOverride,
            ResponseDeadlineDays = responseDeadlineDays,
            PreparedBy = preparedBy,
            SenderDepartment = senderDepartment,
            LogoPath = _branding.LogoPath,
            TodayLocal = _timeZone.TodayDisplayDate,
        });
    }

    public async Task<byte[]?> GeneratePdfAsync(
        int transactionId,
        string? targetEntity,
        string? optionalEditedContent,
        ICurrentUserService currentUser,
        int? templateId = null,
        CancellationToken cancellationToken = default)
    {
        var targets = await ResolveTargetEntitiesAsync(transactionId, targetEntity, cancellationToken);
        if (targets.Count == 0)
            return null;

        var document = await BuildDocumentAsync(
            transactionId,
            targets[0],
            currentUser,
            templateId,
            optionalEditedContent,
            cancellationToken: cancellationToken);

        return document == null ? null : _pdfExporter.GeneratePdf(document);
    }

    public async Task<string?> GeneratePrintViewHtmlAsync(
        int transactionId,
        string? targetEntity,
        string? optionalEditedContent,
        ICurrentUserService currentUser,
        int? templateId = null,
        CancellationToken cancellationToken = default)
    {
        var targets = await ResolveTargetEntitiesAsync(transactionId, targetEntity, cancellationToken);
        if (targets.Count == 0)
            return null;

        var document = await BuildDocumentAsync(
            transactionId,
            targets[0],
            currentUser,
            templateId,
            optionalEditedContent,
            cancellationToken: cancellationToken);

        return document == null ? null : FollowUpLetterPrintViewRenderer.Render([document]);
    }

    public async Task<IReadOnlyList<FollowUpLetterTargetEntity>> ResolveTargetEntitiesAsync(
        int transactionId,
        string? targetEntityHint = null,
        CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrWhiteSpace(targetEntityHint))
            return [new FollowUpLetterTargetEntity(targetEntityHint.Trim())];

        var outgoingDepartments = await _db.TransactionOutgoingDepartments.AsNoTracking()
            .Where(x => x.TransactionId == transactionId)
            .OrderBy(x => x.Id)
            .Select(x => new FollowUpLetterTargetEntity(x.Department.Name, x.DepartmentId, null))
            .ToListAsync(cancellationToken);

        if (outgoingDepartments.Count > 0)
            return outgoingDepartments;

        var outgoingParties = await _db.TransactionOutgoingParties.AsNoTracking()
            .Where(x => x.TransactionId == transactionId)
            .OrderBy(x => x.Id)
            .Select(x => new FollowUpLetterTargetEntity(x.ExternalParty.Name, null, x.ExternalPartyId))
            .ToListAsync(cancellationToken);

        if (outgoingParties.Count > 0)
            return outgoingParties;

        var assignmentDepartments = await _db.Assignments.AsNoTracking()
            .Where(a => a.TransactionId == transactionId && a.Status == AssignmentStatus.Active)
            .OrderBy(a => a.CreatedAt)
            .Select(a => new FollowUpLetterTargetEntity(a.Department.Name, a.DepartmentId, null))
            .ToListAsync(cancellationToken);

        if (assignmentDepartments.Count > 0)
            return assignmentDepartments;

        var transaction = await _db.Transactions.AsNoTracking()
            .Include(t => t.IncomingFromParty)
            .Include(t => t.IncomingFromDepartment)
            .FirstOrDefaultAsync(t => t.Id == transactionId, cancellationToken);

        if (transaction == null)
            return [];

        var fallbackName = transaction.IncomingSourceType switch
        {
            IncomingSourceType.Internal => transaction.IncomingFromDepartment?.Name ?? transaction.IncomingFrom ?? string.Empty,
            IncomingSourceType.External => transaction.IncomingFromParty?.Name ?? transaction.IncomingFrom ?? string.Empty,
            _ => transaction.IncomingFrom ?? string.Empty,
        };

        return string.IsNullOrWhiteSpace(fallbackName)
            ? []
            : [new FollowUpLetterTargetEntity(fallbackName)];
    }

    public Task<bool> CanAccessTransactionAsync(
        int transactionId,
        ICurrentUserService currentUser,
        CancellationToken cancellationToken = default)
    {
        if (currentUser.Role != UserRole.DepartmentUser || !currentUser.DepartmentId.HasValue)
            return Task.FromResult(true);

        return _db.Assignments.AsNoTracking()
            .AnyAsync(
                a => a.TransactionId == transactionId && a.DepartmentId == currentUser.DepartmentId.Value,
                cancellationToken);
    }

    public async Task<LetterTemplate> GetOrCreateDefaultTemplateAsync(CancellationToken cancellationToken = default)
    {
        var template = await _db.LetterTemplates
            .FirstOrDefaultAsync(
                t => t.TemplateType == LetterTemplateType.FollowUp && t.IsDefault,
                cancellationToken);

        template ??= await _db.LetterTemplates
            .FirstOrDefaultAsync(t => t.Code == FollowUpTemplateCode, cancellationToken);

        if (template != null)
            return template;

        template = new LetterTemplate
        {
            Code = FollowUpTemplateCode,
            Name = "قالب خطاب التعقيب",
            Content = DefaultFollowUpContent,
            TemplateType = LetterTemplateType.FollowUp,
            IsActive = true,
            IsDefault = true,
            SortOrder = 0,
            CreatedAt = DateTime.UtcNow,
        };
        _db.LetterTemplates.Add(template);
        await _db.SaveChangesAsync(cancellationToken);
        return template;
    }

    private async Task<LetterTemplate?> GetDefaultTemplateReadOnlyAsync(CancellationToken cancellationToken)
    {
        var template = await _db.LetterTemplates.AsNoTracking()
            .Where(t => t.IsActive)
            .OrderByDescending(t => t.IsDefault)
            .ThenBy(t => t.SortOrder)
            .FirstOrDefaultAsync(t => t.TemplateType == LetterTemplateType.FollowUp, cancellationToken);

        template ??= await _db.LetterTemplates.AsNoTracking()
            .FirstOrDefaultAsync(t => t.Code == FollowUpTemplateCode, cancellationToken);

        if (template != null)
            return template;

        return new LetterTemplate
        {
            Code = FollowUpTemplateCode,
            Name = "قالب خطاب التعقيب",
            Content = DefaultFollowUpContent,
            TemplateType = LetterTemplateType.FollowUp,
            IsActive = true,
            IsDefault = true,
        };
    }

    private async Task<TransactionBundle?> LoadTransactionBundleAsync(int transactionId, CancellationToken cancellationToken)
    {
        var transaction = await _db.Transactions.AsNoTracking()
            .Include(t => t.IncomingFromParty)
            .Include(t => t.IncomingFromDepartment)
            .Include(t => t.CategoryEntity)
            .FirstOrDefaultAsync(t => t.Id == transactionId, cancellationToken);

        if (transaction == null)
            return null;

        var assignments = await _db.Assignments.AsNoTracking()
            .Include(a => a.Department)
            .Where(a => a.TransactionId == transactionId)
            .OrderByDescending(a => a.AssignedDate)
            .ToListAsync(cancellationToken);

        var followUps = await _db.FollowUps.AsNoTracking()
            .Where(f => f.TransactionId == transactionId)
            .OrderByDescending(f => f.FollowUpDate)
            .ToListAsync(cancellationToken);

        return new TransactionBundle(transaction, assignments, followUps);
    }

    private async Task<string?> ResolveSenderDepartmentAsync(ICurrentUserService currentUser, CancellationToken cancellationToken)
    {
        if (!currentUser.DepartmentId.HasValue)
            return null;

        return await _db.Departments.AsNoTracking()
            .Where(d => d.Id == currentUser.DepartmentId.Value)
            .Select(d => d.Name)
            .FirstOrDefaultAsync(cancellationToken);
    }

    private async Task<string?> ResolvePreparedByAsync(ICurrentUserService currentUser, CancellationToken cancellationToken)
    {
        if (!currentUser.IsAuthenticated)
            return null;

        return await _db.Users.AsNoTracking()
            .Where(u => u.Id == currentUser.UserId)
            .Select(u => u.FullName)
            .FirstOrDefaultAsync(cancellationToken);
    }

    private sealed record TransactionBundle(
        Transaction Transaction,
        IReadOnlyList<Assignment> Assignments,
        IReadOnlyList<FollowUp> FollowUps);
}
