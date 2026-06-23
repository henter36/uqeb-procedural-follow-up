# Production Runbook — Uqeb

Operational procedures for the on-prem Windows deployment (`10.0.177.17` per `AGENTS.md`).

---

## Architecture

| Component | Path / URL |
|-----------|------------|
| API | `http://10.0.177.17:5000` |
| UI | `http://10.0.177.17:8080` |
| API publish | `C:\Uqeb\publish\api` |
| Web publish | `C:\Uqeb\publish\web` |
| Logs | `C:\Uqeb\logs\api-runtime.log` |
| Config | `C:\Uqeb\config\appsettings.Production.json` |
| Incoming packages | `C:\Uqeb\incoming` |
| Scheduled task | `UqebApi` |

---

## Daily checks

1. Confirm `UqebApi` task is **Running**.
2. `GET http://10.0.177.17:5000/health/live` → 200
3. `GET http://10.0.177.17:5000/health/ready` → 200
4. Open UI → log in as smoke user → open one transaction.
5. Review `api-runtime.log` for errors in last 24h.

```powershell
Invoke-WebRequest -UseBasicParsing http://localhost:5000/health/ready
Get-Content C:\Uqeb\logs\api-runtime.log -Tail 50
```

Or use `scripts/verify-deployment-health.ps1`.

---

## Deploy new release

**Entry point:** `scripts/deploy-production.ps1` → `deploy-production-v2.ps1`

1. Build release ZIP on dev machine per `AGENTS.md`.
2. Copy `Uqeb-*.zip` + matching `.sha256.txt` to `C:\Uqeb\incoming`.
3. Verify SHA256 before expand.
4. Run deploy script as Administrator.
5. Script stops task, robocopy api/web, preserves production settings, starts task.
6. **Automatic:** live + ready health checks after start.
7. Manual: login from client PC, hard refresh UI (`Ctrl+Shift+R`).

---

## Restart API only

```powershell
schtasks /End /TN "UqebApi"
Start-Sleep -Seconds 3
schtasks /Run /TN "UqebApi"
Start-Sleep -Seconds 8
.\scripts\verify-deployment-health.ps1 -ApiBaseUrl "http://localhost:5000"
```

---

## Common incidents

| Symptom | Check | Action |
|---------|-------|--------|
| UI loads, API fails | `/health/ready`, SQL connectivity | Restore SQL service; verify connection string |
| 401 on all requests | JWT key mismatch after deploy | Ensure `appsettings.Production.json` preserved |
| CORS errors | `AllowedOrigins` | Add UI origin; redeploy not required if settings-only fix |
| Upload fails | Disk space, `FileStorage:Path` permissions | Free space; fix ACLs |
| Scanner unavailable | Scanner Bridge service on client | See `docs/SCANNER_BRIDGE_DESIGN.md` |

---

## Escalation

1. Capture `X-Correlation-ID` from failed API response.
2. Search `api-runtime.log` for correlation or timestamp.
3. If deploy-related, use rollback runbook.
4. Record incident in operational acceptance log.

---

## Do not

- Run EF migrations on production without explicit approval + full DB backup.
- Copy `appsettings.Development.json` to production.
- Use `robocopy /MIR` on API folder.
- Commit secrets to Git.
