# Recurring obligations report

## Purpose

Completes the recurring-obligations work started in PRs #96-#100 with a focused,
read-only report: a summary and a filterable/exportable detail list of every
recurring obligation (`RecurringTransactionTemplate`), classified by whether it's
active, upcoming, due soon, overdue, suspended (paused), or terminated.

This report is **additive**: it does not change how recurring obligations are
created, edited, paused, resumed, terminated, or generated. It reuses the existing
`RecurringPeriodCalculator` for all date math and the existing `ReportsController`
/ `ReportService` (`/api/reports/*`) stack rather than the heavier institutional
report-builder, per the task's explicit scope.

## Data source

- **Obligation**: `RecurringTransactionTemplate` (`backend/Uqeb.Api/Models/Entities/RecurringTransactionTemplate.cs`).
  There is no separate "obligation" entity — the template itself is the obligation,
  and each generated period is a normal `Transaction` linked via `RecurringTemplateId`.
- **Responsible departments**: `RecurringTransactionTemplateDepartment` (many-to-many).
- **Owning department**: `RecurringTransactionTemplate.IncomingFromDepartment`
  (only set when `IncomingSourceType == Internal`; `null` for externally-sourced
  obligations).
- **Next due date**: computed on the fly via
  `RecurringPeriodCalculator.GetNextPeriodKey` + `RecurringPeriodCalculator.Compute`
  (`backend/Uqeb.Api/Helpers/RecurringPeriodCalculator.cs`) — the same calculator
  used by the recurring-templates admin page and by period generation. This report
  does **not** duplicate or reimplement any period-boundary math.
- **Last completion date**: the latest `ResponseCompletedDate ?? ClosedAt` across the
  template's generated `Transaction`s that have either one set.

## Endpoints

All under the existing `ReportsController` (`api/reports/*`), same
`[Authorize(Policy = Policies.CanEditTransactions)]` as every sibling report:

| Method | Path | Purpose |
|---|---|---|
| GET | `/api/reports/recurring-obligations/summary` | Bucket counts (+ optional grouping) |
| GET | `/api/reports/recurring-obligations/details` | Paged, filtered row list |
| GET | `/api/reports/recurring-obligations/export-excel` | XLSX export of the filtered (unpaged) row set |

Backend service: `ReportService.GetRecurringObligationsSummaryAsync` /
`GetRecurringObligationsDetailsAsync` / `ExportRecurringObligationsExcelAsync`
(`backend/Uqeb.Api/Services/ReportService.cs`).

Frontend: a new self-contained card ("تقرير الالتزامات الدورية") appended to the
existing `/reports` page (`frontend/uqeb-ui/src/pages/Reports.tsx`) — not a new
page, and not an addition to the institutional report builder (`/report-builder`).

## Field definitions

| Field | Meaning |
|---|---|
| `title` | The obligation's title (`RecurringTransactionTemplate.Title`) |
| `owningDepartmentName` | `IncomingFromDepartment.Name` when `IncomingSourceType == Internal`; otherwise `null` |
| `responsibleDepartmentNames` | All departments assigned to the template (fan-out list; can be more than one) |
| `recurrenceType` / `recurrenceTypeLabel` | Monthly / Quarterly / SemiAnnual / Annual |
| `startDate` | The template's anchor start date |
| `nextPeriodKey` / `nextPeriodLabel` | The next period to be generated, from `RecurringPeriodCalculator.GetNextPeriodKey` |
| `nextDueDate` / `nextDueDateHijri` | That period's due date (Gregorian and Hijri, the latter via the same `UmAlQuraCalendar`-based `FormatHijriDate` helper already used elsewhere in `ReportService`) |
| `lastCompletionDate` | Latest completion/close date across generated periods, if any |
| `status` | `Active` / `Paused` / `Terminated` (`RecurringTemplateStatus`) |
| `scheduleStatus` | Computed: `Upcoming` / `DueSoon` / `Overdue` / `NotApplicable` — see below |
| `daysRemaining` | Signed day count vs. today (negative = overdue); `null` when `scheduleStatus` is `NotApplicable` |
| `priority` | `Normal` / `Urgent` / `VeryUrgent` |
| `generatedTransactionsCount` | Count of `Transaction`s ever generated from this template |

## Status / count definitions

**Summary buckets** (`GET .../summary`):

- **Total**: obligations matching the applied filters.
- **Active / Suspended / Terminated**: a direct read of `RecurringTemplateStatus`
  (`Suspended` = `Paused`). The current data model has **no separate "cancelled" vs.
  "completed" distinction** — both are represented as `Terminated` with a free-text
  `TerminationReason`. This report does not invent a new status to split them; see
  "Known limitations."
