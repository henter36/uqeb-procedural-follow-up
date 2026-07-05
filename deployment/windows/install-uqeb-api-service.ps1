#Requires -Version 5.1
<#
.SYNOPSIS
  Installs (or reconfigures, idempotently) the UqebApi Windows Service running
  Uqeb.Api.exe directly from C:\Uqeb\current\api, replacing the long-running
  Scheduled Task as the durable production runtime mechanism.

.DESCRIPTION
  - Creates the service if missing, or reconfigures it in place if it already exists.
  - Sets Automatic startup, DisplayName, Description.
  - Sets ASPNETCORE_ENVIRONMENT and ASPNETCORE_URLS via the service's own
    Environment registry value (services do not inherit user/session env vars).
  - Configures failure recovery (restart after 1 min, 1 min, then 5 min; reset
    the failure counter after 1 day) so the service is never left down without
    at least attempting to recover.
  - Opens the inbound firewall port if a matching rule does not already exist.
  - Starts the service and waits for it to reach Running.
  - Does NOT touch the legacy Scheduled Task unless -DisableLegacyScheduledTask
    is passed explicitly; see remarks.

.PARAMETER ServiceName
  Windows Service name. Default: UqebApi.

.PARAMETER BinaryPath
  Full path to Uqeb.Api.exe. Default: C:\Uqeb\current\api\Uqeb.Api.exe.

.PARAMETER ApiPort
  TCP port the API listens on. Default: 5000.

.PARAMETER ApiBindAddress
  Address ASPNETCORE_URLS binds to. Default: 0.0.0.0 (all interfaces).

.PARAMETER SkipUrlsEnvironmentVariable
  Skip setting ASPNETCORE_URLS via the service environment, e.g. if the bind
  address/port is already fully controlled through appsettings/Kestrel config.

.PARAMETER FirewallRuleName
  Display name for the inbound firewall rule. Default: UqebApi-Http-<ApiPort>.

.PARAMETER LegacyScheduledTaskName
  Name of the pre-existing Scheduled Task that used to run the API. Default: UqebApi.
  (Same default name as the new service; they live in different Windows
  namespaces so this is not a conflict, but see -DisableLegacyScheduledTask.)

.PARAMETER DisableLegacyScheduledTask
  After the new service starts and passes a health check, disable (not delete,
  not rename) the legacy Scheduled Task and mark its Description as
  rollback-only. Off by default: run this only after you've confirmed the
  service is stable, so you always have an immediate rollback path
  (Enable-ScheduledTask + Start-ScheduledTask, then Stop-Service UqebApi).

.PARAMETER SkipHealthCheck
  Skip the post-start health/live probe (not recommended).

.EXAMPLE
  .\install-uqeb-api-service.ps1
.EXAMPLE
  .\install-uqeb-api-service.ps1 -DisableLegacyScheduledTask
