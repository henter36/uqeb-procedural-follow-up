using System.Net;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Uqeb.Api.Data;
using Uqeb.Api.Models.Entities;
using Uqeb.Api.Models.Enums;
using Uqeb.Api.Models.Letters;
using Uqeb.Api.Services;
using Xunit;

namespace Uqeb.Api.Tests.Letters;

public class FollowUpPrintAuthorizationSqlServerTests : IClassFixture<FollowUpPrintAuthorizationWebApplicationFactory>
{
    private readonly FollowUpPrintAuthorizationWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public FollowUpPrintAuthorizationSqlServerTests(FollowUpPrintAuthorizationWebApplicationFactory factory)
    {
        _factory = factory;
        _factory.EnsureSeeded();
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task GetJob_OtherUsersJob_ReturnsForbidden()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/follow-up-print/jobs/{_factory.OtherUserJobId}");
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            TestJwtHelper.CreateToken("DataEntry", userId: _factory.OwnerUserId));

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetJob_MissingJob_ReturnsNotFound()
    {
        _client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TestJwtHelper.CreateToken("Admin"));

        var response = await _client.GetAsync("/api/follow-up-print/jobs/999999");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetJob_Owner_ReturnsOk()
    {
        _client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue(
                "Bearer",
                TestJwtHelper.CreateToken("DataEntry", userId: _factory.OwnerUserId));

        var response = await _client.GetAsync($"/api/follow-up-print/jobs/{_factory.OwnerJobId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}

public sealed class FollowUpPrintAuthorizationWebApplicationFactory : WebApplicationFactory<Program>
{
    private bool _seeded;
    public int OwnerUserId { get; private set; }
    public int OtherUserId { get; private set; }
    public int OwnerJobId { get; private set; }
    public int OtherUserJobId { get; private set; }

    protected override void ConfigureWebHost(IWebHostBuilder builder) =>
        HealthTestHostBuilder.Configure(builder, inMemoryDatabaseName: $"follow-up-print-auth-{Guid.NewGuid():N}");

    public void EnsureSeeded()
    {
        if (_seeded)
            return;

        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Database.EnsureCreated();

        var owner = new User
        {
            Username = "owner",
            PasswordHash = "hash",
            FullName = "Owner",
            Role = UserRole.DataEntry,
            IsActive = true,
        };
        var other = new User
        {
            Username = "other",
            PasswordHash = "hash",
            FullName = "Other",
            Role = UserRole.DataEntry,
            IsActive = true,
        };
        db.Users.AddRange(owner, other);

        var template = new LetterTemplate
        {
            Code = "follow_up_auth_test",
            Name = "Follow Up",
            TemplateType = LetterTemplateType.FollowUp,
            Content = FollowUpLetterRenderService.DefaultFollowUpContent,
            IsActive = true,
            IsDefault = true,
        };
        db.LetterTemplates.Add(template);
        db.SaveChanges();

        OwnerUserId = owner.Id;
        OtherUserId = other.Id;

        var ownerJob = new FollowUpPrintJob
        {
            RequestedById = owner.Id,
            Status = FollowUpPrintJobStatus.ReadyToPrint,
            FilterSnapshotJson = "{}",
            TemplateId = template.Id,
            BatchSize = 25,
            TotalTransactions = 1,
            TotalLetters = 1,
            CreatedAt = DateTime.UtcNow,
        };
        var otherJob = new FollowUpPrintJob
        {
            RequestedById = other.Id,
            Status = FollowUpPrintJobStatus.ReadyToPrint,
            FilterSnapshotJson = "{}",
            TemplateId = template.Id,
            BatchSize = 25,
            TotalTransactions = 1,
            TotalLetters = 1,
            CreatedAt = DateTime.UtcNow,
        };
        db.FollowUpPrintJobs.AddRange(ownerJob, otherJob);
        db.SaveChanges();

        OwnerJobId = ownerJob.Id;
        OtherUserJobId = otherJob.Id;
        _seeded = true;
    }
}
