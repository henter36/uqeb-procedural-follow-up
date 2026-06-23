using Microsoft.EntityFrameworkCore;
using Uqeb.Api.Data;
using Uqeb.Api.DTOs.Reports;
using Uqeb.Api.Helpers;
using Uqeb.Api.Models.Enums;
using Uqeb.Api.Reporting.Exporters;
using Uqeb.Api.Reporting.Configuration;
using Uqeb.Api.Reporting.Helpers;
using Uqeb.Api.Reporting.DTOs;
using Uqeb.Api.Reporting.Enums;
using Uqeb.Api.Reporting.Models;
using Uqeb.Api.Models.Entities;
using Uqeb.Api.Services;

namespace Uqeb.Api.Reporting.Services;

public interface IInstitutionalReportService
{
    Task<InstitutionalReportModel> BuildReportModelAsync(ReportBuildRequestDto request, CancellationToken ct = default);
    Task<RenderedReportManifestDto> RenderPreviewAsync(ReportBuildRequestDto request, CancellationToken ct = default);
    Task<ReportExportResultDto> ExportAsync(ReportExportRequestDto request, CancellationToken ct = default);
    Task<List<ReportTemplateDto>> GetTemplatesAsync(CancellationToken ct = default);
    Task<ReportTemplateDto> SaveTemplateAsync(SaveReportTemplateRequestDto request, CancellationToken ct = default);
    Task DeleteTemplateAsync(int id, CancellationToken ct = default);
}

public sealed class InstitutionalReportService : IInstitutionalReportService
{
    private const int MaxDetailRows = 5000;

    private readonly IDbContextFactory<AppDbContext> _dbFactory;
    private readonly ICurrentUserService _currentUser;
    private readonly IAuditService _audit;
    private readonly IInstitutionalReportNumberAllocator _reportNumberAllocator;
    private readonly DepartmentRatingCriteria _ratingCriteria = new();
    private readonly InstitutionalReportRenderer _renderer = new();
    private readonly InstitutionalReportPdfExporter _pdfExporter = new();
    private readonly InstitutionalReportXlsxExporter _xlsxExporter = new();
    private readonly InstitutionalReportDocxExporter _docxExporter = new();

    public InstitutionalReportService(
        IDbContextFactory<AppDbContext> dbFactory,
        ICurrentUserService currentUser,
        IAuditService audit,
        IInstitutionalReportNumberAllocator reportNumberAllocator)
    {
        _dbFactory = dbFactory;
        _currentUser = currentUser;
        _audit = audit;
        _reportNumberAllocator = reportNumberAllocator;
    }

    public Task<InstitutionalReportModel> BuildReportModelAsync(ReportBuildRequestDto request, CancellationToken ct = default) =>
        BuildInternalAsync(request, ct);

    public async Task<RenderedReportManifestDto> RenderPreviewAsync(ReportBuildRequestDto request, CancellationToken ct = default)
    {
        var model = await BuildInternalAsync(request, ct);
        var sections = ResolveSections(request);
        return _renderer.RenderManifest(model, sections);
    }

    public async Task<ReportExportResultDto> ExportAsync(ReportExportRequestDto request, CancellationToken ct = default)
    {
        var model = await BuildInternalAsync(request.BuildRequest, ct);
        var allSections = ResolveSections(request.BuildRequest);
        var manifest = _renderer.RenderManifest(model, allSections);
        var selectedPages = ResolveSelectedPages(request, manifest);

        if (selectedPages.Count == 0)
        {
            throw new FieldValidationException(new Dictionary<string, string>
            {
                ["selectedPages"] = "يجب اختيار صفحة واحدة على الأقل للتصدير."
            });
        }

        var exportManifest = _renderer.BuildExportManifest(manifest, selectedPages, request);

        if (exportManifest.Pages.Count == 0)
        {
            throw new FieldValidationException(new Dictionary<string, string>
            {
                ["selectedPages"] = "لا توجد صفحات قابلة للتصدير ضمن الاختيار الحالي."
            });
        }

        byte[] content;
        string contentType;
        string extension;

        switch (request.ExportFormat)
        {
            case ExportFormat.Docx:
                content = _docxExporter.Export(model, exportManifest, request);
                contentType = "application/vnd.openxmlformats-officedocument.wordprocessingml.document";
                extension = "docx";
                break;
            case ExportFormat.Xlsx:
                content = _xlsxExporter.Export(model, exportManifest, request);
                contentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
                extension = "xlsx";
                break;
            case ExportFormat.Html:
                content = System.Text.Encoding.UTF8.GetBytes(_renderer.RenderHtmlDocument(exportManifest));
                contentType = "text/html; charset=utf-8";
                extension = "html";
                break;
            default:
                content = _pdfExporter.Export(exportManifest, request);
                contentType = "application/pdf";
                extension = "pdf";
                break;
        }

        var fingerprint = InstitutionalReportFingerprint.Compute(content);
        model.Metadata.FileFingerprint = fingerprint;

        await _audit.LogAsync(
            _currentUser.UserId,
            AuditAction.ExportReport,
            "InstitutionalReport",
            null,
            null,
            null,
            $"format={request.ExportFormat};mode={request.ExportMode};pages={string.Join(',', selectedPages)}");

        return new ReportExportResultDto
        {
            Content = content,
            ContentType = contentType,
            FileName = BuildFileName(model.Metadata.ReportNumber, request, extension, selectedPages),
            FileFingerprint = fingerprint,
            Manifest = exportManifest.CloneWithoutHtml()
        };
    }

