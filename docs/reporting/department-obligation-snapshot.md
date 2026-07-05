# Department obligation snapshot

## Purpose

Improves the accuracy of department-level performance analysis by distinguishing
**which role** each department plays on a transaction, instead of attributing
performance only to the primary/owning department. Existing department reports
(`GetByDepartmentAsync`, `GetByOutgoingDepartmentAsync`, `GetDepartmentSummaryAsync`
in `backend/Uqeb.Api/Services/ReportService.cs`) each key on a *single* data source
(`Assignment`, or `TransactionOutgoingDepartment`) and never reference the owning
department (`Transaction.IncomingFromDepartmentId`) at all. This snapshot is the
first department-level report to combine all four department-involvement sources
and make the distinction between them explicit, so that (for example) a department
that only *originates* transactions is never blamed for a delay caused by a
department it referred the transaction to.

This report is **additive**: it does not change transaction creation/editing,
recurring-obligation logic, or any existing report. It reads the same
`Transaction`/`Assignment`/`TransactionOutgoingDepartment`/`DepartmentResponse`
tables every other report already reads, through one new `ReportService` method,
under the same `/api/reports/*` stack and authorization policy used by
`docs/reporting/recurring-obligations-report.md`.

## Data sources

Per the domain research behind this report, department involvement in the current
data model is **five distinct, independently-tracked concepts** — not one:

| Concept | Table / field | Notes |
|---|---|---|
| Owning department | `Transaction.IncomingFromDepartmentId` | Singular; only set when `IncomingSourceType == Internal`. Never used by any existing department report before this one. |
| Referred-to department | `TransactionOutgoingDepartment` | Plural, no status/reply fields at all — a pure distribution record. Unique per (transaction, department) at the DB level. |
| Responsible department | `Assignment` (any row, any status) | Plural; carries the actual accountability state (`RequiresReply`, `DueDate`, `ReplyStatus`, `Status`). **Has no unique constraint** on (TransactionId, DepartmentId) — the real `AddAssignmentAsync` code path does not guard against creating a duplicate row for the same department on the same transaction, so this report explicitly dedupes by transaction wherever it counts "responsible" obligations (see "Attribution rules"). |
| Department that replied (thin flag) | `Assignment.ReplyStatus == Replied` (paired with `Status == Completed`, set together by `TransactionService.ReplyAssignmentAsync`) | Admin/Supervisor-recorded only; DepartmentUser is blocked from this path. |
| Department that submitted/approved a response | `DepartmentResponse` | A richer entity with response content, attachments, and its own review workflow (`Draft → SubmittedForReview → Approved / ReturnedForCorrection / Rejected`). **Not automatically synchronized with the Assignment reply flag above** — a department can have an `Approved` `DepartmentResponse` while its `Assignment.ReplyStatus` is still `Pending`, or vice versa, because nothing in the codebase keeps them in lock-step. This report surfaces that specific divergence as a data-quality signal (`attributionMismatchCount`, see below) instead of silently picking one source as authoritative. |

Recurring obligations are covered implicitly: a generated recurring-period
transaction is a normal `Transaction` row with its own
`IncomingFromDepartmentId` (copied from the owning template) and its own
generated `Assignment` rows (one per `RecurringTransactionTemplateDepartment`).
This report queries `Transactions`/`Assignments` directly, so recurring-generated
obligations are counted the same way as any other transaction, with no
special-casing and no dependency on `RecurringPeriodCalculator`.

## Attribution rules

1. **Every count is a count of distinct obligations (transactions), never a count
   of raw `Assignment`/`TransactionOutgoingDepartment`/`DepartmentResponse` rows.**
   Every per-department count is computed as `Select(row => row.TransactionId).Distinct().Count()`
   over the relevant source, specifically to avoid the double-count risk
   documented above (duplicate `Assignment` rows for the same transaction+department
   are a real possibility, not just a theoretical one — see
   `Snapshot_does_not_double_count_duplicate_assignment_rows_for_the_same_department_and_transaction`
   in the test suite).
2. **Owner and Responsible/Referred are always counted from separate sources and
   never merged.** `ownedCount` comes only from `IncomingFromDepartmentId`;
   `responsibleCount` only from `Assignment`; `referredCount` only from
   `TransactionOutgoingDepartment`. A transaction owned by department A and
   assigned to department B contributes to A's `ownedCount` and to B's
   `responsibleCount`/`referredCount` — never to A's responsible/referred counts,
   and never to B's owned count.
3. **A department is never blamed, on this report, for another department's
   delay.** `overdueCount`/`dueSoonCount`/`pendingActionCount` are computed purely
   from that department's own `Assignment` rows (`Assignment.DepartmentId`), using
   the same `TransactionTemporalCalculator.IsAssignmentOverdue` helper every other
   overdue check in the codebase uses — not from the transaction's overall status
   or from another department's assignment.
