#Requires -Version 5.1
<#
.SYNOPSIS
  Deploy a pre-built Uqeb package to a production install root.

.PARAMETER SourcePackagePath
  Folder containing publish\api and publish\web from the build machine.

.PARAMETER InstallRoot
  Target install root (default C:\Uqeb).
#>

param(
    [Parameter(Mandatory = $true)]
    [string]$SourcePackagePath,

    [string]$InstallRoot = "C:\Uqeb",

    [string]$ScheduledTaskName = "UqebApi",

    [int]$ApiPort = 5000
)

$ErrorActionPreference = "Stop"

function Write-Step {
    param([string]$Message)
    Write-Output ""
    Write-Output ("==> " + $Message)
}

function Write-Info {
    param([string]$Message)
    Write-Output ("[INFO] " + $Message)
}

function Write-WarnMsg {
    param([string]$Message)
    Write-Warning $Message
}

function Get-SpaWebConfigContent {
    @'
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
      <remove statusCode="404" />
      <error statusCode="404" path="/index.html" responseMode="ExecuteURL" />
    </httpErrors>
  </system.webServer>
  <location path="index.html">
    <system.webServer>
      <staticContent>
        <clientCache cacheControlMode="DisableCache" />
      </staticContent>
      <httpProtocol>
        <customHeaders>
          <add name="Cache-Control" value="no-cache, no-store, must-revalidate" />
          <add name="Pragma" value="no-cache" />
          <add name="Expires" value="0" />
        </customHeaders>
      </httpProtocol>
    </system.webServer>
  </location>
</configuration>
'@
}

function Ensure-Directory {
    param([string]$Path)

    if (-not (Test-Path -LiteralPath $Path)) {
        New-Item -ItemType Directory -Force -Path $Path | Out-Null
        Write-Info ("Created directory: " + $Path)
    }
}

function Stop-UqebApi {
    param(
        [int]$Port,
        [string]$TaskName
    )

    Write-Step "Stopping existing Uqeb API if running"

    $task = Get-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue
    $hadTask = $null -ne $task
    if ($hadTask) {
        try {
            Stop-ScheduledTask -TaskName $TaskName -ErrorAction Stop
            Write-Info ("Stopped scheduled task: " + $TaskName)
        } catch {
            Write-WarnMsg ("Could not stop scheduled task " + $TaskName + ": " + $_.Exception.Message)
        }
    }

    try {
        $apiProcesses = Get-Process -Name "Uqeb.Api" -ErrorAction SilentlyContinue
        foreach ($proc in $apiProcesses) {
            Write-Info ("Stopping process Uqeb.Api PID " + $proc.Id)
            Stop-Process -Id $proc.Id -Force
        }
    } catch {
        Write-WarnMsg ("Could not stop Uqeb.Api process: " + $_.Exception.Message)
    }

    try {
        $connections = Get-NetTCPConnection -LocalPort $Port -ErrorAction SilentlyContinue
        foreach ($conn in $connections) {
            $owningProcessId = $conn.OwningProcess
            if (-not $owningProcessId -or $owningProcessId -le 0) { continue }

            $process = Get-Process -Id $owningProcessId -ErrorAction SilentlyContinue
            if ($null -eq $process) { continue }

            if ($process.ProcessName -eq "Uqeb.Api" -or $process.ProcessName -eq "dotnet") {
                Write-Info ("Stopping process on port " + $Port + ": " + $process.ProcessName + " PID " + $process.Id)
                Stop-Process -Id $process.Id -Force
            } else {
                Write-WarnMsg ("Port " + $Port + " is used by " + $process.ProcessName + " PID " + $process.Id + ". Not stopping automatically.")
            }
        }
    } catch {
        Write-WarnMsg ("Could not inspect port " + $Port + ": " + $_.Exception.Message)
    }

    Start-Sleep -Seconds 2
    return $hadTask
}