    public async Task<List<ReportTemplateDto>> GetTemplatesAsync(CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var templates = await db.ReportExportTemplates.AsNoTracking()
            .OrderBy(t => t.Name)
            .ToListAsync(ct);

        return templates.Select(MapTemplate).ToList();
    }

    public async Task<ReportTemplateDto> SaveTemplateAsync(SaveReportTemplateRequestDto request, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var entity = new ReportExportTemplate
        {
            Name = request.Name.Trim(),
            ReportType = request.ReportType,
            SectionIdsJson = System.Text.Json.JsonSerializer.Serialize(request.SectionIds),
            DefaultFiltersJson = System.Text.Json.JsonSerializer.Serialize(request.DefaultFilters),
            DefaultFormat = request.DefaultFormat,
            PageNumberingMode = request.PageNumberingMode,
            IncludePartialCover = request.IncludePartialCover,
            IncludePartialManifest = request.IncludePartialManifest,
            CreatedById = _currentUser.UserId,
            CreatedAt = DateTime.UtcNow
        };
        db.ReportExportTemplates.Add(entity);
        await db.SaveChangesAsync(ct);
        return MapTemplate(entity);
    }

    public async Task DeleteTemplateAsync(int id, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var entity = await db.ReportExportTemplates.FindAsync([id], ct)
            ?? throw new KeyNotFoundException("القالب غير موجود.");
        db.ReportExportTemplates.Remove(entity);
        await db.SaveChangesAsync(ct);
    }

    private async Task<InstitutionalReportModel> BuildInternalAsync(ReportBuildRequestDto request, CancellationToken ct)
    {
        var today = DateTime.UtcNow.Date;
        var snapshots = await LoadSnapshotsAsync(request, ct);
        var metrics = InstitutionalReportMetricsCalculator.Calculate(snapshots, today);
        var reportNumber = await _reportNumberAllocator.AllocateAsync(ct);
        var verificationId = Guid.NewGuid().ToString("N")[..12].ToUpperInvariant();

        var model = new InstitutionalReportModel
        {
            Metadata = new ReportMetadataDto
            {
                ReportNumber = reportNumber,
                ReportType = request.ReportType,
                ReportTypeName = ReportTypeLabel(request.ReportType),
                IssueDate = today,
                PeriodFrom = request.Filters.DateFrom ?? snapshots.MinBy(s => s.IncomingDate)?.IncomingDate ?? today,
                PeriodTo = request.Filters.DateTo ?? snapshots.MaxBy(s => s.IncomingDate)?.IncomingDate ?? today,
                Title = string.IsNullOrWhiteSpace(request.Title)
                    ? "تقرير المتابعة الإجرائية للمعاملات"
                    : request.Title.Trim(),
                Introduction = request.Introduction,
                VerificationId = verificationId,
                GeneratedAt = DateTime.UtcNow
            },
            Filters = request.Filters,
            Summary = BuildExecutiveSummary(metrics, snapshots, today),
            Charts = BuildCharts(metrics, snapshots, today),
            DepartmentPerformance = BuildDepartmentPerformance(metrics.Snapshots, today),
            Risks = BuildRisks(metrics.Snapshots, today),
            Recommendations = BuildRecommendations(metrics.Snapshots, today),
            RiskCounters = BuildRiskCounters(metrics.Snapshots, today),
            Transactions = BuildTransactionDetails(metrics.Snapshots),
            IntegrityWarnings = ValidateIntegrity(metrics)
        };

        return model;
    }