4. **"Completed action" counts either completion signal (`Assignment.Status ==
   Completed` OR `Assignment.ReplyStatus == Replied`)**, since both are legitimate,
   independently-used ways an assignment gets closed out in the current
   application (in practice `ReplyAssignmentAsync` sets both together, but the
   snapshot does not assume that always holds).
5. **A department that appears in a role for a transaction only once, even via
   multiple different roles, is not "multiple departments."** The
   `multiDepartmentObligationsCount` top-level figure counts a transaction as
   "multi-department" only if **two or more distinct department IDs** are
   attributed to it (via owner ∪ responsible ∪ referred ∪ response); a
   transaction touched only by department X's `Assignment` and department X's own
   `DepartmentResponse` is one department in two roles, not two departments.
6. **Average days open is computed per distinct obligation, not per Assignment
   row** — if a department has duplicate `Assignment` rows for the same
   transaction, the earliest `AssignedDate` is used once, not averaged in twice.

## Count definitions

Each row of `departments[]` in the response:

| Field | Meaning |
|---|---|
| `ownedCount` | Distinct transactions where this department is `IncomingFromDepartmentId` |
| `responsibleCount` | Distinct transactions with any `Assignment` (any status) for this department |
| `referredCount` | Distinct transactions with a `TransactionOutgoingDepartment` row for this department |
| `openActionCount` | Distinct transactions with an **Active** `Assignment` for this department (broader than pending — includes assignments not requiring a reply that are still open) |
| `pendingActionCount` | Distinct transactions with an **Active**, **reply-required**, **not-yet-replied** `Assignment` — "still owes a reply" |
| `completedActionCount` | Distinct transactions with `Assignment.Status == Completed` **or** `Assignment.ReplyStatus == Replied` |
| `submittedResponseCount` | Distinct transactions with any `DepartmentResponse` (any status) for this department |
| `approvedResponseCount` | Distinct transactions with an `Approved` `DepartmentResponse` for this department |
| `overdueCount` | Distinct transactions where `TransactionTemporalCalculator.IsAssignmentOverdue` is true for this department's assignment |
| `dueSoonCount` | Distinct transactions in `pendingActionCount` whose `DueDate` is between today and `dueSoonWithinDays` (default 7) from today, and not already overdue |
| `averageDaysOpenAction` | Average age in days (`today - AssignedDate`) of this department's currently-pending obligations; `null` if there are none |
| `attributionMismatchCount` | Distinct transactions, among those requiring a reply from this department, where the `Assignment.ReplyStatus == Replied` flag and an `Approved` `DepartmentResponse` **disagree** (one exists without the other) — see "Known limitations" |
| `involvementCategory` | `"OwnerOnly"` \| `"ResponsibleOrReferredOnly"` \| `"ResponseOnly"` \| `"Both"` \| `"None"` — directly answers "is this department involved only as owner vs. only as responsible/referred party vs. only via a submitted response". `"ResponseOnly"` covers a department with `DepartmentResponse` rows but no ownership/assignment/referral; `"Both"` covers owner plus any other involvement (responsible/referred and/or response) |

Top-level response fields:

| Field | Meaning |
|---|---|
| `totalDepartmentsInScope` | Number of departments with at least one involvement of any kind, after filters |
| `totalDistinctObligations` | Number of distinct transactions touched by any department in scope, after filters |
| `multiDepartmentObligationsCount` | Number of those transactions attributed to **two or more distinct departments** (see attribution rule 5) |

### How to answer the six required questions

1. **Which departments currently have open obligations?** `openActionCount > 0`.
2. **Which departments have overdue obligations?** `overdueCount > 0`.
3. **Which departments responded/completed their part?** `completedActionCount` / `approvedResponseCount`.
4. **Which departments still have pending action?** `pendingActionCount > 0`.
5. **Which departments are involved only as owner vs. responsible/referred?** `involvementCategory`.
6. **Which obligations are counted under multiple departments, and why?** `multiDepartmentObligationsCount` at the top level; per-transaction "why" is that its owner, assignment, outgoing-department, and/or response rows name more than one distinct department (see attribution rule 5 for the specific "one department, multiple roles" exclusion).

## Filters

`DepartmentObligationSnapshotFilterRequest`, matching the field-naming
conventions already used by `ReportFilterRequest`/`RecurringObligationsReportFilterRequest`:

| Filter | Applies to |
|---|---|
| `dateFrom` / `dateTo` | `Transaction.IncomingDate` (inclusive), applied to every source query via its `Transaction` navigation |
| `departmentId` | Scopes the response to a single department's row (used for a focused drill-down; the top-level `totalDistinctObligations`/`multiDepartmentObligationsCount` are still computed only from the filtered — i.e. single-department — data when this is set) |
| `dueSoonWithinDays` | Overrides the 7-day default threshold for `dueSoonCount` |

## Endpoints

Both under the existing `ReportsController` (`api/reports/*`):

| Method | Path | Purpose |
|---|---|---|
| GET | `/api/reports/department-obligation-snapshot` | The snapshot described above |
| GET | `/api/reports/department-obligation-snapshot/export-excel` | XLSX export of the same data (ClosedXML, mirroring the existing `recurring-obligations/export-excel` one-off export pattern — no new export engine) |