function Backup-DirectoryOrThrow {
    param(
        [string]$Path,
        [string]$BackupRoot,
        [string]$Name
    )

    if (-not (Test-Path -LiteralPath $Path)) {
        Write-Info ("No existing folder to back up: " + $Path)
        return $null
    }

    $timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
    $destination = Join-Path $BackupRoot ($Name + "-" + $timestamp)

    try {
        Copy-Item -LiteralPath $Path -Destination $destination -Recurse -Force -ErrorAction Stop
    } catch {
        throw ("Backup failed for " + $Path + " -> " + $destination + ": " + $_.Exception.Message)
    }

    if (-not (Test-Path -LiteralPath $destination)) {
        throw ("Backup destination was not created: " + $destination)
    }

    Write-Info ("Backup created: " + $destination)
    return $destination
}

function Copy-DirectoryContents {
    param(
        [string]$Source,
        [string]$Destination,
        [string[]]$ExcludeFileNames = @()
    )

    Ensure-Directory $Destination

    Get-ChildItem -LiteralPath $Source -Force | ForEach-Object {
        if ($ExcludeFileNames -contains $_.Name) {
            Write-Info ("Skipped package file (preserved locally if present): " + $_.Name)
            return
        }

        $targetPath = Join-Path $Destination $_.Name

        if ($_.PSIsContainer) {
            if (Test-Path -LiteralPath $targetPath) {
                Remove-Item -LiteralPath $targetPath -Recurse -Force
            }
            Copy-Item -LiteralPath $_.FullName -Destination $targetPath -Recurse -Force
        } else {
            Copy-Item -LiteralPath $_.FullName -Destination $targetPath -Force
        }
    }
}

Write-Step "Validating source package"

$sourceRoot = (Resolve-Path -LiteralPath $SourcePackagePath).Path
$apiSource = Join-Path $sourceRoot "publish\api"
$webSource = Join-Path $sourceRoot "publish\web"

if (-not (Test-Path -LiteralPath $apiSource)) {
    throw ("Missing API package folder: " + $apiSource)
}

if (-not (Test-Path -LiteralPath $webSource)) {
    throw ("Missing web package folder: " + $webSource)
}

$apiDll = Join-Path $apiSource "Uqeb.Api.dll"
$apiRuntimeConfig = Join-Path $apiSource "Uqeb.Api.runtimeconfig.json"
$webIndex = Join-Path $webSource "index.html"
$webAssets = Join-Path $webSource "assets"

if (-not (Test-Path -LiteralPath $apiDll)) {
    throw "API package must contain Uqeb.Api.dll"
}

if (-not (Test-Path -LiteralPath $apiRuntimeConfig)) {
    throw "API package must contain Uqeb.Api.runtimeconfig.json"
}

if (-not (Test-Path -LiteralPath $webIndex)) {
    throw "Web package must contain index.html"
}

if (-not (Test-Path -LiteralPath $webAssets)) {
    throw "Web package must contain assets folder"
}

Write-Info ("Source package: " + $sourceRoot)

$apiTarget = Join-Path $InstallRoot "api"
$webTarget = Join-Path $InstallRoot "web"
$logsTarget = Join-Path $InstallRoot "logs"
$uploadsTarget = Join-Path $InstallRoot "uploads"
$backupTarget = Join-Path $InstallRoot "backup"
$productionSettings = Join-Path $apiTarget "appsettings.Production.json"
$existingProductionSettings = $null

if (Test-Path -LiteralPath $productionSettings) {
    $existingProductionSettings = $true
}

Write-Step "Creating target folders"

Ensure-Directory $InstallRoot
Ensure-Directory $apiTarget
Ensure-Directory $webTarget
Ensure-Directory $logsTarget
Ensure-Directory $uploadsTarget
Ensure-Directory $backupTarget

$restartTask = Stop-UqebApi -Port $ApiPort -TaskName $ScheduledTaskName

Write-Step "Backing up current deployment (abort on failure)"

$null = Backup-DirectoryOrThrow -Path $apiTarget -BackupRoot $backupTarget -Name "api"
$null = Backup-DirectoryOrThrow -Path $webTarget -BackupRoot $backupTarget -Name "web"

$excludeFromPackage = @()
if (Test-Path -LiteralPath $productionSettings) {
    $excludeFromPackage = @("appsettings.Production.json")
    Write-Info "Existing appsettings.Production.json will not be replaced from package"
}

Write-Step "Deploying API"

