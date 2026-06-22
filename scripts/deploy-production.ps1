#Requires -Version 5.1
<#
.SYNOPSIS
  Compatibility entry point for the canonical Uqeb production deployment.

.DESCRIPTION
  Delegates to deploy-production-v2.ps1, which uses the approved production
  layout under C:\Uqeb\publish, binds Kestrel to 0.0.0.0 by default, preserves
  separately provisioned production settings, and performs full preflight and
  post-deployment verification.
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$SourcePackagePath,

    [string]$InstallRoot = "C:\Uqeb",

    [string]$ScheduledTaskName = "UqebApi",

    [ValidateRange(1, 65535)]
    [int]$ApiPort = 5000,

    [string]$ApiBindAddress = "0.0.0.0",

    [string]$ProductionSettingsPath,

    [string]$FrontendOrigin = "http://10.0.177.17:8080",

    [string]$FrontendApiBaseUrl = "http://10.0.177.17:5000/api"
)

$canonicalScript = Join-Path $PSScriptRoot "deploy-production-v2.ps1"

if (-not (Test-Path -LiteralPath $canonicalScript)) {
    throw "Canonical deployment script is missing: $canonicalScript"
}

& $canonicalScript `
    -SourcePackagePath $SourcePackagePath `
    -InstallRoot $InstallRoot `
    -ScheduledTaskName $ScheduledTaskName `
    -ApiPort $ApiPort `
    -ApiBindAddress $ApiBindAddress `
    -ProductionSettingsPath $ProductionSettingsPath `
    -FrontendOrigin $FrontendOrigin `
    -FrontendApiBaseUrl $FrontendApiBaseUrl
