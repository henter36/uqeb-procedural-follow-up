using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Uqeb.Api.Data;
using Uqeb.Api.DTOs.DepartmentResponses;
using Uqeb.Api.Helpers;
using Uqeb.Api.Models.Entities;
using Uqeb.Api.Models.Enums;

namespace Uqeb.Api.Services;

public interface IDepartmentResponseService
{
    Task<List<DepartmentTransactionResponseItemDto>> GetDepartmentTransactionsAsync(ICurrentUserService currentUser);
    Task<List<DepartmentResponseSummaryDto>> GetMyDepartmentResponsesAsync(ICurrentUserService currentUser);
    Task<List<DepartmentResponseSummaryDto>> GetPendingReviewAsync(ICurrentUserService currentUser, CancellationToken cancellationToken = default);
    Task<DepartmentResponseStatsDto> GetMyStatsAsync(ICurrentUserService currentUser);
    Task<DepartmentResponseDto?> GetByIdAsync(int id, ICurrentUserService currentUser);
    Task<DepartmentResponseDto> CreateAsync(CreateDepartmentResponseRequest request, ICurrentUserService currentUser);
    Task<DepartmentResponseDto> UpdateAsync(int id, UpdateDepartmentResponseRequest request, ICurrentUserService currentUser);
    Task<DepartmentResponseDto> SubmitAsync(int id, ICurrentUserService currentUser);
    Task<DepartmentResponseDto> ApproveAsync(int id, ICurrentUserService currentUser);
    Task<DepartmentResponseDto> ReturnForCorrectionAsync(int id, ReviewDepartmentResponseRequest request, ICurrentUserService currentUser);
    Task<DepartmentResponseDto> RejectAsync(int id, ReviewDepartmentResponseRequest request, ICurrentUserService currentUser);
    Task<DepartmentResponseAttachmentDto> UploadAttachmentAsync(int id, IFormFile file, ICurrentUserService currentUser);
    Task DeleteAttachmentAsync(int id, int attachmentId, ICurrentUserService currentUser);
    Task<(byte[] Content, string ContentType, string FileName)?> DownloadAttachmentAsync(int id, int attachmentId, ICurrentUserService currentUser);
}

public class DepartmentResponseService : IDepartmentResponseService
{
    private const long MaxFileSizeBytes = 10 * 1024 * 1024; // 10 MB
    private const string DepartmentResponseEntityName = "DepartmentResponse";
    private const string DepartmentResponseAttachmentEntityName = "DepartmentResponseAttachment";
    private const string ResponseNotFoundMessage = "الرد غير موجود.";

    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
        { ".pdf", ".jpg", ".jpeg", ".png", ".docx" };
    private static readonly HashSet<string> BlockedExtensions = new(StringComparer.OrdinalIgnoreCase)
        { ".exe", ".bat", ".cmd", ".sh", ".ps1", ".vbs", ".js", ".dll", ".msi" };

    private readonly AppDbContext _db;
    private readonly IAuditService _audit;
    private readonly string _storagePath;

    public DepartmentResponseService(AppDbContext db, IAuditService audit, IConfiguration config)
    {
        _db = db;
        _audit = audit;
        _storagePath = FileStoragePathResolver.Resolve(config["FileStorage:Path"], "DepartmentResponses");
        Directory.CreateDirectory(_storagePath);
    }

    private static int ResolveDepartmentIdForCreate(
        CreateDepartmentResponseRequest request,
        ICurrentUserService currentUser)
    {
        if (currentUser.Role is UserRole.Admin or UserRole.Supervisor or UserRole.DataEntry)
        {
            return request.DepartmentId
                ?? currentUser.DepartmentId
                ?? throw new InvalidOperationException("يجب تحديد الإدارة عند إنشاء إفادة نيابة عن إدارة أخرى.");
        }

        var departmentId = currentUser.DepartmentId
            ?? throw new UnauthorizedAccessException("المستخدم غير مرتبط بإدارة.");

        if (request.DepartmentId.HasValue && request.DepartmentId.Value != departmentId)
            throw new UnauthorizedAccessException("لا تملك صلاحية تسجيل إفادة لإدارة أخرى.");

        return departmentId;
    }