    private async Task<List<TransactionReportSnapshot>> LoadSnapshotsAsync(ReportBuildRequestDto request, CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var legacyFilter = MapLegacyFilter(request.Filters);
        var query = ApplyInstitutionalFilter(db.Transactions.AsNoTracking(), request.Filters, legacyFilter);

        if (request.ReportType == InstitutionalReportType.SingleTransaction && request.SingleTransactionId.HasValue)
            query = query.Where(t => t.Id == request.SingleTransactionId.Value);

        if (request.ReportType == InstitutionalReportType.OverdueTransactions)
            query = query.Where(t => t.Status != TransactionStatus.Closed && t.Status != TransactionStatus.Cancelled && t.Status != TransactionStatus.Archived);

        if (request.ReportType == InstitutionalReportType.JointDepartmentTransactions)
            query = query.Where(t => t.Assignments.Count(a => a.Status == AssignmentStatus.Active) > 1
                || t.OutgoingDepartments.Count > 1);

        if (request.ReportType == InstitutionalReportType.PartialResponses)
            query = query.Where(t => t.Status == TransactionStatus.PartiallyReplied
                || (t.Assignments.Any(a => a.ReplyStatus == ReplyStatus.Replied)
                    && t.Assignments.Any(a => a.RequiresReply && a.ReplyStatus != ReplyStatus.Replied && a.Status == AssignmentStatus.Active)));

        if (_currentUser.DepartmentId is int deptId && _currentUser.Role != UserRole.Admin)
            query = query.Where(t => t.Assignments.Any(a => a.DepartmentId == deptId)
                || t.OutgoingDepartments.Any(o => o.DepartmentId == deptId));

        var rows = await query
            .OrderByDescending(t => t.IncomingDate)
            .ThenByDescending(t => t.Id)
            .Take(MaxDetailRows)
            .Select(t => new
            {
                t.Id,
                t.InternalTrackingNumber,
                t.IncomingNumber,
                t.IncomingDate,
                t.Subject,
                IncomingParty = t.IncomingSourceType == IncomingSourceType.Internal
                    ? (t.IncomingFromDepartment != null ? t.IncomingFromDepartment.Name : t.IncomingFrom)
                    : (t.IncomingFromParty != null ? t.IncomingFromParty.Name : t.IncomingFrom),
                CategoryName = t.CategoryEntity != null ? t.CategoryEntity.Name : t.Category,
                t.Priority,
                t.Status,
                t.RequiresResponse,
                t.ResponseCompleted,
                t.ResponseDueDate,
                t.ClosedAt,
                t.UpdatedAt,
                t.CreatedAt,
                t.OutgoingNumber,
                t.OutgoingDate,
                Assignments = t.Assignments.Select(a => new
                {
                    a.DepartmentId,
                    DepartmentName = a.Department.Name,
                    a.RequiresReply,
                    a.ReplyStatus,
                    a.Status,
                    a.DueDate
                }).ToList(),
                OutgoingDepartments = t.OutgoingDepartments.Select(o => new
                {
                    o.DepartmentId,
                    DepartmentName = o.Department.Name
                }).ToList(),
                LastFollowUpDate = t.FollowUps.Any()
                    ? t.FollowUps.Max(f => f.CreatedAt > f.FollowUpDate ? f.CreatedAt : f.FollowUpDate)
                    : (DateTime?)null
            })
            .ToListAsync(ct);

        var today = DateTime.UtcNow.Date;
        return rows.Select(row =>
        {
            var activeAssignments = row.Assignments.Where(a => a.Status == AssignmentStatus.Active).ToList();
            var uniqueDepartments = activeAssignments
                .Select(a => new { a.DepartmentId, a.DepartmentName })
                .GroupBy(x => x.DepartmentId)
                .Select(g => g.First())
                .ToList();
            var assignmentDeptIds = uniqueDepartments.Select(x => x.DepartmentId).ToList();
            var assignmentDeptNames = uniqueDepartments.Select(x => x.DepartmentName).ToList();
            var responsible = assignmentDeptNames.FirstOrDefault() ?? "—";

            var uniqueOutgoingDepartments = row.OutgoingDepartments
                .GroupBy(o => o.DepartmentId)
                .Select(g => g.First())
                .ToList();

            var snapshot = new TransactionReportSnapshot
            {
                TransactionId = row.Id,
                TrackingNumber = row.InternalTrackingNumber,
                IncomingNumber = row.IncomingNumber,
                IncomingDate = row.IncomingDate.Date,
                Subject = row.Subject,
                IncomingParty = row.IncomingParty ?? "—",
                CategoryName = row.CategoryName,
                Priority = row.Priority,
                Status = row.Status,
                RequiresResponse = row.RequiresResponse,
                ResponseCompleted = row.ResponseCompleted,
                ResponseDueDate = row.ResponseDueDate?.Date,
                ClosedAt = row.ClosedAt?.Date,
                UpdatedAt = row.UpdatedAt?.Date,
                CreatedAt = row.CreatedAt,
                OutgoingNumber = row.OutgoingNumber,
                OutgoingDate = row.OutgoingDate?.Date,
                ResponsibleDepartment = responsible,
                ResponsibleDepartmentId = assignmentDeptIds.FirstOrDefault(),
                AssignmentDepartmentIds = assignmentDeptIds,
                AssignmentDepartmentNames = assignmentDeptNames,
                OutgoingDepartmentIds = uniqueOutgoingDepartments.Select(o => o.DepartmentId).ToList(),
                OutgoingDepartmentNames = uniqueOutgoingDepartments.Select(o => o.DepartmentName).ToList(),
                ActiveAssignmentCount = activeAssignments.Count,
                RepliedAssignmentCount = activeAssignments.Count(a => a.ReplyStatus == ReplyStatus.Replied),
                PendingReplyAssignmentCount = activeAssignments.Count(a => a.RequiresReply && a.ReplyStatus != ReplyStatus.Replied),
                LastFollowUpDate = row.LastFollowUpDate?.Date,
                LastAssignmentDueDate = activeAssignments.Where(a => a.DueDate.HasValue).Select(a => a.DueDate!.Value).OrderBy(d => d).FirstOrDefault(),
                IsClosed = row.Status == TransactionStatus.Closed,
                IsOpen = InstitutionalReportMetricsCalculator.IsOpenStatus(row.Status),
                ElapsedDays = Math.Max(0, (today - row.IncomingDate.Date).Days)
            };

            snapshot.IsOverdue = InstitutionalReportMetricsCalculator.IsOverdue(snapshot, today);
            snapshot.IsWaitingForStatement = InstitutionalReportMetricsCalculator.IsWaitingForStatement(snapshot);
            snapshot.IsPartialReply = InstitutionalReportMetricsCalculator.IsPartialReply(snapshot);
            snapshot.IsJointDepartment = InstitutionalReportMetricsCalculator.IsJointDepartment(snapshot);
            snapshot.FollowUpStages = InstitutionalReportMetricsCalculator.ResolveFollowUpStages(snapshot, today);

            return snapshot;
        }).ToList();
    }

