using System.Text.Json;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Uqeb.Api.Configuration;
using Uqeb.Api.Data;
using Uqeb.Api.DTOs.FollowUpPrint;
using Uqeb.Api.HostedServices;
using Uqeb.Api.Models.Entities;
using Uqeb.Api.Models.Enums;
using Uqeb.Api.Models.Letters;
using Uqeb.Api.Services;
using Xunit;

namespace Uqeb.Api.Tests.Letters;

public class FollowUpPrintJobResumeSqlServerTests
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private static string? ConnectionString =>
        Environment.GetEnvironmentVariable("UQEB_TRANSACTION_TEST_CONNECTION")
        ?? Environment.GetEnvironmentVariable("UQEB_REFERENCE_DATA_TEST_CONNECTION");

    private static bool IsSqlServerAvailable()
    {
        if (string.IsNullOrWhiteSpace(ConnectionString))
            return false;

        try
        {
            using var connection = new SqlConnection(ConnectionString);
            connection.Open();
            return true;
        }
        catch
        {
            return false;
        }
    }

    [Fact]
    public async Task ProcessorResume_DoesNotDuplicatePayloadAssignments()
    {
        if (!IsSqlServerAvailable())
        {
            return;
        }

        var databaseName = $"UqebFollowUpPrint_{Guid.NewGuid():N}";
        var builder = new SqlConnectionStringBuilder(ConnectionString!)
        {
            InitialCatalog = databaseName,
        };

        await SqlServerTestDatabaseHelper.DropDatabaseAsync(ConnectionString!, databaseName);

        var services = new ServiceCollection();
        services.AddLogging(b => b.AddDebug());
        services.AddDbContext<AppDbContext>(o => o.UseSqlServer(builder.ConnectionString));
        services.Configure<FollowUpLettersOptions>(o =>
        {
            o.DefaultBatchPrintSize = 2;
            o.AbsoluteMaxBatchPrintSize = 100;
            o.JobLeaseSeconds = 30;
        });
        services.AddScoped<IAuditService, AuditService>();
        services.AddScoped<IUserNotificationService, NoOpNotificationService>();
        services.AddScoped<FollowUpPrintJobProcessorHostedService>();

        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.EnsureCreatedAsync();

        var user = new User
        {
            Username = $"print-user-{Guid.NewGuid():N}",
            PasswordHash = "hash",
            FullName = "Print User",
            Role = UserRole.Admin,
            IsActive = true,
        };
        db.Users.Add(user);

        var template = new LetterTemplate
        {
            Code = $"follow_up_{Guid.NewGuid():N}",
            Name = "Follow Up",
            TemplateType = LetterTemplateType.FollowUp,
            Content = FollowUpLetterRenderService.DefaultFollowUpContent,
            IsActive = true,
            IsDefault = true,
        };
        db.LetterTemplates.Add(template);
        await db.SaveChangesAsync();

        var job = new FollowUpPrintJob
        {
            RequestedById = user.Id,
            Status = FollowUpPrintJobStatus.Queued,
            FilterSnapshotJson = "{}",
            TemplateId = template.Id,
            BatchSize = 2,
            TotalTransactions = 3,
            TotalLetters = 3,
            CreatedAt = DateTime.UtcNow,
        };
        db.FollowUpPrintJobs.Add(job);
        await db.SaveChangesAsync();

        for (var i = 1; i <= 3; i++)
        {
            var snapshot = JsonSerializer.Serialize(
                new FollowUpLetterDocumentModel { TransactionId = i, Recipient = "جهة", Body = "test" },
                JsonOptions);

            db.FollowUpPrintJobPayloads.Add(new FollowUpPrintJobPayload
            {
                JobId = job.Id,
                PayloadOrdinal = i,
                TransactionId = i,
                TargetDepartmentId = 1,
                TargetEntityId = i,
                TargetEntityName = $"جهة {i}",
                FollowUpSequence = 1,
                SnapshotJson = snapshot,
                Status = FollowUpPrintJobPayloadStatus.Pending,
            });
        }

        await db.SaveChangesAsync();

        var processor = scope.ServiceProvider.GetRequiredService<FollowUpPrintJobProcessorHostedService>();
        await InvokeProcessOnceAsync(processor, CancellationToken.None);
        await InvokeProcessOnceAsync(processor, CancellationToken.None);

        var assignedCount = await db.FollowUpPrintJobPayloads.CountAsync(p => p.JobId == job.Id && p.PartId != null);
        var partCount = await db.FollowUpPrintJobParts.CountAsync(p => p.JobId == job.Id);

        Assert.Equal(3, assignedCount);
        Assert.Equal(2, partCount);
        Assert.All(
            await db.FollowUpPrintJobPayloads.Where(p => p.JobId == job.Id).ToListAsync(),
            p => Assert.NotNull(p.PartId));

        await SqlServerTestDatabaseHelper.DropDatabaseAsync(ConnectionString!, databaseName);
    }

    private static async Task InvokeProcessOnceAsync(FollowUpPrintJobProcessorHostedService processor, CancellationToken cancellationToken)
    {
        var method = typeof(FollowUpPrintJobProcessorHostedService).GetMethod(
            "ProcessOnceAsync",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("ProcessOnceAsync not found.");

        var task = (Task)method.Invoke(processor, [cancellationToken])!;
        await task;
    }

    private sealed class NoOpNotificationService : IUserNotificationService
    {
        public Task<UserNotificationDto> CreateAsync(int userId, string type, string title, string body, string? link = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(new UserNotificationDto { Type = type, Title = title, Body = body, Link = link });

        public Task<List<UserNotificationDto>> ListForUserAsync(int userId, bool unreadOnly = false, DateTime? since = null, int limit = 50, CancellationToken cancellationToken = default) =>
            Task.FromResult(new List<UserNotificationDto>());

        public Task<UserNotificationDto?> MarkReadAsync(int notificationId, int userId, CancellationToken cancellationToken = default) =>
            Task.FromResult<UserNotificationDto?>(null);
    }
}
