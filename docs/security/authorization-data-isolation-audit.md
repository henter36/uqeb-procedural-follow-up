# PR B — Authorization and Data-Isolation Audit

**Date:** 2026-07-05
**Base branch:** `main`
**Base commit SHA:** `eaeeb46bd089c126496dbe2e2ab1d9198c5f2861` — *chore: post-merge stabilization and smoke verification (#104)*
**Work branch:** `security/authorization-data-isolation-audit`

---

## 1. Method

The system's authorization model was reverse-engineered from code (not assumed from documentation), then every controller/service pair in the required audit areas was traced from the HTTP entry point down to the actual database query or file-system access, verifying that a data-isolation *check* — not just a role *attribute* — exists and is correct. Four parallel deep-dive investigations covered: (1) transaction access, (2) attachments + follow-up print, (3) dashboard + reports, (4) department responses + admin/reference data. Every finding below was independently re-verified by reading the cited file:line before being classified as PASS, DEFECT, or a documented non-fix.

---

## 2. Effective access model (as implemented, not as documented)

| Role | Department-scoped? | Effective trust level |
|---|---|---|
| `Admin` (1) | No | Full access everywhere |
| `Supervisor` (2) | No | Global; can edit/close transactions, review department responses |
| `DataEntry` (3) | No | Global; can edit transactions, create follow-up print jobs, review department responses |
| `DepartmentUser` (4) | **Yes — the only scoped role** | Restricted to transactions where their own department has an active `RequiresReply` assignment or a `DepartmentResponse` row; can submit but not review department responses |
| `Reader` (5) | No | Global read-only; excluded only from mutation and from the new `ViewOperationalDashboard` policy is **not** excluding it (see §4) |

Key mechanism, used consistently across the codebase:
- `ICurrentUserService` (`backend/Uqeb.Api/Services/CurrentUserService.cs`) resolves `Role`/`DepartmentId`/`UserId` from JWT claims set at login (`AuthService.cs:38-46`). A missing/unparseable role claim defaults to `Reader` (least-privileged fallback, `CurrentUserService.cs:23-25`) — **missing user context is rejected**, not trusted.
- `TransactionService.CanAccessTransactionAsync` (`TransactionService.cs:2192-2205`, made `public` in this PR) is the canonical per-transaction gate: non-`DepartmentUser` roles get unrestricted access; `DepartmentUser` requires an active `RequiresReply` assignment or a `DepartmentResponse` row for their own `DepartmentId`. A `DepartmentUser` with no department claim gets `UnauthorizedAccessException` (`RequireDepartmentUserDepartmentId`, fail-closed).
- The same pattern is reimplemented independently and correctly in `DepartmentResponseService` (`CanRead`/`RequireDepartmentOwnership`), `InstitutionalReportSnapshotQuery.ApplyAccessScopeFilter`, and `FollowUpPrintAccessService`.
- Global authorization policies are defined in `backend/Uqeb.Api/Authorization/FollowUpPrintAuthorizationExtensions.cs` and referenced by name from `Policies.cs`.
- Every controller carries a class-level `[Authorize]` or `[Authorize(Policy = ...)]`; `[AllowAnonymous]` is used only for login, health checks, and the login-page branding logo.

---

## 3. Audited endpoints/modules

TransactionsController + TransactionService (list/detail/create/update/close/assignments/followups/attachments/audit-log); AttachmentService; DepartmentResponsesController + DepartmentResponseService; FollowUpPrintController + FollowUpPrintJobService + FollowUpPrintEligibilityService + FollowUpPrintAccessService + FollowUpLetterPrintRecordService + FollowUpLetterRenderService; DashboardController; ReportsController + ReportService; InstitutionalReportsController + InstitutionalReportService + InstitutionalReportSnapshotQuery; AdminControllers (Users/Departments/ExternalParties); CategoriesController; SecurityController; AuditIntegrityController; `Program.cs` global auth wiring; frontend `ProtectedRoute`, `App.tsx` route table, `navConfig.ts`, `AuthContext.tsx`.

---

## 4. Confirmed defects fixed

### DEFECT 1 (critical) — Transaction attachment download had no authorization check
`GET /api/transactions/{id}/attachments/{attachmentId}/download` (`TransactionsController.cs:402-408`, pre-fix) called `AttachmentService.DownloadAsync(id, attachmentId)` directly, with no call to `CanAccessTransactionAsync`. `AttachmentService.DownloadAsync` (`AttachmentService.cs:90-98`) matches only on `attachmentId + transactionId` — any authenticated user, including a `DepartmentUser` from an unrelated department, could download the raw bytes of any attachment (letters, scans) on any transaction by guessing sequential IDs.

**Fix:** both `GetAttachments` and `DownloadAttachment` now call `await _transactions.CanAccessTransactionAsync(id, _currentUser)` and return `404 NotFound` (not 403, to avoid confirming the transaction's existence to an unauthorized caller — matching the existing convention used by `GetAssignments`) before touching `AttachmentService`. `CanAccessTransactionAsync` was promoted from `private` to a new `ITransactionService` interface member so the controller can call it without duplicating the access-scope logic.

- `backend/Uqeb.Api/Services/TransactionService.cs` — interface + method visibility change only, logic unchanged.
- `backend/Uqeb.Api/Controllers/TransactionsController.cs` — two `if` guards added.

### DEFECT 2 — Transaction attachment metadata listing had no authorization check
Same root cause and same fix as Defect 1, applied to `GET /api/transactions/{id}/attachments` (`TransactionsController.cs:378-382`, pre-fix). This leaked filenames, uploader names, sizes, and timestamps cross-department, and was the mechanism by which valid `attachmentId`s could be discovered for Defect 1.

### DEFECT 3 — `DashboardController` (`/api/dashboard/*`) had no role restriction at all
`DashboardController` (`summary`, `action-required`, `top-overdue-departments`, `top-incoming-parties`, `category-distribution`, `status-distribution`) carried only a bare `[Authorize]` — any authenticated role, including `DepartmentUser`, could call it directly and receive **institution-wide** counts and cross-department performance rankings, with zero department filter anywhere in the underlying `ReportService` queries.

**Important scoping note on real-world severity:** the currently-live frontend `Dashboard.tsx` page calls `reportsApi.dashboard()`, which hits the **separate, legacy** `/api/reports/dashboard` endpoint (`ReportsController.cs:31-39`) — that endpoint was **already** correctly restricted to `Policies.CanEditTransactions` (Admin/Supervisor/DataEntry), confirmed by the pre-existing, already-passing test `DepartmentUser_PrivilegedReadEndpoints_ReturnForbidden("/api/reports/dashboard")`. So there was no live, UI-driven cross-department leak through the page users actually see today. However, the sibling `DashboardController` family is a real, independently-reachable, unauthenticated-by-role API surface: it is wired up in `frontend/uqeb-ui/src/api/services.ts` (`dashboardApi.*`) but never called by any current page — meaning any client holding a valid `DepartmentUser` JWT (e.g., via `curl` or a compromised token, not just the current UI) could call it directly and pull global data. This is a confirmed defect at the API layer regardless of current frontend usage, and the kind of orphaned/duplicate endpoint that is easy to miss and easy to re-introduce via a future frontend change.

**Fix:** added `Policies.ViewOperationalDashboard` (Admin, Supervisor, DataEntry, Reader — every role except `DepartmentUser`, matching the access model in §2) and applied it at the `DashboardController` class level. `Reader` is deliberately included (it is not department-scoped anywhere else in the system).

**Frontend follow-up (minimal, required to avoid a broken page once the backend correctly rejects the call):** `Dashboard.tsx`'s index route (`/`) is the post-login landing page for every role including `DepartmentUser` (`App.tsx:43`, no `requiredRoles`). Before this PR, a `DepartmentUser` landing there already got a generic "failed to load" error (the legacy endpoint was already blocking them) — this PR makes that consistent and non-broken: `DashboardPage` now redirects a `DepartmentUser` straight to `/department-responses` (their existing, correctly-scoped landing area, `App.tsx:192`) instead of fetching institution-wide data or showing an error screen, and the sidebar nav item for "لوحة المتابعة" is hidden for `DepartmentUser` (`navConfig.ts`), matching the existing `hideForDepartmentUser` pattern already used for `/transactions` and `/reports`.

---

## 5. Tests added

All new tests were verified to **fail against the pre-fix code and pass with the fix restored** (each fix was temporarily reverted, the test suite re-run, then the fix restored — not just written and assumed correct).

| File | Tests | Proves |
|---|---|---|
| `backend/Uqeb.Api.Tests/TransactionAttachmentAuthorizationTests.cs` (new) | 5 | Own-department `DepartmentUser` can list/download a real, on-disk-backed attachment (200 + correct bytes); a different department's `DepartmentUser` gets 404 on both list and download (not just an empty list — a real cross-department block); Admin unaffected |
| `backend/Uqeb.Api.Tests/DepartmentUserEndpointAuthorizationTests.cs` (extended) | +6 forbidden, +4 positive-role | `DepartmentUser` gets 403 on all six `/api/dashboard/*` routes (extends the existing `DepartmentUser_PrivilegedReadEndpoints_ReturnForbidden` theory); Admin/Supervisor/DataEntry/Reader still get 200 on `/api/dashboard/summary` |
| `backend/Uqeb.Api.Tests/DepartmentUserAuthorizationContractTests.cs` (extended) | +2 | `ViewOperationalDashboard` policy excludes `DepartmentUser` (attribute-level contract, alongside the existing policy contract theory) and explicitly includes `Reader` |
| `frontend/uqeb-ui/src/pages/Dashboard.test.tsx` (extended) | +1 | A `DepartmentUser` is redirected to `/department-responses` and the dashboard API is never called |
| `frontend/uqeb-ui/src/components/layout/Sidebar.test.tsx` (extended, existing test) | assertion added | Dashboard nav link is hidden for `DepartmentUser`, alongside the pre-existing assertions for `/transactions` and `/reports` |

---

## 6. Items reviewed with no issue found

- **Transaction list/search, direct-ID GET (basic/workspace/full), followups, assignments, audit-log:** all correctly route through `CanAccessTransactionAsync`/`ApplyDepartmentUserScope`; a `DepartmentUser` cannot widen scope via query filters (`DepartmentId`, `OutgoingDepartmentId`, etc. are ANDed on top, never OR'd); pagination `totalCount`/`total` are computed from the same scoped query, so they cannot over-report.
- **Excel import (preview/commit):** both `[Authorize(Policy = Policies.AdminOnly)]`, matching documented behavior.
- **Cancel/Archive/Close/CompleteResponse:** each independently re-checks the caller's role server-side inside the service, in addition to the controller policy.
- **Department responses (create/update/submit/review/read-by-id/download-attachment):** every write and read path re-derives the department from `currentUser.DepartmentId`, never trusts a client-supplied department id, and rejects a `DepartmentUser` whose department doesn't match the response's department — including on attachment download (`DepartmentResponseService.DownloadAttachmentAsync` already called `CanRead` before this PR).
- **Follow-up print job endpoints** (create/list/detail/cancel/retry/mark-printed/part-view): all correctly gated through `FollowUpPrintAccessService`, which scopes DataEntry to jobs they created themselves; re-verified that download of a job's rendered output specifically re-checks ownership at that step (not just at creation).
- **Institutional reports (preview/export/templates):** controller is `[Authorize(Policy = Policies.AdminOnly)]` at the class level, applied uniformly to preview and export (no path lets export skip the gate preview enforces); `ApplyAccessScopeFilter` provides a server-side department clamp independent of the client-supplied `DepartmentIds` filter (defense-in-depth, redundant with the Admin-only gate but verified correct); no report-by-id/report-number retrieval endpoint exists, so there is no IDOR surface for previously generated report content.
- **Admin/reference data (Users/Departments/ExternalParties/Categories, Security, AuditIntegrity):** mutation endpoints are `AdminOnly`; read endpoints are open to authenticated users as intended reference data; no individual action silently overrides its controller's stricter default (ASP.NET Core authorization only stacks, never loosens, without an explicit `[AllowAnonymous]`, and none was misused).
- **Frontend route protection:** every route requiring restriction has a matching `requiredRoles` on `ProtectedRoute`; role state used for rendering comes from `localStorage`/JWT payload and cannot itself grant real API access if tampered with, since the server independently re-validates the signed JWT's role/department claims — confirming the project's frontend guards are UX-only, as they should be, with the real boundary enforced server-side (verified for every area above).

---

## 7. Items deferred (not fixed in this PR) and why

| Item | File:line | Why deferred |
|---|---|---|
| `FollowUpLetterRenderService.CanAccessTransactionAsync` grants access when a `DepartmentUser` has no department claim (`if (currentUser.Role != UserRole.DepartmentUser \|\| !currentUser.DepartmentId.HasValue) return true;` — fails **open**, opposite of the fail-closed convention used everywhere else) | `FollowUpLetterRenderService.cs:454` | Currently unreachable: every follow-up-print policy (`PrintFollowUpLetters`, etc.) already excludes `DepartmentUser`, so this branch cannot be hit by any live endpoint today. Per the audit's own instruction ("if a suspected issue is not reproducible, document it and do not change code"), this is documented rather than patched to avoid touching dead code paths outside a demonstrable exploit; **must be fixed before any future change that admits `DepartmentUser` to a follow-up-print policy.** |
| Follow-up print **record**-level endpoints (`confirm`, `reprint`, `link-follow-up`, `print-view`, `pending`/`pending-summary`) do not scope by the requesting user, only by role (`FollowUpLetterPrintRecordService.cs`, `CanAccessRecordAsync` is a no-op for every role that can reach them) | `FollowUpPrintController.cs` record actions; `FollowUpLetterPrintRecordService.cs:515-529` | This is a **cross-user**, not cross-department, gap among `Admin`/`Supervisor`/`DataEntry` — the same three roles already treated as globally trusted everywhere else in this system (transaction edit, attachment upload, report generation). It is inconsistent with the *job*-level scoping (`FollowUpPrintAccessService` correctly limits a DataEntry to jobs they created), which suggests the record service may have been intended to get the same treatment — but changing it requires routing every record action through `IFollowUpPrintAccessService`, a multi-endpoint change beyond a single confirmed department-isolation defect. Flagged for a follow-up PR with product input on whether DataEntry-to-DataEntry print-record isolation is actually desired. |
| Saved institutional report templates have no per-creator ownership check on list/delete (`InstitutionalReportService.cs:127-135, 193-200`) | same | The controller is already `Policies.AdminOnly`, so the blast radius is Admin-to-Admin only (no privilege escalation, no department leak). Whether templates are meant to be shared org-wide config or per-admin-private is a product decision, not a security defect as currently scoped; deferred pending that decision. |
| `DataEntry`/`Supervisor`/`Admin` can edit/upload-to/reply-on **any** transaction regardless of department (`CanEditTransactions` policy has no department dimension) | `TransactionsController.cs` (Update, AddAssignment, AddFollowUp, EnableRecurring, UploadAttachment) | Confirmed **by design**, not a defect: these three roles are treated as globally trusted everywhere in the codebase (this is the same trust boundary the audit confirms for reports, dashboard, department-response review, and follow-up print job creation). Only `DepartmentUser` carries department-level restriction anywhere in this system. Changing this would be a product-level redesign of the role model, explicitly out of scope. |
| `GET /api/transactions/{id}/audit-log` returns the full audit trail (including internal admin actions and actor names) once a `DepartmentUser` is confirmed to have legitimate access to that specific transaction | `TransactionService.cs:1596` onward | Access is already correctly gated by `CanAccessTransactionAsync` — a `DepartmentUser` only ever sees audit history for transactions genuinely involving their own department, so this is not a cross-department leak. Whether the audit trail's *granularity* should be redacted for non-privileged roles within their own authorized transaction is a product/UX decision, not a data-isolation defect as scoped by this audit. |
| Legacy `/api/reports/*` query builder (`ReportService.ApplyReportFilter`, `ReportService.cs:1236-1261`) has no per-user department clamp independent of the client-supplied `filter.DepartmentId` | same | The controller already blocks `DepartmentUser`/`Reader` entirely via `Policies.CanEditTransactions`, so no department-restricted role can reach this path. Relevant only if that policy is ever loosened — noted for that future change, not fixed now. |
| No JWT/session revocation on user deactivation — a deactivated user's already-issued token remains valid until its 480-minute expiry | `AuthService.cs` (`IsActive` checked only at login) | Out of scope for a data-isolation-across-departments audit; this is a session-lifecycle/architecture question (stateless JWT trade-off), not a per-request authorization bypass. Noted as a residual risk in §8. |

---

## 8. Remaining risks

- **JWT revocation:** see above — a deactivated account's token stays valid until expiry (up to 8 hours per `Jwt:ExpireMinutes`). Standard stateless-JWT trade-off; would need a token-blacklist or short-lived-token/refresh-token redesign to close, which is a genuine architecture change beyond this audit's mandate.
- **Follow-up print record-level cross-user access** among globally-trusted roles (§7) — not a cross-department leak, but worth revisiting if the product intent is per-user print-job privacy even among Admin/Supervisor/DataEntry.
- **Dead-code fail-open branch** in `FollowUpLetterRenderService.cs:454` — must be fixed if `DepartmentUser` is ever admitted to any follow-up-print policy.

None of these block the primary goal (cross-department data isolation for `DepartmentUser`), which was the audit's explicit focus.

---

## 9. Validation

### Backend
```bash
dotnet restore backend/Uqeb.sln
dotnet build backend/Uqeb.sln
dotnet test backend/Uqeb.Api.Tests/Uqeb.Api.Tests.csproj -c Release
```
Result: build clean (0 warnings/errors). **Full, unfiltered test suite: 1085/1085 passed** (includes Playwright PDF export, visual regression, and `Category=SqlServer` tests — Chromium and a local SQL Server container were both available in this environment, so no exclusions were needed; a targeted run with the same filter CI's fast job uses also passed at 1031/1031 before adding the SQL/Playwright-tagged tests back in).

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
Result: lint clean, stylelint clean, typecheck clean, build succeeds, **490/490 tests passed** across 58 files.

### Regression-proof method
Every one of the 5 fixes/hardening changes in this PR (attachment metadata, attachment download, dashboard policy, frontend redirect, frontend nav) was verified by temporarily reverting the fix, re-running the new/updated test, confirming it failed with the expected pre-fix symptom (leaked data, wrong status code, or wrong render), then restoring the fix and confirming the suite passed again. This is not asserted from writing the test alone — it was executed.

`git diff --check` — clean, no whitespace issues.

---

## 10. Final security judgment

## **CONDITIONAL GO**

No blocking cross-department data-isolation defect remains for the primary threat model (a `DepartmentUser` accessing another department's data). The three confirmed, live, reproducible defects — attachment metadata listing, attachment download, and the orphaned dashboard API surface — are fixed and covered by regression tests that were proven to catch the original bug.

The "CONDITIONAL" qualifier reflects the deferred items in §7, none of which allow cross-department leakage but which represent either (a) a dead-code fail-open pattern that must not be forgotten if the follow-up-print role model ever changes, or (b) cross-user (not cross-department) access among roles already, consistently, and deliberately treated as globally trusted throughout this codebase — a design question for product, not a confirmed security defect requiring code changes in this PR.
