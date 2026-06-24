# Institutional Reporting — Phase 1 Admin Allowlist Provisioning

Phase 1 enables institutional reporting infrastructure while restricting **runtime access** to explicit internal user IDs only.

## Target configuration

Apply on the **production host only** (`C:\Uqeb\config\appsettings.Production.json` or environment variables). Do **not** commit real user IDs or passwords to Git.

```json
{
  "FeatureFlags": {
    "InstitutionalReports": true
  },
  "ReportingRollout": {
    "EmergencyDisable": false,
    "EnabledForUserIds": [
      "<ADMIN_PILOT_USER_ID>"
    ],
    "EnabledForRoles": [],
    "EnabledForDepartments": [],
    "Percentage": 0
  }
}
```

## Environment variable equivalent

```text
FeatureFlags__InstitutionalReports=true
ReportingRollout__EmergencyDisable=false
ReportingRollout__EnabledForUserIds__0=<ADMIN_PILOT_USER_ID>
ReportingRollout__Percentage=0
```

Leave role, department, and percentage keys unset or empty.

## Resolve pilot user IDs (SQL Server)

Run on the production database (read-only):

```sql
SELECT Id, Username, Role, DepartmentId, IsActive
FROM Users
WHERE Role = 0 -- Admin
  AND IsActive = 1
ORDER BY Id;
```

Use numeric `Id` values only in `EnabledForUserIds`.

## Pre-activation checklist

1. Confirm `main` contains merged PR #19 (`980823c` or later).
2. Backup current production settings:

   ```powershell
   $stamp = Get-Date -Format "yyyyMMdd-HHmmss"
   Copy-Item C:\Uqeb\publish\api\appsettings.Production.json `
     C:\Uqeb\backup\before-phase1-$stamp\appsettings.Production.json
   ```

3. Tag or branch current release:

   ```powershell
   git tag production-pre-institutional-phase1-<stamp>
   ```

4. Record commit SHA from deployed `RELEASE.txt` or package metadata.
5. Verify disk space, Chromium, fonts, temp directory, database, readiness (see script below).
6. Review `docs/institutional_reporting_rollback_runbook.md`.

## Activation script

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\apply-institutional-reporting-phase1-allowlist.ps1 `
  -PilotUserId <ADMIN_PILOT_USER_ID> `
  -ProductionSettingsPath C:\Uqeb\publish\api\appsettings.Production.json `
  -WhatIf
```

Remove `-WhatIf` only after reviewing the diff.

## Verification script

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\verify-institutional-reporting-phase1.ps1 `
  -ApiBaseUrl http://10.0.177.17:5000/api `
  -PilotUsername <pilot-username> `
  -NonPilotAdminUsername <other-admin> `
  -NormalUsername <data-entry-user>
```

Passwords must come from `$env:UQEB_PASSWORD` / `$env:UQEB_NON_PILOT_PASSWORD` — never from command history.

## Smoke test

```bash
export API_BASE_URL="http://10.0.177.17:5000/api"
export USERNAME="<pilot-username>"
export PASSWORD="<from-secret-store>"
bash scripts/reporting-production-smoke-test.sh
```

## Phase 1 prohibitions

Do **not** enable during this phase:

- `EnabledForRoles = ["Admin"]`
- `EnabledForDepartments`
- `Percentage > 0`
- Supervisor / department / percentage rollouts

## Rollback (fast)

1. Set `ReportingRollout:EmergencyDisable=true` and restart API.
2. If issues persist, set `FeatureFlags:InstitutionalReports=false` and restart.
3. Follow `docs/institutional_reporting_rollback_runbook.md`.

## Monitoring window

Observe for **24–48 hours** after activation. Immediate rollback triggers include data leakage, orphaned temp files, sustained 5xx, Chromium hangs, memory pressure, readiness failure, duplicate audit events, or unauthorized access.
