using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Uqeb.Api.Data;
using Uqeb.Api.Models.Entities;
using Uqeb.Api.Models.Enums;
using Xunit;

namespace Uqeb.Api.Tests;

/// <summary>
/// Regression coverage for a confirmed data-isolation defect: the transaction attachment
/// metadata and download endpoints (TransactionsController.GetAttachments/DownloadAttachment)
/// used to call AttachmentService directly with no authorization check at all, so any
/// authenticated user — including a DepartmentUser with no involvement in the transaction —
/// could list and download another department's attachments by guessing sequential IDs. The
/// fix routes both endpoints through the same CanAccessTransactionAsync gate already used by
/// GetAssignments/GetFollowUps/GetAuditLog.
/// </summary>
public class TransactionAttachmentAuthorizationTests : IClassFixture<TransactionAttachmentAuthorizationFactory>
{
    private readonly TransactionAttachmentAuthorizationFactory _factory;

    public TransactionAttachmentAuthorizationTests(TransactionAttachmentAuthorizationFactory factory)
    {
        _factory = factory;
        _factory.EnsureSeeded();
    }

    [Fact]
    public async Task GetAttachments_OwnDepartmentUser_ReturnsMetadata()
    {
        using var client = CreateClient("DepartmentUser", _factory.OwningDepartmentId, userId: 10);

        var response = await client.GetAsync($"/api/transactions/{_factory.TransactionId}/attachments");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var attachments = await response.Content.ReadFromJsonAsync<List<AttachmentListItem>>();
        Assert.NotNull(attachments);
        var attachment = Assert.Single(attachments);
        Assert.Equal("report.pdf", attachment.OriginalFileName);
    }

    [Fact]
    public async Task GetAttachments_OtherDepartmentUser_ReturnsNotFound()
    {
        using var client = CreateClient("DepartmentUser", _factory.OtherDepartmentId, userId: 11);

        var response = await client.GetAsync($"/api/transactions/{_factory.TransactionId}/attachments");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task DownloadAttachment_OwnDepartmentUser_ReturnsFileContent()
    {
        using var client = CreateClient("DepartmentUser", _factory.OwningDepartmentId, userId: 10);

        var response = await client.GetAsync(
            $"/api/transactions/{_factory.TransactionId}/attachments/{_factory.AttachmentId}/download");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var bytes = await response.Content.ReadAsByteArrayAsync();
        Assert.Equal(TransactionAttachmentAuthorizationFactory.FileBytes, bytes);
    }

    [Fact]
    public async Task DownloadAttachment_OtherDepartmentUser_ReturnsNotFound_NotFileContent()
    {
        using var client = CreateClient("DepartmentUser", _factory.OtherDepartmentId, userId: 11);

        var response = await client.GetAsync(
            $"/api/transactions/{_factory.TransactionId}/attachments/{_factory.AttachmentId}/download");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetAttachments_Admin_ReturnsMetadata_RegardlessOfDepartment()
    {
        using var client = CreateClient("Admin", departmentId: null, userId: 1);

        var response = await client.GetAsync($"/api/transactions/{_factory.TransactionId}/attachments");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private HttpClient CreateClient(string role, int? departmentId, int userId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            TestJwtHelper.CreateToken(role, departmentId: departmentId, userId: userId));
        return client;
    }

    private sealed class AttachmentListItem
    {
        public int Id { get; set; }
        public string? OriginalFileName { get; set; }
    }
}

public sealed class TransactionAttachmentAuthorizationFactory : WebApplicationFactory<Program>
{
    internal static readonly byte[] FileBytes = "attachment-content"u8.ToArray();

    private readonly object _seedLock = new();
    private bool _seeded;
    private readonly string _storagePath = Path.Combine(
        Path.GetTempPath(), "uqeb-attachment-auth-tests", Guid.NewGuid().ToString("N"));

    internal int TransactionId { get; private set; }
    internal int AttachmentId { get; private set; }
    internal int OwningDepartmentId { get; private set; }
    internal int OtherDepartmentId { get; private set; }

    protected override void ConfigureWebHost(IWebHostBuilder builder) =>
        HealthTestHostBuilder.Configure(
            builder,
            inMemoryDatabaseName: $"transaction-attachment-auth-{Guid.NewGuid():N}",
            extraConfig: new Dictionary<string, string?>
            {
                ["FileStorage:Path"] = _storagePath,
            });

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (!disposing)
        {
            return;
        }

        try
        {
            if (Directory.Exists(_storagePath))
            {
                Directory.Delete(_storagePath, recursive: true);
            }
        }
        catch (IOException)
        {
            // Best-effort test cleanup only.
        }
        catch (UnauthorizedAccessException)
        {
            // Best-effort test cleanup only.
        }
    }

    internal void EnsureSeeded()
    {
        lock (_seedLock)
        {
            if (_seeded)
                return;

            Directory.CreateDirectory(_storagePath);

            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Database.EnsureDeleted();
            db.Database.EnsureCreated();

            var user = new User
            {
                Username = "attachment-auth-admin",
                PasswordHash = "hash",
                FullName = "Attachment Auth Admin",
                Role = UserRole.Admin,
                IsActive = true,
            };
            db.Users.Add(user);

            var owningDept = new Department { Name = "الإدارة المالكة", IsActive = true };
            var otherDept = new Department { Name = "إدارة أخرى", IsActive = true };
            db.Departments.AddRange(owningDept, otherDept);
            db.SaveChanges();

            OwningDepartmentId = owningDept.Id;
            OtherDepartmentId = otherDept.Id;

            var transaction = new Transaction
            {
                InternalTrackingNumber = "INT-ATT-1",
                IncomingNumber = "IN-ATT-1",
                IncomingDate = DateTime.UtcNow.Date,
                Subject = "معاملة اختبار المرفقات",
                Status = TransactionStatus.Assigned,
                CreatedById = user.Id,
            };
            db.Transactions.Add(transaction);
            db.SaveChanges();
            TransactionId = transaction.Id;

            db.Assignments.Add(new Assignment
            {
                TransactionId = transaction.Id,
                DepartmentId = owningDept.Id,
                Status = AssignmentStatus.Active,
                RequiresReply = true,
                ReplyStatus = ReplyStatus.Pending,
                AssignedDate = DateTime.UtcNow.Date,
                CreatedById = user.Id,
            });
            db.SaveChanges();

            var storedFileName = $"{Guid.NewGuid()}.pdf";
            var filePath = Path.Combine(_storagePath, storedFileName);
            File.WriteAllBytes(filePath, FileBytes);

            var attachment = new Attachment
            {
                TransactionId = transaction.Id,
                OriginalFileName = "report.pdf",
                StoredFileName = storedFileName,
                FilePath = filePath,
                ContentType = "application/pdf",
                FileSize = FileBytes.Length,
                UploadedById = user.Id,
                UploadedAt = DateTime.UtcNow,
            };
            db.Attachments.Add(attachment);
            db.SaveChanges();
            AttachmentId = attachment.Id;

            _seeded = true;
        }
    }
}
