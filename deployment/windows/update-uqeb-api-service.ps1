#Requires -Version 5.1
<#
.SYNOPSIS
  Updates the files backing the UqebApi Windows Service and restarts it, with
  a pre-update health check, a file backup, and automatic rollback if the
  service fails to come back healthy.

.DESCRIPTION
  1. Probes health/live and health/ready before touching anything (best-effort;
     logged, not blocking, since the point of updating may be to recover a
     down service).
  2. Stops the service.
  3. Backs up the current C:\Uqeb\current\api contents to a timestamped folder
     under -BackupRoot.
  4. Copies -SourcePath into -TargetPath via robocopy, excluding
     appsettings*.json (mirrors the existing production deployment convention
     in scripts\install-production-package.ps1 / scripts\deployment\Common.ps1
     Copy-ApplicationPayload), then re-applies the standing production config
     from -ConfigPath.
  5. Starts the service.
  6. Polls health/live and health/ready. On failure: stops the service,
     restores the backup, restarts, and exits non-zero with a clear error.
     On success: exits 0.

.PARAMETER SourcePath
  Directory containing the new Uqeb.Api build to deploy (e.g. a freshly
  published/extracted release). Mandatory.

.PARAMETER TargetPath
  Live application directory. Default: C:\Uqeb\current\api.

.PARAMETER ConfigPath
  Standing production config re-applied after the copy. Default:
  C:\Uqeb\config\appsettings.Production.json.

.PARAMETER BackupRoot
  Where the pre-update backup is stored. Default: C:\Uqeb\backup.

.EXAMPLE
  .\update-uqeb-api-service.ps1 -SourcePath C:\Uqeb\publish\api
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$SourcePath,

    [string]$ServiceName = "UqebApi",
    [string]$TargetPath = "C:\Uqeb\current\api",
    [string]$ConfigPath = "C:\Uqeb\config\appsettings.Production.json",
    [string]$BackupRoot = "C:\Uqeb\backup",
    [int]$ApiPort = 5000,
    [ValidateRange(5, 300)]
    [int]$HealthCheckTimeoutSec = 60
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

function Write-Step { param([string]$Message) Write-Host ""; Write-Host ("==> " + $Message) -ForegroundColor Cyan }
function Write-Info { param([string]$Message) Write-Host ("[info] " + $Message) }

