# Rollback Runbook — Uqeb

Rollback restores the **last known-good backup** under `C:\Uqeb\backup\`.

---

## Prerequisites

- Identify backup folder containing `api\`, `web\`, and `appsettings.Production.json`.
- Confirm backup timestamp predates the failed deploy.
- Maintenance window communicated.

---

## Procedure

```powershell
$ErrorActionPreference = "Stop"

$backupCandidate = Get-ChildItem "C:\Uqeb\backup" -Directory |
  Where-Object {
    (Test-Path (Join-Path $_.FullName "api")) -and
    (Test-Path (Join-Path $_.FullName "web")) -and
    (Test-Path (Join-Path $_.FullName "appsettings.Production.json"))
  } |
  Sort-Object LastWriteTime -Descending |
  Select-Object -First 1

if (-not $backupCandidate) { throw "No complete backup found." }

$backup = $backupCandidate.FullName
$apiTarget = "C:\Uqeb\publish\api"
$webTarget = "C:\Uqeb\publish\web"

schtasks /End /TN "UqebApi" 2>$null
Start-Sleep -Seconds 3

$apiProcess = Get-NetTCPConnection -LocalPort 5000 -State Listen -ErrorAction SilentlyContinue |
  Select-Object -First 1 -ExpandProperty OwningProcess
if ($apiProcess) { Stop-Process -Id $apiProcess -Force }

Remove-Item "$apiTarget\*" -Recurse -Force
Remove-Item "$webTarget\*" -Recurse -Force

Copy-Item "$backup\api\*" $apiTarget -Recurse -Force
Copy-Item "$backup\web\*" $webTarget -Recurse -Force
Copy-Item "$backup\appsettings.Production.json" "$apiTarget\appsettings.Production.json" -Force

schtasks /Run /TN "UqebApi"
Start-Sleep -Seconds 8

.\scripts\verify-deployment-health.ps1 -ApiBaseUrl "http://localhost:5000"
```

---

## Post-rollback verification

1. `/health/live` and `/health/ready` → 200
2. Login from client workstation
3. Open known transaction — assignments/follow-ups/attachments visible
4. Confirm `BUILD_INFO.txt` or backup timestamp in ops log

---

## When not to rollback

- Database migration already applied with schema incompatible with old API — coordinate DB restore instead.
- Data loss risk if new transactions exist after bad deploy — assess DB point-in-time restore.

---

## Dry-run status

**NOT RUN** on acceptance host during this phase.
