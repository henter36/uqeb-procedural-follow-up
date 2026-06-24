#Requires -Version 5.1
<#
.SYNOPSIS
  Rolls back the active Uqeb release using rollback-state.json.
#>

[CmdletBinding()]
param(
    [string]$InstallRoot = "C:\Uqeb",
    [string]$TaskName = "UqebApi",
    [int]$ApiPort = 5000,
    [string]$ConfigPath = "C:\Uqeb\config\appsettings.Production.json"
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$commonPath = Join-Path $PSScriptRoot "deployment\Common.ps1"
if (-not (Test-Path -LiteralPath $commonPath)) {
    throw "deployment\Common.ps1 was not found: $commonPath"
}
. $commonPath

if (-not (Test-IsAdministrator)) {
    throw "Run as Administrator."
}

$releaseManifestPath = Join-Path $InstallRoot "publish\release-manifest.json"
$restored = Invoke-ReleaseRollbackFromState `
    -InstallRoot $InstallRoot `
    -TaskName $TaskName `
    -ApiPort $ApiPort `
    -ConfigPath $ConfigPath `
    -ReleaseManifestPath $releaseManifestPath

if (-not $restored) {
    throw "Release rollback did not restore a previous version."
}

Write-Host "Release rollback completed successfully."