    private static ReportFilterRequest MapLegacyFilter(ReportFiltersDto filters) => new()
    {
        DateFrom = filters.DateFrom,
        DateTo = filters.DateTo,
        CategoryId = filters.CategoryIds.FirstOrDefault(),
        DepartmentId = filters.DepartmentIds.FirstOrDefault(),
        IncomingPartyId = filters.PartyIds.FirstOrDefault(),
        Search = filters.Search
    };

    private static IQueryable<Transaction> ApplyInstitutionalFilter(
        IQueryable<Transaction> query,
        ReportFiltersDto filters,
        ReportFilterRequest legacy)
    {
        if (legacy.DateFrom.HasValue) query = query.Where(t => t.IncomingDate >= legacy.DateFrom);
        if (legacy.DateTo.HasValue) query = query.Where(t => t.IncomingDate <= legacy.DateTo);
        if (filters.CategoryIds.Count > 0) query = query.Where(t => t.CategoryId.HasValue && filters.CategoryIds.Contains(t.CategoryId.Value));
        if (filters.DepartmentIds.Count > 0)
            query = query.Where(t => t.Assignments.Any(a => filters.DepartmentIds.Contains(a.DepartmentId))
                || t.OutgoingDepartments.Any(o => filters.DepartmentIds.Contains(o.DepartmentId)));
        if (filters.PartyIds.Count > 0) query = query.Where(t => t.IncomingFromPartyId.HasValue && filters.PartyIds.Contains(t.IncomingFromPartyId.Value));
        if (filters.Priorities.Count > 0)
        {
            var parsed = filters.Priorities
                .Select(p => Enum.TryParse<Priority>(p, true, out var pr) ? (Priority?)pr : null)
                .Where(p => p.HasValue)
                .Select(p => p!.Value)
                .ToList();
            if (parsed.Count > 0) query = query.Where(t => parsed.Contains(t.Priority));
        }
        if (filters.Statuses.Count > 0)
        {
            var parsed = filters.Statuses
                .Select(s => Enum.TryParse<TransactionStatus>(s, true, out var st) ? (TransactionStatus?)st : null)
                .Where(s => s.HasValue)
                .Select(s => s!.Value)
                .ToList();
            if (parsed.Count > 0) query = query.Where(t => parsed.Contains(t.Status));
        }
        if (!string.IsNullOrWhiteSpace(filters.Search))
        {
            var term = filters.Search.Trim();
            query = query.Where(t => t.IncomingNumber.Contains(term) || t.InternalTrackingNumber.Contains(term) || t.Subject.Contains(term));
        }
        return query;
    }

