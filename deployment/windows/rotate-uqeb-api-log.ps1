#Requires -Version 5.1
<#
.SYNOPSIS
  Rotates and retains the legacy api-runtime.log file so it can never again
  grow unbounded (it previously reached 200+ MB under the Scheduled Task
  model, which redirected stdout with `>> api-runtime.log 2>&1` and no
  rotation).

.DESCRIPTION
  The UqebApi Windows Service does not write to this file at all (no console,
  no shell redirection); this script exists for:
    - the transition period while the legacy Scheduled Task may still exist
      as a rollback path and could still write to it, and
    - cleaning up whatever accumulated there historically.

  If the live log exceeds -MaxSizeMB, it is renamed to a timestamped archive
  under the same directory and a fresh empty file is left in its place.
  Archived files older than -RetentionDays are deleted.

  Intended to be run on a schedule (e.g. a daily Scheduled Task named
  UqebApiLogRotation) but can also be run manually.

.PARAMETER LogPath
  Default: C:\Uqeb\logs\api-runtime.log.

.PARAMETER MaxSizeMB
  Rotate once the live file exceeds this size. Default: 100.

.PARAMETER RetentionDays
  Delete archived (*.log.<timestamp>) files older than this many days. Default: 14.

.EXAMPLE
  .\rotate-uqeb-api-log.ps1
#>
[CmdletBinding()]
param(
    [string]$LogPath = "C:\Uqeb\logs\api-runtime.log",
    [int]$MaxSizeMB = 100,
    [int]$RetentionDays = 14
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

function Write-Info { param([string]$Message) Write-Host ("[info] " + $Message) }

if (-not (Test-Path -LiteralPath $LogPath)) {
    Write-Info "$LogPath does not exist; nothing to rotate."
    exit 0
}

$logDirectory = Split-Path -Parent $LogPath
$logBaseName = Split-Path -Leaf $LogPath

$sizeMB = [math]::Round((Get-Item -LiteralPath $LogPath).Length / 1MB, 1)
if ($sizeMB -gt $MaxSizeMB) {
    $timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
    $archivePath = Join-Path $logDirectory "$logBaseName.$timestamp.old"

    try {
        Rename-Item -LiteralPath $LogPath -NewName (Split-Path -Leaf $archivePath) -Force
        New-Item -ItemType File -Path $LogPath -Force | Out-Null
        Write-Info "Rotated $LogPath ($sizeMB MB) to $archivePath."
    }
    catch {
        Write-Info "Could not rotate (file may be locked by an active writer): $($_.Exception.Message)"
    }
}
else {
    Write-Info "$LogPath is $sizeMB MB (limit $MaxSizeMB MB); no rotation needed."
}

$cutoff = (Get-Date).AddDays(-$RetentionDays)
$archives = Get-ChildItem -LiteralPath $logDirectory -Filter "$logBaseName.*.old" -ErrorAction SilentlyContinue |
    Where-Object { $_.LastWriteTimeUtc -lt $cutoff.ToUniversalTime() }

foreach ($archive in $archives) {
    Remove-Item -LiteralPath $archive.FullName -Force
    Write-Info "Deleted archived log older than $RetentionDays days: $($archive.Name)"
}

exit 0
