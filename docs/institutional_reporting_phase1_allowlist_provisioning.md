# Institutional Reporting — Phase 1 Admin Allowlist Provisioning

Phase 1 enables institutional reporting infrastructure while restricting **runtime access** to the single login account **`admin`** per environment. Each environment resolves `admin` to its own `Users.Id` at activation time.

## Scope

| Environment | Settings file (default) | Backup root (default) |
| --- | --- | --- |
| Development | `backend/Uqeb.Api/appsettings.Development.json` | `artifacts/phase1-settings-backups/` |
| Test | `backend/Uqeb.Api/appsettings.Test.json` | `artifacts/phase1-settings-backups/` |
| Staging | `backend/Uqeb.Api/appsettings.Staging.json` | `artifacts/phase1-settings-backups/` |
| Production | `C:\Uqeb\publish\api\appsettings.Production.json` | `C:\Uqeb\backup\` |

Override paths with `-SettingsPath` when using external configuration stores.

Safe Git templates (not activated):

- `backend/Uqeb.Api/appsettings.example.json`
- `backend/Uqeb.Api/appsettings.Test.json.example`
- `backend/Uqeb.Api/appsettings.Staging.json.example`

## Target configuration (per environment, after activation)

```json
{
  "FeatureFlags": {
    "InstitutionalReports": true
  },
  "ReportingRollout": {
    "EmergencyDisable": false,
    "EnabledForUserIds": [
      "<ADMIN_USER_ID_RESOLVED_FOR_THIS_ENVIRONMENT>"
    ],
    "EnabledForRoles": [],
    "EnabledForDepartments": [],
    "Percentage": 0
  }
}
```

## Phase 1 rules

- Only the **`admin`** login (case-insensitive, trimmed) is resolved and allowlisted.
- Do **not** use `EnabledForRoles`, `EnabledForDepartments`, or `Percentage`.
- Admin role alone does **not** grant access.
- Do not assume `Users.Id` is identical across environments.
- Do not commit resolved IDs, passwords, tokens, or connection strings to Git.
- Do not auto-create `admin` if missing.
- Abort when zero or multiple `admin` usernames exist.
- Abort when `admin` is inactive or not `UserRole.Admin`.
- Run activation **once per environment**; failures do not continue to other environments.

## Activation (one environment at a time)

Preview:

```powershell
.\scripts\apply-institutional-reporting-phase1-allowlist.ps1 `
  -EnvironmentName Development `
  -AdminUsername admin `
  -WhatIf
```

Apply:

```powershell
.\scripts\apply-institutional-reporting-phase1-allowlist.ps1 `
  -EnvironmentName Development `
  -AdminUsername admin
```

Repeat explicitly for `Test`, `Staging`, and `Production`.

The script:

1. Connects to the environment database via `ConnectionStrings:DefaultConnection` in the settings file.
2. Resolves `admin` → `Users.Id` using `Uqeb.Tools.Phase1Rollout`.
3. Backs up settings.
4. Patches allowlist configuration.
5. Prints masked user ID and planned changes (never secrets).

## Verification

```powershell
$env:UQEB_PASSWORD = '<secret>'
$env:UQEB_NON_PILOT_PASSWORD = '<other-admin-secret>'
$env:UQEB_NORMAL_PASSWORD = '<normal-user-secret>'

.\scripts\verify-institutional-reporting-phase1.ps1 `
  -EnvironmentName Production `
  -ApiBaseUrl http://10.0.177.17:5000/api `
  -AdminUsername admin `
  -NonPilotAdminUsername superadmin `
  -NormalUsername dataentry
```

Expected:

| Actor | Result |
| --- | --- |
| `admin` (resolved ID in allowlist) | Login, configuration, readiness, preview, PDF, XLSX → **200** |
| Other admin | **403/404** |
| Normal user | **403/404** |
| `EmergencyDisable=true` | **admin denied** (restore `false` after test) |

## Smoke test

```bash
export API_BASE_URL="http://<host>/api"
export USERNAME="admin"
export PASSWORD="<secret>"
bash scripts/reporting-production-smoke-test.sh
```

## Environment decision matrix

| Environment | Username | User ID | Feature Flag | Allowlist | Readiness | Smoke | Decision |
| --- | --- | --- | --- | --- | --- | --- | --- |
| Development | admin | masked | pending | pending | pending | pending | NO-GO |
| Test | admin | masked | pending | pending | pending | pending | NO-GO |
| Staging | admin | masked | pending | pending | pending | pending | NO-GO |
| Production | admin | masked | pending | pending | pending | pending | NO-GO |

Update this table after each environment passes on-host verification.

## Rollback

1. `ReportingRollout:EmergencyDisable=true` → restart API.
2. If needed: `FeatureFlags:InstitutionalReports=false` → restart API.
3. See `docs/institutional_reporting_rollback_runbook.md`.
