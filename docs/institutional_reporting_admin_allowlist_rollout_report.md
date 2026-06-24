# Institutional Reporting — Phase 1 Admin Allowlist Rollout Report

## Per-environment status

| Environment | Username | User ID | Feature Flag | Allowlist | Readiness | Smoke | Decision |
| --- | --- | --- | --- | --- | --- | --- | --- |
| Development | admin | pending | not applied | not applied | not run | not run | **NO-GO** |
| Test | admin | pending | not applied | not applied | not run | not run | **NO-GO** |
| Staging | admin | pending | not applied | not applied | not run | not run | **NO-GO** |
| Production | admin | pending | not applied | not applied | not run | not run | **NO-GO** |

> User IDs are resolved at activation time and must be masked in reports (example: `a12f…9c41`). Never commit real IDs to Git.

## Summary

| Field | Value |
| --- | --- |
| **Date** | 2026-06-24 |
| **Target commit (main / PR #19)** | `980823c912d9a5e87bbf2a8d8b8b88072067185b` |
| **Phase 1 pilot account** | login `admin` only (per-environment `Users.Id`) |
| **Package commit** | pending push (admin username resolution + scripts) |
| **Overall decision** | **NO-GO — rollout disabled on all environments** |

## Pre-activation record

| Item | Value |
| --- | --- |
| Current production commit | Unknown — requires on-host check |
| Target commit | `980823c` |
| Feature flag defaults in Git | `InstitutionalReports=false`, `EmergencyDisable=true` |
| Secrets committed | **No** |
| Backup location | Created per run under environment backup root |

## Automated verification (CI / local)

| Check | Result |
| --- | --- |
| Admin username → single active Admin resolved | **PASS** (`ReportingPhase1AdminUserResolverTests`) |
| Missing / duplicate / inactive / non-admin aborted | **PASS** |
| Different IDs per environment DB | **PASS** |
| Allowlisted admin ID allowed | **PASS** |
| Different admin role user denied | **PASS** |
| Normal user denied | **PASS** |
| `EmergencyDisable=true` denies admin | **PASS** |
| `InstitutionalReports=false` denies admin | **PASS** |
| PDF / XLSX for allowlisted admin (stub) | **PASS** |

## On-host verification (required for GO)

| Check | Development | Test | Staging | Production |
| --- | --- | --- | --- | --- |
| Backup | not run | not run | not run | not run |
| Admin ID resolution | not run | not run | not run | not run |
| Readiness Ready | not run | not run | not run | not run |
| Smoke PASS | not run | not run | not run | not run |
| PDF / XLSX | not run | not run | not run | not run |
| Other admin denied | not run | not run | not run | not run |
| Normal user denied | not run | not run | not run | not run |
| Emergency disable | not run | not run | not run | not run |
| Rollback drill | not run | not run | not run | not run |
| Metrics / audit / log privacy | not run | not run | not run | not run |

## Final report fields

```text
Development admin ID resolved: not run
Development rollout: NO-GO
Test admin ID resolved: not run
Test rollout: NO-GO
Staging admin ID resolved: not run
Staging rollout: NO-GO
Production admin ID resolved: not run
Production rollout: NO-GO
Other admin denied: PASS (automated) / not run (on-host)
Normal user denied: PASS (automated) / not run (on-host)
Emergency disable: PASS (automated) / not run (on-host toggle)
Feature flag defaults in Git: safe (false / emergency true)
Secrets committed: no
Production smoke: not run
Production readiness: not run
Final decision: NO-GO — rollout disabled on all environments until on-host activation completes per environment
```

## Next steps

1. For each environment, run `apply-institutional-reporting-phase1-allowlist.ps1` with `-WhatIf`, then without.
2. Restart API for that environment only.
3. Run `verify-institutional-reporting-phase1.ps1` and smoke test.
4. Update the matrix above with masked IDs and per-environment **GO** only when all gates pass.
5. Production **GO** requires independent sign-off after 24–48h monitoring.

Do **not** enable role, department, or percentage rollout in Phase 1.