    private static void RequireReviewer(ICurrentUserService currentUser)
    {
        if (currentUser.Role is not (UserRole.Admin or UserRole.Supervisor or UserRole.DataEntry))
            throw new UnauthorizedAccessException("المستخدم غير مخول بمراجعة إفادات الإدارات.");
    }

    // InMemory (test) provider throws InvalidOperationException on BeginTransactionAsync.
    // Production (SQL Server) supports transactions fully.
    private async Task<IDbContextTransaction?> TryBeginTransactionAsync()
    {
        try { return await _db.Database.BeginTransactionAsync(); }
        catch (InvalidOperationException) { return null; }
    }

    public async Task<List<DepartmentTransactionResponseItemDto>> GetDepartmentTransactionsAsync(ICurrentUserService currentUser)
    {
        if (!currentUser.DepartmentId.HasValue)
            return [];

        int deptId = currentUser.DepartmentId.Value;

        var department = await _db.Departments
            .AsNoTracking()
            .Where(d => d.Id == deptId)
            .Select(d => new { d.Id, d.Name })
            .FirstOrDefaultAsync();

        if (department == null)
            return [];

        var assigned = await _db.Assignments
            .AsNoTracking()
            .Where(a => a.DepartmentId == deptId && a.Status == AssignmentStatus.Active)
            .Select(a => new { a.TransactionId, a.AssignedDate })
            .ToListAsync();

        if (assigned.Count == 0)
            return [];

        var txIds = assigned.Select(a => a.TransactionId).ToList();

        var transactions = await _db.Transactions
            .AsNoTracking()
            .Where(t => txIds.Contains(t.Id))
            .Select(t => new {
                t.Id,
                t.InternalTrackingNumber,
                t.Subject,
                t.IncomingDate,
                Priority = t.Priority.ToString()
            })
            .ToListAsync();

        var responses = await _db.DepartmentResponses
            .AsNoTracking()
            .Where(r => r.DepartmentId == deptId && txIds.Contains(r.TransactionId))
            .Select(r => new { r.TransactionId, r.Id, Status = r.Status.ToString() })
            .ToListAsync();

        var responseByTx = responses.ToDictionary(r => r.TransactionId);
        var assignedDateByTx = assigned.ToDictionary(a => a.TransactionId, a => a.AssignedDate);

        var editableStatuses = new[] { "Draft", "ReturnedForCorrection" };

        return transactions
            .Select(t =>
            {
                responseByTx.TryGetValue(t.Id, out var resp);
                assignedDateByTx.TryGetValue(t.Id, out var assignedDate);

                bool canCreate = resp == null;
                bool canEdit = resp != null && editableStatuses.Contains(resp.Status);
                bool canSubmit = canEdit;

                return new DepartmentTransactionResponseItemDto(
                    t.Id,
                    t.InternalTrackingNumber,
                    t.Subject,
                    t.IncomingDate,
                    t.Priority,
                    assignedDate == default ? null : (DateTime?)assignedDate,
                    deptId,
                    department.Name,
                    resp?.Id,
                    resp?.Status,
                    canCreate,
                    canEdit,
                    canSubmit);
            })
            .OrderByDescending(x => x.AssignedDate)
            .ToList();
    }

