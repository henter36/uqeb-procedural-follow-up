using Microsoft.EntityFrameworkCore;
using Uqeb.Api.Data;
using Uqeb.Api.DTOs.DepartmentResponses;
using Uqeb.Api.Models.Entities;
using Uqeb.Api.Models.Enums;

namespace Uqeb.Api.Services;

public interface IDepartmentResponseService
{
    Task<List<DepartmentTransactionItem>> GetDepartmentTransactionsAsync(ICurrentUserService currentUser);
    Task<List<DepartmentResponseSummaryDto>> GetMyDepartmentResponsesAsync(ICurrentUserService currentUser);
    Task<List<DepartmentResponseSummaryDto>> GetPendingReviewAsync();
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
        var basePath = config["FileStorage:Path"] ?? Path.Combine(Directory.GetCurrentDirectory(), "Attachments");
        _storagePath = Path.Combine(basePath, "DepartmentResponses");
        Directory.CreateDirectory(_storagePath);
    }

    public async Task<List<DepartmentTransactionItem>> GetDepartmentTransactionsAsync(ICurrentUserService currentUser)
    {
        if (!currentUser.DepartmentId.HasValue)
            return [];

        int deptId = currentUser.DepartmentId.Value;

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
            .Select(t => new { t.Id, t.InternalTrackingNumber, t.Subject, Status = t.Status.ToString() })
            .ToListAsync();

        var responses = await _db.DepartmentResponses
            .AsNoTracking()
            .Where(r => r.DepartmentId == deptId && txIds.Contains(r.TransactionId))
            .Select(r => new { r.TransactionId, r.Id, Status = r.Status.ToString() })
            .ToListAsync();

        var responseByTx = responses.ToDictionary(r => r.TransactionId);
        var assignedDateByTx = assigned.ToDictionary(a => a.TransactionId, a => a.AssignedDate);

        return transactions
            .Select(t =>
            {
                responseByTx.TryGetValue(t.Id, out var resp);
                assignedDateByTx.TryGetValue(t.Id, out var assignedDate);
                return new DepartmentTransactionItem(
                    t.Id,
                    t.InternalTrackingNumber,
                    t.Subject,
                    t.Status,
                    assignedDate == default ? null : (DateTime?)assignedDate,
                    resp?.Id,
                    resp?.Status);
            })
            .OrderByDescending(x => x.AssignedDate)
            .ToList();
    }

    public async Task<List<DepartmentResponseSummaryDto>> GetMyDepartmentResponsesAsync(ICurrentUserService currentUser)
    {
        if (currentUser.Role == UserRole.DepartmentUser)
        {
            if (!currentUser.DepartmentId.HasValue)
                return [];
        }

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

    public async Task<List<DepartmentResponseSummaryDto>> GetPendingReviewAsync()
    {
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
            .ToListAsync();
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
        var transaction = await _db.Transactions.FindAsync(request.TransactionId)
            ?? throw new InvalidOperationException("المعاملة غير موجودة.");

        if (currentUser.Role == UserRole.DepartmentUser && currentUser.DepartmentId.HasValue)
        {
            var isAssigned = await _db.Assignments.AnyAsync(a =>
                a.TransactionId == request.TransactionId &&
                a.DepartmentId == currentUser.DepartmentId.Value &&
                a.Status == AssignmentStatus.Active);
            if (!isAssigned)
                throw new InvalidOperationException("لا يوجد تكليف نشط لإدارتك في هذه المعاملة.");
        }

        var deptId = currentUser.DepartmentId
            ?? throw new InvalidOperationException("المستخدم غير مرتبط بإدارة.");

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

        _db.DepartmentResponses.Add(response);
        _audit.TrackLog(currentUser.UserId, AuditAction.DepartmentResponseCreated, "DepartmentResponse", null, request.TransactionId, null, null);
        await _db.SaveChangesAsync();

        return MapToDto((await LoadWithDetailsAsync(response.Id))!);
    }

    public async Task<DepartmentResponseDto> UpdateAsync(int id, UpdateDepartmentResponseRequest request, ICurrentUserService currentUser)
    {
        var response = await _db.DepartmentResponses.FindAsync(id)
            ?? throw new InvalidOperationException("الرد غير موجود.");

        RequireDepartmentOwnership(response, currentUser);
        RequireEditableStatus(response);

        response.ResponseText = request.ResponseText;
        response.UpdatedAt = DateTime.UtcNow;

        _audit.TrackLog(currentUser.UserId, AuditAction.DepartmentResponseUpdated, "DepartmentResponse", id, response.TransactionId, null, null);
        await _db.SaveChangesAsync();

        return MapToDto((await LoadWithDetailsAsync(id))!);
    }

    public async Task<DepartmentResponseDto> SubmitAsync(int id, ICurrentUserService currentUser)
    {
        var response = await _db.DepartmentResponses.FindAsync(id)
            ?? throw new InvalidOperationException("الرد غير موجود.");

        RequireDepartmentOwnership(response, currentUser);

        if (response.Status != DepartmentResponseStatus.Draft &&
            response.Status != DepartmentResponseStatus.ReturnedForCorrection)
            throw new InvalidOperationException($"لا يمكن تقديم الرد من الحالة الحالية: {response.Status}.");

        if (string.IsNullOrWhiteSpace(response.ResponseText))
            throw new InvalidOperationException("نص الرد مطلوب قبل التقديم.");

        response.Status = DepartmentResponseStatus.SubmittedForReview;
        response.SubmittedAt = DateTime.UtcNow;
        response.UpdatedAt = DateTime.UtcNow;

        _audit.TrackLog(currentUser.UserId, AuditAction.DepartmentResponseSubmitted, "DepartmentResponse", id, response.TransactionId, DepartmentResponseStatus.Draft.ToString(), DepartmentResponseStatus.SubmittedForReview.ToString());
        await _db.SaveChangesAsync();

        return MapToDto((await LoadWithDetailsAsync(id))!);
    }

    public async Task<DepartmentResponseDto> ApproveAsync(int id, ICurrentUserService currentUser)
    {
        var response = await _db.DepartmentResponses.FindAsync(id)
            ?? throw new InvalidOperationException("الرد غير موجود.");

        if (response.Status != DepartmentResponseStatus.SubmittedForReview)
            throw new InvalidOperationException("لا يمكن قبول الرد إلا إذا كان في حالة 'مقدّم للمراجعة'.");

        response.Status = DepartmentResponseStatus.Approved;
        response.ReviewedByUserId = currentUser.UserId;
        response.ReviewedAt = DateTime.UtcNow;
        response.UpdatedAt = DateTime.UtcNow;

        _audit.TrackLog(currentUser.UserId, AuditAction.DepartmentResponseApproved, "DepartmentResponse", id, response.TransactionId, DepartmentResponseStatus.SubmittedForReview.ToString(), DepartmentResponseStatus.Approved.ToString());
        await _db.SaveChangesAsync();

        return MapToDto((await LoadWithDetailsAsync(id))!);
    }

    public async Task<DepartmentResponseDto> ReturnForCorrectionAsync(int id, ReviewDepartmentResponseRequest request, ICurrentUserService currentUser)
    {
        var response = await _db.DepartmentResponses.FindAsync(id)
            ?? throw new InvalidOperationException("الرد غير موجود.");

        if (response.Status != DepartmentResponseStatus.SubmittedForReview)
            throw new InvalidOperationException("لا يمكن إعادة الرد إلا إذا كان في حالة 'مقدّم للمراجعة'.");

        if (string.IsNullOrWhiteSpace(request.ReviewNote))
            throw new InvalidOperationException("ملاحظة الإعادة مطلوبة.");

        response.Status = DepartmentResponseStatus.ReturnedForCorrection;
        response.ReviewedByUserId = currentUser.UserId;
        response.ReviewedAt = DateTime.UtcNow;
        response.ReviewNote = request.ReviewNote;
        response.UpdatedAt = DateTime.UtcNow;

        _audit.TrackLog(currentUser.UserId, AuditAction.DepartmentResponseReturned, "DepartmentResponse", id, response.TransactionId, DepartmentResponseStatus.SubmittedForReview.ToString(), DepartmentResponseStatus.ReturnedForCorrection.ToString());
        await _db.SaveChangesAsync();

        return MapToDto((await LoadWithDetailsAsync(id))!);
    }

    public async Task<DepartmentResponseDto> RejectAsync(int id, ReviewDepartmentResponseRequest request, ICurrentUserService currentUser)
    {
        var response = await _db.DepartmentResponses.FindAsync(id)
            ?? throw new InvalidOperationException("الرد غير موجود.");

        if (response.Status != DepartmentResponseStatus.SubmittedForReview)
            throw new InvalidOperationException("لا يمكن رفض الرد إلا إذا كان في حالة 'مقدّم للمراجعة'.");

        if (string.IsNullOrWhiteSpace(request.ReviewNote))
            throw new InvalidOperationException("سبب الرفض مطلوب.");

        response.Status = DepartmentResponseStatus.Rejected;
        response.ReviewedByUserId = currentUser.UserId;
        response.ReviewedAt = DateTime.UtcNow;
        response.ReviewNote = request.ReviewNote;
        response.UpdatedAt = DateTime.UtcNow;

        _audit.TrackLog(currentUser.UserId, AuditAction.DepartmentResponseRejected, "DepartmentResponse", id, response.TransactionId, DepartmentResponseStatus.SubmittedForReview.ToString(), DepartmentResponseStatus.Rejected.ToString());
        await _db.SaveChangesAsync();

        return MapToDto((await LoadWithDetailsAsync(id))!);
    }

    public async Task<DepartmentResponseAttachmentDto> UploadAttachmentAsync(int id, IFormFile file, ICurrentUserService currentUser)
    {
        var response = await _db.DepartmentResponses.FindAsync(id)
            ?? throw new InvalidOperationException("الرد غير موجود.");

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

        var storedName = $"{Guid.NewGuid()}{ext}";
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

        _db.DepartmentResponseAttachments.Add(attachment);
        _audit.TrackLog(currentUser.UserId, AuditAction.DepartmentResponseAttachmentUploaded, "DepartmentResponseAttachment", null, response.TransactionId, null, file.FileName);
        await _db.SaveChangesAsync();

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
            ?? throw new InvalidOperationException("الرد غير موجود.");

        RequireDepartmentOwnership(response, currentUser);
        RequireEditableStatus(response);

        var attachment = await _db.DepartmentResponseAttachments
            .FirstOrDefaultAsync(a => a.Id == attachmentId && a.DepartmentResponseId == id && !a.IsDeleted)
            ?? throw new InvalidOperationException("المرفق غير موجود.");

        attachment.IsDeleted = true;
        attachment.DeletedByUserId = currentUser.UserId;
        attachment.DeletedAt = DateTime.UtcNow;

        _audit.TrackLog(currentUser.UserId, AuditAction.DepartmentResponseAttachmentDeleted, "DepartmentResponseAttachment", attachmentId, response.TransactionId, attachment.OriginalFileName, null);
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
            throw new InvalidOperationException("لا تملك صلاحية تعديل رد إدارة أخرى.");
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
