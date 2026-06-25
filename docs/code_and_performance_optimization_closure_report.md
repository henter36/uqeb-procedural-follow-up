# UQEB Code and Performance Optimization — Closure Report

Created: 2026-06-25  
Repository: `henter36/uqeb-procedural-follow-up`  
Final main reference (pre-P10 merge): `4939aa1`

## Executive decision

```text
GO (program scope delivered with documented gaps)
Production deploy: NO-GO (per AGENTS.md — no production migration/deploy in this program)
```

The optimization program completed all required PR stages. Residual operational acceptance items (full LAN smoke, measured k6 baselines on production-like host, backup/restore drill) remain blocked on environment access and are tracked in `docs/post_merge_operational_acceptance.md`.

---

## Baseline

| Item | Status |
| --- | --- |
| P1 baseline schema/templates | **Delivered** — `docs/performance_baseline/` |
| Measured baseline artifacts | **NOT RECORDED** — `artifacts/performance-baseline/` contains template only (`.gitkeep`) |
| k6 read-smoke | **Scripts ready** — not executed in CI for this program |
| Reporting analysis pipeline benchmark | **Optional test** — `RUN_REPORTING_ANALYSIS_BENCHMARK=1` |
| SQL execution plans (slow queries) | **NOT RUN** — see P9 |

---

## Result of every PR