    private ExecutiveSummaryDto BuildExecutiveSummary(
        InstitutionalMetricsResult metrics,
        IReadOnlyList<TransactionReportSnapshot> snapshots,
        DateTime today)
    {
        var cards = new List<KpiCardDto>
        {
            new() { Key = "total", Title = "إجمالي المعاملات", Value = metrics.TotalTransactions.ToString("N0") },
            new() { Key = "closed", Title = "المعاملات المغلقة", Value = metrics.ClosedCount.ToString("N0") },
            new() { Key = "open", Title = "المعاملات المفتوحة", Value = metrics.OpenCount.ToString("N0") },
            new() { Key = "overdue", Title = "المعاملات المتأخرة", Value = metrics.OverdueCount.ToString("N0") },
            new() { Key = "joint", Title = "معاملات الإدارات المشتركة", Value = metrics.JointDepartmentCount.ToString("N0") },
            new() { Key = "partial", Title = "الردود الجزئية", Value = metrics.PartialResponseCount.ToString("N0") },
            new() { Key = "avgDays", Title = "متوسط مدة الإنجاز", Value = $"{metrics.AverageCompletionDays:N1} يوم" },
            new() { Key = "onTime", Title = "نسبة الإنجاز ضمن المهلة", Value = $"{metrics.OnTimeCompletionRate:N1}%" }
        };

        var topOverdueDept = snapshots.Where(s => s.IsOpen && s.IsOverdue)
            .GroupBy(s => s.ResponsibleDepartment)
            .OrderByDescending(g => g.Count())
            .FirstOrDefault()?.Key;

        var narrative = $"خلال الفترة المحددة، بلغ إجمالي المعاملات الفريدة {metrics.TotalTransactions:N0} معاملة، " +
                        $"منها {metrics.ClosedCount:N0} مغلقة و{metrics.OpenCount:N0} مفتوحة. " +
                        $"تضم المعاملات المفتوحة {metrics.OverdueCount:N0} معاملة متأخرة و{metrics.PartialResponseCount:N0} ردًا جزئيًا " +
                        $"و{metrics.JointDepartmentCount:N0} معاملة مشتركة بين الإدارات. " +
                        $"بلغ متوسط مدة الإنجاز للمعاملات المغلقة {metrics.AverageCompletionDays:N1} يومًا، " +
                        $"ونسبة الإنجاز ضمن المهلة {metrics.OnTimeCompletionRate:N1}%." +
                        (topOverdueDept != null ? $" أعلى تركز للتأخير في إدارة «{topOverdueDept}»." : string.Empty);

        return new ExecutiveSummaryDto { KpiCards = cards, ExecutiveNarrative = narrative };
    }

    private static List<ChartDto> BuildCharts(InstitutionalMetricsResult metrics, IReadOnlyList<TransactionReportSnapshot> snapshots, DateTime today)
    {
        var monthly = snapshots
            .GroupBy(s => new DateTime(s.IncomingDate.Year, s.IncomingDate.Month, 1))
            .OrderBy(g => g.Key)
            .Select(g => new ChartSeriesPointDto { Label = g.Key.ToString("yyyy-MM"), Value = g.Count() })
            .ToList();

        var openClosed = new List<ChartSeriesPointDto>
        {
            new() { Label = "مفتوحة", Value = metrics.OpenCount },
            new() { Label = "مغلقة", Value = metrics.ClosedCount }
        };

        var stageGroups = snapshots.Where(s => s.IsOpen)
            .SelectMany(s => s.FollowUpStages)
            .GroupBy(s => s)
            .Select(g => new ChartSeriesPointDto
            {
                Label = InstitutionalReportMetricsCalculator.FollowUpStageLabel(g.Key),
                Value = g.Count()
            }).ToList();

        var priorityGroups = snapshots
            .GroupBy(s => s.Priority)
            .Select(g => new ChartSeriesPointDto { Label = PriorityLabel(g.Key), Value = g.Count() })
            .ToList();

        var topOverdueDepts = snapshots.Where(s => s.IsOpen && s.IsOverdue)
            .GroupBy(s => s.ResponsibleDepartment)
            .OrderByDescending(g => g.Count())
            .Take(8)
            .Select(g => new ChartSeriesPointDto { Label = g.Key, Value = g.Count() })
            .ToList();

        var avgByDept = snapshots.Where(s => s.IsClosed && s.ClosedAt.HasValue)
            .GroupBy(s => s.ResponsibleDepartment)
            .Select(g => new ChartSeriesPointDto
            {
                Label = g.Key,
                Value = (decimal)Math.Round(g.Average(x => (x.ClosedAt!.Value - x.IncomingDate).TotalDays), 1)
            })
            .OrderByDescending(x => x.Value)
            .Take(8)
            .ToList();

        return
        [
            new ChartDto { Key = "trend", Title = "الاتجاه العام للمعاملات خلال الفترة", ChartType = "line", Series = monthly },
            new ChartDto { Key = "openClosed", Title = "المعاملات المفتوحة مقابل المغلقة", ChartType = "bar", Series = openClosed },
            new ChartDto
            {
                Key = "stages",
                Title = "توزيع المعاملات المفتوحة حسب مرحلة المتابعة",
                ChartType = "donut",
                Series = stageGroups,
                Footnote = "قد تظهر المعاملة في أكثر من تصنيف متابعة فرعي."
            },
            new ChartDto { Key = "priority", Title = "توزيع المعاملات حسب الأولوية", ChartType = "donut", Series = priorityGroups },
            new ChartDto { Key = "topOverdueDepts", Title = "أعلى الإدارات في عدد المعاملات المتأخرة", ChartType = "bar", Series = topOverdueDepts },
            new ChartDto { Key = "avgByDept", Title = "متوسط مدة الإنجاز حسب الإدارة", ChartType = "bar", Series = avgByDept }
        ];
    }

