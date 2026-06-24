# Institutional Reporting — Phase 1 Admin Allowlist Rollout Report

## Summary

| Field | Value |
| --- | --- |
| **Date** | 2026-06-24 |
| **Environment** | Production target `10.0.177.17` (Uqeb LAN) — **activation not executed from dev host** |
| **Commit SHA (main / PR #19)** | `980823c912d9a5e87bbf2a8d8b8b88072067185b` |
| **Decision** | **NO-GO — disable rollout immediately** |

## Pre-activation record

| Item | Value |
| --- | --- |
| Current production commit | **Unknown** — host unreachable from dev workstation (`curl` timeout to `10.0.177.17:5000`) |
| Target commit | `980823c` (merge PR #19) |
| Current feature flag | **Unknown** (expected `InstitutionalReports=false` pre-rollout) |
| Current rollout configuration | **Unknown** (expected `EmergencyDisable=true`, empty allowlists) |
| Backup location | **Not created** — requires on-host run of `apply-institutional-reporting-phase1-allowlist.ps1` |
| Rollback command | `ReportingRollout:EmergencyDisable=true` → restart `UqebApi`; then `InstitutionalReports=false` if needed |

## Allowed users (masked)

| Pilot slot | User ID | Status |
| --- | --- | --- |
| Admin pilot #1 | `***` | **Not provisioned** — SQL lookup + config patch pending on production host |

**Count:** 0 active pilots (configuration not applied).

## Configuration target (Phase 1)

```json
{
  "FeatureFlags": { "InstitutionalReports": true },
  "ReportingRollout": {
    "EmergencyDisable": false,
    "EnabledForUserIds": ["<ADMIN_PILOT_USER_ID>"],
    "EnabledForRoles": [],
    "EnabledForDepartments": [],
    "Percentage": 0
  }
}
```

Provisioning guide: `docs/institutional_reporting_phase1_allowlist_provisioning.md`

## Automated verification (dev / CI)

| Check | Result | Notes |
| --- | --- | --- |
| PR #19 merged to `main` | **PASS** | `980823c` |
| Phase 1 decision order unit tests | **PASS** | `ReportingRolloutServiceTests` |
| Allowlist HTTP integration tests | **PASS** | `InstitutionalReportsAllowlistRolloutTests` |
| Role-only rollout excluded in phase 1 tests | **PASS** | `useDefaultRoleRollout: false` |

## Production verification (blocked)

| Check | Result | Notes |
| --- | --- | --- |
| Settings backup | **NOT RUN** | Requires Windows production host |
| Disk / Chromium / fonts / temp / DB | **NOT RUN** | Requires on-host readiness |
| Readiness | **NOT RUN** | Expected `Ready` for GO |
| Smoke test | **NOT RUN** | `API_BASE_URL` to `10.0.177.17` unreachable from dev |
| Authorized admin | **NOT RUN** | |
| Unauthorized admin | **NOT RUN** | Expected 404 |
| Normal user | **NOT RUN** | Expected 404 |
| Emergency disable | **NOT RUN** | |
| PDF / XLSX export | **NOT RUN** | |
| 10k / 20k benchmarks | **NOT RUN** | Run on acceptance host per PR #19 |
| Concurrency / cancellation | **NOT RUN** | |
| Metrics / audit / log privacy | **NOT RUN** | |
| Rollback drill | **NOT RUN** | |

## Readiness (production)

**Not observed.** Any `Degraded` or `Unavailable` state would be **NO-GO**.

## Smoke test

**NOT RUN** on production. Local attempt against `localhost:5000` returned HTTP 403 (non-Uqeb or misconfigured endpoint).

## Incidents

None during this preparation window. **Blocker:** no network path from development workstation to production LAN host for live activation.

## Rollback test

**NOT RUN.** Runbook available: `docs/institutional_reporting_rollback_runbook.md`

## Next steps (required before GO)

1. Deploy release containing `980823c` to `10.0.177.17` using standard Uqeb ZIP pipeline (`AGENTS.md`).
2. On production host:
   - Backup `appsettings.Production.json`
   - Resolve pilot admin `Users.Id` via SQL
   - Run `scripts/apply-institutional-reporting-phase1-allowlist.ps1 -PilotUserId <id>`
   - Restart `UqebApi`
3. Run `scripts/verify-institutional-reporting-phase1.ps1` with pilot / non-pilot / normal credentials via env vars.
4. Run `scripts/reporting-production-smoke-test.sh` with `API_BASE_URL=http://10.0.177.17:5000/api`.
5. Execute 10k/20k acceptance on production-like host; attach artifacts.
6. Monitor 24–48h; update this report with results and change decision to **GO** only if all gates pass.

## Final decision

```text
NO-GO — disable rollout immediately
```

**Reason:** Phase 1 production activation was **not executed**. Production host is unreachable from the development environment; readiness, smoke, export, metrics, and rollback drills were not performed on the target environment. Keep `InstitutionalReports=false` (or `EmergencyDisable=true`) until on-host steps complete successfully.

**Do not expand** to roles, departments, or percentage rollout without a separate review and decision.
