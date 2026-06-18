#Requires -Version 5.1
<#
.SYNOPSIS
  Deploy a pre-built Uqeb package to a production install root.

.PARAMETER SourcePackagePath
  Folder containing publish\api and publish\web from the build machine.

.PARAMETER InstallRoot
  Target install root (default C:\Uqeb).

.PARAMETER ApiPort
  Kestrel listen port (default 5000).
#>

param(
    [Parameter(Mandatory = $true)]
    [string]$SourcePackagePath,

    [string]$InstallRoot = "C:\Uqeb",

    [string]$ScheduledTaskName = "UqebApi",

    [int]$ApiPort = 5000
)

$ErrorActionPreference = "Stop"

function Test-IsAdministrator {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($identity)
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

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

function Get-TargetFrameworkFromRuntimeConfig {
    param([string]$RuntimeConfigPath)

    if (-not (Test-Path -LiteralPath $RuntimeConfigPath)) {
        return "net10.0"
    }

    try {
        $config = Get-Content -LiteralPath $RuntimeConfigPath -Raw | ConvertFrom-Json
        if ($config.runtimeOptions.tfm) {
            return [string]$config.runtimeOptions.tfm
        }
    } catch {
        Write-WarnMsg ("Could not read runtimeconfig.json: " + $_.Exception.Message)
    }

    return "net10.0"
}

function Ensure-Directory {
    param([string]$Path)

    if (-not (Test-Path -LiteralPath $Path)) {
        New-Item -ItemType Directory -Force -Path $Path | Out-Null
        Write-Info ("Created directory: " + $Path)
    }
}

function Test-IsUqebApiProcess {
    param(
        [System.Diagnostics.Process]$Process,
        [string]$ApiDirectory
    )

    if ($Process.ProcessName -eq "Uqeb.Api") {
        return $true
    }

    if ($Process.ProcessName -ne "dotnet") {
        return $false
    }

    try {
        $commandLine = (Get-CimInstance Win32_Process -Filter ("ProcessId=" + $Process.Id)).CommandLine
        if ([string]::IsNullOrWhiteSpace($commandLine)) {
            return $false
        }

        $normalizedApiDir = $ApiDirectory.TrimEnd('\')
        if ($commandLine -like ("*" + $normalizedApiDir + "*")) {
            return $true
        }

        if ($commandLine -like "*Uqeb.Api.dll*") {
            return $true
        }
    } catch {
        Write-WarnMsg ("Could not inspect dotnet command line for PID " + $Process.Id + ": " + $_.Exception.Message)
    }

    return $false
}

function Stop-UqebApi {
    param(
        [int]$Port,
        [string]$TaskName,
        [string]$ApiDirectory
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
        $dotnetProcesses = Get-Process -Name "dotnet" -ErrorAction SilentlyContinue
        foreach ($proc in $dotnetProcesses) {
            if (Test-IsUqebApiProcess -Process $proc -ApiDirectory $ApiDirectory) {
                Write-Info ("Stopping dotnet hosting Uqeb PID " + $proc.Id)
                Stop-Process -Id $proc.Id -Force
            }
        }
    } catch {
        Write-WarnMsg ("Could not stop dotnet processes: " + $_.Exception.Message)
    }

    Start-Sleep -Seconds 2

    $listeners = Get-NetTCPConnection -LocalPort $Port -State Listen -ErrorAction SilentlyContinue
    foreach ($conn in $listeners) {
        $owningProcessId = $conn.OwningProcess
        if (-not $owningProcessId -or $owningProcessId -le 0) { continue }

        $process = Get-Process -Id $owningProcessId -ErrorAction SilentlyContinue
        if ($null -eq $process) { continue }

        if (Test-IsUqebApiProcess -Process $process -ApiDirectory $ApiDirectory) {
            try {
                Write-Info ("Stopping remaining listener on port " + $Port + ": " + $process.ProcessName + " PID " + $process.Id)
                Stop-Process -Id $process.Id -Force
            } catch {
                throw ("Failed to stop Uqeb API process on port " + $Port + " (PID " + $process.Id + "): " + $_.Exception.Message)
            }
            continue
        }

        throw ("Port " + $Port + " is in use by " + $process.ProcessName + " (PID " + $process.Id + "). Stop it manually before deployment.")
    }

    Start-Sleep -Seconds 1
    $stillListening = Get-NetTCPConnection -LocalPort $Port -State Listen -ErrorAction SilentlyContinue
    if ($stillListening) {
        throw ("Port " + $Port + " is still in use after stop attempts. Stop the blocking process manually before deployment.")
    }

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

function Copy-TreeSafe {
    param(
        [string]$Source,
        [string]$Destination,
        [string[]]$ExcludeFileNames = @()
    )

    Ensure-Directory $Destination

    foreach ($item in Get-ChildItem -LiteralPath $Source -Force) {
        if ($ExcludeFileNames -contains $item.Name) {
            Write-Info ("Skipped package file (preserved locally if present): " + $item.Name)
            continue
        }

        $targetPath = Join-Path $Destination $item.Name

        if ($item.PSIsContainer) {
            if (Test-Path -LiteralPath $targetPath) {
                Remove-Item -LiteralPath $targetPath -Recurse -Force
            }
            Copy-Item -LiteralPath $item.FullName -Destination $targetPath -Recurse -Force
        } else {
            Copy-Item -LiteralPath $item.FullName -Destination $targetPath -Force
        }
    }
}

if (-not (Test-IsAdministrator)) {
    throw "deploy-production.ps1 must run as Administrator. Open PowerShell as Administrator and retry."
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

$targetFramework = Get-TargetFrameworkFromRuntimeConfig -RuntimeConfigPath $apiRuntimeConfig
Write-Info ("Source package: " + $sourceRoot)
Write-Info ("Package target framework: " + $targetFramework)

$apiTarget = Join-Path $InstallRoot "api"
$webTarget = Join-Path $InstallRoot "web"
$logsTarget = Join-Path $InstallRoot "logs"
$uploadsTarget = Join-Path $InstallRoot "uploads"
$backupTarget = Join-Path $InstallRoot "backup"
$productionSettings = Join-Path $apiTarget "appsettings.Production.json"

Write-Step "Creating target folders"

Ensure-Directory $InstallRoot
Ensure-Directory $apiTarget
Ensure-Directory $webTarget
Ensure-Directory $logsTarget
Ensure-Directory $uploadsTarget
Ensure-Directory $backupTarget

$restartTask = Stop-UqebApi -Port $ApiPort -TaskName $ScheduledTaskName -ApiDirectory $apiTarget

Write-Step "Backing up current deployment (abort on failure)"

$null = Backup-DirectoryOrThrow -Path $apiTarget -BackupRoot $backupTarget -Name "api"
$null = Backup-DirectoryOrThrow -Path $webTarget -BackupRoot $backupTarget -Name "web"

$excludeFromPackage = @()
if (Test-Path -LiteralPath $productionSettings) {
    $excludeFromPackage = @("appsettings.Production.json")
    Write-Info "Existing appsettings.Production.json will not be replaced from package"
}

Write-Step "Deploying API"

Copy-TreeSafe -Source $apiSource -Destination $apiTarget -ExcludeFileNames $excludeFromPackage

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

$sourceFileCount = (Get-ChildItem -LiteralPath $apiSource -File -Force | Where-Object { $_.Name -ne "appsettings.Production.json" }).Count
$deployedFileCount = (Get-ChildItem -LiteralPath $apiTarget -File -Force | Where-Object { $_.Name -ne "appsettings.Production.json" }).Count
if ($deployedFileCount -lt $sourceFileCount) {
    throw ("API file copy verification failed. Expected at least " + $sourceFileCount + " non-settings files, found " + $deployedFileCount + ".")
}
Write-Info ("API file copy verification passed (" + $deployedFileCount + " files excluding settings).")

Write-Step "Deploying web"

Copy-TreeSafe -Source $webSource -Destination $webTarget

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
TargetFramework: $targetFramework
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
Write-Output ("5. IIS serves static UI only. API runs on Kestrel port " + $ApiPort + ".")
Write-Output "6. /api/health and /swagger are not guaranteed in Production unless explicitly enabled."