    private List<DepartmentPerformanceRowDto> BuildDepartmentPerformance(IReadOnlyList<TransactionReportSnapshot> snapshots, DateTime today)
    {
        var staleThreshold = today.AddDays(-_ratingCriteria.CriticalStaleUpdateDaysThreshold);
        var deptMap = new Dictionary<int, (string Name, List<TransactionReportSnapshot> Items)>();

        foreach (var snapshot in snapshots)
        {
            var pairs = snapshot.AssignmentDepartmentIds.Count > 0
                ? snapshot.AssignmentDepartmentIds.Zip(snapshot.AssignmentDepartmentNames).ToList()
                : [(0, snapshot.ResponsibleDepartment)];

            foreach (var (deptId, deptName) in pairs)
            {
                if (!deptMap.TryGetValue(deptId, out var bucket))
                    deptMap[deptId] = (deptName, []);
                bucket.Items.Add(snapshot);
                deptMap[deptId] = bucket;
            }
        }

        return deptMap
            .Where(kv => kv.Key > 0 || kv.Value.Items.Count > 0)
            .Select(kv =>
            {
                var unique = kv.Value.Items.GroupBy(s => s.TransactionId).Select(g => g.First()).ToList();
                var closed = unique.Where(s => s.IsClosed).ToList();
                var open = unique.Where(s => s.IsOpen).ToList();
                var measurable = closed.Where(s => s.ResponseDueDate.HasValue && s.ClosedAt.HasValue).ToList();
                var onTime = measurable.Count(s => s.ClosedAt!.Value <= s.ResponseDueDate!.Value);
                var onTimeRate = measurable.Count == 0 ? 0 : Math.Round(onTime * 100.0 / measurable.Count, 1);
                var ratingMetrics = new InstitutionalReportMetricsCalculator.DepartmentPerformanceMetrics
                {
                    OnTimeCompletionRate = onTimeRate,
                    OverdueCount = open.Count(s => s.IsOverdue),
                    OldestOpenDays = open.Count == 0 ? 0 : open.Max(s => s.ElapsedDays),
                    PartialResponses = open.Count(s => s.IsPartialReply),
                    StaleUpdates = open.Count(s => (s.UpdatedAt ?? s.CreatedAt) < staleThreshold)
                };
                var rating = InstitutionalReportMetricsCalculator.RateDepartment(ratingMetrics, _ratingCriteria);
                return new DepartmentPerformanceRowDto
                {
                    DepartmentId = kv.Key,
                    DepartmentName = kv.Value.Name,
                    TotalTransactions = unique.Count,
                    ClosedCount = closed.Count,
                    OpenCount = open.Count,
                    WaitingForStatementCount = open.Count(s => s.IsWaitingForStatement),
                    OverdueCount = open.Count(s => s.IsOverdue),
                    JointDepartmentCount = unique.Count(s => s.IsJointDepartment),
                    AverageCompletionDays = closed.Count == 0 ? 0 :
                        Math.Round(closed.Average(s => (s.ClosedAt!.Value - s.IncomingDate).TotalDays), 1),
                    OnTimeCompletionRate = onTimeRate,
                    Rating = rating,
                    RatingLabel = InstitutionalReportMetricsCalculator.RatingLabel(rating)
                };
            })
            .OrderByDescending(r => r.TotalTransactions)
            .ToList();
    }

    private static List<RiskAlertRowDto> BuildRisks(IReadOnlyList<TransactionReportSnapshot> snapshots, DateTime today)
    {
        var seq = 1;
        var risks = new List<RiskAlertRowDto>();
        foreach (var s in snapshots.Where(s => s.IsOpen).OrderByDescending(s => s.ElapsedDays).Take(25))
        {
            if (s.IsOverdue)
                risks.Add(new RiskAlertRowDto
                {
                    Sequence = seq++,
                    Alert = $"معاملة متأخرة: {s.Subject}",
                    DepartmentName = s.ResponsibleDepartment,
                    Severity = RiskSeverity.High,
                    SeverityLabel = "مرتفع",
                    ElapsedDays = s.ElapsedDays,
                    SuggestedAction = "متابعة فورية وتحديد مسؤول الإنجاز"
                });
            if (s.IsPartialReply)
                risks.Add(new RiskAlertRowDto
                {
                    Sequence = seq++,
                    Alert = $"رد جزئي متوقف: {s.Subject}",
                    DepartmentName = s.ResponsibleDepartment,
                    Severity = RiskSeverity.Elevated,
                    SeverityLabel = "يحتاج متابعة",
                    ElapsedDays = s.ElapsedDays,
                    SuggestedAction = "استكمال ردود الإدارات المتبقية"
                });
        }
        return risks.Take(20).Select((r, i) => { r.Sequence = i + 1; return r; }).ToList();
    }

