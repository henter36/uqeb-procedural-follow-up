# Institutional Reporting — Phase 1 Admin Allowlist Rollout Report

## Access decisions

```text
Feature availability: GO
Rollout enforcement: PENDING — ObserveOnly
```

Institutional reports are visible and usable for all users who pass normal authentication and RBAC. Rollout decisions are computed and measured in ObserveOnly mode; they are not enforced until `-Enforce` is applied per environment.

## Per-environment status

| Environment | Current access mode | Effective audience | Target enforced audience | Enforcement activation |
| --- | --- | --- | --- | --- |
| Development | ObserveOnly | all normally authorized users | resolved admin allowlist | pending |
| Test | ObserveOnly | all normally authorized users | resolved admin allowlist | pending |
| Staging | ObserveOnly | all normally authorized users | resolved admin allowlist | pending |
| Production | ObserveOnly | all normally authorized users | resolved admin allowlist | pending |

> User IDs are resolved at activation time and must be masked in reports (example: `a12f…9c41`). Never commit real IDs to Git.

## Summary

| Field | Value |
| --- | --- |
| **Date** | 2026-06-24 |
| **Target commit (main / PR #19)** | `980823c912d9a5e87bbf2a8d8b8b88072067185b` |
| **Phase 1 pilot account** | login `admin` only (per-environment `Users.Id`) |
| **Feature flag default in Git** | `InstitutionalReports=true` |
| **Rollout enforcement default** | `EnforcementMode=ObserveOnly`, `EmergencyDisable=false` |
| **Secrets committed** | **No** |
| **Backup location** | Created per run under environment backup root |

## Automated verification (CI / local)

| Check | Result |
| --- | --- |
| Admin username → single active Admin resolved | **PASS** (`ReportingPhase1AdminUserResolverTests`) |
| Missing / duplicate / inactive / non-admin aborted | **PASS** |
| Different IDs per environment DB | **PASS** |
| ObserveOnly: allowlisted admin allowed | **PASS** |
| ObserveOnly: unmatched authorized admin allowed | **PASS** |
| ObserveOnly: rollout decision still evaluated | **PASS** |
| Enforced: allowlisted admin allowed | **PASS** |
| Enforced: different admin role user denied | **PASS** |
| Normal user denied by RBAC | **PASS** |
| `EmergencyDisable=true` denies admin (ObserveOnly) | **PASS** |
| `InstitutionalReports=false` denies admin | **PASS** |
| PDF / XLSX for authorized admin (stub) | **PASS** |
| Rollout metrics recorded | **PASS** |

## On-host verification (required before Enforced)

| Check | Development | Test | Staging | Production |
| --- | --- | --- | --- | --- |
| Backup | not run | not run | not run | not run |
| Admin ID resolution | not run | not run | not run | not run |
| Readiness Ready | not run | not run | not run | not run |
| Smoke PASS | not run | not run | not run | not run |
| PDF / XLSX | not run | not run | not run | not run |
| ObserveOnly: other authorized admin allowed | not run | not run | not run | not run |
| Normal user denied (RBAC) | not run | not run | not run | not run |
| Emergency disable | not run | not run | not run | not run |
| Enforced rollout (with `-Enforce`) | pending | pending | pending | pending |
| Rollback drill | not run | not run | not run | not run |
| Metrics / audit / log privacy | not run | not run | not run | not run |

## Final report fields

```text
Feature availability: GO
Rollout enforcement: PENDING — ObserveOnly
Development admin ID resolved: not run
Test admin ID resolved: not run
Staging admin ID resolved: not run
Production admin ID resolved: not run
ObserveOnly unmatched user allowed: PASS (automated) / not run (on-host)
Enforced unmatched user denied: PASS (automated) / pending activation
Normal user denied: PASS (automated) / not run (on-host)
Emergency disable: PASS (automated) / not run (on-host toggle)
Feature flag defaults in Git: InstitutionalReports=true
Enforcement mode default: ObserveOnly
Secrets committed: no
Production smoke: not run
Production readiness: not run
```

## Next steps

1. For each environment, run `apply-institutional-reporting-phase1-allowlist.ps1` with `-WhatIf`, then without (keeps ObserveOnly).
2. Restart API for that environment only.
3. Run `verify-institutional-reporting-phase1.ps1` and smoke test.
4. Monitor rollout metrics (`reporting_rollout_decisions_total`) in ObserveOnly.
5. When ready, re-run apply script with `-Enforce` per environment after explicit approval.
6. Production enforcement requires independent sign-off after 24–48h monitoring.

Do **not** enable role, department, or percentage rollout in Phase 1.