- **Upcoming / DueSoon / Overdue**: sub-classification of **Active** obligations
  only, computed by `RecurringObligationScheduleClassifier.Classify`
  (`backend/Uqeb.Api/Helpers/RecurringObligationReportHelpers.cs`):
  - `remainingDays = (nextDueDate.Date - today.Date).Days` — **date-only**
    subtraction, never a `DateTime` with a time component, so the result cannot
    shift by time zone or time-of-day (see "Data accuracy" below).
  - `remainingDays < 0` → **Overdue**
  - `remainingDays <= dueSoonWithinDays` (default 7, override via `?dueSoonWithinDays=`) → **DueSoon**
  - otherwise → **Upcoming**
  - Invariant: `Active == Upcoming + DueSoon + Overdue` (tested).
- Paused templates still show an **informational** `nextDueDate`/`nextPeriodLabel`
  (what it would be if resumed today), but `scheduleStatus` is always
  `NotApplicable` and `daysRemaining` is `null` — a paused obligation is not "counted
  down." Terminated templates show no next due date at all (they will never
  generate another period).

**Grouping** (`?groupBy=department|status|recurrenceType`): grouped counts of the
already-filtered rows. Department grouping uses **owning department** only (not
each responsible department), because a template can have multiple responsible
departments and fanning out per responsible department would double-count a single
obligation across groups. Ungrouped rows without an owning department (external
source, or no department set) are grouped under "غير محدد / خارجي".

## Filters

Matches the existing `/api/reports/*` filter convention
(`ReportFilterRequest`/`ReportPagedFilterRequest`) where field names overlap:

| Filter | Applies to |
|---|---|
| `dateFrom` / `dateTo` | The computed `nextDueDate` (inclusive, date-only). A `Terminated` obligation (no next due date) never matches a date-range filter. |
| `departmentId` | Matches either the owning department (`IncomingFromDepartmentId`) or any responsible department (`RecurringTransactionTemplateDepartment`) |
| `status` | `RecurringTemplateStatus` (Active/Paused/Terminated) |
| `recurrenceType` | `RecurrenceType` (Monthly/Quarterly/SemiAnnual/Annual) |
| `priority` | `Priority` (Normal/Urgent/VeryUrgent) |
| `scheduleStatus` | The computed classification (Upcoming/DueSoon/Overdue/NotApplicable) |
| `search` | Matches `Title` or `SubjectTemplate` |
| `groupBy` | `department` / `status` / `recurrenceType` — adds grouped counts to the summary response |
| `dueSoonWithinDays` | Overrides the 7-day default threshold for the DueSoon bucket |

## Authorization behavior

This report lives under `ReportsController`, so it inherits the controller's
existing `Policies.CanEditTransactions` (`Admin`, `Supervisor`, `DataEntry`) on all
three actions (summary, details, export) — **the same policy already enforced on
every other `/api/reports/*` endpoint**. No new authorization surface was
introduced.

- **DepartmentUser**: gets `403 Forbidden` on all three actions, identical to every
  other report/dashboard endpoint. This is a deliberate reuse of the model
  established by PR #105/#106: institution-wide aggregate reports exclude
  DepartmentUser entirely (see `Controllers/DashboardController.cs`'s comment on
  `Policies.ViewOperationalDashboard`) rather than attempting per-request
  department-scoped visibility for an aggregate report. "Department users must not
  receive obligations outside their authorized scope" is satisfied trivially and
  robustly: they receive nothing from this report at all, same as `/api/reports/dashboard`.
- **Reader**: also `403 Forbidden` — `Policies.CanEditTransactions` does not include
  `Reader` (unlike `Policies.ViewOperationalDashboard`, which does). This matches
  every other `/api/reports/*` action; Reader was never granted access to this
  controller.
- **Admin / Supervisor / DataEntry**: full institution-wide visibility on all three
  actions, matching every sibling report.
- **Preview vs. export parity**: `details` (preview) and `export-excel` both run
  through the exact same `BuildRecurringObligationRowsAsync` filter/mapping
  pipeline, so there is no possibility of the two diverging — a `departmentId`
  filter that scopes a preview scopes the export identically, by construction, not
  by separately-maintained logic.
- Covered by `DepartmentUserEndpointAuthorizationTests.cs`
  (`NonDepartmentUserRoles_CanReachRecurringObligationsReport`,
  `Reader_CannotReachRecurringObligationsReport`, and the three new routes added to
  `DepartmentUser_PrivilegedReadEndpoints_ReturnForbidden`).

## Export parity

Only **XLSX** export is provided, via the existing ClosedXML-based pipeline
(`ReportService.ExportRecurringObligationsExcelAsync`, mirroring the existing
`department-incoming-closed/export-excel` one-off export — not the generic
Transaction-row `reportType` switch, since this report's row shape is
template-centric, not transaction-centric).