    private static List<RecommendationRowDto> BuildRecommendations(IReadOnlyList<TransactionReportSnapshot> snapshots, DateTime today)
    {
        var recs = new List<RecommendationRowDto>();
        var overdueDept = snapshots.Where(s => s.IsOverdue)
            .GroupBy(s => s.ResponsibleDepartment)
            .OrderByDescending(g => g.Count())
            .FirstOrDefault();
        if (overdueDept != null)
        {
            recs.Add(new RecommendationRowDto
            {
                Observation = $"تراكم {overdueDept.Count()} معاملة متأخرة في {overdueDept.Key}",
                RequiredAction = "عقد اجتماع متابعة أسبوعي وتحديد خطة إغلاق",
                ResponsibleDepartment = overdueDept.Key,
                Priority = "عالية",
                TargetDate = today.AddDays(7).ToString("yyyy-MM-dd"),
                Source = RecommendationSource.Automated,
                SourceLabel = "مولّد آليًا"
            });
        }
        return recs;
    }

    private static RiskSummaryCountersDto BuildRiskCounters(IReadOnlyList<TransactionReportSnapshot> snapshots, DateTime today)
    {
        var staleThreshold = today.AddDays(-14);
        return new RiskSummaryCountersDto
        {
            DepartmentsNeedingFollowUp = snapshots.Where(s => s.IsOpen && s.IsOverdue).Select(s => s.ResponsibleDepartment).Distinct().Count(),
            OpenJointDepartmentTransactions = snapshots.Count(s => s.IsOpen && s.IsJointDepartment),
            PartialResponses = snapshots.Count(s => s.IsOpen && s.IsPartialReply),
            TransactionsWithoutRecentUpdate = snapshots.Count(s => s.IsOpen && (s.UpdatedAt ?? s.CreatedAt) < staleThreshold),
            DataIntegrityIssues = 0
        };
    }

    private static List<TransactionDetailRowDto> BuildTransactionDetails(IReadOnlyList<TransactionReportSnapshot> snapshots) =>
        snapshots.Select((s, index) => new TransactionDetailRowDto
        {
            Sequence = index + 1,
            TransactionId = s.TransactionId,
            TrackingNumber = s.TrackingNumber,
            IncomingNumber = s.IncomingNumber,
            IncomingDate = s.IncomingDate,
            Subject = s.Subject,
            IncomingParty = s.IncomingParty,
            ResponsibleDepartment = s.ResponsibleDepartment,
            JointDepartments = string.Join("، ", s.AssignmentDepartmentNames.Concat(s.OutgoingDepartmentNames).Distinct()),
            Priority = PriorityLabel(s.Priority),
            Status = StatusLabel(s.Status),
            FollowUpStage = string.Join("، ", s.FollowUpStages.Select(InstitutionalReportMetricsCalculator.FollowUpStageLabel)),
            ElapsedDays = s.ElapsedDays,
            DueDate = s.ResponseDueDate?.ToString("yyyy-MM-dd"),
            LastActionDate = (s.UpdatedAt ?? s.LastFollowUpDate)?.ToString("yyyy-MM-dd"),
            ResponseState = s.ResponseCompleted ? "مكتمل" : s.RequiresResponse ? "بانتظار" : "—",
            OutgoingNumber = s.OutgoingNumber,
            OutgoingDate = s.OutgoingDate?.ToString("yyyy-MM-dd")
        }).ToList();

    private static List<IntegrityWarningDto> ValidateIntegrity(InstitutionalMetricsResult metrics)
    {
        var warnings = new List<IntegrityWarningDto>();
        var bucketTotal = metrics.OpenCount + metrics.ClosedCount + metrics.CancelledCount + metrics.ArchivedCount;
        if (bucketTotal != metrics.TotalTransactions)
        {
            warnings.Add(new IntegrityWarningDto
            {
                Code = "TOTAL_MISMATCH",
                Message = "مجموع المفتوحة والمغلقة والملغاة والمؤرشفة لا يساوي إجمالي المعاملات الفريدة.",
                Severity = "critical"
            });
        }
        if (metrics.OverdueCount > metrics.OpenCount)
        {
            warnings.Add(new IntegrityWarningDto
            {
                Code = "OVERDUE_OUTSIDE_OPEN",
                Message = "عدد المتأخرة يتجاوز عدد المفتوحة.",
                Severity = "critical"
            });
        }
        return warnings;
    }

