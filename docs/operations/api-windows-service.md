# UqebApi Windows Service (production runtime)

## Background

Production was running `backend/Uqeb.Api` via a Scheduled Task named `UqebApi`
that executes `C:\Uqeb\run-api.cmd` (WorkingDirectory `C:\Uqeb`, account
`SYSTEM`). That `.cmd` launches `C:\Uqeb\current\api\Uqeb.Api.exe` and
redirects its stdout to `C:\Uqeb\logs\api-runtime.log` with no rotation, which
is why that file previously grew past 200 MB.

On 2026-07-04 the API stopped and left no listener on port 5000, causing
`ERR_CONNECTION_REFUSED` on `http://10.0.177.17:5000/api/auth/login` for every
user. The Scheduled Task had to be started manually. As a stopgap it was
hardened (`ExecutionTimeLimit=PT0S`, `RestartCount=5`, `RestartInterval=PT1M`),
but a Scheduled Task is not a supervised, self-healing service — it has no
first-class "restart on crash" concept, and its 72-hour default execution
limit is exactly the kind of setting that can silently kill a long-running
API again.

This document describes the durable replacement: running `Uqeb.Api.exe` as a
native Windows Service (`UqebApi`), with OS-level recovery policy, automatic
startup, and log rotation.

The application code change is additive: `Program.cs` calls
`builder.Host.UseWindowsService(...)`, which is a no-op unless the process is
actually started by the Service Control Manager. `dotnet run` and the
existing Scheduled Task continue to work unchanged.

## What changed in the app

- `backend/Uqeb.Api/Uqeb.Api.csproj`: added
  `Microsoft.Extensions.Hosting.WindowsServices`.
- `backend/Uqeb.Api/Program.cs`: added
  `builder.Host.UseWindowsService(options => options.ServiceName = "UqebApi");`
  right after `WebApplication.CreateBuilder(args)`. This only activates when
  `WindowsServiceHelpers.IsWindowsService()` is true (i.e. the parent process
  is `services.exe`), in which case it:
  - sets `ContentRootPath` to `AppContext.BaseDirectory` — services start with
    an unreliable working directory (often `C:\Windows\System32`), so this
    guarantees `appsettings.json`, `Attachments`, etc. resolve correctly
    regardless of how the SCM launches the process;
  - switches the host lifetime to `WindowsServiceLifetime` so `net stop`
    shuts the app down cleanly;
  - registers the Windows Event Log logging provider, so service-mode
    diagnostics land in Event Viewer (Application log, source `UqebApi`)
    even with no console attached.
  No endpoints, business logic, or API contracts changed.
- `backend/Uqeb.Api/appsettings.Production.json`: added
  `Microsoft.EntityFrameworkCore.Database.Command: Warning` so SQL command
  text isn't logged at `Information` level in Production (EF Core logs
  "Executed DbCommand" at Information by default, which is a major source of
  log volume). To temporarily re-enable verbose SQL logging for diagnostics,
  set the environment variable
  `Logging__LogLevel__Microsoft.EntityFrameworkCore.Database.Command=Information`
  on the service (see "Diagnostics" below) and restart it — no code or config
  file change needed, and remember to unset it afterwards.

## New scripts (`deployment/windows/`)

These are standalone; they don't depend on `scripts/deployment/Common.ps1` or
the existing `scripts/deploy-production*.ps1` pipeline, so they can be copied
to the production host on their own.

| Script | Purpose |
|---|---|
| `install-uqeb-api-service.ps1` | Create or reconfigure the `UqebApi` service (idempotent), set recovery policy, open the firewall port, start it, register daily log rotation. |
| `update-uqeb-api-service.ps1` | Health-check, stop, back up, deploy new files, start, health-check again; auto-rollback on failure. |
| `remove-uqeb-api-service.ps1` | Stop and delete the service. No-op if it doesn't exist. Never touches DB/logs/deployed files/firewall unless told to. |
| `verify-uqeb-api-service.ps1` | Read-only health/state audit with a PASS/FAIL line per check and a clear overall verdict. |
| `rotate-uqeb-api-log.ps1` | Rotates/retains the legacy `api-runtime.log`. |

All scripts require an elevated (Administrator) PowerShell session and use
`sc.exe`/native cmdlets, so they are Windows PowerShell 5.1 compatible.

