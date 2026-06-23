# Post-Merge Operational Acceptance — Uqeb

**Phase:** Post-Merge Operational Acceptance & Production Hardening  
**Base merge SHA:** `3bd7f13d8c2c69b885e62b0641cd381f3c4fbeb7` (PR #13 merged)  
**Work branch:** `test/post-merge-operational-acceptance`  
**Initial decision:** `NO-GO` for production  
**Last updated:** 2026-06-23

---

## 1. Scope

Validate operational readiness after merging PR #13 (`feature/ui-operational-workspace-phase2`). This phase fixes production/stability gaps only — no new product features unless required to unblock real usage.

---

## 2. Test environment

| Item | Status | Notes |
|------|--------|-------|
| SQL Server (dedicated acceptance DB) | **BLOCKED** | Not available on current dev host |
| API Release build | **BLOCKED** | Requires .NET SDK 10.0.301 (`global.json`) |
| Frontend production build | **PASS** | `npm run build` on `3bd7f13` |
| JWT / test accounts | **BLOCKED** | Credentials must be supplied via env vars outside Git |
| Scanner Bridge (WIA) | **BLOCKED** | Requires Windows workstation with scanner service |
| Attachment storage (isolated path) | **BLOCKED** | Requires acceptance server path provisioning |
| Timezone validation (`Asia/Riyadh`) | **PARTIAL** | Covered by `todayLocalIso` unit tests on merged code; browser validation **BLOCKED** |

### Environment variables (do not commit values)

```bash
UQEB_ACCEPTANCE_API_BASE_URL=
UQEB_ACCEPTANCE_UI_BASE_URL=
UQEB_TEST_ADMIN_USERNAME=
UQEB_TEST_ADMIN_PASSWORD=
UQEB_TEST_SUPERVISOR_USERNAME=
UQEB_TEST_SUPERVISOR_PASSWORD=
UQEB_TEST_DEPT_USERNAME=
UQEB_TEST_DEPT_PASSWORD=
UQEB_TEST_READER_USERNAME=
UQEB_TEST_READER_PASSWORD=
UQEB_REFERENCE_DATA_TEST_CONNECTION=   # optional SQL integration tests
UQEB_ATTACHMENT_STORAGE_PATH=
```

Template: see `docs/env.acceptance.example.env` (no secrets).

---

## 3. Automated baseline (main @ `3bd7f13`)

| Check | Result | Detail |
|-------|--------|--------|
| `git diff --check` | **PASS** | Clean working tree at branch creation |
| Backend `dotnet build` | **BLOCKED** | Host has SDK 9.0.102; repo requires 10.0.301 |
| Backend tests | **BLOCKED** | Same SDK constraint |
| Frontend `npm ci` | **PASS** | |
| Frontend tests | **PASS** | **127/127** (was 127 on PR #13 — no regression) |
| Frontend `npm run build` | **PASS** | |
| Frontend `npm run lint` | **FAIL (pre-existing)** | 4 errors in `AuthContext.tsx`, `SecurityPage.tsx`, `ReferenceDataProvider.tsx` — outside acceptance hardening scope unless promoted to blocker |
| `npm audit --omit=dev` | **PASS** | 0 vulnerabilities |

### Delta vs PR #13 automated gate

- Test count unchanged: **127** frontend, **45** backend (backend not re-run here).
- Lint debt unchanged (pre-existing).
- New hardening in this branch adds health endpoints + deployment health verification (see PR for this phase).

---

## 4. Scenario matrix

Legend: **PASS** | **FAIL** | **BLOCKED** | **NOT RUN**

### 4.1 Authentication (all roles)

| Scenario | Admin | Supervisor | DeptUser | Reader | Result |
|----------|-------|------------|----------|--------|--------|
| Valid login | — | — | — | — | **BLOCKED** |
| Invalid password | — | — | — | — | **BLOCKED** |
| Inactive account | — | — | — | — | **BLOCKED** |
| Expired token | — | — | — | — | **BLOCKED** |
| Logout / refresh | — | — | — | — | **BLOCKED** |
| Direct deep link guard | — | — | — | — | **BLOCKED** |
| No default credentials in UI | — | — | — | — | **PASS** (code review — no hardcoded creds in repo) |

### 4.2 Dashboard

| Scenario | Result |
|----------|--------|
| Metrics load | **BLOCKED** |
| Empty / error states | **BLOCKED** |
| No console errors | **BLOCKED** |

### 4.3 Transaction detail (merged UI)

| Scenario | Result |
|----------|--------|
| Three tabs; details default | **PASS** (automated — `TransactionDetail.test.tsx`) |
| Hero + inline card forms | **PASS** (automated interaction tests) |
| Add assignment (real API + DB) | **BLOCKED** |
| Add follow-up (real API + DB) | **BLOCKED** |
| Add attachment / scanner | **BLOCKED** |
| Reply on assignment/follow-up | **BLOCKED** (automated UI only) |
| Complete response / PDF letter | **BLOCKED** |
| Local date (`Asia/Riyadh`) | **PARTIAL** (`todayLocalIso` tests) |
| Audit + timeline after mutations | **BLOCKED** |

### 4.4 Reports & export

| Scenario | Result |
|----------|--------|
| Auto-load + debounced search | **PASS** (automated — `Reports.test.tsx`) |
| Filters / pagination / export | **BLOCKED** |

### 4.5 RBAC (UI + API)

| Scenario | Result |
|----------|--------|
| Admin full mutation set | **BLOCKED** |
| Supervisor scoped actions | **BLOCKED** |
| DepartmentUser reply-only | **PARTIAL** (automated UI permissions tests) |
| Reader read-only | **PARTIAL** (automated UI permissions tests) |
| API bypass attempts (403) | **BLOCKED** |

### 4.6 Performance (k6)

| Load | Result |
|------|--------|
| 10 / 25 / 50 / 100 VUs | **BLOCKED** — scripts in `tests/performance/`; requires running API + credentials |

### 4.7 Database

| Scenario | Result |
|----------|--------|
| Migrations / indexes review | **NOT RUN** |
| Backup + restore drill | **BLOCKED** |
| Execution plans for slow queries | **BLOCKED** |

### 4.8 Failure recovery

| Scenario | Result |
|----------|--------|
| API stop / SQL stop / disk full | **BLOCKED** |
| Scanner offline | **BLOCKED** |
| Token expiry mid-action | **BLOCKED** |
| Partial response + attachment failure | **PASS** (automated — `CompleteResponseFormPanel.test.tsx`) |

### 4.9 Deployment / rollback

| Scenario | Result |
|----------|--------|
| Package hash verification | **PASS** (documented in `AGENTS.md` / `deploy-production-v2.ps1`) |
| Health check after deploy | **ADDED** in this phase (`/health/live`, `/health/ready`) |
| Rollback script dry-run | **NOT RUN** on acceptance host |

---

## 5. Defects found

| ID | Severity | Summary | Status |
|----|----------|---------|--------|
| ACC-001 | **Major** | No `/health` endpoints before this phase — deploy relied on port listen + login probe only | **Fixed** in work branch |
| ACC-002 | **Minor** | No correlation ID on API responses | **Fixed** in work branch |
| ACC-003 | **Minor** | Deploy script lacked post-start health verification | **Fixed** in work branch |
| ACC-004 | **Info** | Acceptance host missing .NET 10 SDK — cannot verify backend locally | **BLOCKED** |
| ACC-005 | **Info** | Full manual smoke requires LAN production-like host | **BLOCKED** |

No **Blocker** or **Critical** defects confirmed in automated scope.

---

## 6. Hardening delivered in work branch

- `GET /health/live` — liveness (no DB)
- `GET /health/ready` — SQL connectivity
- `GET /health` — summary
- `X-Correlation-ID` middleware
- Security headers middleware (production HTTPS HSTS when applicable)
- `scripts/verify-deployment-health.ps1`
- Deploy script invokes live + ready checks after service start
- k6 smoke scripts under `tests/performance/`
- Runbooks: production readiness, runbook, backup/restore, rollback

---

## 7. Final decision

```text
NO-GO
```

### Reasons

1. Full browser smoke test with real SQL Server, four roles, Scanner Bridge, and exports **not executed** (BLOCKED on current host).
2. Backend build/tests **not re-verified** on this host (.NET 10 SDK missing).
3. Performance baselines **not measured**.
4. Backup/restore drill **not executed**.

### Conditions for GO

- [ ] Backend 48/48+ tests pass on CI or host with .NET 10
- [ ] All four roles smoke-tested on acceptance environment
- [ ] Scanner Bridge verified on Windows
- [ ] Reports Excel/PDF export verified with real data
- [ ] k6 thresholds met or documented with remediation plan
- [ ] Backup + restore successful on test database
- [ ] Rollback dry-run successful
- [ ] Sonar Quality Gate pass on hardening PR
- [ ] Zero open Blocker/Critical defects

---

## 8. Evidence links

- Merge commit: `3bd7f13`
- PR #13 merge content: operational workspace UI, 127 frontend tests
- Scanner design: `docs/SCANNER_BRIDGE_DESIGN.md`
- Production deployment: `docs/PRODUCTION_DEPLOYMENT_TROUBLESHOOTING.md`, `AGENTS.md`
