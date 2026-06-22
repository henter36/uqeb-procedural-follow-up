#Requires -Version 5.1
<#
.SYNOPSIS
  Deploy a pre-built Uqeb package to the canonical production layout.

.DESCRIPTION
  Accepts either of these extracted package layouts:

    <package>\api and <package>\web
    <package>\publish\api and <package>\publish\web (legacy)

  Deploys to:

    C:\Uqeb\publish\api
    C:\Uqeb\publish\web

  Production settings must already exist in the deployed API folder or be
  provisioned separately under C:\Uqeb\config. Package settings are never used
  as an automatic source of production secrets.

  The UqebApi scheduled task must be provisioned before running this script.
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

function Write-Step {
    param([string]$Message)
    Write-Output ""
    Write-Output ("==> " + $Message)
}

function Write-Info {
    param([string]$Message)
    Write-Output ("[INFO] " + $Message)
}

function Test-IsAdministrator {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($identity)
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Ensure-Directory {
    param([string]$Path)

    if (-not (Test-Path -LiteralPath $Path)) {
        New-Item -ItemType Directory -Path $Path -Force | Out-Null
        Write-Info ("Created directory: " + $Path)
    }
}

function Invoke-RobocopyOrThrow {
    param(
        [string]$Source,
        [string]$Destination,
        [string[]]$ExtraArguments = @()
    )

    Ensure-Directory $Destination

    $arguments = @(
        $Source,
        $Destination,
        "/E",
        "/R:2",
        "/W:2"
    ) + $ExtraArguments

    & robocopy @arguments
    $exitCode = $LASTEXITCODE

    if ($exitCode -ge 8) {
        throw ("Robocopy failed with exit code " + $exitCode + ": " + $Source + " -> " + $Destination)
    }
}

function Test-DirectoryHasContent {
    param([string]$Path)

    if (-not (Test-Path -LiteralPath $Path)) {
        return $false
    }

    return $null -ne (Get-ChildItem -LiteralPath $Path -Force -ErrorAction SilentlyContinue | Select-Object -First 1)
}

function Resolve-PackageLayout {
    param([string]$PackageRoot)

    $directApi = Join-Path $PackageRoot "api"
    $directWeb = Join-Path $PackageRoot "web"

    if ((Test-Path -LiteralPath $directApi) -and (Test-Path -LiteralPath $directWeb)) {
        return [pscustomobject]@{
            Api = $directApi
            Web = $directWeb
            Layout = "direct"
        }
    }

    $legacyApi = Join-Path $PackageRoot "publish\api"
    $legacyWeb = Join-Path $PackageRoot "publish\web"

    if ((Test-Path -LiteralPath $legacyApi) -and (Test-Path -LiteralPath $legacyWeb)) {
        return [pscustomobject]@{
            Api = $legacyApi
            Web = $legacyWeb
            Layout = "legacy-publish"
        }
    }

    throw "Package must contain api/web or publish/api and publish/web."
}

function Get-ListenerProcess {
    param([int]$Port)

    $listener = Get-NetTCPConnection -LocalPort $Port -State Listen -ErrorAction SilentlyContinue |
        Select-Object -First 1

    if (-not $listener) {
        return $null
    }

    return Get-Process -Id $listener.OwningProcess -ErrorAction SilentlyContinue
}

function Stop-UqebApi {
    param(
        [int]$Port,
        [string]$TaskName
    )

    Write-Step "Stopping existing API"

    $task = Get-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue
    if (-not $task) {
        throw ("Scheduled task must be provisioned before deployment: " + $TaskName)
    }

    Stop-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue
    Start-Sleep -Seconds 2

    $process = Get-ListenerProcess -Port $Port
    if ($process) {
        if ($process.ProcessName -ne "Uqeb.Api" -and $process.ProcessName -ne "dotnet") {
            throw ("Port " + $Port + " is owned by " + $process.ProcessName + " (PID " + $process.Id + ").")
        }

        Write-Info ("Stopping " + $process.ProcessName + " PID " + $process.Id)
        Stop-Process -Id $process.Id -Force
    }

    Start-Sleep -Seconds 3

    if (Get-NetTCPConnection -LocalPort $Port -State Listen -ErrorAction SilentlyContinue) {
        throw ("API port is still listening after stop: " + $Port)
    }
}

function Get-SpaWebConfigContent {
    return @'
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <system.webServer>
    <defaultDocument enabled="true">
      <files>
        <clear />
        <add value="index.html" />
      </files>
    </defaultDocument>
    <httpErrors errorMode="Custom" existingResponse="Replace">
      <remove statusCode="404" subStatusCode="-1" />
      <error statusCode="404" path="/index.html" responseMode="ExecuteURL" />
    </httpErrors>
  </system.webServer>
</configuration>
'@
}

function Assert-FrontendReferences {
    param(
        [string]$WebRoot,
        [string]$ExpectedApiBaseUrl
    )

    $forbidden = Get-ChildItem -LiteralPath $WebRoot -Recurse -File |
        Select-String -Pattern "localhost:5000|127\.0\.0\.1:5000" -AllMatches

    if ($forbidden) {
        throw "Frontend contains a forbidden localhost API reference."
    }

    $expected = Get-ChildItem -LiteralPath $WebRoot -Recurse -File |
        Select-String -Pattern ([regex]::Escape($ExpectedApiBaseUrl)) -AllMatches

    if (-not $expected) {
        throw ("Frontend does not contain the expected API base URL: " + $ExpectedApiBaseUrl)
    }
}

if (-not (Test-IsAdministrator)) {
    throw "Run deploy-production-v2.ps1 from an elevated PowerShell session."
}

$sourceRoot = (Resolve-Path -LiteralPath $SourcePackagePath).Path
$layout = Resolve-PackageLayout -PackageRoot $sourceRoot
$apiSource = $layout.Api
$webSource = $layout.Web

$publishRoot = Join-Path $InstallRoot "publish"
$apiTarget = Join-Path $publishRoot "api"
$webTarget = Join-Path $publishRoot "web"
$logsTarget = Join-Path $InstallRoot "logs"
$backupRoot = Join-Path $InstallRoot "backup"
$configRoot = Join-Path $InstallRoot "config"
$productionSettingsTarget = Join-Path $apiTarget "appsettings.Production.json"

if ([string]::IsNullOrWhiteSpace($ProductionSettingsPath)) {
    $ProductionSettingsPath = Join-Path $configRoot "appsettings.Production.json"
}

$requiredPackagePaths = @(
    (Join-Path $apiSource "Uqeb.Api.dll"),
    (Join-Path $apiSource "Uqeb.Api.runtimeconfig.json"),
    (Join-Path $webSource "index.html"),
    (Join-Path $webSource "assets")
)

foreach ($requiredPath in $requiredPackagePaths) {
    if (-not (Test-Path -LiteralPath $requiredPath)) {
        throw ("Missing required package path: " + $requiredPath)
    }
}

Assert-FrontendReferences -WebRoot $webSource -ExpectedApiBaseUrl $FrontendApiBaseUrl

Ensure-Directory $InstallRoot
Ensure-Directory $publishRoot
Ensure-Directory $apiTarget
Ensure-Directory $webTarget
Ensure-Directory $logsTarget
Ensure-Directory $backupRoot
Ensure-Directory $configRoot

if (-not (Get-ScheduledTask -TaskName $ScheduledTaskName -ErrorAction SilentlyContinue)) {
    throw ("Scheduled task must be provisioned before deployment: " + $ScheduledTaskName)
}

$firstDeployment = $false
if (Test-Path -LiteralPath $productionSettingsTarget) {
    $settingsSource = $productionSettingsTarget
}
elseif (Test-Path -LiteralPath $ProductionSettingsPath) {
    $settingsSource = $ProductionSettingsPath
    $firstDeployment = $true
}
else {
    throw "No approved production settings found."
}

$settings = Get-Content -LiteralPath $settingsSource -Raw | ConvertFrom-Json
if ([string]::IsNullOrWhiteSpace($settings.ConnectionStrings.DefaultConnection)) {
    throw "Production settings are missing DefaultConnection."
}
if ($settings.AllowedOrigins -notcontains $FrontendOrigin) {
    throw ("AllowedOrigins must include " + $FrontendOrigin)
}

$stamp = Get-Date -Format "yyyyMMdd-HHmmss"
$backup = Join-Path $backupRoot ("before-" + $stamp)
$backupApi = Join-Path $backup "api"
$backupWeb = Join-Path $backup "web"
$backupSettings = Join-Path $backup "appsettings.Production.json"

Ensure-Directory $backupApi
Ensure-Directory $backupWeb

if (Test-DirectoryHasContent -Path $apiTarget) {
    Invoke-RobocopyOrThrow -Source $apiTarget -Destination $backupApi
}
else {
    Write-Info "First deployment: no existing API files to back up."
}

if (Test-DirectoryHasContent -Path $webTarget) {
    Invoke-RobocopyOrThrow -Source $webTarget -Destination $backupWeb
}
else {
    Write-Info "First deployment: no existing web files to back up."
}

Copy-Item -LiteralPath $settingsSource -Destination $backupSettings -Force
if ($firstDeployment) {
    Set-Content -LiteralPath (Join-Path $backup "FIRST_DEPLOYMENT.txt") -Value "No previous deployment existed." -Encoding UTF8
}

Stop-UqebApi -Port $ApiPort -TaskName $ScheduledTaskName

Invoke-RobocopyOrThrow `
    -Source $apiSource `
    -Destination $apiTarget `
    -ExtraArguments @("/XF", "appsettings.json", "appsettings.Development.json", "appsettings.Production.json")

Copy-Item -LiteralPath $backupSettings -Destination $productionSettingsTarget -Force

Get-ChildItem -LiteralPath $webTarget -Force -ErrorAction SilentlyContinue |
    Remove-Item -Recurse -Force
Invoke-RobocopyOrThrow -Source $webSource -Destination $webTarget -ExtraArguments @("/PURGE")
Set-Content -LiteralPath (Join-Path $webTarget "web.config") -Value (Get-SpaWebConfigContent) -Encoding UTF8
Assert-FrontendReferences -WebRoot $webTarget -ExpectedApiBaseUrl $FrontendApiBaseUrl

$apiExecutable = Join-Path $apiTarget "Uqeb.Api.exe"
$apiDll = Join-Path $apiTarget "Uqeb.Api.dll"
$apiLog = Join-Path $logsTarget "api-runtime.log"
$launchCommand = if (Test-Path -LiteralPath $apiExecutable) { '"' + $apiExecutable + '"' } else { 'dotnet "' + $apiDll + '"' }
$runApiPath = Join-Path $InstallRoot "run-api.cmd"

$runApiContent = @"
@echo off
cd /d "$apiTarget"
set ASPNETCORE_ENVIRONMENT=Production
set DOTNET_ENVIRONMENT=Production
set ASPNETCORE_URLS=http://$ApiBindAddress`:$ApiPort
$launchCommand >> "$apiLog" 2>&1
"@
Set-Content -LiteralPath $runApiPath -Value $runApiContent -Encoding ASCII

$taskAction = New-ScheduledTaskAction -Execute $env:ComSpec -Argument ('/c "' + $runApiPath + '"') -WorkingDirectory $apiTarget
Set-ScheduledTask -TaskName $ScheduledTaskName -Action $taskAction | Out-Null
Start-ScheduledTask -TaskName $ScheduledTaskName

$deadline = (Get-Date).AddSeconds(30)
$listener = $null
while ((Get-Date) -lt $deadline) {
    $listener = Get-NetTCPConnection -LocalPort $ApiPort -State Listen -ErrorAction SilentlyContinue
    if ($listener) { break }
    Start-Sleep -Seconds 2
}

if (-not $listener) {
    throw ("API did not start. Review " + $apiLog)
}

$validBinding = $listener | Where-Object {
    $_.LocalAddress -eq $ApiBindAddress -or $_.LocalAddress -eq "0.0.0.0" -or $_.LocalAddress -eq "::"
}
if (-not $validBinding) {
    throw "API is not listening on a LAN-capable address."
}

$buildInfo = @"
Uqeb Production Deployment
DeployedAtLocal: $(Get-Date -Format "yyyy-MM-dd HH:mm:ss")
SourcePackage: $sourceRoot
PackageLayout: $($layout.Layout)
ApiTarget: $apiTarget
WebTarget: $webTarget
ApiBinding: http://$ApiBindAddress`:$ApiPort
Backup: $backup
"@
Set-Content -LiteralPath (Join-Path $InstallRoot "BUILD_INFO.txt") -Value $buildInfo -Encoding UTF8

Write-Step "Deployment completed successfully"
Write-Output ("Backup: " + $backup)
Write-Output ("UI: " + $FrontendOrigin)
