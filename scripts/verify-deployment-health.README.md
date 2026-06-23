# PowerShell validation notes for verify-deployment-health.ps1

Run on Windows PowerShell 5.1+ or PowerShell 7.

## Parse check

```powershell
$errors = $null
[void][System.Management.Automation.Language.Parser]::ParseFile(
    (Resolve-Path ".\scripts\verify-deployment-health.ps1"),
    [ref]$null,
    [ref]$errors)
if ($errors) { throw $errors }
```

Repeat for `deploy-production-v2.ps1` health section (full script parse).

## Manual scenarios (against running API)

| Scenario | Expected |
|----------|----------|
| Healthy API | exit 0 |
| `/health/live` down | exit 1 |
| `/health/ready` returns 503 | exit 1 |
| `/health` degraded | exit 1 |
| Missing correlation header (mock) | exit 1 |
| API starts after 2 retries | exit 0 with `-RetryCount 5` |
| Base URL with trailing slash | exit 0 |
| Base URL without trailing slash | exit 0 |

Use `scripts/verify-deployment-health.ps1 -ApiBaseUrl http://localhost:5000`.

## Automated Pester tests (Windows)

```powershell
Install-Module Pester -MinimumVersion 5.0 -Force -Scope CurrentUser
Invoke-Pester -Path .\scripts\verify-deployment-health.Tests.ps1 -Output Detailed
```

Automated Pester coverage mocks `Invoke-WebRequest` for healthy API, live failure, ready 503, summary degraded, missing correlation header, retry success, and trailing-slash base URL scenarios.