## Install

```powershell
cd C:\path\to\uqeb\deployment\windows
.\install-uqeb-api-service.ps1
```

Defaults: service name `UqebApi`, binary
`C:\Uqeb\current\api\Uqeb.Api.exe`, port `5000`, bind address `0.0.0.0`,
`ASPNETCORE_ENVIRONMENT=Production`. Override any of these with parameters,
e.g.:

```powershell
.\install-uqeb-api-service.ps1 -ApiPort 5000 -ApiBindAddress 0.0.0.0
```

Running it again against an already-installed service reconfigures it in
place (stops it, updates `binPath`/`DisplayName`/`Description`, re-applies
environment and recovery settings, restarts) — safe to re-run.

What it does, in order:
1. Validates the binary exists.
2. Creates the service (or reconfigures it if present).
3. Sets `ASPNETCORE_ENVIRONMENT`, `DOTNET_ENVIRONMENT`, and `ASPNETCORE_URLS`
   via the service's own `Environment` registry value at
   `HKLM:\SYSTEM\CurrentControlSet\Services\UqebApi\Environment` — services do
   **not** inherit a user's environment variables, so this is the only
   reliable way to set them.
4. Configures failure recovery: restart after 1 minute (1st failure), restart
   after 1 minute (2nd failure), restart after 5 minutes (3rd+ failure),
   reset the failure counter after 1 day.
5. Opens an inbound firewall rule for the port if one doesn't already exist.
6. Starts the service and waits for `health/live` to return 200.
7. Registers a daily Scheduled Task (`UqebApiLogRotation`, 03:30) running
   `rotate-uqeb-api-log.ps1`.
8. **Does not** touch the legacy Scheduled Task unless you pass
   `-DisableLegacyScheduledTask` — see "Retiring the legacy Scheduled Task"
   below.

## Retiring the legacy Scheduled Task

Do this only after `verify-uqeb-api-service.ps1` has passed and you've
observed the service for a while (it survives a reboot, survives a killed
process, etc. — see "Testing recovery" below). Two services/tasks bound to
the same port 5000 must never run at once.

```powershell
.\install-uqeb-api-service.ps1 -DisableLegacyScheduledTask
```

This **disables** (does not delete or rename) the Scheduled Task named
`UqebApi` and prefixes its Description with a rollback note. To roll back to
the Scheduled Task at any time:

```powershell
Stop-Service UqebApi
Enable-ScheduledTask -TaskName UqebApi
Start-ScheduledTask -TaskName UqebApi
```

## Update (deploying a new build)

```powershell
.\update-uqeb-api-service.ps1 -SourcePath C:\Uqeb\publish\api
```

- Probes `health/live` and `health/ready` first (informational only — logged,
  not blocking).
- Stops the service.
- Backs up the current `C:\Uqeb\current\api` to
  `C:\Uqeb\backup\current-api-before-<timestamp>`.
- Copies `-SourcePath` into `C:\Uqeb\current\api` via `robocopy`, excluding
  `appsettings*.json` (same exclusion the existing
  `scripts\install-production-package.ps1` / `Copy-ApplicationPayload`
  pattern uses), then re-applies the standing production config from
  `C:\Uqeb\config\appsettings.Production.json`.
- Starts the service and polls `health/live` + `health/ready` for up to 60
  seconds (`-HealthCheckTimeoutSec`).
- **On failure**: automatically stops the service, restores the backup,
  restarts, and re-checks health — then exits non-zero either way, with the
  manual rollback command printed for reference:

```powershell
robocopy "C:\Uqeb\backup\current-api-before-<timestamp>" "C:\Uqeb\current\api" /E /R:2 /W:2
Copy-Item "C:\Uqeb\config\appsettings.Production.json" "C:\Uqeb\current\api\appsettings.Production.json" -Force
Restart-Service UqebApi
```

This script does not touch the database. It follows the existing production
deployment file-layout convention (`C:\Uqeb\current\api`,
`C:\Uqeb\config\appsettings.Production.json`) but is independent of the
`scripts\deploy-production*.ps1` pipeline.

## Verify

```powershell
.\verify-uqeb-api-service.ps1
.\verify-uqeb-api-service.ps1 -ApiBindAddress 10.0.177.17
```