    public async Task<List<DepartmentResponseSummaryDto>> GetMyDepartmentResponsesAsync(ICurrentUserService currentUser)
    {
        if (currentUser.Role == UserRole.DepartmentUser && !currentUser.DepartmentId.HasValue)
            return [];

        var query = _db.DepartmentResponses
            .Include(r => r.Transaction)
            .Include(r => r.Department)
            .AsNoTracking();

        if (currentUser.Role == UserRole.DepartmentUser)
            query = query.Where(r => r.DepartmentId == currentUser.DepartmentId!.Value);

        return await query
            .OrderByDescending(r => r.CreatedAt)
            .Select(r => new DepartmentResponseSummaryDto(
                r.Id,
                r.TransactionId,
                r.Transaction.Subject,
                r.Transaction.InternalTrackingNumber,
                r.DepartmentId,
                r.Department.Name,
                r.Status.ToString(),
                r.SubmittedAt,
                r.CreatedAt))
            .ToListAsync();
    }

    public async Task<DepartmentResponseStatsDto> GetMyStatsAsync(ICurrentUserService currentUser)
    {
        if (!currentUser.DepartmentId.HasValue)
            return new DepartmentResponseStatsDto(0, 0, 0, 0, 0, 0, 0);

        int deptId = currentUser.DepartmentId.Value;

        var totalAssigned = await _db.Assignments
            .AsNoTracking()
            .CountAsync(a => a.DepartmentId == deptId && a.Status == AssignmentStatus.Active);

        var responseCounts = await _db.DepartmentResponses
            .AsNoTracking()
            .Where(r => r.DepartmentId == deptId)
            .GroupBy(r => r.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync();

        int CountFor(DepartmentResponseStatus s) =>
            responseCounts.FirstOrDefault(x => x.Status == s)?.Count ?? 0;

        var draft = CountFor(DepartmentResponseStatus.Draft);
        var submittedForReview = CountFor(DepartmentResponseStatus.SubmittedForReview);
        var returnedForCorrection = CountFor(DepartmentResponseStatus.ReturnedForCorrection);
        var approved = CountFor(DepartmentResponseStatus.Approved);
        var rejected = CountFor(DepartmentResponseStatus.Rejected);

        var totalWithResponse = draft + submittedForReview + returnedForCorrection + approved + rejected;
        var pendingResponse = Math.Max(0, totalAssigned - totalWithResponse);

        return new DepartmentResponseStatsDto(
            totalAssigned,
            pendingResponse,
            draft,
            submittedForReview,
            returnedForCorrection,
            approved,
            rejected);
    }

    public async Task<List<DepartmentResponseSummaryDto>> GetPendingReviewAsync(
        ICurrentUserService currentUser,
        CancellationToken cancellationToken = default)
    {
        RequireReviewer(currentUser);
        return await _db.DepartmentResponses
            .Include(r => r.Transaction)
            .Include(r => r.Department)
            .AsNoTracking()
            .Where(r => r.Status == DepartmentResponseStatus.SubmittedForReview)
            .OrderBy(r => r.SubmittedAt)
            .Select(r => new DepartmentResponseSummaryDto(
                r.Id,
                r.TransactionId,
                r.Transaction.Subject,
                r.Transaction.InternalTrackingNumber,
                r.DepartmentId,
                r.Department.Name,
                r.Status.ToString(),
                r.SubmittedAt,
                r.CreatedAt))
            .ToListAsync(cancellationToken);
    }

    public async Task<DepartmentResponseDto?> GetByIdAsync(int id, ICurrentUserService currentUser)
    {
        var r = await LoadWithDetailsAsync(id);
        if (r == null) return null;
        if (!CanRead(r, currentUser)) return null;
        return MapToDto(r);
    }

    public async Task<DepartmentResponseDto> CreateAsync(CreateDepartmentResponseRequest request, ICurrentUserService currentUser)
    {
        if (!await _db.Transactions.AnyAsync(t => t.Id == request.TransactionId))
            throw new InvalidOperationException("المعاملة غير موجودة.");

        var deptId = ResolveDepartmentIdForCreate(request, currentUser);

        var isAssigned = await _db.Assignments.AnyAsync(a =>
            a.TransactionId == request.TransactionId &&
            a.DepartmentId == deptId &&
            a.Status == AssignmentStatus.Active);
        if (!isAssigned)
            throw new InvalidOperationException("لا يوجد تكليف نشط لهذه الإدارة في هذه المعاملة.");

        var alreadyExists = await _db.DepartmentResponses.AnyAsync(r =>
            r.TransactionId == request.TransactionId && r.DepartmentId == deptId);
        if (alreadyExists)
            throw new InvalidOperationException("يوجد رد إدارة مسبق لهذه المعاملة من إدارتك.");

        var response = new DepartmentResponse
        {
            TransactionId = request.TransactionId,
            DepartmentId = deptId,
            ResponseText = request.ResponseText,
            Status = DepartmentResponseStatus.Draft,
            SubmittedByUserId = currentUser.UserId,
            CreatedAt = DateTime.UtcNow,
        };

        var tx = await TryBeginTransactionAsync();
        try
        {
            _db.DepartmentResponses.Add(response);
            await _db.SaveChangesAsync();

            _audit.TrackLog(
                currentUser.UserId,
                AuditAction.DepartmentResponseCreated,
                DepartmentResponseEntityName,
                response.Id,
                response.TransactionId,
                null,
                null);
            await _db.SaveChangesAsync();

            if (tx != null) await tx.CommitAsync();
        }
        catch
        {
            if (tx != null) await tx.RollbackAsync();
            throw;
        }
        finally
        {
            if (tx != null) await tx.DisposeAsync();
        }

        return MapToDto((await LoadWithDetailsAsync(response.Id))!);
    }

    public async Task<DepartmentResponseDto> UpdateAsync(int id, UpdateDepartmentResponseRequest request, ICurrentUserService currentUser)
    {
        var response = await _db.DepartmentResponses.FindAsync(id)
            ?? throw new InvalidOperationException(ResponseNotFoundMessage);

        RequireDepartmentOwnership(response, currentUser);
        RequireEditableStatus(response);

        response.ResponseText = request.ResponseText;
        response.UpdatedAt = DateTime.UtcNow;

        _audit.TrackLog(currentUser.UserId, AuditAction.DepartmentResponseUpdated, DepartmentResponseEntityName, id, response.TransactionId, null, null);
        await _db.SaveChangesAsync();

        return MapToDto((await LoadWithDetailsAsync(id))!);
    }

    public async Task<DepartmentResponseDto> SubmitAsync(int id, ICurrentUserService currentUser)
    {
        var response = await _db.DepartmentResponses.FindAsync(id)
            ?? throw new InvalidOperationException(ResponseNotFoundMessage);

        RequireDepartmentOwnership(response, currentUser);

        if (response.Status != DepartmentResponseStatus.Draft &&
            response.Status != DepartmentResponseStatus.ReturnedForCorrection)
            throw new InvalidOperationException($"لا يمكن تقديم الرد من الحالة الحالية: {response.Status}.");

        if (string.IsNullOrWhiteSpace(response.ResponseText))
            throw new InvalidOperationException("نص الرد مطلوب قبل التقديم.");

        var previousStatus = response.Status;
        response.Status = DepartmentResponseStatus.SubmittedForReview;
        response.SubmittedAt = DateTime.UtcNow;
        response.ReviewedByUserId = null;
        response.ReviewedAt = null;
        response.ReviewNote = null;
        response.UpdatedAt = DateTime.UtcNow;

        _audit.TrackLog(currentUser.UserId, AuditAction.DepartmentResponseSubmitted, DepartmentResponseEntityName, id, response.TransactionId, previousStatus.ToString(), DepartmentResponseStatus.SubmittedForReview.ToString());
        await _db.SaveChangesAsync();

        return MapToDto((await LoadWithDetailsAsync(id))!);
    }

    public async Task<DepartmentResponseDto> ApproveAsync(int id, ICurrentUserService currentUser)
    {
        RequireReviewer(currentUser);

        var response = await _db.DepartmentResponses.FindAsync(id)
            ?? throw new InvalidOperationException(ResponseNotFoundMessage);

        if (response.Status != DepartmentResponseStatus.SubmittedForReview)
            throw new InvalidOperationException("لا يمكن قبول الرد إلا إذا كان في حالة 'مقدّم للمراجعة'.");

        response.Status = DepartmentResponseStatus.Approved;
        response.ReviewedByUserId = currentUser.UserId;
        response.ReviewedAt = DateTime.UtcNow;
        response.UpdatedAt = DateTime.UtcNow;

        _audit.TrackLog(currentUser.UserId, AuditAction.DepartmentResponseApproved, DepartmentResponseEntityName, id, response.TransactionId, DepartmentResponseStatus.SubmittedForReview.ToString(), DepartmentResponseStatus.Approved.ToString());
        await _db.SaveChangesAsync();

        return MapToDto((await LoadWithDetailsAsync(id))!);
    }

    public async Task<DepartmentResponseDto> ReturnForCorrectionAsync(int id, ReviewDepartmentResponseRequest request, ICurrentUserService currentUser)
    {
        RequireReviewer(currentUser);

        var response = await _db.DepartmentResponses.FindAsync(id)
            ?? throw new InvalidOperationException(ResponseNotFoundMessage);

        if (response.Status != DepartmentResponseStatus.SubmittedForReview)
            throw new InvalidOperationException("لا يمكن إعادة الرد إلا إذا كان في حالة 'مقدّم للمراجعة'.");

        if (string.IsNullOrWhiteSpace(request.ReviewNote))
            throw new InvalidOperationException("ملاحظة الإعادة مطلوبة.");

        response.Status = DepartmentResponseStatus.ReturnedForCorrection;
        response.ReviewedByUserId = currentUser.UserId;
        response.ReviewedAt = DateTime.UtcNow;
        response.ReviewNote = request.ReviewNote;
        response.UpdatedAt = DateTime.UtcNow;

        _audit.TrackLog(currentUser.UserId, AuditAction.DepartmentResponseReturned, DepartmentResponseEntityName, id, response.TransactionId, DepartmentResponseStatus.SubmittedForReview.ToString(), DepartmentResponseStatus.ReturnedForCorrection.ToString());
        await _db.SaveChangesAsync();

        return MapToDto((await LoadWithDetailsAsync(id))!);
    }

    public async Task<DepartmentResponseDto> RejectAsync(int id, ReviewDepartmentResponseRequest request, ICurrentUserService currentUser)
    {
        RequireReviewer(currentUser);

        var response = await _db.DepartmentResponses.FindAsync(id)
            ?? throw new InvalidOperationException(ResponseNotFoundMessage);

        if (response.Status != DepartmentResponseStatus.SubmittedForReview)
            throw new InvalidOperationException("لا يمكن رفض الرد إلا إذا كان في حالة 'مقدّم للمراجعة'.");

        if (string.IsNullOrWhiteSpace(request.ReviewNote))
            throw new InvalidOperationException("سبب الرفض مطلوب.");

        response.Status = DepartmentResponseStatus.Rejected;
        response.ReviewedByUserId = currentUser.UserId;
        response.ReviewedAt = DateTime.UtcNow;
        response.ReviewNote = request.ReviewNote;
        response.UpdatedAt = DateTime.UtcNow;

        _audit.TrackLog(currentUser.UserId, AuditAction.DepartmentResponseRejected, DepartmentResponseEntityName, id, response.TransactionId, DepartmentResponseStatus.SubmittedForReview.ToString(), DepartmentResponseStatus.Rejected.ToString());
        await _db.SaveChangesAsync();

        return MapToDto((await LoadWithDetailsAsync(id))!);
    }

    public async Task<DepartmentResponseAttachmentDto> UploadAttachmentAsync(int id, IFormFile file, ICurrentUserService currentUser)
    {
        var response = await _db.DepartmentResponses.FindAsync(id)
            ?? throw new InvalidOperationException(ResponseNotFoundMessage);

        RequireDepartmentOwnership(response, currentUser);
        RequireEditableStatus(response);

        if (file.Length > MaxFileSizeBytes)
            throw new InvalidOperationException($"حجم الملف يتجاوز الحد المسموح ({MaxFileSizeBytes / 1024 / 1024} MB).");

        // Strip any path components the client may have included in the filename
        var safeOriginalName = Path.GetFileName(file.FileName ?? string.Empty);
        var ext = Path.GetExtension(safeOriginalName);
        if (BlockedExtensions.Contains(ext))
            throw new InvalidOperationException("نوع الملف غير مسموح به.");
        if (!AllowedExtensions.Contains(ext))
            throw new InvalidOperationException("يُسمح فقط بملفات PDF وJPG وPNG وDOCX.");

        // Derive MIME type from extension; do not trust the client-supplied ContentType
        var mimeType = ext.ToLowerInvariant() switch
        {
            ".pdf" => "application/pdf",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            _ => "application/octet-stream",
        };

        // Use a pure GUID with no user-derived components so the stored path
        // contains no tainted data. ContentType and OriginalFileName in the DB
        // carry all information needed to serve the file correctly.
        var storedName = Guid.NewGuid().ToString("N");
        var filePath = Path.Combine(_storagePath, storedName);

        string sha256;
        using (var stream = new FileStream(filePath, FileMode.Create))
        {
            await file.CopyToAsync(stream);
        }
        using (var hashStream = File.OpenRead(filePath))
        {
            var hashBytes = await System.Security.Cryptography.SHA256.HashDataAsync(hashStream);
            sha256 = Convert.ToHexString(hashBytes).ToLowerInvariant();
        }

        var attachment = new DepartmentResponseAttachment
        {
            DepartmentResponseId = id,
            OriginalFileName = safeOriginalName,
            StoredFileName = storedName,
            ContentType = mimeType,
            FileSizeBytes = file.Length,
            StoragePath = filePath,
            UploadedByUserId = currentUser.UserId,
            UploadedAt = DateTime.UtcNow,
            Sha256 = sha256,
        };

        var tx = await TryBeginTransactionAsync();
        try
        {
            _db.DepartmentResponseAttachments.Add(attachment);
            await _db.SaveChangesAsync();

            _audit.TrackLog(
                currentUser.UserId,
                AuditAction.DepartmentResponseAttachmentUploaded,
                DepartmentResponseAttachmentEntityName,
                attachment.Id,
                response.TransactionId,
                null,
                safeOriginalName);
            await _db.SaveChangesAsync();

            if (tx != null) await tx.CommitAsync();
        }
        catch
        {
            if (tx != null) await tx.RollbackAsync();
            if (File.Exists(filePath))
                try { File.Delete(filePath); } catch { /* best effort */ }
            throw;
        }
        finally
        {
            if (tx != null) await tx.DisposeAsync();
        }

        var uploader = await _db.Users.FindAsync(currentUser.UserId);
        return new DepartmentResponseAttachmentDto(
            attachment.Id,
            attachment.OriginalFileName,
            attachment.ContentType,
            attachment.FileSizeBytes,
            uploader?.FullName ?? "",
            attachment.UploadedAt);
    }

    public async Task DeleteAttachmentAsync(int id, int attachmentId, ICurrentUserService currentUser)
    {
        var response = await _db.DepartmentResponses.FindAsync(id)
            ?? throw new InvalidOperationException(ResponseNotFoundMessage);

        RequireDepartmentOwnership(response, currentUser);
        RequireEditableStatus(response);

        var attachment = await _db.DepartmentResponseAttachments
            .FirstOrDefaultAsync(a => a.Id == attachmentId && a.DepartmentResponseId == id && !a.IsDeleted)
            ?? throw new InvalidOperationException("المرفق غير موجود.");

        attachment.IsDeleted = true;
        attachment.DeletedByUserId = currentUser.UserId;
        attachment.DeletedAt = DateTime.UtcNow;

        _audit.TrackLog(currentUser.UserId, AuditAction.DepartmentResponseAttachmentDeleted, DepartmentResponseAttachmentEntityName, attachmentId, response.TransactionId, attachment.OriginalFileName, null);
        await _db.SaveChangesAsync();
    }

    public async Task<(byte[] Content, string ContentType, string FileName)?> DownloadAttachmentAsync(int id, int attachmentId, ICurrentUserService currentUser)
    {
        var response = await _db.DepartmentResponses.FindAsync(id);
        if (response == null) return null;
        if (!CanRead(response, currentUser)) return null;

        var attachment = await _db.DepartmentResponseAttachments
            .FirstOrDefaultAsync(a => a.Id == attachmentId && a.DepartmentResponseId == id && !a.IsDeleted);
        if (attachment == null || !File.Exists(attachment.StoragePath)) return null;

        var content = await File.ReadAllBytesAsync(attachment.StoragePath);

        if (!string.IsNullOrEmpty(attachment.Sha256))
        {
            var actualHash = Convert
                .ToHexString(System.Security.Cryptography.SHA256.HashData(content))
                .ToLowerInvariant();
            if (!string.Equals(actualHash, attachment.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("فشل التحقق من سلامة المرفق.");
        }

        return (content, attachment.ContentType ?? "application/octet-stream", attachment.OriginalFileName);
    }

    private async Task<DepartmentResponse?> LoadWithDetailsAsync(int id)
    {
        return await _db.DepartmentResponses
            .Include(r => r.Transaction)
            .Include(r => r.Department)
            .Include(r => r.SubmittedBy)
            .Include(r => r.ReviewedBy)
            .Include(r => r.Attachments.Where(a => !a.IsDeleted))
                .ThenInclude(a => a.UploadedBy)
            .FirstOrDefaultAsync(r => r.Id == id);
    }

    private static bool CanRead(DepartmentResponse response, ICurrentUserService currentUser)
    {
        if (currentUser.Role is UserRole.Admin or UserRole.Supervisor or UserRole.DataEntry)
            return true;
        return currentUser.Role == UserRole.DepartmentUser &&
               currentUser.DepartmentId == response.DepartmentId;
    }

    private static void RequireDepartmentOwnership(DepartmentResponse response, ICurrentUserService currentUser)
    {
        if (currentUser.Role is UserRole.Admin or UserRole.Supervisor or UserRole.DataEntry)
            return;
        if (currentUser.DepartmentId != response.DepartmentId)
            throw new UnauthorizedAccessException("لا تملك صلاحية تعديل رد إدارة أخرى.");
    }

    private static void RequireEditableStatus(DepartmentResponse response)
    {
        if (response.Status != DepartmentResponseStatus.Draft &&
            response.Status != DepartmentResponseStatus.ReturnedForCorrection)
            throw new InvalidOperationException("لا يمكن تعديل الرد في حالته الحالية.");
    }

    private static DepartmentResponseDto MapToDto(DepartmentResponse r) => new(
        r.Id,
        r.TransactionId,
        r.Transaction.Subject,
        r.Transaction.InternalTrackingNumber,
        r.DepartmentId,
        r.Department.Name,
        r.ResponseText,
        r.Status.ToString(),
        r.SubmittedBy.FullName,
        r.SubmittedAt,
        r.ReviewedBy?.FullName,
        r.ReviewedAt,
        r.ReviewNote,
        r.CreatedAt,
        r.UpdatedAt,
        r.Attachments
            .OrderBy(a => a.UploadedAt)
            .Select(a => new DepartmentResponseAttachmentDto(
                a.Id,
                a.OriginalFileName,
                a.ContentType,
                a.FileSizeBytes,
                a.UploadedBy.FullName,
                a.UploadedAt))
            .ToList()
    );
}