#>
[CmdletBinding()]
param(
    [string]$ServiceName = "UqebApi",
    [string]$DisplayName = "UQEB API",
    [string]$Description = "UQEB Procedural Follow-up API (backend/Uqeb.Api). Managed as a Windows Service; replaces the long-running Scheduled Task runtime model.",
    [string]$BinaryPath = "C:\Uqeb\current\api\Uqeb.Api.exe",
    [int]$ApiPort = 5000,
    [string]$ApiBindAddress = "0.0.0.0",
    [switch]$SkipUrlsEnvironmentVariable,
    [string]$FirewallRuleName = "",
    [string]$LegacyScheduledTaskName = "UqebApi",
    [switch]$DisableLegacyScheduledTask,
    [switch]$SkipHealthCheck,
    [ValidateRange(5, 300)]
    [int]$HealthCheckTimeoutSec = 60,
    [switch]$SkipLogRotationTask,
    [string]$LogRotationTaskName = "UqebApiLogRotation",
    [string]$LogRotationScriptPath = ""
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

. (Join-Path $PSScriptRoot "UqebServiceCommon.ps1")

function Test-IsAdministrator {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($identity)
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Invoke-ScExe {
    param([string[]]$Arguments)
    $output = & sc.exe @Arguments 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "sc.exe $($Arguments -join ' ') failed with exit code $LASTEXITCODE`: $output"
    }
    return $output
}

function Set-UqebServiceEnvironment {
    param(
        [string]$ServiceName,
        [string[]]$EnvironmentEntries
    )

    $regPath = "HKLM:\SYSTEM\CurrentControlSet\Services\$ServiceName"
    if (-not (Test-Path -LiteralPath $regPath)) {
        throw "Service registry key not found: $regPath"
    }

    New-ItemProperty -LiteralPath $regPath -Name "Environment" -PropertyType MultiString -Value $EnvironmentEntries -Force | Out-Null
}

function Set-UqebServiceRecovery {
    param(
        [string]$ServiceName
    )

    # First failure: restart after 1 minute. Second failure: restart after 1 minute.
    # Third and subsequent failures: restart after 5 minutes. Reset the failure
    # counter after 1 day (86400 seconds) of continuous uptime.
    Invoke-ScExe -Arguments @(
        "failure", $ServiceName,
        "reset=", "86400",
        "actions=", "restart/60000/restart/60000/restart/300000"
    ) | Out-Null
}

try {
    if (-not (Test-IsAdministrator)) {
        throw "This script must be run as Administrator."
    }

    if ([string]::IsNullOrWhiteSpace($FirewallRuleName)) {
        $FirewallRuleName = "UqebApi-Http-$ApiPort"
    }

    Write-Step "Validating binary path"
    if (-not (Test-Path -LiteralPath $BinaryPath)) {
        throw "Uqeb.Api.exe not found at: $BinaryPath"
    }
    Write-Info "Binary: $BinaryPath"

    $quotedBinaryPath = "`"$BinaryPath`""

    Write-Step "Creating or reconfiguring service '$ServiceName'"
    $existingService = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue

    if (-not $existingService) {
        Write-Info "Service does not exist yet; creating it."
        New-Service `
            -Name $ServiceName `
            -BinaryPathName $quotedBinaryPath `
            -DisplayName $DisplayName `
            -Description $Description `
            -StartupType Automatic | Out-Null
    }
    else {
        Write-Info "Service already exists; reconfiguring in place (idempotent update)."
        if ($existingService.Status -eq 'Running') {
            Write-Info "Stopping service before reconfiguration."
            Stop-Service -Name $ServiceName -Force
            $existingService.WaitForStatus('Stopped', (New-TimeSpan -Seconds 30))
        }

        Invoke-ScExe -Arguments @(
            "config", $ServiceName,
            "binPath=", $quotedBinaryPath,
            "start=", "auto",
            "DisplayName=", $DisplayName
        ) | Out-Null
        Invoke-ScExe -Arguments @("description", $ServiceName, $Description) | Out-Null
    }

    Write-Step "Setting service environment variables"
    $envEntries = New-Object System.Collections.Generic.List[string]
    $envEntries.Add("ASPNETCORE_ENVIRONMENT=Production") | Out-Null
    $envEntries.Add("DOTNET_ENVIRONMENT=Production") | Out-Null
    if (-not $SkipUrlsEnvironmentVariable) {
        $envEntries.Add("ASPNETCORE_URLS=http://${ApiBindAddress}:${ApiPort}") | Out-Null
    }
    Set-UqebServiceEnvironment -ServiceName $ServiceName -EnvironmentEntries @($envEntries)
    foreach ($entry in $envEntries) { Write-Info "Environment: $entry" }

    Write-Step "Configuring failure recovery policy"
    Set-UqebServiceRecovery -ServiceName $ServiceName
    Write-Info "Recovery: restart after 1 min, 1 min, then 5 min; failure counter resets after 1 day."

    Write-Step "Ensuring firewall rule for port $ApiPort"
    $existingRule = Get-NetFirewallRule -DisplayName $FirewallRuleName -ErrorAction SilentlyContinue
    if (-not $existingRule) {
        New-NetFirewallRule `
            -DisplayName $FirewallRuleName `
            -Direction Inbound `
            -Protocol TCP `
            -LocalPort $ApiPort `
            -Action Allow | Out-Null
        Write-Info "Created inbound firewall rule '$FirewallRuleName' for TCP/$ApiPort."
    }
    else {
        Write-Info "Firewall rule '$FirewallRuleName' already exists; leaving it as-is."
    }

    Write-Step "Starting service"
    Start-Service -Name $ServiceName
    $service = Get-Service -Name $ServiceName
    $service.WaitForStatus('Running', (New-TimeSpan -Seconds 30))
    Write-Info "Service status: $($service.Status)"

    if (-not $SkipHealthCheck) {
        $healthHost = Get-UqebHealthHost -ApiBindAddress $ApiBindAddress
        $healthUri = "http://${healthHost}:$ApiPort/health/live"
        Write-Step "Waiting for $healthUri to return 200"
        $deadline = (Get-Date).AddSeconds($HealthCheckTimeoutSec)
        $healthy = $false
        do {
            try {
                $response = Invoke-WebRequest -Uri $healthUri -UseBasicParsing -TimeoutSec 5
                if ($response.StatusCode -eq 200) {
                    $healthy = $true
                    break
                }
            }
            catch {
                # Connection refused/timeout while the service is still coming up;
                # fall through to the shared sleep below and retry.
            }

            if (-not $healthy) {
                Start-Sleep -Seconds 2
            }
        } while ((Get-Date) -lt $deadline)

        if (-not $healthy) {
            throw "Service started but $healthUri did not return 200 within $HealthCheckTimeoutSec seconds. Check the Application/System event logs and the UqebApi source in Event Viewer."
        }
        Write-Info "health/live returned 200."
    }

    if ($DisableLegacyScheduledTask) {
        Write-Step "Disabling legacy Scheduled Task '$LegacyScheduledTaskName' (rollback path preserved)"
        $legacyTask = Get-ScheduledTask -TaskName $LegacyScheduledTaskName -ErrorAction SilentlyContinue
        if ($legacyTask) {
            Stop-ScheduledTask -TaskName $LegacyScheduledTaskName -ErrorAction SilentlyContinue
            Disable-ScheduledTask -TaskName $LegacyScheduledTaskName | Out-Null
            $legacyTask.Description = "[LEGACY / ROLLBACK ONLY] Superseded by the UqebApi Windows Service on $(Get-Date -Format 'yyyy-MM-dd'). Do not enable while the Windows Service is running on the same port. Rollback: Stop-Service UqebApi; Enable-ScheduledTask -TaskName '$LegacyScheduledTaskName'; Start-ScheduledTask -TaskName '$LegacyScheduledTaskName'."
            Set-ScheduledTask -InputObject $legacyTask | Out-Null
            Write-Info "Legacy Scheduled Task disabled and marked rollback-only. It was NOT deleted."
        }
        else {
            Write-Info "No Scheduled Task named '$LegacyScheduledTaskName' found; nothing to disable."
        }
    }
    else {
        Write-Info "Skipping legacy Scheduled Task changes (pass -DisableLegacyScheduledTask once the service is confirmed stable)."
    }

    if (-not $SkipLogRotationTask) {
        Write-Step "Registering daily log rotation task '$LogRotationTaskName'"
        if ([string]::IsNullOrWhiteSpace($LogRotationScriptPath)) {
            $LogRotationScriptPath = Join-Path $PSScriptRoot "rotate-uqeb-api-log.ps1"
        }
        if (-not (Test-Path -LiteralPath $LogRotationScriptPath)) {
            Write-Info "rotate-uqeb-api-log.ps1 not found at '$LogRotationScriptPath'; skipping log rotation task registration."
        }
        else {
            $existingRotationTask = Get-ScheduledTask -TaskName $LogRotationTaskName -ErrorAction SilentlyContinue
            $rotationAction = New-ScheduledTaskAction -Execute "powershell.exe" `
                -Argument "-NoProfile -ExecutionPolicy Bypass -File `"$LogRotationScriptPath`""
            $rotationTrigger = New-ScheduledTaskTrigger -Daily -At "03:30"
            $rotationPrincipal = New-ScheduledTaskPrincipal -UserId "SYSTEM" -LogonType ServiceAccount -RunLevel Highest

            if ($existingRotationTask) {
                Set-ScheduledTask -TaskName $LogRotationTaskName -Action $rotationAction -Trigger $rotationTrigger -Principal $rotationPrincipal | Out-Null
                Write-Info "Updated existing '$LogRotationTaskName' scheduled task."
            }
            else {
                Register-ScheduledTask -TaskName $LogRotationTaskName -Action $rotationAction -Trigger $rotationTrigger -Principal $rotationPrincipal -Description "Rotates C:\Uqeb\logs\api-runtime.log to prevent unbounded growth." | Out-Null
                Write-Info "Registered '$LogRotationTaskName' to run daily at 03:30."
            }
        }
    }
    else {
        Write-Info "Skipping log rotation task registration (-SkipLogRotationTask)."
    }

    Write-Step "Install complete"
    Write-Info "Service '$ServiceName' is Running and healthy on port $ApiPort."
    exit 0
}
catch {
    Write-Error $_.Exception.Message
    exit 1
}
