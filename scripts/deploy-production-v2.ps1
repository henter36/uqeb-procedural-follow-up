#Requires -Version 5.1
<#
.SYNOPSIS
  Canonical Uqeb production deployment.

.DESCRIPTION
  This script is intentionally separated from deploy-production.ps1 so the
  compatibility entry point remains stable. It uses C:\Uqeb\publish\api and
  C:\Uqeb\publish\web, binds Kestrel to 0.0.0.0 by default, preserves approved
  production settings, and supports an empty first-deployment target.
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

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

function Write-Step { param([string]$Message) Write-Output ""; Write-Output ("==> " + $Message) }
function Write-Info { param([string]$Message) Write-Output ("[INFO] " + $Message) }

function Test-IsAdministrator {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($identity)
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Ensure-Directory {
    param([string]$Path)
    if (-not (Test-Path -LiteralPath $Path)) {
        New-Item -ItemType Directory -Path $Path -Force | Out-Null
    }
}

function Invoke-RobocopyOrThrow {
    param([string]$Source, [string]$Destination, [string[]]$ExtraArguments = @())
    Ensure-Directory $Destination
    & robocopy $Source $Destination /E /R:2 /W:2 @ExtraArguments
    if ($LASTEXITCODE -ge 8) { throw ("Robocopy failed: " + $LASTEXITCODE) }
}

function Test-DirectoryHasContent {
    param([string]$Path)
    if (-not (Test-Path -LiteralPath $Path)) { return $false }
    return $null -ne (Get-ChildItem -LiteralPath $Path -Force -ErrorAction SilentlyContinue | Select-Object -First 1)
}

function Resolve-PackageLayout {
    param([string]$PackageRoot)
    $api = Join-Path $PackageRoot "api"
    $web = Join-Path $PackageRoot "web"
    if ((Test-Path $api) -and (Test-Path $web)) { return [pscustomobject]@{ Api=$api; Web=$web; Layout="direct" } }
    $api = Join-Path $PackageRoot "publish\api"
    $web = Join-Path $PackageRoot "publish\web"
    if ((Test-Path $api) -and (Test-Path $web)) { return [pscustomobject]@{ Api=$api; Web=$web; Layout="legacy-publish" } }
    throw "Package must contain api/web or publish/api and publish/web."
}

function Assert-FrontendReferences {
    param([string]$WebRoot, [string]$ExpectedApiBaseUrl)
    if (Get-ChildItem $WebRoot -Recurse -File | Select-String -Pattern "localhost:5000|127\.0\.0\.1:5000") {
        throw "Frontend contains a forbidden local API reference."
    }
    if (-not (Get-ChildItem $WebRoot -Recurse -File | Select-String -Pattern ([regex]::Escape($ExpectedApiBaseUrl)))) {
        throw "Frontend does not contain the production API URL."
    }
}

if (-not (Test-IsAdministrator)) { throw "Run as Administrator." }

$sourceRoot = (Resolve-Path -LiteralPath $SourcePackagePath).Path
$layout = Resolve-PackageLayout $sourceRoot
$apiSource = $layout.Api
$webSource = $layout.Web
$publishRoot = Join-Path $InstallRoot "publish"
$apiTarget = Join-Path $publishRoot "api"
$webTarget = Join-Path $publishRoot "web"
$logsTarget = Join-Path $InstallRoot "logs"
$backupRoot = Join-Path $InstallRoot "backup"
$configRoot = Join-Path $InstallRoot "config"
$settingsTarget = Join-Path $apiTarget "appsettings.Production.json"
if ([string]::IsNullOrWhiteSpace($ProductionSettingsPath)) { $ProductionSettingsPath = Join-Path $configRoot "appsettings.Production.json" }

foreach ($path in @((Join-Path $apiSource "Uqeb.Api.dll"),(Join-Path $apiSource "Uqeb.Api.runtimeconfig.json"),(Join-Path $webSource "index.html"),(Join-Path $webSource "assets"))) {
    if (-not (Test-Path $path)) { throw ("Missing package path: " + $path) }
}
Assert-FrontendReferences $webSource $FrontendApiBaseUrl

foreach ($path in @($InstallRoot,$publishRoot,$apiTarget,$webTarget,$logsTarget,$backupRoot,$configRoot)) { Ensure-Directory $path }

if (-not (Get-ScheduledTask -TaskName $ScheduledTaskName -ErrorAction SilentlyContinue)) { throw "UqebApi task must be provisioned first." }
if (Test-Path $settingsTarget) { $settingsSource = $settingsTarget; $firstDeployment = $false }
elseif (Test-Path $ProductionSettingsPath) { $settingsSource = $ProductionSettingsPath; $firstDeployment = $true }
else { throw "Approved production settings are missing." }

$settings = Get-Content $settingsSource -Raw | ConvertFrom-Json
if ([string]::IsNullOrWhiteSpace($settings.ConnectionStrings.DefaultConnection)) { throw "DefaultConnection is missing." }
if ([string]::IsNullOrWhiteSpace($settings.Jwt.Key) -or $settings.Jwt.Key.Length -lt 32) { throw "Jwt:Key is invalid." }
if ($settings.AllowedOrigins -notcontains $FrontendOrigin) { throw "AllowedOrigins is missing the production UI origin." }

$stamp = Get-Date -Format "yyyyMMdd-HHmmss"
$backup = Join-Path $backupRoot ("before-" + $stamp)
$backupApi = Join-Path $backup "api"
$backupWeb = Join-Path $backup "web"
$backupSettings = Join-Path $backup "appsettings.Production.json"
Ensure-Directory $backupApi
Ensure-Directory $backupWeb
if (Test-DirectoryHasContent $apiTarget) { Invoke-RobocopyOrThrow $apiTarget $backupApi }
if (Test-DirectoryHasContent $webTarget) { Invoke-RobocopyOrThrow $webTarget $backupWeb }
Copy-Item $settingsSource $backupSettings -Force
if ($firstDeployment) { Set-Content (Join-Path $backup "FIRST_DEPLOYMENT.txt") "No previous deployment existed." }

Stop-ScheduledTask -TaskName $ScheduledTaskName -ErrorAction SilentlyContinue
Start-Sleep -Seconds 2
$listeners = Get-NetTCPConnection -LocalPort $ApiPort -State Listen -ErrorAction SilentlyContinue
foreach ($listener in $listeners) {
    $process = Get-Process -Id $listener.OwningProcess -ErrorAction SilentlyContinue
    if ($process -and $process.ProcessName -ne "Uqeb.Api" -and $process.ProcessName -ne "dotnet") { throw "Unexpected process owns API port." }
    if ($process) { Stop-Process -Id $process.Id -Force }
}
Start-Sleep -Seconds 3
if (Get-NetTCPConnection -LocalPort $ApiPort -State Listen -ErrorAction SilentlyContinue) { throw "API port is still busy." }

Invoke-RobocopyOrThrow $apiSource $apiTarget @("/XF","appsettings.json","appsettings.Development.json","appsettings.Production.json")
Copy-Item $backupSettings $settingsTarget -Force

Get-ChildItem $webTarget -Force -ErrorAction SilentlyContinue | Remove-Item -Recurse -Force
Invoke-RobocopyOrThrow $webSource $webTarget @("/PURGE")
Assert-FrontendReferences $webTarget $FrontendApiBaseUrl

$apiExecutable = Join-Path $apiTarget "Uqeb.Api.exe"
$apiDll = Join-Path $apiTarget "Uqeb.Api.dll"
$apiLog = Join-Path $logsTarget "api-runtime.log"
$launchCommand = if (Test-Path $apiExecutable) { '"'+$apiExecutable+'"' } else { 'dotnet "'+$apiDll+'"' }
$runApiPath = Join-Path $InstallRoot "run-api.cmd"
$runApi = "@echo off`r`ncd /d `"$apiTarget`"`r`nset ASPNETCORE_ENVIRONMENT=Production`r`nset DOTNET_ENVIRONMENT=Production`r`nset ASPNETCORE_URLS=http://$ApiBindAddress`:$ApiPort`r`n$launchCommand >> `"$apiLog`" 2>&1`r`n"
Set-Content $runApiPath $runApi -Encoding ASCII
$action = New-ScheduledTaskAction -Execute $env:ComSpec -Argument ('/c "'+$runApiPath+'"') -WorkingDirectory $apiTarget
Set-ScheduledTask -TaskName $ScheduledTaskName -Action $action | Out-Null
Start-ScheduledTask -TaskName $ScheduledTaskName

$deadline = (Get-Date).AddSeconds(30)
do {
    $listener = Get-NetTCPConnection -LocalPort $ApiPort -State Listen -ErrorAction SilentlyContinue
    if ($listener) { break }
    Start-Sleep -Seconds 2
} while ((Get-Date) -lt $deadline)
if (-not $listener) { throw ("API did not start. Review " + $apiLog) }
if (-not ($listener | Where-Object { $_.LocalAddress -in @($ApiBindAddress,"0.0.0.0","::") })) { throw "API binding is not LAN-capable." }

try {
    $healthScript = Join-Path $PSScriptRoot "verify-deployment-health.ps1"
    $baseUrl = "http://localhost:$ApiPort"

    if (-not (Test-Path -LiteralPath $healthScript)) {
        throw "Health verification script not found: $healthScript"
    }

    & $healthScript `
        -ApiBaseUrl $baseUrl `
        -TimeoutSec 20 `
        -RetryCount 5 `
        -RetryDelaySec 2

    Write-Info "Post-deploy health verification passed."
}
catch {
    throw (
        "Post-deploy health verification failed. Review " +
        $apiLog +
        ". Details: " +
        $_.Exception.Message
    )
}

$buildInfo = "DeployedAt=$(Get-Date -Format s)`r`nApiTarget=$apiTarget`r`nWebTarget=$webTarget`r`nApiBinding=http://$ApiBindAddress`:$ApiPort`r`nBackup=$backup"
Set-Content (Join-Path $InstallRoot "BUILD_INFO.txt") $buildInfo -Encoding UTF8
Write-Step "Deployment completed successfully"
Write-Output ("Backup: " + $backup)
