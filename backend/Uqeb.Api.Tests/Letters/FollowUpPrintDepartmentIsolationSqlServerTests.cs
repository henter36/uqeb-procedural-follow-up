using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Uqeb.Api.Data;
using Uqeb.Api.Models.Entities;
using Uqeb.Api.Models.Enums;
using Uqeb.Api.Services;
using Xunit;

namespace Uqeb.Api.Tests.Letters;

/// <summary>
/// Verifies that DepartmentUser cannot access follow-up print jobs.
/// </summary>
[Trait("Category", "SqlServer")]
public class FollowUpPrintDepartmentIsolationSqlServerTests
    : IClassFixture<FollowUpPrintDepartmentIsolationFactory>
{
    private readonly FollowUpPrintDepartmentIsolationFactory _factory;

    public FollowUpPrintDepartmentIsolationSqlServerTests(FollowUpPrintDepartmentIsolationFactory factory)
    {
        _factory = factory;
        _factory.EnsureSeeded();
    }

    private HttpClient MakeClient(string role, int userId, int? departmentId = null)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            TestJwtHelper.CreateToken(role, userId: userId, departmentId: departmentId));
        return client;
    }

    // ─── Department users are not print operators ──────────────────────────────

    private static async Task<List<int>> GetJobIdsAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        var paged = JsonSerializer.Deserialize<PagedJobsEnvelope>(body,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        return paged?.Items?.Select(j => j.Id).ToList() ?? [];
    }

    private sealed class PagedJobsEnvelope
    {
        public List<JobIdEnvelope> Items { get; set; } = [];
    }

    private sealed class JobIdEnvelope
    {
        public int Id { get; set; }
    }

    [Fact]
    public async Task ListJobs_Dept1User_ReturnsForbidden()
    {
        using var client = MakeClient("DepartmentUser", _factory.Dept1UserId, departmentId: 1);
        var response = await client.GetAsync("/api/follow-up-print/jobs");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task ListJobs_Dept10User_ReturnsForbidden()
    {
        using var client = MakeClient("DepartmentUser", _factory.Dept10UserId, departmentId: 10);
        var response = await client.GetAsync("/api/follow-up-print/jobs");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetJob_Dept1User_ReturnsForbiddenForOwnDeptJob()
    {
        using var client = MakeClient("DepartmentUser", _factory.Dept1UserId, departmentId: 1);
        var response = await client.GetAsync($"/api/follow-up-print/jobs/{_factory.Dept1JobId}");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetJob_Dept10User_ReturnsForbiddenForOwnDeptJob()
    {
        using var client = MakeClient("DepartmentUser", _factory.Dept10UserId, departmentId: 10);
        var response = await client.GetAsync($"/api/follow-up-print/jobs/{_factory.Dept10JobId}");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task DataEntry_CanListJobs()
    {
        using var client = MakeClient("DataEntry", _factory.UnrelatedUserId);
        var response = await client.GetAsync("/api/follow-up-print/jobs");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}

public sealed class FollowUpPrintDepartmentIsolationFactory : WebApplicationFactory<Program>
{
    private bool _seeded;

    public int Dept1UserId { get; private set; }
    public int Dept10UserId { get; private set; }
    public int UnrelatedUserId { get; private set; }
    public int Dept1JobId { get; private set; }
    public int Dept10JobId { get; private set; }

    protected override void ConfigureWebHost(IWebHostBuilder builder) =>
        HealthTestHostBuilder.Configure(builder, inMemoryDatabaseName: $"follow-up-print-dept-isolation-{Guid.NewGuid():N}");

    public void EnsureSeeded()
    {
        if (_seeded)
            return;

        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Database.EnsureCreated();

        var dept1User = new User { Username = $"dept1-{Guid.NewGuid():N}", PasswordHash = "h", FullName = "Dept1", Role = UserRole.DepartmentUser, IsActive = true };
        var dept10User = new User { Username = $"dept10-{Guid.NewGuid():N}", PasswordHash = "h", FullName = "Dept10", Role = UserRole.DepartmentUser, IsActive = true };
        var unrelated = new User { Username = $"unrelated-{Guid.NewGuid():N}", PasswordHash = "h", FullName = "Unrelated", Role = UserRole.DataEntry, IsActive = true };
        db.Users.AddRange(dept1User, dept10User, unrelated);

        var template = new LetterTemplate
        {
            Code = $"dept-isolation-{Guid.NewGuid():N}",
            Name = "Test",
            TemplateType = LetterTemplateType.FollowUp,
            Content = FollowUpLetterRenderService.DefaultFollowUpContent,
            IsActive = true,
            IsDefault = true,
        };
        db.LetterTemplates.Add(template);
        db.SaveChanges();

        Dept1UserId = dept1User.Id;
        Dept10UserId = dept10User.Id;
        UnrelatedUserId = unrelated.Id;

        var dept1Job = new FollowUpPrintJob
        {
            RequestedById = dept1User.Id,
            ScopeDepartmentId = 1,
            Status = FollowUpPrintJobStatus.ReadyToPrint,
            FilterSnapshotJson = """{"departmentId":1}""",
            TemplateId = template.Id,
            BatchSize = 25,
            TotalTransactions = 1,
            TotalLetters = 1,
            CreatedAt = DateTime.UtcNow,
        };

        var dept10Job = new FollowUpPrintJob
        {
            RequestedById = dept10User.Id,
            ScopeDepartmentId = 10,
            Status = FollowUpPrintJobStatus.ReadyToPrint,
            FilterSnapshotJson = """{"departmentId":10}""",
            TemplateId = template.Id,
            BatchSize = 25,
            TotalTransactions = 1,
            TotalLetters = 1,
            CreatedAt = DateTime.UtcNow,
        };

        db.FollowUpPrintJobs.AddRange(dept1Job, dept10Job);
        db.SaveChanges();

        Dept1JobId = dept1Job.Id;
        Dept10JobId = dept10Job.Id;
        _seeded = true;
    }
}