Prints one `[PASS|FAIL|SKIP] CheckName - detail` line per check:
`ServiceExists`, `ServiceRunning`, `ProcessPathMatches`, `PortListening`,
`HealthLive_Localhost`, `HealthReady_Localhost`, `HealthLive_NetworkIp`
(SKIP if `-ApiBindAddress` omitted or unreachable), `RecentLogErrors`,
`LogFileSize`. Ends with `OVERALL: PASS` (exit 0) or `OVERALL: FAIL` (exit 1)
naming how many checks failed.

## Remove / rollback to nothing

```powershell
.\remove-uqeb-api-service.ps1
```

Idempotent — exits 0 with a message if the service doesn't exist. Stops and
deletes the service only. Add `-RemoveFirewallRule` and/or
`-RemoveDeploymentFiles` to also remove those (both off by default). Never
touches the database or `C:\Uqeb\logs` regardless of switches.

## Diagnosing ERR_CONNECTION_REFUSED

1. `.\verify-uqeb-api-service.ps1 -ApiBindAddress 10.0.177.17` — this alone
   usually pinpoints it: service missing/stopped, port not listening, or
   health endpoint down.
2. If `ServiceRunning` is FAIL:
   `Get-Service UqebApi | Select Status, StartType` then
   `Get-EventLog -LogName Application -Source UqebApi -Newest 20` (or
   `Get-WinEvent -LogName Application -FilterXPath "*[System[Provider[@Name='UqebApi']]]" -MaxEvents 20`)
   for the crash reason.
3. If `PortListening` is FAIL but the service is Running: check
   `ASPNETCORE_URLS` in the registry
   (`Get-ItemProperty 'HKLM:\SYSTEM\CurrentControlSet\Services\UqebApi' -Name Environment`)
   matches the intended bind address/port.
4. If everything above passes but the browser still can't reach it: check
   Windows Firewall (`Get-NetFirewallRule -DisplayName UqebApi-Http-5000`) and
   any network-level firewall/NSG between the client and `10.0.177.17:5000`.

## Health check commands

```powershell
Invoke-WebRequest http://localhost:5000/health/live  -UseBasicParsing
Invoke-WebRequest http://localhost:5000/health/ready -UseBasicParsing
Invoke-WebRequest http://10.0.177.17:5000/health/live -UseBasicParsing
```

## Testing recovery

```powershell
# Confirm the recovery policy is configured as expected:
sc.exe qfailure UqebApi

# Simulate a crash and confirm the SCM restarts it:
Get-CimInstance Win32_Service -Filter "Name='UqebApi'" | Select ProcessId
Stop-Process -Id <ProcessId> -Force
Start-Sleep -Seconds 70
Get-Service UqebApi   # should be Running again within ~1 minute

# Confirm it starts automatically after a reboot:
Get-Service UqebApi | Select StartType   # should be Automatic
```

## Log management

- **Windows Service mode**: no console is attached, so `Console.Out` writes
  are discarded; diagnostics go to the Windows Event Log (Application log,
  source `UqebApi`) instead of a growing file. There is no unbounded file to
  manage for the service itself.
- **Legacy `C:\Uqeb\logs\api-runtime.log`**: produced only by the old
  `run-api.cmd` → Scheduled Task path (`... >> api-runtime.log 2>&1`, no
  rotation). `rotate-uqeb-api-log.ps1` is registered by
  `install-uqeb-api-service.ps1` as a daily Scheduled Task
  (`UqebApiLogRotation`, 03:30): archives the file once it exceeds 100 MB
  (`-MaxSizeMB`) and deletes archives older than 14 days (`-RetentionDays`).
  Run it manually any time:
  ```powershell
  .\rotate-uqeb-api-log.ps1
  ```
- **EF Core SQL command logging**: `Microsoft.EntityFrameworkCore.Database.Command`
  is `Warning` in Production (see "What changed in the app"). To check the
  effective log levels without editing files:
  ```powershell
  Get-ItemProperty 'HKLM:\SYSTEM\CurrentControlSet\Services\UqebApi' -Name Environment
  ```
- Retention/location summary: Event Viewer manages its own retention for
  service-mode logs; `C:\Uqeb\logs\api-runtime.log` is capped at 100 MB with
  14-day archive retention via the scheduled rotation task.
