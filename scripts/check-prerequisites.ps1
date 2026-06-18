#Requires -Version 5.1
<#
.SYNOPSIS
  Read-only production prerequisite checks for Uqeb (no system changes).
#>

param(
    [string]$InstallRoot = "C:\Uqeb",
    [int]$ApiPort = 5000
)

$ErrorActionPreference = "Stop"

$results = New-Object System.Collections.Generic.List[object]
$requiredFailures = 0

function Add-Check {
    param(
        [string]$Name,
        [bool]$Passed,
        [string]$Detail,
        [bool]$Required = $true
    )

    $results.Add([pscustomobject]@{
        Check    = $Name
        Passed   = $Passed
        Detail   = $Detail
        Required = $Required
    }) | Out-Null

    if ($Required -and -not $Passed) {
        $script:requiredFailures++
    }
}

function Test-CommandExists {
    param([string]$CommandName)
    return [bool](Get-Command $CommandName -ErrorAction SilentlyContinue)
}

function Write-CheckLine {
    param([string]$Message)
    Write-Output $Message
}

Write-CheckLine ""
Write-CheckLine "Uqeb production prerequisites check"
Write-CheckLine "==================================="
Write-CheckLine ("Install root: " + $InstallRoot)
Write-CheckLine ""

Add-Check -Name "PowerShell" -Passed $true -Detail ("Version: " + $PSVersionTable.PSVersion.ToString())

$iisOk = $false
$iisDetail = "IIS not detected"

try {
    $svc = Get-Service -Name W3SVC -ErrorAction SilentlyContinue
    if ($null -ne $svc) {
        $iisOk = $true
        $iisDetail = "W3SVC service found. Status: " + $svc.Status
    }
} catch {
    $iisDetail = "Unable to query W3SVC: " + $_.Exception.Message
}

if (-not $iisOk) {
    try {
        $feature = Get-WindowsOptionalFeature -Online -FeatureName IIS-WebServer -ErrorAction SilentlyContinue
        if ($null -ne $feature -and $feature.State -eq "Enabled") {
            $iisOk = $true
            $iisDetail = "IIS-WebServer feature enabled"
        }
    } catch {
        Write-Warning ("Optional feature check unavailable: " + $_.Exception.Message)
    }

    try {
        $serverFeature = Get-WindowsFeature -Name Web-Server -ErrorAction SilentlyContinue
        if ($null -ne $serverFeature -and $serverFeature.Installed) {
            $iisOk = $true
            $iisDetail = "Web-Server role installed"
        }
    } catch {
        Write-Warning ("Windows Server feature check unavailable: " + $_.Exception.Message)
    }
}

Add-Check -Name "IIS / Web Server" -Passed $iisOk -Detail $iisDetail

$runtimeConfigPath = Join-Path $InstallRoot "api\Uqeb.Api.runtimeconfig.json"
$expectedTfm = "net10.0"
$tfmDetail = "No installed runtimeconfig at " + $runtimeConfigPath + "; checking for ASP.NET Core 10.x"

if (Test-Path -LiteralPath $runtimeConfigPath) {
    try {
        $runtimeConfig = Get-Content -LiteralPath $runtimeConfigPath -Raw | ConvertFrom-Json
        if ($runtimeConfig.runtimeOptions.tfm) {
            $expectedTfm = [string]$runtimeConfig.runtimeOptions.tfm
            $tfmDetail = "Installed runtimeconfig TFM: " + $expectedTfm
        }
    } catch {
        Write-Warning ("Could not parse runtimeconfig.json: " + $_.Exception.Message)
    }
}

$dotnetOk = $false
$dotnetDetail = "dotnet command not found"

