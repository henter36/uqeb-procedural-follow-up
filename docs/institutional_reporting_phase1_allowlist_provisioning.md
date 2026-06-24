# Institutional Reporting — Phase 1 Admin Allowlist Provisioning

Institutional reporting is **visible and available** to all users who pass normal authentication and RBAC. The rollout gate runs in **ObserveOnly** mode by default: decisions are measured and logged, but access is not blocked by allowlist membership until enforcement is explicitly enabled.

Phase 1 tooling resolves the login account **`admin`** to its environment-specific `Users.Id` and prepares the allowlist for a future transition to **Enforced** mode.

## Current access model

| Layer | Behavior |
| --- | --- |
| Feature flag `InstitutionalReports=true` | Feature visible and enabled |
| Normal RBAC | Required for report endpoints |
| `EnforcementMode=ObserveOnly` (default) | Rollout computed and recorded; **does not block** |
| `EnforcementMode=Enforced` | Allowlist / roles / departments / percentage enforced |
| `EmergencyDisable=true` | Blocks all users (even in ObserveOnly) |
| `InstitutionalReports=false` | Disables feature entirely |

```text
Feature availability: GO
Rollout enforcement: PENDING — ObserveOnly
```

## Scope

| Environment | Settings file (default) | Backup root (default) |
| --- | --- | --- |
| Development | `backend/Uqeb.Api/appsettings.Development.json` | `artifacts/phase1-settings-backups/` |
| Test | `backend/Uqeb.Api/appsettings.Test.json` | `artifacts/phase1-settings-backups/` |
| Staging | `backend/Uqeb.Api/appsettings.Staging.json` | `artifacts/phase1-settings-backups/` |
| Production | `C:\Uqeb\publish\api\appsettings.Production.json` | `C:\Uqeb\backup\` |

Override paths with `-SettingsPath` when using external configuration stores.

Safe Git templates:

- `backend/Uqeb.Api/appsettings.example.json`
- `backend/Uqeb.Api/appsettings.Test.json.example`
- `backend/Uqeb.Api/appsettings.Staging.json.example`

## Target configuration (ObserveOnly — default in this phase)

```json
{
  "FeatureFlags": {
    "InstitutionalReports": true
  },
  "ReportingRollout": {
    "EnforcementMode": "ObserveOnly",
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

## Future enforced configuration (after Phase 1 verification)

Apply with `-Enforce` only after on-host validation and explicit approval:

```json
{
  "ReportingRollout": {
    "EnforcementMode": "Enforced"
  }
}
```

## Phase 1 rules

- Only the **`admin`** login (case-insensitive, trimmed) is resolved and allowlisted.
- Do **not** use `EnabledForRoles`, `EnabledForDepartments`, or `Percentage` in Phase 1.
- Admin role alone does **not** grant rollout access when enforcement is enabled.
- Do not assume `Users.Id` is identical across environments.
- Do not commit resolved IDs, passwords, tokens, or connection strings to Git.
- Abort when zero or multiple `admin` usernames exist.
- Abort when `admin` is inactive or not `UserRole.Admin`.
- Run activation **once per environment**; failures do not continue to other environments.
- Without `-Enforce`, the script keeps `EnforcementMode=ObserveOnly` and does not block users.

## Activation (one environment at a time)

Preview:

```powershell
.\scripts\apply-institutional-reporting-phase1-allowlist.ps1 `
  -EnvironmentName Development `
  -AdminUsername admin `
  -WhatIf
```

Apply (ObserveOnly — does not block non-allowlisted authorized users):

```powershell
.\scripts\apply-institutional-reporting-phase1-allowlist.ps1 `
  -EnvironmentName Development `
  -AdminUsername admin
```

Enable enforcement (future, after verification):

```powershell
.\scripts\apply-institutional-reporting-phase1-allowlist.ps1 `
  -EnvironmentName Production `
  -AdminUsername admin `
  -Enforce
```

The script:

1. Connects to the environment database via `ConnectionStrings:DefaultConnection` in the settings file.
2. Resolves `admin` → `Users.Id` using `Uqeb.Tools.Phase1Rollout`.
3. Backs up settings.
4. Patches allowlist configuration and `EnforcementMode`.
5. Prints current/target mode, masked user ID, and planned changes (never secrets).

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

Expected in **ObserveOnly** (default):

| Actor | Result |
| --- | --- |
| `admin` (resolved ID in allowlist) | Login, configuration, readiness, preview, PDF, XLSX → **200** |
| Other authorized admin | **200** (rollout not enforced) |
| Normal user without report permission | **403/404** (normal RBAC) |
| `EmergencyDisable=true` | **admin denied** (restore `false` after test) |

Expected when **Enforced** (`-Enforce` applied):

| Actor | Result |
| --- | --- |
| Allowlisted `admin` | **200** |
| Other admin | **403/404** |

## Smoke test

```bash
export API_BASE_URL="http://<host>/api"
export USERNAME="admin"
export PASSWORD="<secret>"
bash scripts/reporting-production-smoke-test.sh
```

## Environment decision matrix

| Environment | Current access mode | Effective audience | Target enforced audience | Enforcement activation |
| --- | --- | --- | --- | --- |
| Development | ObserveOnly | all normally authorized users | resolved admin allowlist | pending |
| Test | ObserveOnly | all normally authorized users | resolved admin allowlist | pending |
| Staging | ObserveOnly | all normally authorized users | resolved admin allowlist | pending |
| Production | ObserveOnly | all normally authorized users | resolved admin allowlist | pending |

## Rollback

1. `ReportingRollout:EmergencyDisable=true` → restart API.
2. If needed: `FeatureFlags:InstitutionalReports=false` → restart API.
3. See `docs/institutional_reporting_rollback_runbook.md`.
