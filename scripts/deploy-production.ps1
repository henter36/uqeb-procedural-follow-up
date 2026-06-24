#Requires -Version 5.1
<#
.SYNOPSIS
  Compatibility entry point for the canonical Uqeb production deployment.

.DESCRIPTION
  Compatibility entry point. ZIP packages delegate to install-production-package.ps1.
  Unpacked folders still delegate to deploy-production-v2.ps1 (legacy).
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

if ([System.IO.Path]::GetExtension($SourcePackagePath) -eq ".zip") {
    $installScript = Join-Path $PSScriptRoot "install-production-package.ps1"
    if (-not (Test-Path -LiteralPath $installScript)) {
        throw "Preferred installer is missing: $installScript"
    }

    & $installScript `
        -PackagePath $SourcePackagePath `
        -InstallRoot $InstallRoot `
        -TaskName $ScheduledTaskName `
        -ApiPort $ApiPort `
        -ConfigPath $(if ($ProductionSettingsPath) { $ProductionSettingsPath } else { Join-Path $InstallRoot "config\appsettings.Production.json" })
    return
}

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
