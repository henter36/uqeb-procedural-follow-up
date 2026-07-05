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

. (Join-Path $PSScriptRoot "UqebServiceCommon.ps1")

# Normalize first so a relative path or a bare filename (e.g. "api-runtime.log")
# resolves correctly instead of producing an empty parent directory from
# Split-Path -Parent. [System.IO.Path]::GetFullPath resolves against .NET's
# Environment.CurrentDirectory, which Set-Location/cd does NOT keep in sync
# with PowerShell's own $PWD - so it silently resolves against the wrong
# directory whenever this script is run after cd'ing somewhere first (a very
# real interactive-operator scenario). GetUnresolvedProviderPathFromPSPath
# resolves against PowerShell's actual current location instead, and (unlike
# Resolve-Path) works for paths that don't exist yet.
$resolvedLogPath = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($LogPath)

if (-not (Test-Path -LiteralPath $resolvedLogPath)) {
    Write-Info "$resolvedLogPath does not exist; nothing to rotate."
    exit 0
}

$logDirectory = Split-Path -Parent $resolvedLogPath
$logBaseName = Split-Path -Leaf $resolvedLogPath

$sizeMB = [math]::Round((Get-Item -LiteralPath $resolvedLogPath).Length / 1MB, 1)
if ($sizeMB -gt $MaxSizeMB) {
    $timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
    $archivePath = Join-Path $logDirectory "$logBaseName.$timestamp.old"

    try {
        Move-Item -LiteralPath $resolvedLogPath -Destination $archivePath -Force
        New-Item -ItemType File -Path $resolvedLogPath -Force | Out-Null
        Write-Info "Rotated $resolvedLogPath ($sizeMB MB) to $archivePath."
    }
    catch {
        Write-Info "Could not rotate (file may be locked by an active writer): $($_.Exception.Message)"
    }
}
else {
    Write-Info "$resolvedLogPath is $sizeMB MB (limit $MaxSizeMB MB); no rotation needed."
}

$cutoff = (Get-Date).AddDays(-$RetentionDays)
$archives = Get-ChildItem -LiteralPath $logDirectory -Filter "$logBaseName.*.old" -ErrorAction SilentlyContinue |
    Where-Object { $_.LastWriteTimeUtc -lt $cutoff.ToUniversalTime() }

foreach ($archive in $archives) {
    Remove-Item -LiteralPath $archive.FullName -Force
    Write-Info "Deleted archived log older than $RetentionDays days: $($archive.Name)"
}

exit 0