function Test-IsAdministrator {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($identity)
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Test-UqebHealthEndpoints {
    param(
        [int]$Port,
        [int]$TimeoutSec,
        [switch]$Quiet
    )

    $paths = @('/health/live', '/health/ready')
    $allOk = $true
    foreach ($path in $paths) {
        $uri = "http://localhost:$Port$path"
        try {
            $response = Invoke-WebRequest -Uri $uri -UseBasicParsing -TimeoutSec $TimeoutSec
            $ok = $response.StatusCode -eq 200
        }
        catch {
            $ok = $false
        }
        if (-not $Quiet) {
            Write-Info "$uri -> $(if ($ok) { 'OK' } else { 'FAIL' })"
        }
        if (-not $ok) { $allOk = $false }
    }
    return $allOk
}

function Wait-UqebHealthy {
    param(
        [int]$Port,
        [int]$TimeoutSec
    )

    $deadline = (Get-Date).AddSeconds($TimeoutSec)
    do {
        if (Test-UqebHealthEndpoints -Port $Port -TimeoutSec 5 -Quiet) {
            return $true
        }
        Start-Sleep -Seconds 2
    } while ((Get-Date) -lt $deadline)

    return $false
}

function Invoke-UqebRobocopy {
    param(
        [string]$Source,
        [string]$Destination
    )

    if (-not (Test-Path -LiteralPath $Destination)) {
        New-Item -ItemType Directory -Path $Destination -Force | Out-Null
    }

    $arguments = @($Source, $Destination, '/E', '/R:2', '/W:2', '/XF', 'appsettings.json', 'appsettings.Development.json', 'appsettings.Production.json')
    & robocopy.exe @arguments | Out-Null
    if ($LASTEXITCODE -ge 8) {
        throw "robocopy failed from '$Source' to '$Destination' with exit code $LASTEXITCODE."
    }
}

try {
    if (-not (Test-IsAdministrator)) {
        throw "This script must be run as Administrator."
    }

    if (-not (Test-Path -LiteralPath $SourcePath)) {
        throw "SourcePath not found: $SourcePath"
    }
    if (-not (Test-Path -LiteralPath (Join-Path $SourcePath "Uqeb.Api.dll"))) {
        throw "SourcePath does not look like a published Uqeb.Api build (Uqeb.Api.dll missing): $SourcePath"
    }
    if (-not (Get-Service -Name $ServiceName -ErrorAction SilentlyContinue)) {
        throw "Service '$ServiceName' does not exist. Run install-uqeb-api-service.ps1 first."
    }

    Write-Step "Pre-update health check (informational)"
    $preHealthy = Test-UqebHealthEndpoints -Port $ApiPort -TimeoutSec 5
    if (-not $preHealthy) {
        Write-Info "Service is not currently healthy — proceeding anyway, since this update may be the fix."
    }

    $timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
    $backupPath = Join-Path $BackupRoot "current-api-before-$timestamp"

    Write-Step "Stopping service"
    Stop-Service -Name $ServiceName -Force
    (Get-Service -Name $ServiceName).WaitForStatus('Stopped', (New-TimeSpan -Seconds 30))

    Write-Step "Backing up current deployment to $backupPath"
    if (Test-Path -LiteralPath $TargetPath) {
        New-Item -ItemType Directory -Path $backupPath -Force | Out-Null
        Invoke-UqebRobocopy -Source $TargetPath -Destination $backupPath
    }
    else {
        Write-Info "TargetPath did not exist yet; nothing to back up."
    }

    $updateSucceeded = $false
    try {
        Write-Step "Copying new build into $TargetPath"
        Invoke-UqebRobocopy -Source $SourcePath -Destination $TargetPath

        if (Test-Path -LiteralPath $ConfigPath) {
            Write-Info "Re-applying standing production config from $ConfigPath"
            Copy-Item -LiteralPath $ConfigPath -Destination (Join-Path $TargetPath "appsettings.Production.json") -Force
        }
        else {
            Write-Info "No standing config found at $ConfigPath; leaving whatever appsettings.Production.json shipped in SourcePath (if any)."
        }

        Write-Step "Starting service"
        Start-Service -Name $ServiceName
        (Get-Service -Name $ServiceName).WaitForStatus('Running', (New-TimeSpan -Seconds 30))

        Write-Step "Waiting for health/live and health/ready"
        if (-not (Wait-UqebHealthy -Port $ApiPort -TimeoutSec $HealthCheckTimeoutSec)) {
            throw "Service did not become healthy within $HealthCheckTimeoutSec seconds after update."
        }

        Test-UqebHealthEndpoints -Port $ApiPort -TimeoutSec 5 | Out-Null
        $updateSucceeded = $true
    }
    finally {
        if (-not $updateSucceeded) {
            Write-Step "Update failed health check — rolling back to backup"
            try {
                Stop-Service -Name $ServiceName -Force -ErrorAction SilentlyContinue
                (Get-Service -Name $ServiceName).WaitForStatus('Stopped', (New-TimeSpan -Seconds 30))

                if (Test-Path -LiteralPath $backupPath) {
                    Invoke-UqebRobocopy -Source $backupPath -Destination $TargetPath
                    if (Test-Path -LiteralPath $ConfigPath) {
                        Copy-Item -LiteralPath $ConfigPath -Destination (Join-Path $TargetPath "appsettings.Production.json") -Force
                    }
                    Start-Service -Name $ServiceName
                    (Get-Service -Name $ServiceName).WaitForStatus('Running', (New-TimeSpan -Seconds 30))
                    $rolledBack = Wait-UqebHealthy -Port $ApiPort -TimeoutSec $HealthCheckTimeoutSec
                    if ($rolledBack) {
                        Write-Info "Rollback restored a healthy service from $backupPath."
                    }
                    else {
                        Write-Info "Rollback restarted the service from $backupPath but it is still not healthy. Manual intervention required."
                    }
                }
                else {
                    Write-Info "No backup available to roll back to (TargetPath did not exist before update)."
                }
            }
            catch {
                Write-Info "Automatic rollback itself failed: $($_.Exception.Message)"
            }
            Write-Info "Manual rollback path: robocopy `"$backupPath`" `"$TargetPath`" /E /R:2 /W:2 ; Copy-Item `"$ConfigPath`" `"$TargetPath\appsettings.Production.json`" -Force ; Restart-Service $ServiceName"
        }
    }

    Write-Step "Update complete"
    Write-Info "Service '$ServiceName' is Running and healthy. Backup retained at $backupPath."
    exit 0
}
catch {
    Write-Error $_.Exception.Message
    exit 1
}
