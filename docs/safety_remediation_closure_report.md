# UQEB Post-Update Safety Remediation — Closure Report

**Baseline main SHA:** `f9a76e6ac614a97fd6663a6ac780a2169659ddf4`
**Remediation stack HEAD:** `fix/sonar-maintainability-cleanup` (includes PRs #37–#40)
**Report date:** 2026-06-25
**Production access:** none (offline/dev verification only)

## Pull requests

| PR | Branch | Merge SHA | Scope |
|----|--------|-----------|--------|
| [#37](https://github.com/henter36/uqeb-procedural-follow-up/pull/37) | `fix/production-offline-deployment-safety` | pending | Offline install contract, bind address, rollback phases, junction safety, streaming BAK hash |
| [#38](https://github.com/henter36/uqeb-procedural-follow-up/pull/38) | `fix/transaction-audit-integrity` | pending | Audit linkage backfill, diagnostic report, dead code removal |
| [#39](https://github.com/henter36/uqeb-procedural-follow-up/pull/39) | `fix/observability-and-test-reliability` | pending | Metric bucketing, stage outcomes, benchmark assertions |
| [#40](https://github.com/henter36/uqeb-procedural-follow-up/pull/40) | `fix/sonar-maintainability-cleanup` | pending | Deployment test alignment, nullability cleanup |

Merge order: **#37 → #38 → #39 → #40** (sequential review).

## Verification summary

| Gate | Result |
|------|--------|
| Backend tests (CI filter) | 412 passed locally |
| Backend tests (full suite) | 434 passed locally |
| DatabaseProvision hash test | 1 passed |
| Pester `deployment-package.Tests.ps1` | 77 passed, 1 skipped (non-Windows junction) |
| `git diff --check` | clean on remediation branches |
| SonarCloud (PR #37) | pass (informational) |
| Production host / DB | not accessed |

## Offline deployment proof (code + tests)

- Official command: `install-production-package.ps1 -PackagePath C:\Uqeb\incoming\Uqeb-<version>.zip` runs **DB backup + verify + migrations** by default (no `-ApplyDatabaseMigration`).
- Flow order: validate package → SQL config → backup → migrations → stop API → optional file backup → promote → health → retention → move ZIP to `incoming\deployed`.
- `run-api.cmd` defaults to `ASPNETCORE_URLS=http://10.0.177.17:5000`; health defaults to `http://10.0.177.17:5000`.
- Phase-aware rollback: pre-stop failures skip file rollback; post-promotion uses release rollback; **no automatic `RESTORE DATABASE`**.
- Junction handling uses PowerShell/.NET reparse APIs (no `cmd /c mklink/rmdir`).
- BAK SHA256 uses streaming hash in `Uqeb.Tools.DatabaseProvision`.

## Transaction / audit integrity

- Create path defers audit persistence until transaction and assignment IDs exist; backfill runs before final audit save in the same DB transaction.
- Admin read-only report: `GET /api/security/audit-integrity-report` (classifies missing links; no auto-repair).

## Observability

- Analysis duration metric tag: `snapshot_count_bucket` (`0`, `1-100`, `101-1000`, `1001-5000`, `5001+`).
- Stage metrics include `stage_outcome=success|failed`; failed stages log warnings, not completion messages.

## Production data impact

**None.** All changes verified on dev/in-memory/SQL-less mocks. No production settings, host, or database were modified.

## Decision: **CONDITIONAL GO**

Code and automated gates are ready for **user-operated** production deployment after sequential PR merge and on-host smoke checks.

### Remaining user-operated checks (on `10.0.177.17`)

1. Merge PRs #37–#40 in order after review.
2. Build release ZIP on updated `main` with `scripts/build-production-package.ps1` (verify `VITE_API_BASE_URL=http://10.0.177.17:5000/api` in dist).
3. Copy ZIP + SHA256 to `C:\Uqeb\incoming`.
4. Run as Administrator:
   ```powershell
   $package = Get-ChildItem "C:\Uqeb\incoming\Uqeb-*.zip" |
       Sort-Object LastWriteTime -Descending |
       Select-Object -First 1
   C:\UqebTools\install-production-package.ps1 -PackagePath $package.FullName
   ```
5. Confirm installer output shows DB backup path, migration success, and `database=pass` from health check.
6. Verify `C:\Uqeb\run-api.cmd` contains `http://10.0.177.17:5000`.
7. Login from LAN client; confirm API requests target `http://10.0.177.17:5000/api`.
8. Optional: run `GET /api/security/audit-integrity-report` after deploy to baseline historical audit gaps (read-only).

### Post-deploy rollback reminder

- File rollback is automatic only for post-promotion failures when backups exist.
- Database rollback remains **manual** via `RESTORE DATABASE` from `C:\Uqeb\backup\db\` using the command printed in the failure report.
