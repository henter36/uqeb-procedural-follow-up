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
        FollowUpLetterBuildRequest request);

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

    Task<string> GenerateTemplatePreviewHtmlAsync(
        LetterTemplatePreviewRequest request,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<FollowUpLetterTargetEntity>> ResolveTargetEntitiesAsync(
        int transactionId,
        string? targetEntityHint = null,
        CancellationToken cancellationToken = default);

    Task<Dictionary<int, IReadOnlyList<FollowUpLetterTargetEntity>>> ResolveTargetEntitiesBulkAsync(
        IReadOnlyList<int> transactionIds,
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
    private readonly FollowUpLettersOptions _printOptions;

    public FollowUpLetterRenderService(
        AppDbContext db,
        IFollowUpLetterDocumentBuilder documentBuilder,
        IFollowUpLetterPdfExporter pdfExporter,
        IFollowUpLetterTimeZone timeZone,
        IOptions<OrganizationBrandingOptions> branding,
        IOptions<FollowUpLettersOptions> printOptions)
    {
        _db = db;
        _documentBuilder = documentBuilder;
        _pdfExporter = pdfExporter;
        _timeZone = timeZone;
        _branding = branding.Value;
        _printOptions = printOptions.Value;
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
        var document = await BuildDocumentAsync(new FollowUpLetterBuildRequest
        {
            TransactionId = transactionId,
            Target = selectedTarget,
            CurrentUser = currentUser,
            TemplateId = templateId,
            BodyOverride = optionalEditedContent,
            ResponseDeadlineDays = responseDeadlineDays,
            CancellationToken = cancellationToken,
        });

        if (document == null)
            return null;

        return new FollowUpLetterPreviewDto
        {
            Content = document.Body,
            TargetEntity = selectedTarget.Name,
        };
    }

    public async Task<FollowUpLetterDocumentModel?> BuildDocumentAsync(FollowUpLetterBuildRequest request)
    {
        if (!await CanAccessTransactionAsync(request.TransactionId, request.CurrentUser, request.CancellationToken))
            return null;

        var bundle = await LoadTransactionBundleAsync(request.TransactionId, request.CancellationToken);
        if (bundle == null)
            return null;

        var template = request.TemplateId.HasValue
            ? await _db.LetterTemplates.AsNoTracking().FirstOrDefaultAsync(
                t => t.Id == request.TemplateId.Value &&
                     t.TemplateType == LetterTemplateType.FollowUp &&
                     t.IsActive,
                request.CancellationToken)
            : await GetDefaultTemplateReadOnlyAsync(request.CancellationToken);

        if (template == null)
            throw new InvalidOperationException("قالب خطاب التعقيب غير موجود أو غير نشط.");

        var allTargets = await ResolveTargetEntitiesAsync(request.TransactionId, cancellationToken: request.CancellationToken);
        var senderDepartment = await ResolveSenderDepartmentAsync(request.CurrentUser, request.CancellationToken);
        var preparedBy = await ResolvePreparedByAsync(request.CurrentUser, request.CancellationToken);

        return _documentBuilder.Build(new FollowUpLetterDocumentBuildRequest
        {
            Transaction = bundle.Transaction,
            Template = template,
            Target = request.Target,
            Assignments = bundle.Assignments,
            FollowUps = bundle.FollowUps,
            AllTargets = allTargets,
            BodyOverride = request.BodyOverride,
            FollowUpSequenceOverride = request.FollowUpSequenceOverride,
            ResponseDeadlineDays = request.ResponseDeadlineDays,
            PreparedBy = preparedBy,
            SenderDepartment = senderDepartment,
            LogoPath = _branding.LogoPath,
            TodayLocal = _timeZone.TodayDisplayDate,
            SignatoryPosition = request.SignatoryPosition,
            SignatoryRank = request.SignatoryRank,
            SignatoryNameOverride = request.SignatoryNameOverride,
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

        var document = await BuildDocumentAsync(new FollowUpLetterBuildRequest
        {
            TransactionId = transactionId,
            Target = targets[0],
            CurrentUser = currentUser,
            TemplateId = templateId,
            BodyOverride = optionalEditedContent,
            CancellationToken = cancellationToken,
        });

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

        var document = await BuildDocumentAsync(new FollowUpLetterBuildRequest
        {
            TransactionId = transactionId,
            Target = targets[0],
            CurrentUser = currentUser,
            TemplateId = templateId,
            BodyOverride = optionalEditedContent,
            CancellationToken = cancellationToken,
        });

        return document == null ? null : FollowUpLetterPrintViewRenderer.Render([document], _printOptions);
    }

    public Task<string> GenerateTemplatePreviewHtmlAsync(
        LetterTemplatePreviewRequest request,
        CancellationToken cancellationToken = default)
    {
        var today = _timeZone.TodayDisplayDate;
        var content = string.IsNullOrWhiteSpace(request.Content)
            ? DefaultFollowUpContent
            : request.Content;

        var body = FollowUpLetterVariableReplacer.Render(
            content,
            FollowUpLetterVariableReplacer.BuildValues(new FollowUpLetterRenderContext
            {
                TransactionId = 1001,
                TransactionNumber = "TR-1001",
                IncomingNumber = "وارد-تجريبي-1447",
                IncomingDateLocal = today.AddDays(-18),
                Subject = "موضوع تجريبي لمعاينة خطاب التعقيب",
                TargetEntity = "إدارة تجريبية",
                TargetEntities = "إدارة تجريبية، جهة خارجية تجريبية",
                TargetDepartments = "إدارة تجريبية",
                AssignmentDateLocal = today.AddDays(-14),
                DueDateLocal = today.AddDays(-3),
                DaysOverdue = 3,
                Priority = "عاجل",
                Category = "تصنيف تجريبي",
                TodayLocal = today,
                SenderDepartment = "الإدارة العامة للمتابعة",
                PreparedBy = "اسم معد الخطاب",
                FollowUpNumber = "تعقيب-تجريبي-001",
                FollowUpDateLocal = today.AddDays(-7),
                FollowUpSequence = 2,
                FollowUpSequenceText = "التعقيب الثاني",
                ResponseDeadlineDays = 5,
            }));

        var document = new FollowUpLetterDocumentModel
        {
            TransactionId = 1001,
            TemplateId = null,
            LogoPath = OrganizationBrandingPaths.LogoApiUrl,
            OrganizationName = "الإدارة العامة للمتابعة",
            LetterNumber = "خطاب-تجريبي-001",
            GregorianDate = HijriDateFormatter.FormatGregorianArabic(today),
            HijriDate = HijriDateFormatter.Format(today) ?? string.Empty,
            Recipient = "إدارة تجريبية",
            Subject = "موضوع تجريبي لمعاينة خطاب التعقيب",
            Body = body,
            SenderDepartment = "الإدارة العامة للمتابعة",
            FollowUpSequence = 2,
            FollowUpSequenceText = "التعقيب الثاني",
            ResponseDeadlineDays = 5,
            Footer = "هذه معاينة تجريبية لا تعتمد كسجل رسمي.",
            SignatoryName = "اسم صاحب الصلاحية",
            SignatoryTitle = "مدير المتابعة",
        };

        return Task.FromResult(FollowUpLetterPrintViewRenderer.Render([document], _printOptions, "معاينة القالب"));
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

    public async Task<Dictionary<int, IReadOnlyList<FollowUpLetterTargetEntity>>> ResolveTargetEntitiesBulkAsync(
        IReadOnlyList<int> transactionIds,
        CancellationToken cancellationToken = default)
    {
        if (transactionIds.Count == 0)
            return [];

        var distinctIds = transactionIds.Distinct().ToList();
        var result = new Dictionary<int, IReadOnlyList<FollowUpLetterTargetEntity>>();

        await FillOutgoingDepartmentsAsync(result, distinctIds, cancellationToken);
        await FillOutgoingPartiesAsync(result, distinctIds, cancellationToken);
        await FillAssignmentDepartmentsAsync(result, distinctIds, cancellationToken);
        await FillFallbackNamesAsync(result, distinctIds, cancellationToken);

        foreach (var transactionId in distinctIds.Where(id => !result.ContainsKey(id)))
            result[transactionId] = [];

        return result;
    }

    private async Task FillOutgoingDepartmentsAsync(
        Dictionary<int, IReadOnlyList<FollowUpLetterTargetEntity>> result,
        List<int> distinctIds,
        CancellationToken cancellationToken)
    {
        var rows = await _db.TransactionOutgoingDepartments.AsNoTracking()
            .Where(x => distinctIds.Contains(x.TransactionId))
            .OrderBy(x => x.TransactionId).ThenBy(x => x.Id)
            .Select(x => new { x.TransactionId, Target = new FollowUpLetterTargetEntity(x.Department.Name, x.DepartmentId, null) })
            .ToListAsync(cancellationToken);

        foreach (var group in rows.GroupBy(x => x.TransactionId))
            result[group.Key] = group.Select(x => x.Target).ToList();
    }

    private async Task FillOutgoingPartiesAsync(
        Dictionary<int, IReadOnlyList<FollowUpLetterTargetEntity>> result,
        List<int> distinctIds,
        CancellationToken cancellationToken)
    {
        var remaining = distinctIds.Where(id => !result.ContainsKey(id)).ToList();
        if (remaining.Count == 0)
            return;

        var rows = await _db.TransactionOutgoingParties.AsNoTracking()
            .Where(x => remaining.Contains(x.TransactionId))
            .OrderBy(x => x.TransactionId).ThenBy(x => x.Id)
            .Select(x => new { x.TransactionId, Target = new FollowUpLetterTargetEntity(x.ExternalParty.Name, null, x.ExternalPartyId) })
            .ToListAsync(cancellationToken);

        foreach (var group in rows.GroupBy(x => x.TransactionId))
            result[group.Key] = group.Select(x => x.Target).ToList();
    }

    private async Task FillAssignmentDepartmentsAsync(
        Dictionary<int, IReadOnlyList<FollowUpLetterTargetEntity>> result,
        List<int> distinctIds,
        CancellationToken cancellationToken)
    {
        var remaining = distinctIds.Where(id => !result.ContainsKey(id)).ToList();
        if (remaining.Count == 0)
            return;

        var rows = await _db.Assignments.AsNoTracking()
            .Where(a => remaining.Contains(a.TransactionId) && a.Status == AssignmentStatus.Active)
            .OrderBy(a => a.TransactionId).ThenBy(a => a.CreatedAt)
            .Select(a => new { a.TransactionId, Target = new FollowUpLetterTargetEntity(a.Department.Name, a.DepartmentId, null) })
            .ToListAsync(cancellationToken);

        foreach (var group in rows.GroupBy(x => x.TransactionId))
            result[group.Key] = group.Select(x => x.Target).ToList();
    }

    private async Task FillFallbackNamesAsync(
        Dictionary<int, IReadOnlyList<FollowUpLetterTargetEntity>> result,
        List<int> distinctIds,
        CancellationToken cancellationToken)
    {
        var remaining = distinctIds.Where(id => !result.ContainsKey(id)).ToList();
        if (remaining.Count == 0)
            return;

        var transactions = await _db.Transactions.AsNoTracking()
            .Include(t => t.IncomingFromParty)
            .Include(t => t.IncomingFromDepartment)
            .Where(t => remaining.Contains(t.Id))
            .ToListAsync(cancellationToken);

        foreach (var transaction in transactions)
        {
            var fallbackName = transaction.IncomingSourceType switch
            {
                IncomingSourceType.Internal => transaction.IncomingFromDepartment?.Name ?? transaction.IncomingFrom ?? string.Empty,
                IncomingSourceType.External => transaction.IncomingFromParty?.Name ?? transaction.IncomingFrom ?? string.Empty,
                _ => transaction.IncomingFrom ?? string.Empty,
            };

            if (!string.IsNullOrWhiteSpace(fallbackName))
                result[transaction.Id] = [new FollowUpLetterTargetEntity(fallbackName)];
        }
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
                t => t.TemplateType == LetterTemplateType.FollowUp && t.IsDefault && t.IsActive,
                cancellationToken);

        template ??= await _db.LetterTemplates
            .FirstOrDefaultAsync(
                t => t.Code == FollowUpTemplateCode &&
                     t.TemplateType == LetterTemplateType.FollowUp &&
                     t.IsActive,
                cancellationToken);

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
            .FirstOrDefaultAsync(
                t => t.Code == FollowUpTemplateCode &&
                     t.TemplateType == LetterTemplateType.FollowUp &&
                     t.IsActive,
                cancellationToken);

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