**PDF, DOCX, and HTML are not provided, and this is a pre-existing limitation of
the `/api/reports/*` stack itself, not something newly introduced by this
report**: none of the other simple reports on this same controller (`overdue`,
`open`, `waiting-replies`, etc.) have PDF/DOCX/HTML export either — only the
single `department-incoming-closed` report has a QuestPDF-based PDF export, and
only the separate, heavier institutional report-builder (`/report-builder`,
`InstitutionalReportsController`) supports all four formats via Playwright
(PDF/HTML) and DocumentFormat.OpenXml (DOCX). Adding this report to that pipeline
would require extending `InstitutionalReportType`, `InstitutionalReportModel`, and
the DOCX/XLSX/PDF exporters' `ReportSectionId` switch statements — explicitly out
of scope ("do not redesign the report builder," "do not introduce a new export
engine"). If DOCX/PDF export for this report is wanted later, that is a separate,
larger PR.

## Known limitations

- **No distinct "Completed" bucket for obligations.** The requirement ("Completed/
  closed obligations if supported by current data") is honored literally: the
  current data model has no such status for a *recurring obligation* (as opposed to
  an individual generated period/transaction, which does have `Closed`/
  `ResponseCompleted`). A template's lifecycle is only `Active` → `Paused` ⇄
  `Active` → `Terminated`; `Terminated` covers both "cancelled" and "no longer
  needed because it's done," with no separate tracking, and `Terminated` is a dead
  end (cannot be resumed). This report does not invent a new status to distinguish
  them, per the constraint against redesigning recurrence rules.
- **`scheduleStatus` classification only applies to Active obligations.** A Paused
  obligation's informational `nextDueDate` is not counted as overdue/upcoming/due
  soon even if the date has passed, since it isn't actively being tracked while
  paused.
- **Grouping by department uses the owning department only**, not each responsible
  department, to avoid double-counting an obligation with multiple responsible
  departments across groups.
- **No PDF/DOCX/HTML export** — see "Export parity" above.
- **In-memory pagination.** `RecurringPeriodCalculator` computations aren't
  SQL-translatable, so obligations matching the SQL-side filters (status,
  recurrence type, priority, department, search) are materialized once, then
  mapped and paged in memory — the same pattern already used by
  `RecurringTransactionTemplateService.GetAllAsync` for the admin templates list.
  This is appropriate at the expected scale of recurring obligation *templates*
  (an admin-configured list, not a per-transaction volume) but would need
  revisiting if that scale assumption changes materially.

## Data accuracy

- All date math reuses `RecurringPeriodCalculator` directly — no duplicated or
  reimplemented period-boundary logic. Month-end anchors (e.g. Jan 31) are clamped
  by `DateTime.AddMonths` exactly as the calculator already handles them, proven by
  `RecurringObligationsReportServiceTests.MapToRecurringObligationRow_preserves_month_end_clamped_due_date`
  (anchored on Jan 31, 2028 → due date lands on Mar 31, 2028, matching
  `RecurringPeriodCalculatorTests`'s existing coverage of the same case).
- **Date-only, no timezone/day-shift bugs**: the Upcoming/DueSoon/Overdue
  classification (`RecurringObligationScheduleClassifier.Classify`) compares
  `dueDate.Date` and `now.Date` only — never `DateTime` values with a time
  component. `RecurringObligationScheduleClassifierTests` explicitly proves this
  with a `now` at 23:59:59 on the due date (a naive time-inclusive subtraction
  would misclassify this as overdue by truncating a small negative fraction of a
  day) and a `dueDate` carrying a non-midnight time component.
- **Hijri display**: `nextDueDateHijri` reuses the exact same `FormatHijriDate`
  helper (`UmAlQuraCalendar`) already used for `IncomingHijriDate` elsewhere in
  `ReportService`, not a new Hijri conversion.

## Validation commands and results

Backend (run from `backend/`):

```bash
dotnet restore Uqeb.sln
dotnet build Uqeb.sln -c Release
dotnet test Uqeb.Api.Tests/Uqeb.Api.Tests.csproj -c Release
```

Result: build succeeded with 0 warnings/0 errors; full test suite
**1113/1113 passed** (1085 pre-existing + 28 new: 21 in
`RecurringObligationsReportServiceTests`/`RecurringObligationScheduleClassifierTests`,
7 new authorization test methods in `DepartmentUserEndpointAuthorizationTests`).

Frontend (run from `frontend/uqeb-ui/`):

```bash
npm ci
npm run lint
npm run lint:css
npx tsc -b
npm run build
npm test -- --run
```

Result: lint clean, stylelint clean, `tsc -b` clean, production build succeeded,
full test suite **491/491 passed** (490 pre-existing + 1 new test in
`Reports.test.tsx` covering the new section's mount/load behavior).

No validation step was skipped.
