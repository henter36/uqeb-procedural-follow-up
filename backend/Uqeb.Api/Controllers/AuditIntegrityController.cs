using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Uqeb.Api.Authorization;
using Uqeb.Api.Services;

namespace Uqeb.Api.Controllers;

[ApiController]
[Route("api/security")]
[Authorize(Policy = Policies.AdminOnly)]
public sealed class AuditIntegrityController : ControllerBase
{
    private readonly IAuditIntegrityDiagnosticService _auditIntegrity;

    public AuditIntegrityController(IAuditIntegrityDiagnosticService auditIntegrity) =>
        _auditIntegrity = auditIntegrity;

    [HttpGet("audit-integrity-report")]
    public async Task<IActionResult> GetAuditIntegrityReport(CancellationToken cancellationToken) =>
        Ok(await _auditIntegrity.GetHistoricalReportAsync(cancellationToken));
}