Backend service: `ReportService.GetDepartmentObligationSnapshotAsync` /
`ExportDepartmentObligationSnapshotExcelAsync`.

Frontend: a new self-contained "لقطة التزامات الإدارات" card appended to the
existing `/reports` page (`frontend/uqeb-ui/src/pages/Reports.tsx`) — not a new
page, not an addition to the institutional report builder.

## Authorization behavior

Both actions inherit `ReportsController`'s existing class-level
`[Authorize(Policy = Policies.CanEditTransactions)]` (`Admin`, `Supervisor`,
`DataEntry`) — the exact same policy as every other `/api/reports/*` endpoint,
including the recurring obligations report. **No new authorization surface was
introduced.**

- **DepartmentUser**: `403 Forbidden` on both actions. This is intentional: this
  snapshot is an institution-wide, cross-department aggregate by definition (its
  entire purpose is comparing departments against each other), so — consistent
  with `Policies.ViewOperationalDashboard`'s rationale for excluding
  DepartmentUser from the dashboard, and with the recurring obligations report's
  identical choice — DepartmentUser does not get a narrowed, department-scoped
  version of it either. It receives nothing from this endpoint, which trivially
  satisfies "must not see unauthorized cross-department details."
- **Reader**: also `403 Forbidden` — `Policies.CanEditTransactions` does not
  include `Reader`, matching every sibling `/api/reports/*` action.
- **Admin / Supervisor / DataEntry**: full institution-wide visibility, for both
  the preview endpoint and the export endpoint — both run through the exact same
  `BuildDepartmentObligationSnapshotAsync` query/attribution pipeline, so preview
  and export can never diverge in what they scope.
- Covered by `DepartmentUserEndpointAuthorizationTests.cs`
  (`NonDepartmentUserRoles_CanReachDepartmentObligationSnapshot`,
  `Reader_CannotReachDepartmentObligationSnapshot`, and the two new routes added
  to `DepartmentUser_PrivilegedReadEndpoints_ReturnForbidden`).

## Known limitations

- **`attributionMismatchCount` does not say which side is "correct."** It only
  flags that the two independent completion signals (Assignment-level reply flag
  vs. DepartmentResponse approval) disagree for a given obligation — resolving
  that disagreement is a data-quality/process question, not something this report
  can decide, since neither the older Assignment-reply path nor the newer
  DepartmentResponse workflow is treated as universally authoritative elsewhere in
  the codebase either.
- **`openActionCount` is intentionally broader than `pendingActionCount`.** It
  includes `Active` assignments that don't require a reply at all (e.g. an
  FYI-only referral). If a future requirement needs "open action" to mean
  strictly "still owes a reply," use `pendingActionCount` instead.
- **In-memory aggregation.** Like the recurring obligations report, the four
  source queries are materialized (each already filtered/date-scoped in SQL) and
  then grouped/deduped/classified in memory, because computing `Distinct()` per
  department across four independently-sourced sets is significantly simpler and
  safer to get correct in C# than as one combined SQL query. This is appropriate
  at the expected scale (departments: dozens; transactions: the same volume every
  other report already loads) but would need revisiting if that scale assumption
  changes materially.
- **No PDF/DOCX/HTML export**, for the same reason documented in
  `recurring-obligations-report.md`: the legacy `/api/reports/*` stack this report
  extends only has XLSX (and, for one unrelated report, a QuestPDF PDF) export;
  adding other formats would mean extending the separate, heavier institutional
  report-builder pipeline, which is out of scope here.
- **Owning-department attribution only exists for internally-sourced
  transactions.** Externally-sourced transactions (`IncomingSourceType ==
  External`) never contribute to any department's `ownedCount`, by design — there
  is no department that "owns" a transaction that arrived from outside the
  institution.

## Validation commands and results

Backend (run from `backend/`):

```
dotnet restore Uqeb.sln
dotnet build Uqeb.sln
dotnet test Uqeb.Api.Tests/Uqeb.Api.Tests.csproj
```

Result: build succeeded with 0 warnings/0 errors; full test suite
**1128/1128 passed** (1113 pre-existing + 15 new: 9 in
`DepartmentObligationSnapshotServiceTests` covering owner-vs-responsible
separation, differing A/B counts, duplicate-row dedup, overdue/pending/average
attribution, mismatch detection, multi-department counting, department-filter
scoping, empty/no-data behavior, and export smoke; 6 new authorization test
cases in `DepartmentUserEndpointAuthorizationTests`).

Frontend (run from `frontend/uqeb-ui/`):

```
npm run lint
npm run lint:css
npx tsc -b
npm run build
npm test -- --run
```

Result: lint clean, stylelint clean, `tsc -b` clean, production build succeeded,
full test suite **492/492 passed** (491 pre-existing + 1 new test covering the
new section's mount/load behavior and owner-vs-responsible label rendering).

No validation step was skipped.
