# Reporting Production-Like Acceptance Environment

## Scope

This environment validates institutional reporting under conditions that mirror production:

- Windows Server or Linux host with production-like resource limits
- .NET 10 runtime
- Node 24 for frontend build verification only
- SQL Server (no InMemory database)
- Playwright Chromium preinstalled
- Arabic fonts and `institutional-report.css` deployed with API
- Timezone `Asia/Riyadh`, culture `ar-SA`
- Feature flag default: `InstitutionalReports=true`
- Rollout default: `EnforcementMode=ObserveOnly`, `EmergencyDisable=false`

## Requirements

| Component | Production-like value |
| --- | --- |
| OS | Windows Server 2019+ or Ubuntu 22.04+ |
| .NET | 10.0.x |
| Node | 24.x |
| SQL Server | 2019+ |
| Chromium | Installed via Playwright (`playwright.ps1 install --with-deps chromium`) |
| Kestrel | `ASPNETCORE_URLS=http://0.0.0.0:5000` |
| Temp path | Writable dedicated directory (`Reporting:TempFileRoot`) |
| Memory | >= 8 GB recommended for 20k acceptance |
| CPU | >= 4 cores recommended |

## Setup

1. Provision SQL Server database and apply migrations.
2. Copy production-like `appsettings.Production.json` with reporting limits.
3. Install Chromium dependencies on Linux:
   ```bash
   pwsh backend/Uqeb.Api/bin/Release/net10.0/playwright.ps1 install --with-deps chromium
   ```
4. Enable acceptance-only flags in a dedicated config overlay:
   ```json
   {
     "FeatureFlags": { "InstitutionalReports": true },
     "ReportingRollout": {
       "EnforcementMode": "ObserveOnly",
       "EmergencyDisable": false,
       "EnabledForRoles": [],
       "EnabledForUserIds": [],
       "Percentage": 0
     }
   }
   ```
5. Ensure font assets and stylesheet are present in published API output.

## Run commands

```bash
export RUN_REPORTING_ACCEPTANCE=1
dotnet test backend/Uqeb.Api.Tests/Uqeb.Api.Tests.csproj \
  --filter "FullyQualifiedName~ReportingAcceptanceBenchmarkTests.SmallBenchmark"

export RUN_REPORTING_ACCEPTANCE_LARGE=1
dotnet test backend/Uqeb.Api.Tests/Uqeb.Api.Tests.csproj \
  --filter "FullyQualifiedName~ReportingAcceptanceBenchmarkTests.LargeBenchmark"
```

Artifacts are written to `artifacts/reporting-acceptance/`.

## Tests

- Synthetic seed: `ReportingSyntheticSeedGeneratorTests` (gated by `RUN_REPORTING_ACCEPTANCE=1`)
- Benchmark runner: `ReportingAcceptanceBenchmarkTests`
- Concurrency: `ReportingExportConcurrencyGateTests`
- Temp isolation: `ReportingTempFileManagerTests`
- Chromium probe: `ReportingChromiumProbe` + `pdf-linux` CI job
- Smoke: `scripts/reporting-production-smoke-test.ps1`

## Results

Store benchmark JSON/MD under `artifacts/reporting-acceptance/` and attach as CI artifacts. Do not commit large raw outputs to Git.

## Cleanup

```powershell
Remove-Item -Recurse -Force C:\Uqeb\reporting-temp\* -ErrorAction SilentlyContinue
```

Or rely on `ReportingTempFileCleanupHostedService` and `Reporting:TempFileMaxAgeMinutes`.

## Rollback

See `docs/institutional_reporting_rollback_runbook.md`.