if (Test-CommandExists "dotnet") {
    $runtimes = & dotnet --list-runtimes 2>$null
    $tfmMajor = if ($expectedTfm -match "net(\d+)") { $Matches[1] } else { "10" }
    $aspnetMatch = $runtimes | Where-Object { $_ -match ("Microsoft\.AspNetCore\.App " + $tfmMajor + "\.") }

    if ($aspnetMatch) {
        $dotnetOk = $true
        $dotnetDetail = $tfmDetail + ". Runtime: " + (($aspnetMatch | Select-Object -First 1).Trim())
    } else {
        $aspnetAny = $runtimes | Where-Object { $_ -match "Microsoft\.AspNetCore\.App" }
        $dotnetDetail = if ($aspnetAny) {
            $tfmDetail + ". Required Microsoft.AspNetCore.App " + $tfmMajor + ".x not found. Installed: " + ($aspnetAny -join "; ")
        } else {
            "dotnet exists, but Microsoft.AspNetCore.App runtime was not found."
        }
    }
}

Add-Check -Name ("ASP.NET Core Runtime (" + $expectedTfm + ")") -Passed $dotnetOk -Detail $dotnetDetail

$sqlOk = $false
$sqlDetail = "SQL Server service not found"

try {
    $sqlServices = Get-Service -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -eq "MSSQLSERVER" -or $_.Name -like "MSSQL`$*" }

    if ($sqlServices) {
        $sqlOk = $true
        $sqlDetail = (($sqlServices | ForEach-Object { $_.Name + "=" + $_.Status }) -join "; ")
    }
} catch {
    $sqlDetail = "Unable to query SQL services: " + $_.Exception.Message
}

Add-Check -Name "SQL Server Service" -Passed $sqlOk -Detail $sqlDetail

$sqlcmdOk = Test-CommandExists "sqlcmd"
$sqlcmdDetail = if ($sqlcmdOk) {
    "sqlcmd found. Use -C with local SQL Server when certificate is not trusted."
} else {
    "sqlcmd not found. Optional for manual DB checks."
}
Add-Check -Name "sqlcmd (optional)" -Passed $true -Detail $sqlcmdDetail -Required $false

$nodeDetail = if (Test-CommandExists "node") { (& node --version) } else { "Not required when web dist is pre-built." }
Add-Check -Name "Node.js (optional)" -Passed $true -Detail $nodeDetail -Required $false

$npmDetail = if (Test-CommandExists "npm") { (& npm --version) } else { "Not required when web dist is pre-built." }
Add-Check -Name "npm (optional)" -Passed $true -Detail $npmDetail -Required $false

$folders = @(
    $InstallRoot,
    (Join-Path $InstallRoot "api"),
    (Join-Path $InstallRoot "web"),
    (Join-Path $InstallRoot "logs"),
    (Join-Path $InstallRoot "uploads"),
    (Join-Path $InstallRoot "backup")
)

foreach ($folder in $folders) {
    $exists = Test-Path -LiteralPath $folder
    Add-Check -Name ("Folder: " + $folder) -Passed $exists -Required $false -Detail $(if ($exists) {
        "Exists"
    } else {
        "Missing. deploy-production.ps1 creates api/web/logs/uploads/backup on first deploy."
    })
}

$portDetail = "Port " + $ApiPort + " appears free"
try {
    $conn = Get-NetTCPConnection -LocalPort $ApiPort -ErrorAction SilentlyContinue
    if ($conn) {
        $portDetail = "Port " + $ApiPort + " is currently in use (API may already be running)"
    }
} catch {
    $portDetail = "Unable to check port " + $ApiPort + ": " + $_.Exception.Message
}

Add-Check -Name ("Port " + $ApiPort) -Passed $true -Detail $portDetail -Required $false

Write-CheckLine ""
Write-CheckLine "Results"
Write-CheckLine "-------"

foreach ($r in $results) {
    $mark = if ($r.Passed) { "[OK]" } else { if ($r.Required) { "[FAIL]" } else { "[WARN]" } }
    Write-CheckLine ($mark + " " + $r.Check + " - " + $r.Detail)
}

Write-CheckLine ""
if ($requiredFailures -eq 0) {
    Write-CheckLine "All required checks passed."
    exit 0
}

Write-CheckLine "One or more required checks failed."
Write-CheckLine "Review PREREQUISITES.md and DEPLOYMENT_NOTES.md."
exit 1
