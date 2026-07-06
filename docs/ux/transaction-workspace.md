# Transaction Workspace

## Page purpose

`frontend/uqeb-ui/src/pages/TransactionDetail.tsx` (route `/transactions/:id`) is the single
page where a user understands and acts on one transaction: its identity, current status,
who it's been referred to, what response/statement has been recorded, what follow-ups have
gone out, and what's attached — without navigating to separate screens for each concern.

The page is a **thin orchestrator**: it owns data fetching, the "which form is currently
open" state (`activeAction`/`activeActionContext`), and mutation success/refresh handlers. All
rendering is delegated to focused components under
`frontend/uqeb-ui/src/components/transaction-workspace/`.

## Section order (details tab)

The `تفاصيل المعاملة` tab renders, top to bottom, as a vertical stack of full-width cards:

1. **Header / hero** (`TransactionWorkspaceHeader.tsx`) — transaction number, status/priority/
   overdue/pending badges, subject, source department + category + destination department
   badges, the global action bar (`TransactionActionBar.tsx`: تعديل / تسجيل إفادة or استكمال
   إفادة / إغلاق المعاملة only — see "Deduplicated actions" below), the metric tile grid
   (incoming date, due date, closing date, completion days, delay status, days since last
   follow-up, open referrals, attachment count), recurring-template info, and admin-only
   date-correction/recurring-enablement bars. A collapsed "تفاصيل إضافية" block keeps only
   secondary fields (tracking number, incoming date, source type, notes) — response/outgoing
   details were moved to the Responses section (see below) so they're no longer buried here.
2. **Current action status** (`TransactionActionStatusCard.tsx`, NEW) — always-visible card,
   not a toggle panel. Shows a one-line "what's required now" (register a response / waiting
   on departments / no action needed / closed), the list of departments still pending a reply
   (from `tx.pendingDepartmentNames`), and a one-line summary of the latest response with a
   jump link (`#transaction-responses-section`) into the Responses section.
3. **Referrals** (`TransactionReferralsSection.tsx`, aria-label "الاحالات") — assignment table,
   per-department reply-status badges, "+ إضافة احالة" trigger, admin inline-edit entry
   points.
4. **Responses / statements** (`TransactionResponsesSection.tsx`, NEW, `id=transaction-responses-section`)
   — persistent card. Shows the recorded response's date/summary/status/outgoing number-date
   when one exists, or "لم تُسجَّل أي إفادة لهذه المعاملة بعد." when it doesn't. Docks
   `DepartmentResponseInlinePanel` (department users) or `CompleteResponseFormPanel` (admin/
   supervisor/data-entry) when the hero's "تسجيل إفادة"/"استكمال إفادة" button is clicked —
   the button stays in the hero for top-of-page visibility, but the form now opens in this
   section instead of floating above the referrals/follow-ups grid.
5. **Follow-ups** (`TransactionFollowUpsSection.tsx`, aria-label "التعقيبات") — follow-up
   table, "+ إضافة تعقيب" trigger, and the "خطاب تعقيب PDF" trigger (relocated here from the
   hero action bar, since generating a follow-up letter is contextually about follow-ups).
6. **Attachments** (`TransactionAttachmentsSection.tsx`, aria-label "المرفقات") — unchanged
   position and behavior (last, full-width).
7. **Timeline / audit** — unchanged; already isolated behind their own tabs (`الخط الزمني`,
   `سجل التدقيق`), not competing for space with the details tab.

## Deduplicated actions

Previously, "إضافة احالة"/"إضافة تعقيب"/"إضافة مرفق" each appeared both in the hero action
bar and in their own section header (two triggers for the same action). The hero action bar
now carries only page-level actions: تعديل, the response-registration button, إغلاق
المعاملة, and a read-only hint when no action is available. Add/reply/attach triggers live
solely next to the list they affect.

## Role-specific visible actions

Derived in `TransactionDetailContent` from `useAuth()`:

- `showMutationActions` (`canEdit && !isDepartmentUser`, i.e. Admin/Supervisor/DataEntry):
  gates تعديل, all "+ إضافة..." section triggers, "خطاب تعقيب PDF", and the "إضافة أول..."
  empty-state CTAs.
- `canReply` (same condition): gates the per-row "تسجيل رد" buttons on referrals/follow-ups.
- `isDepartmentUser`: switches which response form docks in the Responses section
  (`DepartmentResponseInlinePanel` vs `CompleteResponseFormPanel`) and drives the response
  button's label/status badge.
- `isAdmin`: gates the "تصحيح التواريخ (إداري)" bar/panel and the clickable department-name /
  reply-status-badge links in the referrals table (admin inline edit).
- `canClose`: gates "إغلاق المعاملة" and the read-only fallback hint.
- Reader-role / department users without an assignment on the transaction see the full page
  read-only: no mutation triggers anywhere, verified in
  `TransactionDetail.test.tsx` ("department user permissions" / "permissions" describe
  blocks) and manually in a live browser session logged in as `deptuser`.

## Known limitations (not fixed in this PR — out of scope)

- `TransactionWorkspace.allowedActions` (`api/types.ts`) is fetched from the backend but never
  read by the page — `TransactionDetailContent` recomputes the same booleans client-side from
  `useAuth()` + transaction fields. This is pre-existing duplication; resolving it would be a
  backend-adjacent change beyond "transaction workspace UX only" scope.
- Metric tiles (open referrals, attachment count, days-since-last-follow-up, etc.) render
  identically for every role — there is no department-specific statistics view. No hidden or
  misleading statistics were found for department users during this work, so nothing needed
  correcting here.
- The stale outgoing-letter-number bug (new assignment forms inheriting the transaction's
  `outgoingNumber`) was already fixed on `main` prior to this PR (commit `0dad3ab`); this PR
  only carries the existing regression test forward through the section extraction.

## Validation performed

- `npm run lint`, `npm run lint:css`, `npx tsc -b`, `npm run build`, `npm test -- --run` all
  pass (508/508 tests, including 5 new `TransactionActionStatusCard` unit tests and 3 new
  `TransactionDetail.test.tsx` integration tests for the persistent Responses section and
  pending-department status).
- Manual verification in a live browser session (backend on SQL Server + seeded data,
  frontend dev server): logged in as `admin`, confirmed the full section order, the
  always-visible Current Action Status card, the Responses section's persistent empty state
  and its docking/scroll-into-view behavior when opening "تسجيل إفادة", and the Follow-ups
  section's two co-located triggers (`+ إضافة تعقيب`, `خطاب تعقيب PDF`) both docking
  correctly. Logged in as `deptuser` and confirmed no mutation affordances are present
  anywhere on a transaction outside that department's assignment.