Copy-DirectoryContents -Source $apiSource -Destination $apiTarget -ExcludeFileNames $excludeFromPackage

if (-not (Test-Path -LiteralPath $productionSettings)) {
    $packageProductionSettings = Join-Path $apiSource "appsettings.Production.json"
    if (Test-Path -LiteralPath $packageProductionSettings) {
        Copy-Item -LiteralPath $packageProductionSettings -Destination $productionSettings -Force
        Write-Info "Copied appsettings.Production.json from package (first deploy only)"
    } else {
        Write-WarnMsg "appsettings.Production.json is missing. Create it before running the API in production."
    }
} else {
    Write-Info "Preserved existing appsettings.Production.json"
}

Write-Step "Deploying web"

Copy-DirectoryContents -Source $webSource -Destination $webTarget

$webConfigPath = Join-Path $webTarget "web.config"
Set-Content -LiteralPath $webConfigPath -Value (Get-SpaWebConfigContent) -Encoding UTF8
Write-Info ("Wrote SPA web.config: " + $webConfigPath)

$requiredAfterDeploy = @(
    (Join-Path $apiTarget "Uqeb.Api.dll"),
    (Join-Path $apiTarget "Uqeb.Api.runtimeconfig.json"),
    (Join-Path $webTarget "index.html"),
    (Join-Path $webTarget "assets")
)

$missing = @()
foreach ($item in $requiredAfterDeploy) {
    if (-not (Test-Path -LiteralPath $item)) {
        $missing += $item
    }
}

if ($missing.Count -gt 0) {
    throw ("Deployment verification failed. Missing:`n - " + ($missing -join "`n - "))
}

$buildInfoPath = Join-Path $InstallRoot "BUILD_INFO.txt"
$buildInfo = @"
Uqeb Production Deployment
==========================
DeployedAtUtc: $((Get-Date).ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss")) UTC
DeployedAtLocal: $(Get-Date -Format "yyyy-MM-dd HH:mm:ss")
SourcePackage: $sourceRoot
InstallRoot: $InstallRoot
ScheduledTask: $ScheduledTaskName
ApiPort: $ApiPort
TargetFramework: net10.0
"@
Set-Content -LiteralPath $buildInfoPath -Value $buildInfo -Encoding UTF8
Write-Info ("Wrote BUILD_INFO.txt: " + $buildInfoPath)

$startApiScript = Join-Path $InstallRoot "start-api.ps1"
$startApiContent = @"
`$ErrorActionPreference = "Stop"
Set-Location "$apiTarget"
`$env:ASPNETCORE_ENVIRONMENT = "Production"
`$env:ASPNETCORE_URLS = "http://localhost:$ApiPort"
dotnet .\Uqeb.Api.dll *>> "$logsTarget\api.log"
"@
Set-Content -LiteralPath $startApiScript -Value $startApiContent -Encoding UTF8
Write-Info ("Startup script: " + $startApiScript)

if ($restartTask) {
    Write-Step "Restarting scheduled task"
    try {
        Start-ScheduledTask -TaskName $ScheduledTaskName -ErrorAction Stop
        Write-Info ("Started scheduled task: " + $ScheduledTaskName)
    } catch {
        Write-WarnMsg ("Could not start scheduled task " + $ScheduledTaskName + ": " + $_.Exception.Message)
        Write-WarnMsg ("Start API manually: powershell -ExecutionPolicy Bypass -File " + $startApiScript)
    }
}

Write-Step "Deployment completed"
Write-Output ""
Write-Output "Next checks:"
Write-Output "1. Verify appsettings.Production.json AllowedOrigins includes the IIS site URL"
Write-Output "2. Start API if needed:"
Write-Output ("   powershell -ExecutionPolicy Bypass -File " + $startApiScript)
Write-Output ("3. Test login: POST http://localhost:" + $ApiPort + "/api/auth/login")
Write-Output "4. Point IIS website physical path to:"
Write-Output ("   " + $webTarget)
Write-Output "5. IIS serves static UI only. API runs on Kestrel port 5000."
Write-Output "6. /api/health and /swagger are not guaranteed in Production unless explicitly enabled."
