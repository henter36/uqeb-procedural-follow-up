using Microsoft.EntityFrameworkCore;
using Uqeb.Api.Data;
using Uqeb.Api.DTOs.Transactions;
using Uqeb.Api.Helpers;
using Uqeb.Api.Models.Entities;
using Uqeb.Api.Models.Enums;

namespace Uqeb.Api.Services;

public interface IAttachmentService
{
    Task<List<AttachmentDto>> GetByTransactionAsync(int transactionId);
    Task<AttachmentDto> UploadAsync(int transactionId, IFormFile file, string? attachmentType, int userId);
    Task<(byte[] Content, string ContentType, string FileName)?> DownloadAsync(int transactionId, int attachmentId);
}

public class AttachmentService : IAttachmentService
{
    private readonly AppDbContext _db;
    private readonly IAuditService _audit;
    private readonly string _storagePath;

    public AttachmentService(AppDbContext db, IAuditService audit, IConfiguration config)
    {
        _db = db;
        _audit = audit;
        _storagePath = FileStoragePathResolver.Resolve(config["FileStorage:Path"]);
        Directory.CreateDirectory(_storagePath);
    }

    public async Task<List<AttachmentDto>> GetByTransactionAsync(int transactionId)
    {
        return await _db.Attachments
            .Include(a => a.UploadedBy)
            .Where(a => a.TransactionId == transactionId)
            .Select(a => new AttachmentDto
            {
                Id = a.Id,
                AttachmentType = a.AttachmentType,
                OriginalFileName = a.OriginalFileName,
                ContentType = a.ContentType,
                FileSize = a.FileSize,
                UploadedByName = a.UploadedBy.FullName,
                UploadedAt = a.UploadedAt
            })
            .ToListAsync();
    }

    public async Task<AttachmentDto> UploadAsync(int transactionId, IFormFile file, string? attachmentType, int userId)
    {
        var transaction = await _db.Transactions.FindAsync(transactionId)
            ?? throw new InvalidOperationException("المعاملة غير موجودة");

        var storedName = $"{Guid.NewGuid()}{Path.GetExtension(file.FileName)}";
        var filePath = Path.Combine(_storagePath, storedName);

        using (var stream = new FileStream(filePath, FileMode.Create))
            await file.CopyToAsync(stream);

        var attachment = new Attachment
        {
            TransactionId = transactionId,
            AttachmentType = attachmentType,
            OriginalFileName = file.FileName,
            StoredFileName = storedName,
            FilePath = filePath,
            ContentType = file.ContentType,
            FileSize = file.Length,
            UploadedById = userId,
            UploadedAt = DateTime.UtcNow
        };

        _db.Attachments.Add(attachment);
        await _db.SaveChangesAsync();
        await _audit.LogAsync(userId, AuditAction.Update, "Attachment", attachment.Id, transactionId, null, file.FileName);

        var user = await _db.Users.FindAsync(userId);
        return new AttachmentDto
        {
            Id = attachment.Id,
            AttachmentType = attachment.AttachmentType,
            OriginalFileName = attachment.OriginalFileName,
            ContentType = attachment.ContentType,
            FileSize = attachment.FileSize,
            UploadedByName = user?.FullName ?? "",
            UploadedAt = attachment.UploadedAt
        };
    }

    public async Task<(byte[] Content, string ContentType, string FileName)?> DownloadAsync(int transactionId, int attachmentId)
    {
        var attachment = await _db.Attachments
            .FirstOrDefaultAsync(a => a.Id == attachmentId && a.TransactionId == transactionId);
        if (attachment == null || !File.Exists(attachment.FilePath)) return null;

        var content = await File.ReadAllBytesAsync(attachment.FilePath);
        return (content, attachment.ContentType ?? "application/octet-stream", attachment.OriginalFileName);
    }
}
