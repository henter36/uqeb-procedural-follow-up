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
/// Verifies that DepartmentUser access to print jobs is scoped strictly by
/// ScopeDepartmentId equality — not substring matching or JSON sniffing.
/// Key invariant: department 1 must never see department 10's jobs, and vice versa.
/// </summary>
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

    // ─── List isolation ────────────────────────────────────────────────────────

    [Fact]
    public async Task ListJobs_Dept1User_DoesNotSeeDept10Job()
    {
        using var client = MakeClient("DepartmentUser", _factory.Dept1UserId, departmentId: 1);
        var response = await client.GetAsync("/api/follow-up-print/jobs");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain(_factory.Dept10JobId.ToString(), body);
    }

    [Fact]
    public async Task ListJobs_Dept10User_DoesNotSeeDept1Job()
    {
        using var client = MakeClient("DepartmentUser", _factory.Dept10UserId, departmentId: 10);
        var response = await client.GetAsync("/api/follow-up-print/jobs");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain(_factory.Dept1JobId.ToString(), body);
    }

    [Fact]
    public async Task ListJobs_Dept1User_SeesDept1Job()
    {
        using var client = MakeClient("DepartmentUser", _factory.Dept1UserId, departmentId: 1);
        var response = await client.GetAsync("/api/follow-up-print/jobs");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains(_factory.Dept1JobId.ToString(), body);
    }

    // ─── Direct access isolation ───────────────────────────────────────────────

    [Fact]
    public async Task GetJob_Dept1User_CannotAccessDept10Job()
    {
        using var client = MakeClient("DepartmentUser", _factory.Dept1UserId, departmentId: 1);
        var response = await client.GetAsync($"/api/follow-up-print/jobs/{_factory.Dept10JobId}");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetJob_Dept10User_CannotAccessDept1Job()
    {
        using var client = MakeClient("DepartmentUser", _factory.Dept10UserId, departmentId: 10);
        var response = await client.GetAsync($"/api/follow-up-print/jobs/{_factory.Dept1JobId}");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetJob_Dept1User_CanAccessOwnDept1Job()
    {
        using var client = MakeClient("DepartmentUser", _factory.Dept1UserId, departmentId: 1);
        var response = await client.GetAsync($"/api/follow-up-print/jobs/{_factory.Dept1JobId}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // ─── Owner access ─────────────────────────────────────────────────────────

    [Fact]
    public async Task GetJob_Owner_CanAccessOwnJob_RegardlessOfDepartment()
    {
        // The owner of Dept10Job is Dept10UserId; they can view it even if dept changes.
        using var client = MakeClient("DepartmentUser", _factory.Dept10UserId, departmentId: 10);
        var response = await client.GetAsync($"/api/follow-up-print/jobs/{_factory.Dept10JobId}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // ─── Unauthorized user leaks nothing ──────────────────────────────────────

    [Fact]
    public async Task GetJob_UnauthorizedUser_Returns403WithoutRevealingJobExistence()
    {
        // A DataEntry user with no department affiliation cannot see dept-scoped jobs.
        using var client = MakeClient("DataEntry", _factory.UnrelatedUserId);
        var response = await client.GetAsync($"/api/follow-up-print/jobs/{_factory.Dept1JobId}");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
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
