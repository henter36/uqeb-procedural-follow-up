# Institutional Reporting Production Acceptance Gate

## Scope

PR #19 adds production acceptance infrastructure for institutional reporting:

- ESLint CI gate
- Reporting limits, concurrency, temp management, metrics, audit, rollout
- Acceptance benchmarks (1k–50k synthetic)
- Smoke scripts and rollback runbook

Feature remains **disabled by default** in production configuration.

## Environment

See `docs/institutional_reporting_production_acceptance_environment.md`.

## Versions

- Backend: .NET 10
- Frontend: Node 24 / Vite
- Playwright Chromium for PDF

## Dataset sizes

| Size | CI default | Manual/scheduled |
| --- | --- | --- |
| 1,000 | Yes (`RUN_REPORTING_ACCEPTANCE=1`) | — |
| 5,000 | Yes | — |
| 10,000 | No | `RUN_REPORTING_ACCEPTANCE_LARGE=1` |
| 20,000 | No | `RUN_REPORTING_ACCEPTANCE_LARGE=1` |
| 50,000 | No | Manual only |

## Defensive limits (from `ReportingOptions`)

| Limit | Default |
| --- | ---: |
| Max concurrent PDF exports | 2 |
| Max concurrent non-PDF exports | 4 |
| Max export duration (seconds) | 120 |
| Max PDF rows per part | 5,000 |
| Max DOCX rows | 20,000 |
| Max XLSX rows | 100,000 |
| Max ZIP/output size (MB) | 100 |
| Min free temp space (MB) | 512 |

Values must be revised after benchmark evidence on the acceptance host.

## Rollout plan

| Phase | Config | Duration | Failure threshold | Rollback |
| --- | --- | --- | --- | --- |
| 0 | Flag off | Permanent default | N/A | N/A |
| 1 | Admin allowlist | 1 week | >5% export failures | `EmergencyDisable=true` |
| 2 | Supervisor allowlist | 2 weeks | >3% export failures | Disable allowlist |
| 3 | Department pilot | 2 weeks | >2% export failures | Remove department IDs |
| 4 | 25% hashed users | 2 weeks | p95 export > 120s | Set `Percentage=0` |
| 5 | 50% | 2 weeks | Memory pressure alerts | Set `Percentage=25` |
| 6 | 100% | After sign-off | SLO breach | Phase 5 rollback |

## Remaining risks

- Per-process concurrency gate does not enforce a global limit across multiple API instances; use a distributed queue in a future phase.
- Large PDF paths (20k+) remain memory-intensive when using `byte[]` responses.
- Chromium availability is environment-dependent; Linux CI covers baseline probe.

## Decision

### Current decision: **NO-GO** for broad production rollout

**GO** only for **Phase 1 (Admin allowlist)** after:

- 10k/20k benchmarks executed on the production-like acceptance host
- Artifacts attached to the release record
- On-call runbook acknowledged

**Blockers until acceptance host run:**

- 10k/20k benchmark artifacts not yet produced on production-like SQL Server host
- Concurrent export saturation tests (1/2/4/8) pending on acceptance host

Default production config remains:

```json
"FeatureFlags": { "InstitutionalReports": false },
"ReportingRollout": { "EmergencyDisable": true }
```
