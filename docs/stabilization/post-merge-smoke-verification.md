# PR A — Post-Merge Stabilization and Smoke Verification

**Date of verification:** 2026-07-04 (20:00–20:25 UTC)
**Base branch:** `main`
**Base commit SHA:** `0dad3ab3e1e8a179613a520c3f0296118bdf9227` — *fix(transactions): stop new assignment form from inheriting outgoing letter number (#103)*
**Work branch:** `chore/post-merge-stabilization`

---

## 1. Environment used

| Item | Value |
|---|---|
| OS | macOS 15.7.7 (x86_64) |
| .NET SDK | `10.0.301` (matches `global.json`) |
| Node.js | `v24.17.0` (matches README requirement) |
| npm | `11.17.0` (matches README requirement) |
| SQL Server | Docker container `uqeb-sql`, image `mcr.microsoft.com/mssql/server:2022-latest`, already running and reachable on `127.0.0.1:1433` |
| Chromium (Playwright) | Available and functional in this environment (confirmed via `/api/institutional-reports/readiness` → `chromiumLaunchSuccessful: true`, and via a live report preview render) |
| Backend config | `backend/Uqeb.Api/appsettings.json` (tracked, pre-existing local dev connection string, unmodified by this PR) |
| Frontend config | `frontend/uqeb-ui/.env.local` (git-ignored, pre-existing) |

No new environment files, secrets, or connection strings were introduced by this PR.

---

## 2. Commands executed

### Backend
```bash
dotnet restore backend/Uqeb.sln
dotnet build backend/Uqeb.sln -c Release
dotnet ef migrations list --project backend/Uqeb.Api/Uqeb.Api.csproj
dotnet test backend/Uqeb.Api.Tests/Uqeb.Api.Tests.csproj -c Release --no-restore
```

### Frontend
```bash
cd frontend/uqeb-ui
npm ci
npm run lint
npm run lint:css
npx tsc -b
npm run build
npm test -- --run --maxWorkers=2
```

### Runtime / smoke
```bash
ASPNETCORE_ENVIRONMENT=Development ASPNETCORE_URLS=http://localhost:5080 \
  dotnet run --no-launch-profile --project backend/Uqeb.Api/Uqeb.Api.csproj
curl http://localhost:5080/health/live
curl http://localhost:5080/health/ready
curl http://localhost:5080/health
curl -X POST http://localhost:5080/api/auth/login -d '{"username":"admin","password":"Admin@123"}'
```
Frontend dev server (`npm run dev`, port 5173) was already running from a prior local session against the same repo/commit and was reused for browser-based smoke checks rather than restarted.

---

## 3. Backend result

| Check | Result |
|---|---|
| `dotnet restore` | **PASS** — up to date, ~4s |
| `dotnet build -c Release` | **PASS** — 0 warnings, 0 errors, ~20s |
| `dotnet ef migrations list` | **PASS** — 30 migrations listed, none marked pending |
| DB migration history (`__EFMigrationsHistory`, verified directly via `sqlcmd`) | **PASS** — latest applied migration `20260703151000_AddRecurringNextTransactionCreationMethod` matches the latest migration in code |
| Backend tests — CI-equivalent fast filter (excludes Playwright/visual/SQL-integration/`Category=SqlServer`) | **PASS** — **1014/1014**, 0 failed, ~27s |
| Backend tests — `Category=SqlServer` | **PASS** — **29/29**, ~1s |
| Backend tests — `InstitutionalReportPlaywrightPdfExporterTests` | **PASS** — **12/12**, ~43s |
| Backend tests — `InstitutionalReportVisualRegressionTests` | **PASS** — **11/11**, ~35s |
| Backend tests — `InstitutionalReportPreviewPdfParityTests` | **PASS** — **2/2** |
| Backend tests — `TransactionPersistenceSqlServerIntegrationTests` | **PASS** — **1/1** |
| **Backend tests — full unfiltered suite (all of the above combined)** | **PASS — 1068/1068, 0 failed, 0 skipped**, ~1m37s |

Chromium and SQL Server both happened to be available in this environment, so none of the categories the CI job normally excludes needed to be skipped — the entire backend test suite ran, not just a subset.

---

## 4. Frontend result

| Check | Result |
|---|---|
| `npm ci` | **PASS** — 382 packages, ~7s. 1 low-severity `npm audit` finding (see §7, deferred) |
| `npm run lint` (ESLint) | **PASS** — 0 errors |
| `npm run lint:css` (Stylelint) | **PASS** — 0 errors |
| `npx tsc -b` (typecheck) | **PASS** — 0 errors |
| `npm run build` (Vite production build) | **PASS** — succeeds; pre-existing informational warning about one >500 kB chunk (not a regression, not addressed — out of scope) |
| `npm test -- --run --maxWorkers=2` | **PASS — 489/489 tests, 58/58 test files**, ~55s |

Full frontend suite ran — no subset was needed.

---

## 5. Health endpoint result

Checked before and after the smoke pass (API running against the live local SQL Server):

| Endpoint | Before smoke | After smoke |
|---|---|---|
| `GET /health/live` | `200` `{"status":"live"}` | `200` `{"status":"live"}` |
| `GET /health/ready` | `200` `{"status":"ready", ...}` | `200` `{"status":"ready", ...}` |
| `GET /health` | `200` `{"status":"healthy", ...}` | `200` `{"status":"healthy", ...}` |

`/health` and `/health/ready` sub-checks, all `pass`: `database`, `playwrightChromium`, `reportNumberSequence`, `institutionalReporting`, `followUpPrintSchema`, `followUpDefaultTemplate`, `followUpPrintOptions`, `followUpPrintProcessor`.

No report-related health check failed unexpectedly at any point.

---

## 6. Smoke verification result

All checks performed against the running API (port 5080) and the running frontend dev server (port 5173, `admin` / `Admin@123`), using existing data already present in the local database — no new flows were invented.

| # | Path | Method | Result |
|---|---|---|---|
| 1 | Login/authentication | `POST /api/auth/login` (API) + login form (browser) | **PASS** — JWT issued; browser login redirected to the authenticated dashboard with live KPIs (13 open transactions, 6 overdue, 11 pending follow-up) |
| 2 | Transaction list loads | `GET /api/transactions` (API) + `/transactions` (browser) | **PASS** — 13 real transactions returned/rendered with filters, search, and status tabs |
| 3 | Create or view transaction | `GET /api/transactions/{id}` + click-through from list | **PASS** — opened transaction `٧٦٦٦٧` (id 3015) from the list |
| 4 | Transaction details page loads | `/transactions/3015` (browser) | **PASS** — full workspace: status, dates, referrals, follow-ups, attachments, audit trail sections all rendered |
| 5 | Referral/add referral flow does not regress | Opened "إضافة احالة" panel on transaction 3015 | **PASS** — letter number field (`رقم الخطاب`) confirmed empty on open, screenshotted and zoomed to verify no stray value. Existing referrals on the same transaction show two *different* letter numbers per department (`2456` for الموارد البشرية, `98765` for الشؤون الإدارية) — direct live confirmation that PR #103's fix behaves correctly against real data, not just under test |
| 6 | Reply/statement flow does not regress | UI presence check ("تسجيل إفادة" action visible and enabled on transaction workspace) | **PASS** (UI-level) — not exercised end-to-end interactively in this pass; fully covered by the 489/489 passing frontend suite (`ReplyFormPanel`, `AdminEditResponseFormPanel`, `CompleteResponseFormPanel` test files) and 1068/1068 backend suite |
| 7 | Follow-up/print-related path | `/follow-up-print/eligible` (browser) | **PASS** — page loads with real counts (13 matching, 1 eligible, 2 excluded-as-recently-printed, 1 expected letter/part) |
| 8 | Attachments path | Attachments card on transaction 3015 | **PASS** — empty-state UI renders correctly ("لا توجد مرفقات لهذه المعاملة") with a working "إضافة أول مرفق" affordance; not exercised with an actual upload in this pass (covered by `AttachmentFormPanel.test.tsx`, 4/4 passing) |
| 9 | Report builder page loads | `/report-builder` (browser) | **PASS** — full settings UI (report type, title, content level, saved templates, date range, filters incl. the new PR #102 department-time-series section) |
| 10 | Report preview/export does not fail with infrastructure errors | Clicked "معاينة التقرير" on the report builder | **PASS** — preview rendered successfully: 22 pages, real preview report number, correct date range and title, no error banner, no infra failure |
| 11 | Health/readiness after smoke remains healthy | Re-checked all three health endpoints after steps 1–10 | **PASS** — unchanged, all `pass`/`healthy` (see §5) |
| 12 | Logs contain no new critical runtime errors | `grep` over the full API stdout/stderr log for the session | **PASS** — zero lines matching `^(fail|crit|error):` (.NET log-level prefixes), zero "Unhandled exception", zero "500 Internal"; browser console also clean (no errors/exceptions reported for the tab across the whole session) |

---

## 7. Failures found

**None.** No backend test failed, no frontend test failed, no lint/typecheck/build error, no health check failure, no smoke-path regression, and no unexpected runtime error in logs.

---

## 8. Fixes made

**None required.** The repository at `main@0dad3ab` builds, tests, and runs cleanly end-to-end in this environment. No code changes were made in this PR beyond adding this verification document.

---

## 9. Items intentionally skipped and why

| Item | Why skipped |
|---|---|
| Interactive end-to-end exercise of the reply/statement flow (item 6) and an actual file upload for attachments (item 8) | Both are already exhaustively covered by the passing automated suites (`ReplyFormPanel.test.tsx`, `AdminEditResponseFormPanel.test.tsx`, `CompleteResponseFormPanel.test.tsx`, `AttachmentFormPanel.test.tsx`, plus backend `Assignment`/`Response` test coverage). Re-driving them manually would duplicate coverage without adding new signal, and this PR's mandate is stabilization, not new exploratory QA. UI presence/availability was confirmed instead. |
| Installing/relying on `k6` or Node-based performance tests under `performance-tests/` and `tests/performance/` | Explicitly out of scope for a smoke/stabilization pass — these are load/performance suites, not correctness gates, and README documents them as a separate concern from the local verification gate. |
| `reporting-acceptance-large` CI job | Documented in README as `workflow_dispatch`-only (manually triggered in CI), not part of the standard local verification gate. Not run here. |
| `npm audit fix` for the 1 low-severity `esbuild` advisory (dev-server arbitrary file read, Windows-only attack surface) | Low severity, dev-only, platform-scoped (Windows), and fixing it means bumping a transitive dependency version outside this PR's stated scope ("no unrelated cleanup", "keep changes minimal"). Documented here instead of silently fixed. |

---

## 10. Items deferred to later PRs

| Item | Defer to | Reason |
|---|---|---|
| `esbuild` dev-dependency advisory (see §9) | PR B or a dedicated dependency-bump PR | Needs its own lockfile-only change and verification; not a build/test/runtime blocker today. |
| Any suspected authorization/data-isolation nuance | PR B | Per task instructions, authorization/data-isolation redesign or investigation is explicitly out of scope for PR A. No such issue was observed to *block* the smoke path in this pass, so nothing concrete is being flagged here beyond noting that a dedicated review was not performed (not requested for PR A). |
| Recurring-period logic | Not touched, not deferred | No current failing test or reproducible bug was found against recurring-period logic; per task rules it must not be changed without one. All recurring-related backend/frontend tests pass. |
| Report feature additions | Not applicable | No new report features were considered or requested in this PR. |
| Frontend bundle size warning (`>500 kB` chunk) | Backlog / future perf PR | Pre-existing, informational only, not a build failure; unrelated to this stabilization pass's mandate. |

---

## 11. Final judgment

**GO** — main is stable and safe to build on for PR B.

Rationale:
1. The app builds cleanly (backend Release build, frontend production build) with zero warnings/errors.
2. The full test suite is green with no exclusions needed in this environment: **backend 1068/1068**, **frontend 489/489**.
3. The API starts successfully and all three health endpoints (`/health/live`, `/health/ready`, `/health`) report healthy both before and after the smoke pass.
4. All 12 requested smoke-path checks passed, including a live, visual, in-browser confirmation that PR #103's letter-number fix holds against real data (two referrals on the same transaction retain two distinct letter numbers, and the "add referral" form opens empty).
5. No unrelated behavior changes were introduced — this PR adds only this documentation file.
6. All non-blocking findings are explicitly classified above as skipped (with reason) or deferred (with target PR).
