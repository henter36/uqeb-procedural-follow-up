# Institutional Reporting Rollback Runbook

## 1. Disable feature flag

```json
"FeatureFlags": {
  "InstitutionalReports": false
}
```

## 2. Disable rollout

```json
"ReportingRollout": {
  "EmergencyDisable": true,
  "EnabledForRoles": [],
  "EnabledForUserIds": [],
  "EnabledForDepartments": [],
  "Percentage": 0
}
```

## 3. Restart application

```powershell
schtasks /End /TN "UqebApi"
Start-Sleep -Seconds 3
schtasks /Run /TN "UqebApi"
```

## 4. Stop in-flight exports

- Restart drains scoped export guards and Playwright browser gate.
- Verify no long-running `Uqeb.Api` child Chromium processes remain.

## 5. Clean temp files

```powershell
Remove-Item -Recurse -Force C:\Uqeb\reporting-temp\* -ErrorAction SilentlyContinue
```

## 6. Verify health

- `GET /health/live` → 200
- `GET /health/ready` → 200 (database reachable)
- Institutional endpoints → 404 when flag disabled

## 7. Verify database

- No schema rollback required for this feature.
- Confirm `AuditLogs` continue recording non-reporting actions.

## 8. Verify logs

- Search for `Report export rejected` / `Report export failed` without report body content.
- Confirm no spike in `reporting_failures_total`.

## 9. Return to previous release (if needed)

Use standard Uqeb deployment rollback from `AGENTS.md` with verified backup under `C:\Uqeb\backup`.

## 10. Post-rollback verification

- Login works
- Core transactions unaffected
- `/api/institutional-reports/*` returns 404
- Temp directory empty or stable

Rollback does **not** delete transactional data.