| Stage | PR | Merge SHA | Result |
| --- | --- | --- | --- |
| PR #24 | [#24](https://github.com/henter36/uqeb-procedural-follow-up/pull/24) | `6267905` | PASS — institutional reporting fixes, server-side limits, tests |
| D1 | [#25](https://github.com/henter36/uqeb-procedural-follow-up/pull/25) | `7b50f67` | PASS — explicit DB provisioning |
| D2 | [#26](https://github.com/henter36/uqeb-procedural-follow-up/pull/26) | `bf09073` | PASS — atomic release promotion + rollback state |
| D3 | [#27](https://github.com/henter36/uqeb-procedural-follow-up/pull/27) | `52ea1fb` | PASS — Windows installed-artifact proof in CI |
| P1 | [#28](https://github.com/henter36/uqeb-procedural-follow-up/pull/28) | `36606dd` | PASS — performance baseline fixtures (no runtime change) |
| P2 | [#29](https://github.com/henter36/uqeb-procedural-follow-up/pull/29) | `459b3ec` | PASS — analysis pipeline instrumentation |
| P3 | [#30](https://github.com/henter36/uqeb-procedural-follow-up/pull/30) | `84b9e66` | PASS — audit unit of work + create/update atomicity |
| P4 | [#31](https://github.com/henter36/uqeb-procedural-follow-up/pull/31) | `2e0eada` | PASS — workflow command atomicity |
| P5 | [#32](https://github.com/henter36/uqeb-procedural-follow-up/pull/32) | `f12da2e` | PASS — side-effect-free reads |
| P6 | [#33](https://github.com/henter36/uqeb-procedural-follow-up/pull/33) | `16a1009` | PASS — unified transaction workspace read model |
| P7 | [#34](https://github.com/henter36/uqeb-procedural-follow-up/pull/34) | `f901175` | PASS — dashboard/report query consolidation + cache |
| P8 | [#35](https://github.com/henter36/uqeb-procedural-follow-up/pull/35) | `4939aa1` | PASS — backward-compatible cursor pagination |
| P9 | — | — | **NOT NEEDED** — no execution-plan evidence (see below) |
| P10 | [#36](https://github.com/henter36/uqeb-procedural-follow-up/pull/36) | pending | Legacy export hardening + this closure report |

---

## P9 — Evidence review (NOT NEEDED)

```text
P9 status: NOT NEEDED
Evidence reviewed:
  - docs/performance_baseline/README.md (templates only)
  - artifacts/performance-baseline/ (no measured runs)
  - docs/post_merge_operational_acceptance.md §4.7 (execution plans NOT RUN)
  - Existing migrations: 20260611074059, 20260611130000, 20260611160000,
    20260611200000 (verify indexes), AppDbContext snapshot (Transactions +
    Assignments + AuditLogs + FollowUps indexes already present)
Reason:
  No baseline, actual execution plans, logical reads, duration, key lookup, or
  scan-behavior measurements demonstrating a missing index. Per program gate,
  no index PR was created.
```

---

## P10 — Legacy export scope

| Change | Detail |
| --- | --- |
| Cancellation | `ExportReportDetailsExcelAsync` honors `CancellationToken` between batched reads and before workbook serialization |
| Client disconnect | `ReportsController` passes `HttpContext.RequestAborted` |
| Path traversal | `LegacyReportExportHelper` sanitizes export filenames |
| Streaming/temp files | **Not added** — no measured memory pressure evidence; existing 1000-row batching retained |
| Institutional exports | **Unchanged** — out of P10 scope |

### Tests added (P10)

- `LegacyReportExportHelperTests` — filename sanitization
- `LegacyReportExportTests` — full batched export (1005 rows), cancellation, current-page export, concurrent exports

---

## Performance metrics (p50 / p95 / p99)

| Scenario | Before | After | Notes |
| --- | --- | --- | --- |
| API read-smoke (k6) | not measured | not measured | Threshold contract: p95 < 1500 ms |
| Dashboard load | 6 HTTP calls | 1 HTTP call | P7 frontend + consolidated backend |
| Page summary counts | 7 queries | 1 aggregate | P7 `GetPageSummaryAsync` |
| Analysis pipeline stages | not measured | instrumented | P2 OpenTelemetry histograms |
| Legacy Excel full export | batched (1000) | batched + cancellable | P10 |
| Transaction search cursor | offset only | optional cursor mode | P8 |

No production p50/p95/p99 numbers were captured in this program.

---

## SQL / SaveChanges / memory

| Area | Before (conceptual) | After |
| --- | --- | --- |
| Read GET mutations | Overdue status writes on read | Removed (P5) |
| Create/update audit | Multiple SaveChanges | Single transaction (P3) |
| Workflow commands | Partial commits possible | Atomic transactions (P4) |
| Dashboard counts | Multiple round-trips | Consolidated + cached (P7) |
| Indexes added in program | — | **None** (P9 NOT NEEDED) |
| Legacy export memory | Full workbook in memory | Unchanged (batch DB reads only) |

---

## Throughput / caching

- Versioned cache keys + stampede protection for dashboard and page-summary (P7)
- Side-effect-free reads reduce write lock contention on hot GET paths (P5)

---

## Windows package / rollback proof

| Item | Status |
| --- | --- |
| D3 installed-artifact CI proof | PASS — PR #27 |
| D2 rollback state + promotion | PASS — PR #26 |
| Production deploy | **NOT EXECUTED** (program constraint) |

Rollback command reference: `AGENTS.md` § التراجع الآمن

---

## Open risks

1. Measured performance baselines not recorded — future regressions rely on CI unit/integration tests only.
2. SQL Server execution plans not reviewed on production-scale data — index needs unknown.
3. Full operational acceptance (LAN smoke, backup/restore, k6 at load) still **BLOCKED** on host/credentials.
4. Legacy Excel exports still materialize full workbook bytes in memory for very large datasets.
5. Institutional PDF export concurrency uses semaphores — legacy exports do not share that gate.

---

## Verification summary (program gates)

| Gate | P10 branch status |
| --- | --- |
| Backend full suite | Run in CI |
| Frontend full suite | Run in CI |
| SonarCloud | Run in CI |
| SQL Server integration | Existing tests in CI (when configured) |
| k6 | Scripts present; not part of required CI for these PRs |
| Visual/PDF | CI jobs pass on prior merges |
| Windows package proof | Delivered in D3 |

---

## Recommended next steps (outside this program)

1. Record baselines per `docs/performance_baseline/README.md` on a staging SQL Server instance.
2. Capture execution plans for top 5 API/report queries; re-evaluate P9 indexes with evidence.
3. Execute `docs/post_merge_operational_acceptance.md` blocked scenarios on production-like LAN host.
4. Production release via `scripts/deploy-production-v2.ps1` only after explicit GO from acceptance.
