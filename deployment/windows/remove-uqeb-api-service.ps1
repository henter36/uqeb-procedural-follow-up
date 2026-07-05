#Requires -Version 5.1
<#
.SYNOPSIS
  Removes the UqebApi Windows Service. Idempotent: a no-op (exit 0) if the
  service does not exist.

.DESCRIPTION
  Stops and deletes the Windows Service only. Never touches the database,
  logs, deployment files, or the firewall rule unless the corresponding
  -Remove* switch is passed explicitly.

.PARAMETER RemoveFirewallRule
  Also remove the inbound firewall rule created by install-uqeb-api-service.ps1.
  Off by default.

.PARAMETER RemoveDeploymentFiles
  Also delete -DeploymentPath (default C:\Uqeb\current\api). Off by default.
  This does NOT touch the database or C:\Uqeb\logs.

.EXAMPLE
  .\remove-uqeb-api-service.ps1
.EXAMPLE
  .\remove-uqeb-api-service.ps1 -RemoveFirewallRule
#>
[CmdletBinding()]
param(
    [string]$ServiceName = "UqebApi",
    [string]$FirewallRuleName = "",
    [int]$ApiPort = 5000,
    [switch]$RemoveFirewallRule,
    [switch]$RemoveDeploymentFiles,
    [string]$DeploymentPath = "C:\Uqeb\current\api"
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

try {
    if (-not (Test-IsAdministrator)) {
        throw "This script must be run as Administrator."
    }

    if ([string]::IsNullOrWhiteSpace($FirewallRuleName)) {
        $FirewallRuleName = "UqebApi-Http-$ApiPort"
    }

    Write-Step "Checking for service '$ServiceName'"
    $service = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue

    if (-not $service) {
        Write-Info "Service '$ServiceName' does not exist. Nothing to remove (idempotent no-op)."
    }
    else {
        if ($service.Status -ne 'Stopped') {
            Write-Step "Stopping service"
            Stop-Service -Name $ServiceName -Force -ErrorAction SilentlyContinue
            try {
                $service.WaitForStatus('Stopped', (New-TimeSpan -Seconds 30))
            }
            catch {
                Write-Info "Service did not confirm Stopped within 30s; proceeding to delete anyway."
            }
        }

        Write-Step "Deleting service"
        $deleteOutput = & sc.exe delete $ServiceName 2>&1
        if ($LASTEXITCODE -ne 0) {
            throw "sc.exe delete $ServiceName failed with exit code ${LASTEXITCODE}: $deleteOutput"
        }
        Write-Info "Service '$ServiceName' deleted."
    }

    if ($RemoveFirewallRule) {
        Write-Step "Removing firewall rule '$FirewallRuleName'"
        $rule = Get-NetFirewallRule -DisplayName $FirewallRuleName -ErrorAction SilentlyContinue
        if ($rule) {
            Remove-NetFirewallRule -DisplayName $FirewallRuleName
            Write-Info "Firewall rule removed."
        }
        else {
            Write-Info "Firewall rule '$FirewallRuleName' not found; nothing to remove."
        }
    }
    else {
        Write-Info "Leaving firewall rule '$FirewallRuleName' in place (pass -RemoveFirewallRule to remove it)."
    }

    if ($RemoveDeploymentFiles) {
        Write-Step "Removing deployment files at $DeploymentPath"
        if (Test-Path -LiteralPath $DeploymentPath) {
            Remove-Item -LiteralPath $DeploymentPath -Recurse -Force
            Write-Info "Deployment files removed."
        }
        else {
            Write-Info "Deployment path does not exist; nothing to remove."
        }
    }
    else {
        Write-Info "Leaving deployment files, database, and logs untouched (pass -RemoveDeploymentFiles to also delete $DeploymentPath)."
    }

    Write-Step "Remove complete"
    exit 0
}
catch {
    Write-Error $_.Exception.Message
    exit 1
}
