# Institutional Reporting — Visual & Scale Acceptance Gate (PR #18)

## Scope

PR #18 builds on the institutional reporting foundation merged in PR #17. It adds:

- Shared report design system (`Reporting/Rendering/institutional-report.css`)
- Preview/PDF stylesheet parity via embedded `manifest.stylesheet`
- Configurable export limits (`Reporting` section)
- Expanded manifest metadata (truncation, parts, overflow action)
- Visual regression pipeline (Playwright + Ubuntu CI job)
- Large-export acceptance tests (500 / 1,000 / 5,000 XLSX sampling)
- Reporting readiness/configuration endpoints
- Report Builder UX improvements (stats, stylesheet injection)

**Feature flag remains disabled by default:** `FeatureFlags:InstitutionalReports=false`

## Design system

| Token | Value |
|-------|-------|
| `--report-primary` | `#123F32` |
| `--report-page-width` | `210mm` |
| Template version | `2026.06.1` |

Backend source: `backend/Uqeb.Api/Reporting/Rendering/institutional-report.css`  
Frontend mirror: `frontend/uqeb-ui/src/styles/institutional-report/report.css`

## Export limits (defaults — tune after benchmark)

| Setting | Default |
|---------|---------|
| MaxPreviewDetailRows | 500 |
| MaxPdfDetailRows | 5,000 |
| MaxPdfDetailRowsPerPart | 5,000 |
| MaxDocxDetailRows | 20,000 |
| MaxXlsxDetailRows | 100,000 |
| MaxHtmlDetailRows | 20,000 |
| MaxPdfParts | 20 |

KPI/metrics always computed from full matched population. Detail tables may be sampled/truncated with explicit manifest flags.

## Visual regression

- Tests: `backend/Uqeb.Api.Tests/Reporting/Visual/`
- CI job: `visual-regression` (Ubuntu + Chromium, locale `ar-SA`, timezone `Asia/Riyadh`)
- Baselines: `Reporting/Visual/Baselines/*.png`
- On failure: `*.actual.png` uploaded as artifact
- **Do not auto-update baselines in CI.** Commit baseline PNG updates intentionally after visual review.

## Local commands

```bash
dotnet test backend/Uqeb.Api.Tests/Uqeb.Api.Tests.csproj --filter "FullyQualifiedName~InstitutionalReportVisual"
REQUIRE_PLAYWRIGHT_TESTS=1 dotnet test backend/Uqeb.Api.Tests/Uqeb.Api.Tests.csproj --filter "FullyQualifiedName~InstitutionalReportPlaywrightPdfExporterTests"

cd frontend/uqeb-ui
npm ci && npm run build && npm test -- --run
```

Enable locally:

```bash
# backend appsettings.Development.json
"FeatureFlags": { "InstitutionalReports": true }
"Reporting": { "MaxPreviewDetailRows": 500 }

# frontend .env.local
VITE_ENABLE_INSTITUTIONAL_REPORTS=true
```

Install Playwright Chromium (first time):

```bash
pwsh backend/Uqeb.Api/bin/Debug/net10.0/playwright.ps1 install chromium
```

## Readiness check

`GET /api/institutional-reports/readiness` (feature flag must be enabled) returns font/stylesheet/temp/config status without failing global `/health`.

## Rollback

1. Disable feature flag (default).
2. Revert PR #18 deployment package.
3. No migration changes in this PR.

## Deferred (out of scope)

- Background export queue
- Email delivery / scheduling
- Digital signatures
- Production feature-flag enablement
- Full 20,000-row benchmark in every CI run

## GO / NO-GO checklist

- [x] Backend CI PASS (local: 168 tests excl. Playwright)
- [x] Frontend CI PASS (local: build + 178 tests)
- [ ] pdf-linux PASS (pending CI)
- [ ] visual-regression PASS (pending CI — baselines auto-seed on first run)
- [x] Large export acceptance PASS (500 / 1,000 / 5,000 unit tests)
- [x] KPI integrity tests PASS
- [x] Security tests PASS
- [ ] SonarCloud PASS (pending CI)
- [x] Feature flag default false

**Local decision (2026-06-24):** NO-GO for production enablement. Conditional merge review after CI `visual-regression` and `pdf-linux` pass and baseline PNGs are reviewed.
