# Production Readiness Checklist — Uqeb

Use before declaring **GO** for production deployment.

**Base SHA:** `3bd7f13d8c2c69b885e62b0641cd381f3c4fbeb7`

---

## Build & tests

- [ ] `dotnet build backend/Uqeb.sln -c Release` — PASS
- [ ] `dotnet test backend/Uqeb.sln -c Release` — PASS (45+ unit/integration)
- [ ] `npm ci && npm test && npm run build` — PASS (127 frontend)
- [ ] `npm audit --omit=dev` — 0 high/critical
- [ ] Sonar Quality Gate — PASS on release branch

## Configuration

- [ ] `appsettings.Production.json` provisioned outside package (`C:\Uqeb\config\`)
- [ ] `Jwt:Key` ≥ 32 chars, not in Git
- [ ] `ConnectionStrings:DefaultConnection` valid
- [ ] `AllowedOrigins` includes UI origin only
- [ ] `FileStorage:Path` exists, writable, on fast disk
- [ ] `VITE_API_BASE_URL` baked into frontend build (no localhost)
- [ ] Kestrel binds `0.0.0.0:5000` (or approved address)
- [ ] Timezone on server documented (`Asia/Riyadh` for KSA ops)

## Security

- [ ] No secrets in repository
- [ ] Login rate limiting active (`login` policy)
- [ ] RBAC verified UI + API for Admin / Supervisor / DepartmentUser / Reader
- [ ] Security headers present on API responses
- [ ] CORS allowlist enforced in Production
- [ ] Attachment upload size/type validation verified
- [ ] No stack traces returned to clients

## Health & monitoring

- [ ] `GET /health/live` returns 200
- [ ] `GET /health/ready` returns 200 when DB up
- [ ] `X-Correlation-ID` present on responses
- [ ] API log file rotating (`C:\Uqeb\logs`)
- [ ] Alerts defined for 5xx, DB down, disk space, login failures

## Operational smoke (manual)

- [ ] Login all four roles
- [ ] Transaction detail: assignment, follow-up, attachment, reply
- [ ] Complete response + PDF letter
- [ ] Scanner Bridge scan → upload
- [ ] Reports tabs + Excel/PDF export
- [ ] Audit log + timeline after mutations

## Data & recovery

- [ ] Backup taken before deploy
- [ ] Restore tested on non-production database
- [ ] Rollback procedure tested
- [ ] SHA256 package verification before deploy

## Performance

- [ ] k6 read smoke: p95 GET ≤ 1.5s, error rate < 1%
- [ ] k6 write smoke: p95 mutation ≤ 2.5s
- [ ] Reports p95 ≤ 4s (or documented exception)

## Sign-off

| Role | Name | Date | GO / NO-GO |
|------|------|------|------------|
| Technical lead | | | |
| Operations | | | |
| Security | | | |

**Current status:** `NO-GO` — see `docs/post_merge_operational_acceptance.md`.