    private static List<ReportSectionId> ResolveSections(ReportBuildRequestDto request)
    {
        if (request.SectionIds.Count > 0)
            return request.SectionIds.Distinct().ToList();

        return
        [
            ReportSectionId.Cover,
            ReportSectionId.ExecutiveSummary,
            ReportSectionId.IndicatorsDashboard,
            ReportSectionId.DepartmentPerformance,
            ReportSectionId.RisksAndAlerts,
            ReportSectionId.ExecutiveRecommendations,
            ReportSectionId.TransactionDetails,
            ReportSectionId.ReportMetadata
        ];
    }

    private static List<int> ResolveSelectedPages(ReportExportRequestDto request, RenderedReportManifestDto manifest)
    {
        return request.ExportMode switch
        {
            ExportMode.CurrentPage when request.CurrentPageNumber.HasValue => [request.CurrentPageNumber.Value],
            ExportMode.SelectedPages when !string.IsNullOrWhiteSpace(request.PageRangeExpression)
                => ParsePageRangeOrThrow(request.PageRangeExpression, manifest.TotalPages),
            ExportMode.SelectedPages when request.SelectedPageNumbers.Count > 0 => request.SelectedPageNumbers.Distinct().OrderBy(p => p).ToList(),
            ExportMode.SelectedPages => throw new FieldValidationException(new Dictionary<string, string>
            {
                ["selectedPages"] = "يجب تحديد الصفحات عبر التحديد اليدوي أو نطاق الصفحات."
            }),
            ExportMode.SelectedSections => manifest.Pages
                .Where(p => request.SelectedSectionIds.Contains(p.SectionId))
                .Select(p => p.OriginalPageNumber)
                .Distinct()
                .OrderBy(p => p)
                .ToList(),
            _ => manifest.Pages.Select(p => p.OriginalPageNumber).ToList()
        };
    }

    private static List<int> ParsePageRangeOrThrow(string expression, int totalPages)
    {
        var parsed = ReportPageRangeParser.Parse(expression, totalPages);
        if (!parsed.IsValid)
        {
            throw new FieldValidationException(new Dictionary<string, string>
            {
                ["pageRangeExpression"] = parsed.ErrorMessage ?? "صيغة الصفحات غير صالحة."
            });
        }

        if (parsed.PageNumbers.Count == 0)
        {
            throw new FieldValidationException(new Dictionary<string, string>
            {
                ["pageRangeExpression"] = "لم يتم العثور على صفحات صالحة ضمن التعبير المحدد."
            });
        }

        return parsed.PageNumbers;
    }

    private static string BuildFileName(string reportNumber, ReportExportRequestDto request, string extension, List<int> pages)
    {
        var baseName = reportNumber.Replace('/', '-');
        return request.ExportMode switch
        {
            ExportMode.SelectedSections => $"{baseName}-SECTIONS.{extension}",
            ExportMode.SelectedPages or ExportMode.CurrentPage => pages.Count <= 6
                ? $"{baseName}-PAGES-{string.Join('-', pages)}.{extension}"
                : $"{baseName}-PAGES-{Guid.NewGuid():N[..8]}.{extension}",
            _ => $"{baseName}-FULL.{extension}"
        };
    }

    private static string ReportTypeLabel(InstitutionalReportType type) => type switch
    {
        InstitutionalReportType.OverdueTransactions => "تقرير المعاملات المتأخرة",
        InstitutionalReportType.JointDepartmentTransactions => "تقرير معاملات الإدارات المشتركة",
        InstitutionalReportType.PartialResponses => "تقرير الإفادات والردود الجزئية",
        InstitutionalReportType.SingleTransaction => "تقرير معاملة واحدة",
        _ => "التقرير التنفيذي الشامل"
    };

    private static string PriorityLabel(Priority priority) => priority switch
    {
        Priority.Urgent => "عاجل",
        Priority.VeryUrgent => "عاجل جدًا",
        _ => "عادي"
    };

    private static string StatusLabel(TransactionStatus status) => status switch
    {
        TransactionStatus.Closed => "مغلقة",
        TransactionStatus.WaitingForReply => "بانتظار رد",
        TransactionStatus.PartiallyReplied => "رد جزئي",
        TransactionStatus.Overdue => "متأخرة",
        TransactionStatus.ReadyForResponse => "جاهزة للإفادة",
        _ => "مفتوحة"
    };

    private static ReportTemplateDto MapTemplate(ReportExportTemplate entity) => new()
    {
        Id = entity.Id,
        Name = entity.Name,
        ReportType = entity.ReportType,
        SectionIds = System.Text.Json.JsonSerializer.Deserialize<List<ReportSectionId>>(entity.SectionIdsJson) ?? [],
        DefaultFilters = System.Text.Json.JsonSerializer.Deserialize<ReportFiltersDto>(entity.DefaultFiltersJson) ?? new(),
        DefaultFormat = entity.DefaultFormat,
        PageNumberingMode = entity.PageNumberingMode,
        IncludePartialCover = entity.IncludePartialCover,
        IncludePartialManifest = entity.IncludePartialManifest
    };
}
