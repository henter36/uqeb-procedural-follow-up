using System.Net;
using System.Net.Http.Headers;
using System.Reflection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Uqeb.Api.Controllers;
using Uqeb.Api.Services;
using Xunit;

namespace Uqeb.Api.Tests;

public class SecurityAuditIntegrityAuthorizationTests : IClassFixture<SecurityAuditIntegrityWebApplicationFactory>
{
    private readonly HttpClient _client;

    public SecurityAuditIntegrityAuthorizationTests(SecurityAuditIntegrityWebApplicationFactory factory) =>
        _client = factory.CreateClient();

    [Fact]
    public async Task AuditIntegrityReport_Unauthenticated_ReturnsUnauthorized()
    {
        var response = await _client.GetAsync("/api/security/audit-integrity-report");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task AuditIntegrityReport_Admin_ReturnsOk()
    {
        using var request = CreateAuthorizedRequest("Admin");
        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task AuditIntegrityReport_Supervisor_ReturnsForbidden()
    {
        using var request = CreateAuthorizedRequest("Supervisor");
        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public void SecurityController_does_not_depend_on_audit_integrity_service()
    {
        var constructor = typeof(SecurityController).GetConstructors().Single();
        var parameterTypes = constructor.GetParameters().Select(parameter => parameter.ParameterType).ToArray();

        Assert.Single(parameterTypes);
        Assert.Equal(typeof(ISecurityAuditService), parameterTypes[0]);
        Assert.DoesNotContain(typeof(IAuditIntegrityDiagnosticService), parameterTypes);
    }

    [Fact]
    public void AuditIntegrityController_depends_only_on_audit_integrity_service()
    {
        var constructor = typeof(AuditIntegrityController).GetConstructors().Single();
        var parameterTypes = constructor.GetParameters().Select(parameter => parameter.ParameterType).ToArray();

        Assert.Single(parameterTypes);
        Assert.Equal(typeof(IAuditIntegrityDiagnosticService), parameterTypes[0]);
        Assert.DoesNotContain(typeof(ISecurityAuditService), parameterTypes);
    }

    private static HttpRequestMessage CreateAuthorizedRequest(string role) =>
        new(HttpMethod.Get, "/api/security/audit-integrity-report")
        {
            Headers =
            {
                Authorization = new AuthenticationHeaderValue("Bearer", TestJwtHelper.CreateToken(role)),
            },
        };
}

public sealed class SecurityAuditIntegrityWebApplicationFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder) =>
        HealthTestHostBuilder.Configure(builder);
}
